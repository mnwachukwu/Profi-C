using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;

namespace ProfiC.Compiler.Semantics;

/// <summary>
/// <para>What is known at a point in the program: which variables hold a value, and whether
/// the point can be reached at all.</para>
/// </summary>
public sealed class FlowState
{
    private FlowState(HashSet<Symbol> assigned, bool reachable)
    {
        Assigned = assigned;
        Reachable = reachable;
    }

    /// <summary>Variables that certainly hold a value here.</summary>
    public HashSet<Symbol> Assigned { get; }

    /// <summary>False after something that never returns, such as a yield or a throw.</summary>
    public bool Reachable { get; }

    public static FlowState Empty() => new([], reachable: true);

    /// <summary>The state after something that does not return.</summary>
    public FlowState Unreachable() => new([.. Assigned], reachable: false);

    public FlowState Clone() => new([.. Assigned], Reachable);

    public FlowState With(Symbol symbol)
    {
        HashSet<Symbol> next = [.. Assigned, symbol];
        return new FlowState(next, Reachable);
    }

    /// <summary>
    /// <para>Joins two paths that meet.</para>
    /// <para>Only what both paths guarantee survives, which is the whole idea: a variable
    /// assigned on one branch and not the other is not assigned afterwards. An unreachable
    /// path contributes nothing, so it does not weaken the other.</para>
    /// </summary>
    public static FlowState Merge(FlowState left, FlowState right)
    {
        if (!left.Reachable)
        {
            return right.Clone();
        }

        if (!right.Reachable)
        {
            return left.Clone();
        }

        HashSet<Symbol> both = [.. left.Assigned];
        both.IntersectWith(right.Assigned);

        return new FlowState(both, reachable: true);
    }
}

/// <summary>
/// <para>Checks that nothing is read before it has been given a value.</para>
/// <para>This is what lets the language do without null. A variable with no value cannot be
/// read, so there is no unset state to represent and nothing for a null to stand for. It also
/// means a constructor must fill in every field, which is why an optional field is the way a
/// self-referential model gets a base case.</para>
/// </summary>
public sealed class DefiniteAssignment
{
    private readonly SemanticModel _model;
    private readonly DiagnosticBag _diagnostics;

    private DefiniteAssignment(SemanticModel model, DiagnosticBag diagnostics)
    {
        _model = model;
        _diagnostics = diagnostics;
    }

    /// <summary>Analyzes every function in a compilation unit.</summary>
    public static void Analyze(
        CompilationUnit unit,
        SemanticModel model,
        DiagnosticBag diagnostics)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(diagnostics);

        DefiniteAssignment analysis = new(model, diagnostics);

