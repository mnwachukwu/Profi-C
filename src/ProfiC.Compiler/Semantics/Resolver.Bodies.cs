using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;

namespace ProfiC.Compiler.Semantics;

public sealed partial class Resolver
{
    /// <summary>
    /// The second pass: bind the names inside every function body, now that every type and
    /// member is known.
    /// </summary>
    private void BindBodies(CompilationUnit unit)
    {
        _currentFile = unit.Source;

        foreach (Declaration declaration in unit.Declarations)
        {
            _cancellation.ThrowIfCancellationRequested();
            BindDeclaration(declaration);
        }

        _currentFile = null;
    }

    private void BindDeclaration(Declaration declaration)
    {
        switch (declaration)
        {
            case NamespaceDecl namespaceDecl:
                foreach (Declaration member in namespaceDecl.Declarations)
                {
                    BindDeclaration(member);
                }

                break;

            case ModelDecl model:
                BindTypeMembers(model, model.Members);
                break;

            case StructureDecl structure:
                BindTypeMembers(structure, structure.Members);
                break;
        }
    }

    private void BindTypeMembers(Declaration declaration, IReadOnlyList<Declaration> members)
    {
        DeclaredTypeSymbol? symbol = _model.GetSymbol(declaration) as DeclaredTypeSymbol;

        DeclaredTypeSymbol? savedType = _currentType;
        ModelSymbol? savedModel = _currentModel;

        // Names inside a body are read from where the type sits, so a body reaches its
        // neighbors unqualified and reaches outward from there.
        (NamespaceSymbol? Scope, Text.SourceText? File) savedContext = symbol is null
            ? (_lookupNamespace, _lookupFile)
            : EnterTypeContext(symbol);

        _currentType = symbol;
        _currentModel = symbol as ModelSymbol;

        try
        {
            foreach (Declaration member in members)
            {
                switch (member)
                {
                    case FieldDecl field:
                        BindField(field);
                        break;

                    case FunctionDecl function:
                        BindFunction(function);
                        break;

                    case ModelDecl nestedModel:
                        BindTypeMembers(nestedModel, nestedModel.Members);
                        break;

                    case StructureDecl nestedStructure:
                        BindTypeMembers(nestedStructure, nestedStructure.Members);
                        break;
                }
            }

            // A model that writes no constructor still gets one, and that one has the same
            // parent to build as any other. Checked here rather than in BindFunction, which
            // never runs for a constructor nobody wrote.
            if (symbol is ModelSymbol { BaseType: { } parent } model
                && !members.OfType<FunctionDecl>().Any(IsConstructorOf)
                && NeedsArgumentsToBuild(parent))
            {
                Report(
                    DiagnosticDescriptors.NoParentConstructorToReach,
                    declaration,
                    model.Name,
                    parent.Name,
                    Wording.Either([.. TakenBy(parent)]));
            }
        }
        finally
        {
            _currentType = savedType;
            _currentModel = savedModel;
            RestoreContext(savedContext);
        }
    }

    private void BindField(FieldDecl field)
    {
        if (_model.GetSymbol(field) is FieldSymbol symbol)
        {
            // Settled when signatures were, so it is read rather than resolved again.
            _model.BindType(field, symbol.Type);

            // A shared model has no instances, so an instance member on one could never be
            // reached.
            if (_currentModel is { IsShared: true } && !symbol.IsShared)
            {
                Report(DiagnosticDescriptors.SharedModelMemberNotShared, field, _currentModel.Name);
            }
        }

        if (field.Initializer is not null)
        {
            // A field initializer runs before the constructor body and has no locals around
            // it, so it binds in an empty scope — and with nothing built yet, so 'this' and
            // 'base' are out of reach until a constructor starts.
            string? savedField = _initializingField;
            _initializingField = field.Name;

            try
            {
                InScope(field.Span, () => BindExpression(field.Initializer));
            }
            finally
            {
                _initializingField = savedField;
            }
        }
    }

