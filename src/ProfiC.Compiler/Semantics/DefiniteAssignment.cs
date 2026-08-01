using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;

namespace ProfiC.Compiler.Semantics;

/// <summary>
/// <para>What is known at a point in the program: which variables hold a value, and whether
/// the point can be reached at all.</para>
/// </summary>
public sealed class FlowState
{
    private FlowState(HashSet<Symbol> assigned, HashSet<Symbol> assignedSomewhere, bool reachable)
    {
        Assigned = assigned;
        AssignedSomewhere = assignedSomewhere;
        Reachable = reachable;
    }

    /// <summary>Variables that certainly hold a value here.</summary>
    public HashSet<Symbol> Assigned { get; }

    /// <summary>
    /// <para>Variables that hold a value on at least one path here.</para>
    /// <para>Kept beside the certain ones only so a mistake can be described accurately. A
    /// variable in this set and not in <see cref="Assigned"/> was given a value somewhere and
    /// missed somewhere else — almost always a branch without its other half — and telling
    /// someone it was never assigned would send them looking in the wrong place.</para>
    /// </summary>
    public HashSet<Symbol> AssignedSomewhere { get; }

    /// <summary>False after something that never returns, such as a yield or a throw.</summary>
    public bool Reachable { get; }

    public static FlowState Empty() => new([], [], reachable: true);

    /// <summary>The state after something that does not return.</summary>
    public FlowState Unreachable() => new([.. Assigned], [.. AssignedSomewhere], reachable: false);

    public FlowState Clone() => new([.. Assigned], [.. AssignedSomewhere], Reachable);

    public FlowState With(Symbol symbol) =>
        new([.. Assigned, symbol], [.. AssignedSomewhere, symbol], Reachable);

    /// <summary>
    /// Records that some path assigned these without promising this one did. A loop body may
    /// not run, so what it assigns is possible here rather than certain.
    /// </summary>
    public FlowState WithPossible(IEnumerable<Symbol> symbols) =>
        new([.. Assigned], [.. AssignedSomewhere, .. symbols], Reachable);

    /// <summary>
    /// <para>Joins two paths that meet.</para>
    /// <para>Only what both paths guarantee survives, which is the whole idea: a variable
    /// assigned on one branch and not the other is not assigned afterwards. An unreachable
    /// path contributes nothing, so it does not weaken the other.</para>
    /// <para>What either path managed survives in the other set, which is what lets the
    /// difference between the two be reported as the different mistake it is.</para>
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

        HashSet<Symbol> either = [.. left.AssignedSomewhere, .. right.AssignedSomewhere];

        return new FlowState(both, either, reachable: true);
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

    /// <summary>
    /// What each function declared among statements in the member being walked reaches for.
    /// Held so that naming one can be weighed against what holds a value at that point.
    /// </summary>
    private readonly Dictionary<FunctionSymbol, CaptureSet> _captures = [];

    private DefiniteAssignment(SemanticModel model, DiagnosticBag diagnostics)
    {
        _model = model;
        _diagnostics = diagnostics;
    }

    /// <summary>Analyzes every function in every file of a compilation.</summary>
    public static void Analyze(
        IReadOnlyList<CompilationUnit> units,
        SemanticModel model,
        DiagnosticBag diagnostics)
    {
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(diagnostics);

        DefiniteAssignment analysis = new(model, diagnostics);

        foreach (CompilationUnit unit in units)
        {
            using DiagnosticBag.FileScope reporting = diagnostics.InFile(unit.Source);

            foreach (Declaration declaration in unit.Declarations)
            {
                analysis.AnalyzeDeclaration(declaration);
            }
        }
    }

