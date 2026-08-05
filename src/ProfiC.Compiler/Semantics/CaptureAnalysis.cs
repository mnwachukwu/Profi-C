using ProfiC.Compiler.Ast;

namespace ProfiC.Compiler.Semantics;

/// <summary>
/// <para>What a lambda or a local function reaches for outside itself.</para>
/// <para>A function value written inside another function may name the locals around it, and
/// those locals stop being locals the moment it does: the value can outlive the call that made
/// it, so what it names has to live somewhere that outlives the call too. This pass answers
/// which names those are, for each function value, and nothing more — moving them is closure
/// conversion's job.</para>
/// <para>Capture is by <em>cell</em> rather than by copy, which is what makes this worth
/// getting exactly right. A write through either side is seen by the other:</para>
/// <code>
/// integer total = 1;
/// integer delegate() read = () yield total;
/// total = 99;
/// read();            yields 99, not 1
/// </code>
/// <para>So a captured name cannot be handed over as an argument at the point the value is
/// made. Both the function value and the code around it have to end up naming one place.</para>
/// </summary>
public sealed class CaptureAnalysis : SyntaxVisitor
{
    private readonly SemanticModel _model;

    /// <summary>
    /// The function values enclosing the walk, outermost first. Index is depth: the member
    /// being analyzed sits at 0 and holds no node, so a symbol declared there is captured by
    /// anything nested and by nothing else.
    /// </summary>
    private readonly List<SyntaxNode?> _enclosing = [null];

    /// <summary>Which depth each local, parameter, or loop variable was declared at.</summary>
    private readonly Dictionary<Symbol, int> _declaredAt = [];

    private readonly Dictionary<SyntaxNode, CaptureSet> _captures = [];

    private CaptureAnalysis(SemanticModel model) => _model = model;

    private int Depth => _enclosing.Count - 1;

    /// <summary>
    /// <para>Finds what every function value inside a member captures.</para>
    /// <para>Called per member rather than per file, because a capture never crosses one: a
    /// member's body is the outermost thing a lambda can reach out of.</para>
    /// </summary>
    public static IReadOnlyDictionary<SyntaxNode, CaptureSet> Analyze(
        FunctionDecl member,
        SemanticModel model)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(model);

        CaptureAnalysis analysis = new(model);

        foreach (ParameterDecl parameter in member.Parameters)
        {
            analysis.Declare(parameter);
        }

        foreach (Statement statement in member.Body ?? [])
        {
            analysis.Visit(statement);
        }