    /// <summary>
    /// <para>Binds a function's parameters and body.</para>
    /// <para><paramref name="isMember"/> separates a declared member from a function declared
    /// among statements. A local function is not a member, so the rules about what a shared
    /// model may hold do not apply to it — and it inherits whether <c>this</c> is available
    /// from the member it sits inside, rather than deciding for itself.</para>
    /// </summary>
    private void BindFunction(FunctionDecl function, bool isMember = true)
    {
        FunctionSymbol? symbol = _model.GetSymbol(function) as FunctionSymbol;

        // A member's signature was settled once every type was known. A local function or a
        // lambda has no collected symbol, so its own types are resolved here instead.
        if (symbol is null && function.ReturnType is not null)
        {
            ResolveType(function.ReturnType);
        }

        bool savedShared = _inSharedMember;

        if (isMember)
        {
            _inSharedMember = symbol?.IsShared ?? false;

            if (_currentModel is { IsShared: true } && symbol is { IsShared: false })
            {
                Report(DiagnosticDescriptors.SharedModelMemberNotShared, function, _currentModel.Name);
            }
        }

        try
        {
            InScope(function.Span, () =>
            {
                for (int index = 0; index < function.Parameters.Count; index++)
                {
                    ParameterDecl parameter = function.Parameters[index];

                    // A member's parameters already exist on its symbol, correctly typed. Using
                    // those rather than making a second set is what keeps the type a caller is
                    // checked against and the type the body sees from ever disagreeing.
                    ParameterSymbol parameterSymbol =
                        symbol is not null && index < symbol.Parameters.Count
                            ? symbol.Parameters[index]
                            : new ParameterSymbol(parameter.Name, ResolveWrittenType(parameter))
                            {
                                Declaration = parameter,
                            };

                    Declare(parameterSymbol, parameter);
                    _model.Bind(parameter, parameterSymbol);
                }

                BindStatements(function.Body ?? []);
            });

            if (isMember && symbol is { IsConstructor: true })
            {
                CheckHowTheParentIsBuilt(function);
            }
        }
        finally
        {
            _inSharedMember = savedShared;
        }
    }

    /// <summary>
    /// <para>Holds a constructor to the two rules about its parent: that <c>base(...)</c> comes
    /// first if it is written, and that it is written where the parent needs it.</para>
    /// <para>Both exist because a parent decides its own state before a child adds to it. A call
    /// written lower down would run statements against fields the parent had not filled in yet;
    /// a call left out entirely leaves them holding nothing at all, which is the worse of the two
    /// because the program runs and simply reads empty.</para>
    /// </summary>
    private void CheckHowTheParentIsBuilt(FunctionDecl constructor)
    {
        if (_currentModel?.BaseType is not { } parent)
        {
            return;
        }

        IReadOnlyList<Statement> body = constructor.Body ?? [];
        Expression? opening = body is [ExpressionStmt first, ..] ? first.Expression : null;

        BaseCalls written = new();

        foreach (Statement statement in body)
        {
            statement.Accept(written);
        }

        foreach (CallExpr call in written.Found)
        {
            if (!ReferenceEquals(call, opening))
            {
                Report(DiagnosticDescriptors.BaseCallMustComeFirst, call, parent.Name);
            }
            else if (call.Arguments.Count == 0 && Constructors(parent).Count() <= 1)
            {
                // The parent this reaches is the one that takes nothing, which is the one a
                // constructor reaches without being told. Saying so runs nothing extra.
                //
                // Only where the parent has nothing to choose between. Where it declares
                // several, 'base()' picks the one taking nothing out of them, and which
                // constructor a parent is built by is worth reading off the line.
                Report(DiagnosticDescriptors.BaseCallSaysWhatHappensAnyway, call, parent.Name);
            }
        }

        if (written.Found.Count == 0 && NeedsArgumentsToBuild(parent))
        {
            Report(
                DiagnosticDescriptors.NoParentConstructorToReach,
                constructor,
                _currentModel.Name,
                parent.Name,
                Wording.Either([.. TakenBy(parent)]));
        }
    }