        foreach (Declaration declaration in unit.Declarations)
        {
            analysis.AnalyzeDeclaration(declaration);
        }
    }

    private void AnalyzeDeclaration(Declaration declaration)
    {
        switch (declaration)
        {
            case NamespaceDecl namespaceDecl:
                foreach (Declaration member in namespaceDecl.Declarations)
                {
                    AnalyzeDeclaration(member);
                }

                break;

            case ModelDecl model:
                AnalyzeType(model, model.Members);
                break;

            case StructureDecl structure:
                AnalyzeType(structure, structure.Members);
                break;
        }
    }

    private void AnalyzeType(Declaration declaration, IReadOnlyList<Declaration> members)
    {
        DeclaredTypeSymbol? owner = _model.GetSymbol(declaration) as DeclaredTypeSymbol;

        foreach (Declaration member in members)
        {
            switch (member)
            {
                case FunctionDecl function:
                    AnalyzeFunction(function, owner);
                    break;

                case ModelDecl nestedModel:
                    AnalyzeType(nestedModel, nestedModel.Members);
                    break;

                case StructureDecl nestedStructure:
                    AnalyzeType(nestedStructure, nestedStructure.Members);
                    break;
            }
        }
    }

    private void AnalyzeFunction(FunctionDecl function, DeclaredTypeSymbol? owner)
    {
        FlowState state = FlowState.Empty();

        // Parameters arrive holding values.
        foreach (ParameterDecl parameter in function.Parameters)
        {
            if (_model.GetSymbol(parameter) is { } symbol)
            {
                state = state.With(symbol);
            }
        }

        state = AnalyzeStatements(function.Body, state);

        if (_model.GetSymbol(function) is FunctionSymbol { IsConstructor: true } && owner is not null)
        {
            CheckConstructorAssignedEveryField(function, owner, state);
        }
    }

    /// <summary>
    /// <para>Checks that a constructor leaves no field without a value.</para>
    /// <para>A field with an initializer is already covered, since initializers run first. An
    /// optional field is exempt, because empty is a value — and that exemption is what makes a
    /// self-referential model constructible at all.</para>
    /// </summary>
    private void CheckConstructorAssignedEveryField(
        FunctionDecl function,
        DeclaredTypeSymbol owner,
        FlowState state)
    {
        foreach (List<Symbol> group in owner.Members.Values)
        {
            foreach (Symbol member in group)
            {
                if (member is not FieldSymbol field
                    || field.IsGlobal
                    || field.Type is OptionalType
                    || field.Declaration is not FieldDecl { Initializer: null })
                {
                    continue;
                }

                if (!state.Assigned.Contains(field))
                {
                    _diagnostics.Report(
                        DiagnosticDescriptors.FieldNotAssignedInConstructor,
                        function.Span,
                        field.Name);
                }
            }
        }
    }

    // ---- Statements ---------------------------------------------------------------------------

    private FlowState AnalyzeStatements(IReadOnlyList<Statement> statements, FlowState state)
    {
        foreach (Statement statement in statements)
        {
            state = AnalyzeStatement(statement, state);
        }

        return state;
    }

    private FlowState AnalyzeStatement(Statement statement, FlowState state) => statement switch
    {
        BlockStmt block => AnalyzeStatements(block.Statements, state),
        VarDeclStmt declaration => AnalyzeVarDecl(declaration, state),
        LocalDeclStmt { Declaration: FunctionDecl } => state,
        IfStmt branch => AnalyzeIf(branch, state),
        WhileStmt loop => AnalyzeWhile(loop, state),
        ForStmt loop => AnalyzeRangeLoop(loop, state),
        ForEachStmt loop => AnalyzeForEach(loop, state),
        SwitchStmt switchStmt => AnalyzeSwitch(switchStmt, state),
        TryStmt tryStmt => AnalyzeTry(tryStmt, state),
        ThrowStmt throwStmt => AnalyzeExpression(throwStmt.Exception, state).Unreachable(),
        YieldStmt yieldStmt => AnalyzeYield(yieldStmt, state),
        BreakStmt or ContinueStmt => state.Unreachable(),
        ExpressionStmt expression => AnalyzeExpression(expression.Expression, state),
        AssignmentStmt assignment => AnalyzeAssignment(assignment, state),
        _ => state,
    };

    private FlowState AnalyzeVarDecl(VarDeclStmt declaration, FlowState state)
    {
        if (declaration.Initializer is not null)
        {
            state = AnalyzeExpression(declaration.Initializer, state);
        }

        if (_model.GetSymbol(declaration) is not { } symbol)
        {
            return state;
        }

        if (declaration.Initializer is not null)
        {
            return state.With(symbol);
        }

        // An optional needs no initializer, because empty is already a value. That exemption
        // is the same one that lets a self-referential model be constructed at all.
        if (symbol is LocalSymbol { Type: OptionalType })
        {
            return state.With(symbol);
        }

        // Anything else is declared without a value, which is legal; reading it before one
        // arrives is not.
        return state;
    }

    private FlowState AnalyzeIf(IfStmt branch, FlowState state)
    {
        state = AnalyzeExpression(branch.Condition, state);

        FlowState afterThen = AnalyzeStatements(branch.ThenBody, state.Clone());
        FlowState result = afterThen;
        FlowState beforeNext = state;

        foreach (ElseIfClause clause in branch.ElseIfClauses)
        {
            beforeNext = AnalyzeExpression(clause.Condition, beforeNext);
            result = FlowState.Merge(result, AnalyzeStatements(clause.Body, beforeNext.Clone()));
        }

        // With no else, the path where nothing matched contributes the state as it stood.
        return FlowState.Merge(
            result,
            branch.ElseBody is null
                ? beforeNext.Clone()
                : AnalyzeStatements(branch.ElseBody, beforeNext.Clone()));
    }

    /// <summary>
    /// A loop body may run no times at all, so nothing it assigns can be counted on
    /// afterwards.
    /// </summary>
    private FlowState AnalyzeWhile(WhileStmt loop, FlowState state)
    {
        state = AnalyzeExpression(loop.Condition, state);
        AnalyzeStatements(loop.Body, state.Clone());
        return state;
    }

    private FlowState AnalyzeRangeLoop(ForStmt loop, FlowState state)
    {
        state = AnalyzeExpression(loop.Start, state);
        state = AnalyzeExpression(loop.Bound, state);

        if (loop.Step is not null)
        {
            state = AnalyzeExpression(loop.Step, state);
        }

        FlowState inside = state.Clone();

        if (_model.GetSymbol(loop) is { } variable)
        {
            inside = inside.With(variable);
        }

        AnalyzeStatements(loop.Body, inside);
        return state;
    }

    private FlowState AnalyzeForEach(ForEachStmt loop, FlowState state)
    {
        state = AnalyzeExpression(loop.Sequence, state);

        FlowState inside = state.Clone();

        if (_model.GetSymbol(loop) is { } variable)
        {
            inside = inside.With(variable);
        }

        AnalyzeStatements(loop.Body, inside);
        return state;
    }

    /// <summary>
    /// Every arm is joined, and without a default clause the path where nothing matched
    /// counts too — so a switch can only guarantee an assignment if it handles everything.
    /// </summary>
    private FlowState AnalyzeSwitch(SwitchStmt switchStmt, FlowState state)
    {
        state = AnalyzeExpression(switchStmt.Subject, state);

        FlowState? result = null;

        foreach (CaseGroup group in switchStmt.Cases)
        {
            FlowState arm = AnalyzeStatements(group.Body, state.Clone());
            result = result is null ? arm : FlowState.Merge(result, arm);
        }

        if (switchStmt.DefaultBody is not null)
        {
            FlowState fallback = AnalyzeStatements(switchStmt.DefaultBody, state.Clone());
            result = result is null ? fallback : FlowState.Merge(result, fallback);
        }
        else
        {
            result = result is null ? state.Clone() : FlowState.Merge(result, state.Clone());
        }

        return result;
    }

    /// <summary>
    /// <para>The subtle one.</para>
    /// <para>A catch clause may be entered from anywhere inside the try, including before the
    /// first statement, so it can rely only on what was known on the way in. The same is true
    /// of a finally clause. What a finally clause assigns, though, is certain afterwards,
    /// because it runs whichever way the try turned out.</para>
    /// </summary>
    private FlowState AnalyzeTry(TryStmt tryStmt, FlowState state)
    {
        FlowState onEntry = state.Clone();
        FlowState afterBody = AnalyzeStatements(tryStmt.Body, state.Clone());

        FlowState result = afterBody;

        foreach (CatchClause clause in tryStmt.Catches)
        {
            // From the entry state, not from after the body: the exception may have been
            // thrown before anything in the body ran.
            // Catching is what gives the variable its value, so the body may read it.
            FlowState entering = _model.GetSymbol(clause) is { } caught
                ? onEntry.With(caught)
                : onEntry.Clone();

            FlowState afterCatch = AnalyzeStatements(clause.Body, entering);
            result = FlowState.Merge(result, afterCatch);
        }

        if (tryStmt.FinallyBody is null)
        {
            return result;
        }

        FlowState afterFinally = AnalyzeStatements(tryStmt.FinallyBody, onEntry.Clone());

        // Whatever the finally clause assigned holds no matter which way the try went.
        FlowState combined = result.Clone();

        foreach (Symbol symbol in afterFinally.Assigned)
        {
            combined = combined.With(symbol);
        }

        return combined;
    }

    private FlowState AnalyzeYield(YieldStmt yieldStmt, FlowState state)
    {
        if (yieldStmt.Value is not null)
        {
            state = AnalyzeExpression(yieldStmt.Value, state);
        }

        return state.Unreachable();
    }

    private FlowState AnalyzeAssignment(AssignmentStmt assignment, FlowState state)
    {
        state = AnalyzeExpression(assignment.Value, state);

        // A plain name on the left is being given a value rather than read. Anything more
        // complex, such as an index or a member, reads its receiver first.
        if (assignment.Target is IdentifierExpr identifier)
        {
            return _model.GetSymbol(identifier) is { } symbol ? state.With(symbol) : state;
        }

        state = AnalyzeExpression(assignment.Target, state);

        // Assigning to a field counts as giving that field a value, which is what a
        // constructor has to do for every one of them.
        if (assignment.Target is MemberExpr { Receiver: ReceiverExpr } member
            && _model.GetSymbol(member) is FieldSymbol field)
        {
            return state.With(field);
        }

        return state;
    }

    // ---- Expressions ----------------------------------------------------------------------------

    /// <summary>
    /// Walks an expression, reporting anything read before it holds a value. Evaluation order
    /// is left to right, which is what makes "let x = x;" a use before assignment.
    /// </summary>
    private FlowState AnalyzeExpression(Expression expression, FlowState state)
    {
        switch (expression)
        {
            case IdentifierExpr identifier:
                CheckRead(identifier, state);
                return state;

            case LambdaExpr lambda:
                // A lambda body runs later, so what it assigns cannot be counted on here.
                AnalyzeLambda(lambda, state);
                return state;

            default:
                foreach (SyntaxNode child in expression.Children)
                {
                    if (child is Expression inner)
                    {
                        state = AnalyzeExpression(inner, state);
                    }
                }

                return state;
        }
    }

    private void AnalyzeLambda(LambdaExpr lambda, FlowState state)
    {
        FlowState inside = state.Clone();

        foreach (ParameterDecl parameter in lambda.Parameters)
        {
            if (_model.GetSymbol(parameter) is { } symbol)
            {
                inside = inside.With(symbol);
            }
        }

        if (lambda.ExpressionBody is not null)
        {
            AnalyzeExpression(lambda.ExpressionBody, inside);
            return;
        }

        if (lambda.Body is not null)
        {
            AnalyzeStatements(lambda.Body, inside);
        }
    }

    private void CheckRead(IdentifierExpr identifier, FlowState state)
    {
        if (_model.GetSymbol(identifier) is not LocalSymbol local)
        {
            return;
        }

        if (state.Assigned.Contains(local))
        {
            return;
        }

        // A local declared without a value that has been assigned on no path at all gets the
        // plainer message; one assigned on some paths gets the message about paths.
        _diagnostics.Report(
            DiagnosticDescriptors.UseBeforeAssignment,
            identifier.Span,
            local.Name);
    }
}