        return analysis._captures;
    }

    /// <summary>
    /// <para>The same, for a field's initializer.</para>
    /// <para>Nothing written there can capture a local, because a field is not declared among
    /// statements and there are none around it to reach. What is wanted is the other half of
    /// this pass's answer: <em>which nodes are function values at all</em>. A lambda that
    /// captures nothing still has to stop being a lambda, and only a walk finds it.</para>
    /// </summary>
    public static IReadOnlyDictionary<SyntaxNode, CaptureSet> Analyze(
        Expression initializer,
        SemanticModel model)
    {
        ArgumentNullException.ThrowIfNull(initializer);
        ArgumentNullException.ThrowIfNull(model);

        CaptureAnalysis analysis = new(model);

        analysis.Visit(initializer);

        return analysis._captures;
    }

    // ---- Entering and leaving a function value ---------------------------------------------

    public override void VisitLambdaExpr(LambdaExpr node)
    {
        ArgumentNullException.ThrowIfNull(node);

        Enter(node, node.Parameters);

        foreach (SyntaxNode child in node.Body ?? (IEnumerable<SyntaxNode>)[])
        {
            Visit(child);
        }

        if (node.ExpressionBody is { } body)
        {
            Visit(body);
        }

        Leave();
    }

    /// <summary>
    /// <para>A function declared among statements, which captures exactly as a lambda does.
    /// </para>
    /// <para>Reached only from inside a body:
    /// <see cref="Analyze(FunctionDecl, SemanticModel)"/> walks a member's statements rather than
    /// the member itself, so the only declaration this ever meets is a local one.</para>
    /// </summary>
    public override void VisitFunctionDecl(FunctionDecl node)
    {
        ArgumentNullException.ThrowIfNull(node);

        Enter(node, node.Parameters);

        foreach (Statement statement in node.Body ?? [])
        {
            Visit(statement);
        }

        Leave();
    }

    private void Enter(SyntaxNode value, IReadOnlyList<ParameterDecl> parameters)
    {
        _enclosing.Add(value);
        _captures[value] = new CaptureSet();

        foreach (ParameterDecl parameter in parameters)
        {
            Declare(parameter);
        }
    }

    private void Leave() => _enclosing.RemoveAt(_enclosing.Count - 1);

    // ---- What introduces a name ------------------------------------------------------------

    public override void VisitVarDeclStmt(VarDeclStmt node)
    {
        ArgumentNullException.ThrowIfNull(node);

        // The initializer is read before the name exists, which is the order the language
        // resolves them in and the reason 'let x = x' names something else or nothing at all.
        if (node.Initializer is { } initializer)
        {
            Visit(initializer);
        }

        Declare(node);
    }

    public override void VisitForStmt(ForStmt node)
    {
        ArgumentNullException.ThrowIfNull(node);

        Visit(node.Start);
        Visit(node.Bound);

        if (node.Step is { } step)
        {
            Visit(step);
        }

        // Declared inside rather than around the loop: each turn binds a fresh counter, so a
        // function made on one turn holds that turn's value. Nothing here depends on that —
        // the counter is captured either way — but the depth it is declared at is the same.
        Declare(node);

        foreach (Statement statement in node.Body)
        {
            Visit(statement);
        }
    }

    public override void VisitCatchClause(CatchClause node)
    {
        ArgumentNullException.ThrowIfNull(node);

        Declare(node);

        foreach (Statement statement in node.Body)
        {
            Visit(statement);
        }
    }

    public override void VisitParameterDecl(ParameterDecl node) => Declare(node);

    private void Declare(SyntaxNode declaration)
    {
        Symbol? symbol = _model.GetSymbol(declaration);

        if (symbol is LocalSymbol or ParameterSymbol)
        {
            _declaredAt[symbol] = Depth;
        }
    }

    // ---- What reaches for one --------------------------------------------------------------

    public override void VisitIdentifierExpr(IdentifierExpr node)
    {
        ArgumentNullException.ThrowIfNull(node);

        Symbol? symbol = _model.GetSymbol(node);

        if (symbol is not (LocalSymbol or ParameterSymbol)
            || !_declaredAt.TryGetValue(symbol, out int declared)
            || declared == Depth)
        {
            return;
        }

        // Every function value between the name and its declaration captures it, not only the
        // innermost. A lambda inside a lambda reaching two levels out means the one in the
        // middle has to carry the name too, or the inner one has nowhere to read it from.
        for (int depth = declared + 1; depth <= Depth; depth++)
        {
            if (_enclosing[depth] is { } scope)
            {
                _captures[scope].Add(symbol);
            }
        }
    }

    /// <summary>
    /// <para><c>this</c> and <c>base</c>, which a function value captures as surely as a local.
    /// </para>
    /// <para>Recorded apart from the names because it is not one: there is no symbol to move
    /// into a field, and what has to be carried is the receiver the member was called on.</para>
    /// </summary>
    public override void VisitReceiverExpr(ReceiverExpr node)
    {
        ArgumentNullException.ThrowIfNull(node);

        for (int depth = 1; depth <= Depth; depth++)
        {
            if (_enclosing[depth] is not { } scope)
            {
                continue;
            }

            _captures[scope].CapturesReceiver = true;

            if (node.Receiver == ReceiverKind.Base)
            {
                _captures[scope].CapturesBase = true;
            }
        }
    }
}

/// <summary>
/// <para>What one function value reaches for.</para>
/// <para>Ordered by first mention rather than by name, so that a tree built from this is the
/// same tree every run — a golden file compares text, and a field order that follows a hash
/// would differ between machines.</para>
/// </summary>
public sealed class CaptureSet
{
    private readonly List<Symbol> _names = [];
    private readonly HashSet<Symbol> _seen = [];

    /// <summary>The locals and parameters read from outside, in the order first named.</summary>
    public IReadOnlyList<Symbol> Names => _names;

    /// <summary>True when the body names <c>this</c> or <c>base</c>.</summary>
    public bool CapturesReceiver { get; internal set; }

    /// <summary>
    /// <para>True when the body names <c>base</c> specifically.</para>
    /// <para>Told apart from <c>this</c> because carrying the instance is not enough for it:
    /// <c>base.Speak()</c> means the parent's version whatever the instance turns out to be,
    /// so what has to travel is the call as well as the receiver.</para>
    /// </summary>
    public bool CapturesBase { get; internal set; }

    /// <summary>True when nothing is reached for, which is the common case.</summary>
    public bool IsEmpty => _names.Count == 0 && !CapturesReceiver;

    internal void Add(Symbol symbol)
    {
        if (_seen.Add(symbol))
        {
            _names.Add(symbol);
        }
    }
}