    private bool IsConstructorOf(FunctionDecl declaration) =>
        _model.GetSymbol(declaration) is FunctionSymbol { IsConstructor: true };

    /// <summary>
    /// <para>Whether a model can only be built by handing it something.</para>
    /// <para>A model that declares no constructor at all takes nothing, which is why the absence
    /// of one is not the same as having none that fit.</para>
    /// </summary>
    private static bool NeedsArgumentsToBuild(ModelSymbol parent)
    {
        FunctionSymbol[] declared = [.. Constructors(parent)];

        return declared.Length > 0 && !declared.Any(c => c.Parameters.Count == 0);
    }

    private static IEnumerable<FunctionSymbol> Constructors(ModelSymbol model) =>
        model.Lookup(model.Name).OfType<FunctionSymbol>().Where(c => c.IsConstructor);

    /// <summary>How each of a parent's constructors reads, for a message that shows the choice.</summary>
    private static IEnumerable<string> TakenBy(ModelSymbol parent) =>
        Constructors(parent)
            .Select(c => $"({string.Join(", ", c.Parameters.Select(p => p.Type?.Display ?? "?"))})");

    /// <summary>
    /// <para>Every <c>base(...)</c> written in a constructor, wherever it sits.</para>
    /// <para>Found by walking rather than by reading the first statement, so that one buried in
    /// an <c>if</c> or a loop is reported too — which is the shape somebody reaches for when they
    /// want a parent built one way or another, and the shape the rule most needs to catch.</para>
    /// </summary>
    private sealed class BaseCalls : SyntaxVisitor
    {
        public List<CallExpr> Found { get; } = [];

        public override void VisitCallExpr(CallExpr node)
        {
            ArgumentNullException.ThrowIfNull(node);

            if (node.Callee is ReceiverExpr { Receiver: ReceiverKind.Base })
            {
                Found.Add(node);
            }

            base.VisitCallExpr(node);
        }

        // A function written inside a constructor has a body of its own, and a 'base' in there
        // belongs to that body rather than to this constructor's opening.
        public override void VisitLocalDeclStmt(LocalDeclStmt node)
        {
        }

        public override void VisitLambdaExpr(LambdaExpr node)
        {
        }
    }

    // ---- Statements -----------------------------------------------------------------------

    /// <summary>
    /// <para>Binds a run of statements, the functions it declares first.</para>
    /// <para>A function declared among statements is in scope throughout the run rather than
    /// from its own line onward, so a call may be written above it and two of them may call
    /// each other. Where a declaration sits says where to read it, not when it exists — the
    /// same as for a member, and for the same reason.</para>
    /// <para>What this costs is that a call can be written before a local the function names
    /// has been given a value. Nothing here can see that: it is a question about paths through
    /// the body, so <see cref="DefiniteAssignment"/> answers it.</para>
    /// </summary>
    private void BindStatements(IReadOnlyList<Statement> statements)
    {
        foreach (Statement statement in statements)
        {
            if (statement is LocalDeclStmt { Declaration: FunctionDecl function })
            {
                DeclareLocalFunction(function);
            }
        }

        foreach (Statement statement in statements)
        {
            _cancellation.ThrowIfCancellationRequested();
            BindStatement(statement);
        }
    }

