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
        foreach (Declaration declaration in unit.Declarations)
        {
            BindDeclaration(declaration);
        }
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
        }
        finally
        {
            _currentType = savedType;
            _currentModel = savedModel;
        }
    }

    private void BindField(FieldDecl field)
    {
        if (_model.GetSymbol(field) is FieldSymbol symbol)
        {
            // Settled when signatures were, so it is read rather than resolved again.
            _model.BindType(field, symbol.Type);

            // A global model has no instances, so an instance member on one could never be
            // reached.
            if (_currentModel is { IsGlobal: true } && !symbol.IsGlobal)
            {
                Report(DiagnosticDescriptors.GlobalModelMemberNotGlobal, field, _currentModel.Name);
            }
        }

        if (field.Initializer is not null)
        {
            // A field initializer runs before the constructor body and has no locals around
            // it, so it binds in an empty scope.
            InScope(() => BindExpression(field.Initializer));
        }
    }

    /// <summary>
    /// <para>Binds a function's parameters and body.</para>
    /// <para><paramref name="isMember"/> separates a declared member from a function declared
    /// among statements. A local function is not a member, so the rules about what a global
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

        bool savedGlobal = _inGlobalMember;

        if (isMember)
        {
            _inGlobalMember = symbol?.IsGlobal ?? false;

            if (_currentModel is { IsGlobal: true } && symbol is { IsGlobal: false })
            {
                Report(DiagnosticDescriptors.GlobalModelMemberNotGlobal, function, _currentModel.Name);
            }
        }

        try
        {
            InScope(() =>
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
                            : new ParameterSymbol(parameter.Name, ResolveType(parameter.Type))
                            {
                                Declaration = parameter,
                            };

                    Declare(parameterSymbol, parameter);
                    _model.Bind(parameter, parameterSymbol);
                }

                BindStatements(function.Body);
            });
        }
        finally
        {
            _inGlobalMember = savedGlobal;
        }
    }

    // ---- Statements -----------------------------------------------------------------------

    private void BindStatements(IReadOnlyList<Statement> statements)
    {
        foreach (Statement statement in statements)
        {
            BindStatement(statement);
        }
    }

    private void BindStatement(Statement statement)
    {
        switch (statement)
        {
            case BlockStmt block:
                InScope(() => BindStatements(block.Statements));
                break;

            case VarDeclStmt declaration:
                BindVarDecl(declaration);
                break;

            case LocalDeclStmt local when local.Declaration is FunctionDecl function:
                BindLocalFunction(function);
                break;

            case IfStmt statement2:
                BindExpression(statement2.Condition);
                InScope(() => BindStatements(statement2.ThenBody));

                foreach (ElseIfClause clause in statement2.ElseIfClauses)
                {
                    BindExpression(clause.Condition);
                    InScope(() => BindStatements(clause.Body));
                }

                if (statement2.ElseBody is not null)
                {
                    InScope(() => BindStatements(statement2.ElseBody));
                }

                break;

            case WhileStmt loop:
                BindExpression(loop.Condition);
                InScope(() => BindStatements(loop.Body));
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

                    InScope(() => BindStatements(group.Body));
                }

                if (switchStmt.DefaultBody is not null)
                {
                    InScope(() => BindStatements(switchStmt.DefaultBody));
                }

                break;

            case TryStmt tryStmt:
                InScope(() => BindStatements(tryStmt.Body));

                foreach (CatchClause clause in tryStmt.Catches)
                {
                    BindCatch(clause);
                }

                if (tryStmt.FinallyBody is not null)
                {
                    InScope(() => BindStatements(tryStmt.FinallyBody));
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
    /// <para>Binds a function declared among statements.</para>
    /// <para>Members were collected in the first pass, but a local function is not a member —
    /// it is introduced by a statement, so its symbol is built here. It goes into the
    /// enclosing scope, which is both what makes it callable by name and what lets its body
    /// see the locals around it.</para>
    /// </summary>
    private void BindLocalFunction(FunctionDecl function)
    {
        TypeSymbol? returnType =
            function.ReturnType is null ? null : ResolveType(function.ReturnType);

        List<ParameterSymbol> parameters =
        [
            .. function.Parameters.Select(p =>
                new ParameterSymbol(p.Name, ResolveType(p.Type)) { Declaration = p }),
        ];

        FunctionSymbol symbol = new(function.Name, returnType, parameters, function.Modifiers)
        {
            Declaration = function,
        };

        Declare(symbol, function);
        _model.Bind(function, symbol);

        BindFunction(function, isMember: false);
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

        InScope(() =>
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

        InScope(() =>
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
        InScope(() =>
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