    /// <summary>Analyzes one file, which is a compilation of one.</summary>
    public static void Analyze(
        CompilationUnit unit,
        SemanticModel model,
        DiagnosticBag diagnostics)
    {
        ArgumentNullException.ThrowIfNull(unit);

        Analyze([unit], model, diagnostics);
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
        // An abstract function has no body to walk, and nothing to answer for: reaching a
        // result is the obligation of whatever writes it.
        if (function.Body is not { } body)
        {
            return;
        }

        FlowState state = FlowState.Empty();

        // What each function declared among statements reaches for, so that naming one can be
        // weighed against what has been given a value by then. Worked out once for the member
        // rather than at each mention, and only for the ones reached by name.
        _captures.Clear();

        foreach ((SyntaxNode value, CaptureSet uses) in CaptureAnalysis.Analyze(function, _model))
        {
            if (value is FunctionDecl local && _model.GetSymbol(local) is FunctionSymbol named)
            {
                _captures[named] = uses;
            }
        }

        // Parameters arrive holding values.
        foreach (ParameterDecl parameter in function.Parameters)
        {
            if (_model.GetSymbol(parameter) is { } symbol)
            {
                state = state.With(symbol);
            }
        }

        state = AnalyzeStatements(body, state);

        if (_model.GetSymbol(function) is not FunctionSymbol declared)
        {
            return;
        }

        if (declared.IsConstructor && owner is not null)
        {
            CheckConstructorAssignedEveryField(function, owner, state);
        }

        // Control still reaching the end means a path through the body yields nothing. A
        // function that declares no result has nothing to be missing, and one whose result did
        // not resolve has already been reported for that.
        if (state.Reachable
            && !declared.IsConstructor
            && declared.ReturnType is { IsError: false } result)
        {
            _diagnostics.Report(
                DiagnosticDescriptors.NotEveryPathYields,
                function.Span,
                function.Name,
                result.WithArticle());
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
                    || field.IsShared
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
        bool reported = false;

        foreach (Statement statement in statements)
        {
            // The first statement past where control can arrive is the one worth naming. The
            // rest are unreachable for the same reason, and saying so once per statement would
            // bury the yield or throw that caused it.
            if (!state.Reachable && !reported)
            {
                _diagnostics.Report(DiagnosticDescriptors.UnreachableCode, statement.Span);
                reported = true;
            }

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

        // The body may not run, so nothing it assigns is certain afterwards. What it manages
        // is still worth carrying: a variable assigned only inside a loop was missed on a
        // path rather than never given a value at all.
        FlowState body = AnalyzeStatements(loop.Body, state.Clone());

        return state.WithPossible(body.AssignedSomewhere);
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

        FlowState body = AnalyzeStatements(loop.Body, inside);
        return state.WithPossible(body.AssignedSomewhere);
    }

    private FlowState AnalyzeForEach(ForEachStmt loop, FlowState state)
    {
        state = AnalyzeExpression(loop.Sequence, state);

        FlowState inside = state.Clone();

        if (_model.GetSymbol(loop) is { } variable)
        {
            inside = inside.With(variable);
        }

        FlowState body = AnalyzeStatements(loop.Body, inside);
        return state.WithPossible(body.AssignedSomewhere);
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
                CheckReachedInTime(identifier, state);
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

    /// <summary>
    /// <para>Naming a function declared among statements, before something it uses is ready.
    /// </para>
    /// <para>Such a function is in scope for the whole run it sits in, so where it is written
    /// says where to read it rather than when it exists. What it names is another matter: the
    /// locals it reaches for come into being in order, and reaching it from above one of them
    /// would read a place holding nothing.</para>
    /// <para>Asked of the name rather than of the call, because handing the function somewhere
    /// else is as good as calling it — the run that gets it can call it whenever it likes.
    /// </para>
    /// </summary>
    private void CheckReachedInTime(IdentifierExpr identifier, FlowState state)
    {
        if (_model.GetSymbol(identifier) is not FunctionSymbol function
            || !_captures.TryGetValue(function, out CaptureSet? uses))
        {
            return;
        }

        foreach (Symbol used in uses.Names)
        {
            if (used is LocalSymbol local && !state.Assigned.Contains(local))
            {
                _diagnostics.Report(
                    DiagnosticDescriptors.CalledBeforeWhatItUsesIsReady,
                    identifier.Span,
                    function.Name,
                    local.Name);

                return;
            }
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

        // Assigned somewhere but not everywhere is a different mistake from never assigned at
        // all, and is nearly always a branch missing its other half. Saying it was never given
        // a value would send the reader looking in the wrong place.
        DiagnosticDescriptor descriptor = state.AssignedSomewhere.Contains(local)
            ? DiagnosticDescriptors.UseBeforeAssignmentOnSomePath
            : DiagnosticDescriptors.UseBeforeAssignment;

        _diagnostics.Report(descriptor, identifier.Span, local.Name);
    }
}