    private void BindStatement(Statement statement)
    {
        switch (statement)
        {
            case BlockStmt block:
                InScope(SpanOver(block.Statements), () => BindStatements(block.Statements));
                break;

            case VarDeclStmt declaration:
                BindVarDecl(declaration);
                break;

            case LocalDeclStmt local when local.Declaration is FunctionDecl function:
                BindFunction(function, isMember: false);
                break;

            case IfStmt statement2:
                BindExpression(statement2.Condition);
                InScope(SpanOver(statement2.ThenBody), () => BindStatements(statement2.ThenBody));

                foreach (ElseIfClause clause in statement2.ElseIfClauses)
                {
                    BindExpression(clause.Condition);
                    InScope(SpanOver(clause.Body), () => BindStatements(clause.Body));
                }

                if (statement2.ElseBody is not null)
                {
                    InScope(
                        SpanOver(statement2.ElseBody), () => BindStatements(statement2.ElseBody));
                }

                break;

            case WhileStmt loop:
                BindExpression(loop.Condition);
                InScope(SpanOver(loop.Body), () => BindStatements(loop.Body));
                break;

            // The condition is bound after the body's scope closes, so a local declared inside
            // the loop is not visible to it — the body runs again from the top, where that
            // local does not yet exist.
            case LoopUntilStmt loop:
                InScope(SpanOver(loop.Body), () => BindStatements(loop.Body));
                BindExpression(loop.Condition);
                break;

            case LoopForeverStmt loop:
                InScope(SpanOver(loop.Body), () => BindStatements(loop.Body));
                break;

            case ForStmt loop:
                BindRangeLoop(loop);
                break;

            case ForEachStmt loop:
                BindForEach(loop);
                break;

            case SwitchStmt switchStmt:
                BindExpression(switchStmt.Subject);

                foreach (CaseGroup group in switchStmt.Cases)
                {
                    foreach (Expression label in group.Labels)
                    {
                        BindExpression(label);
                    }

                    InScope(SpanOver(group.Body), () => BindStatements(group.Body));
                }

                if (switchStmt.DefaultBody is not null)
                {
                    InScope(
                        SpanOver(switchStmt.DefaultBody),
                        () => BindStatements(switchStmt.DefaultBody));
                }

                break;

            case TryStmt tryStmt:
                InScope(SpanOver(tryStmt.Body), () => BindStatements(tryStmt.Body));

                foreach (CatchClause clause in tryStmt.Catches)
                {
                    BindCatch(clause);
                }

                if (tryStmt.FinallyBody is not null)
                {
                    InScope(
                        SpanOver(tryStmt.FinallyBody),
                        () => BindStatements(tryStmt.FinallyBody));
                }

                break;

            case ThrowStmt throwStmt:
                BindExpression(throwStmt.Exception);
                break;

            case YieldStmt yieldStmt when yieldStmt.Value is not null:
                BindExpression(yieldStmt.Value);
                break;

            case ExpressionStmt expression:
                BindExpression(expression.Expression);
                break;

            case AssignmentStmt assignment:
                BindAssignment(assignment);
                break;
        }
    }

    /// <summary>
    /// <para>Puts a function declared among statements into the scope around it.</para>
    /// <para>Members were collected in the first pass, but a local function is not a member —
    /// it is introduced by a statement, so its symbol is built here. It goes into the enclosing
    /// scope, which is both what makes it callable by name and what lets its body see the
    /// locals around it.</para>
    /// <para>Done for a whole run before any of it is bound, which is what lets a call sit
    /// above the declaration it reaches.</para>
    /// </summary>
    private void DeclareLocalFunction(FunctionDecl function)
    {
        TypeSymbol? returnType =
            function.ReturnType is null ? null : ResolveType(function.ReturnType);

        List<ParameterSymbol> parameters =
        [
            .. function.Parameters.Select(p =>
                new ParameterSymbol(p.Name, ResolveWrittenType(p)) { Declaration = p }),
        ];

        FunctionSymbol symbol = new(function.Name, returnType, parameters, function.Modifiers)
        {
            Declaration = function,
        };

        RefuseThrowawayAsAName(function.Name, function, "a function");
        RefuseThrowawayParameters(function.Parameters);

        Declare(symbol, function);
        _model.Bind(function, symbol);
    }

    private void BindVarDecl(VarDeclStmt declaration)
    {
        // The initializer binds first, so that "let x = x;" cannot see the x being declared.
        if (declaration.Initializer is not null)
        {
            BindExpression(declaration.Initializer);
        }

        TypeSymbol type = declaration.Type is null
            ? ErrorType.Instance
            : ResolveType(declaration.Type);

        LocalSymbol local = new(declaration.Name, type, declaration.IsConstant)
        {
            Declaration = declaration,
        };

        // A throwaway is written for the value it drops, so one with no value drops nothing
        // and the line does nothing at all.
        if (Throwaway.Is(declaration.Name) && declaration.Initializer is null)
        {
            Report(DiagnosticDescriptors.ThrowawayNeedsAValue, declaration);
        }

        Declare(local, declaration);
        _model.Bind(declaration, local);
    }

    private void BindRangeLoop(ForStmt loop)
    {
        BindExpression(loop.Start);
        BindExpression(loop.Bound);

        if (loop.Step is not null)
        {
            BindExpression(loop.Step);
        }

        InScope(loop.Span, () =>
        {
            // Fixed by the construct rather than written or inferred: a range loop counts.
            LocalSymbol variable = new(loop.VariableName, PrimitiveType.Integer, isConstant: false)
            {
                Declaration = loop,
                IsLoopVariable = true,
            };

            Declare(variable, loop);
            _model.Bind(loop, variable);

            BindStatements(loop.Body);
        });
    }

    private void BindForEach(ForEachStmt loop)
    {
        BindExpression(loop.Sequence);

        InScope(loop.Span, () =>
        {
            // The element type is worked out while type checking; the resolver only needs the
            // name to exist.
            LocalSymbol variable = new(loop.VariableName, ErrorType.Instance, isConstant: false)
            {
                Declaration = loop,
                IsLoopVariable = true,
            };

            Declare(variable, loop);
            _model.Bind(loop, variable);

            BindStatements(loop.Body);
        });
    }

    private void BindCatch(CatchClause clause)
    {
        InScope(clause.Span, () =>
        {
            TypeSymbol type = ResolveType(clause.ExceptionType);

            LocalSymbol caught = new(clause.VariableName, type, isConstant: false)
            {
                Declaration = clause,
            };

            Declare(caught, clause);
            _model.Bind(clause, caught);

            BindStatements(clause.Body);
        });
    }

    /// <summary>
    /// Binds an assignment, and rejects the targets that cannot be written to. A constant and
    /// a loop variable are both read-only, for different reasons.
    /// </summary>
    private void BindAssignment(AssignmentStmt assignment)
    {
        // Assigning to a throwaway is allowed and says nothing, so the value is bound and the
        // target is not — there is no name on the left to bind. Lowering drops the assignment
        // afterwards, leaving the statement that was going to run either way.
        if (assignment.Target is IdentifierExpr name && Throwaway.Is(name.Name))
        {
            Report(DiagnosticDescriptors.ThrowawayAssignmentSaysNothing, assignment);
            BindExpression(assignment.Value);
            return;
        }

        BindExpression(assignment.Target);
        BindExpression(assignment.Value);

        Symbol? target = _model.GetSymbol(assignment.Target);

        switch (target)
        {
            case LocalSymbol { IsLoopVariable: true } loopVariable:
                Report(DiagnosticDescriptors.CannotAssignToLoopVariable,
                       assignment.Target, loopVariable.Name);
                break;

            case LocalSymbol { IsConstant: true } constant:
                Report(DiagnosticDescriptors.CannotAssignToConstant, assignment.Target, constant.Name);
                break;

            case FieldSymbol { IsConstant: true } constantField:
                Report(DiagnosticDescriptors.CannotAssignToConstant,
                       assignment.Target, constantField.Name);
                break;
        }
    }
}
