using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Text;

namespace ProfiC.Compiler.Semantics;

/// <summary>
/// <para>Moves what a function value captures out of the call that made it.</para>
/// <para>A local lives as long as the call it was declared in. A function value made in that
/// call can outlive it, and <see cref="CaptureAnalysis"/> says which locals such a value names.
/// This pass gives those locals somewhere else to live: a <em>frame</em>, an ordinary model
/// with a field per captured name, made where the names were declared.</para>
/// <para>The result is ordinary Profi-C. A frame is a model, its fields are fields, the body
/// that captured them is a function on it, and the value itself is that function named through
/// the frame — which the language already had, since <c>b.Plus</c> is a function value bound to
/// <c>b</c>. Nothing downstream learns a new shape, and the interpreter runs the output with
/// the code it uses for any other program. That is what makes the pass testable before an
/// emitter exists: the same corpus run twice, converted and not, must print the same thing.
/// </para>
/// <para><b>Capture is by cell, so the code around the value is rewritten too.</b> Writing
/// <c>total</c> after a lambda has captured it is seen by the lambda, and a write inside is
/// seen outside, so both sides have to end up naming one field. Handing the values over as
/// arguments where the value is made would break that, silently, and only for programs that
/// write to a captured name afterwards.</para>
/// <para><b>A frame is made where the names are declared, not once per call.</b> A loop body
/// declares its names afresh on every turn, so a value made on one turn holds that turn's
/// frame. This is what makes three lambdas built in a loop over 1 to 3 answer 1, 2, 3 rather
/// than 3, 3, 3.</para>
/// <para><b>A value naming <c>this</c> carries the instance in the frame too</b>, under a name
/// no program could write. Inside the frame's own function <c>this</c> is the frame, so what
/// the body meant by it is a field on that frame rather than the receiver it now has.</para>
/// <para><b>Frames are linked outward</b>, so a value reaching into several runs at once is
/// written onto the innermost and follows the links for the rest. A lambda made in a loop that
/// names both the counter and something declared before the loop reads one off its own frame
/// and the other off the frame that one points to.</para>
/// <para><b>A value naming <c>base</c> reaches its parent through a stand-in.</b> A frame
/// extends nothing, so <c>base</c> cannot be written on one and <c>&lt;self&gt;.Speak()</c>
/// would run the overriding version rather than the parent's. The type that does have a parent
/// gains a member holding that call, and the frame calls that.</para>
/// <para>Nothing is left as a lambda: after this pass every function value is a function on a
/// model, so the emitter meets one shape and never reasons about capture at all.</para>
/// </summary>
public sealed class ClosureConversion
{
    /// <summary>
    /// What the instance is called on a frame. Marked the way a frame's own name is, so that
    /// no program can write it and nothing reaches it except this pass.
    /// </summary>
    private const string SelfName = "<self>";

    /// <summary>
    /// What a frame calls the frame around it. Inside a frame's own function only that frame is
    /// in hand, so a name belonging to a run further out is reached by following these.
    /// </summary>
    private const string UpName = "<up>";

    private readonly SemanticModel _model;

    /// <summary>Frames made for the member being rewritten, innermost last.</summary>
    private readonly List<Frame> _open = [];

    /// <summary>The frames made for this file, to be declared beside the types they serve.</summary>
    private readonly List<Declaration> _declared = [];

    private IReadOnlyDictionary<SyntaxNode, CaptureSet> _captures =
        new Dictionary<SyntaxNode, CaptureSet>();

    /// <summary>Which frame holds each captured name, once its declaration has been passed.</summary>
    private readonly Dictionary<Symbol, Frame> _movedTo = [];

    /// <summary>
    /// The frame whose function is being written, if any. Inside one, the frame is reached as
    /// <c>this</c>; outside, through the local that holds it.
    /// </summary>
    private Frame? _inside;

    /// <summary>The type whose members are being rewritten, which is what <c>this</c> is one of.</summary>
    private DeclaredTypeSymbol? _currentType;

    /// <summary>
    /// True while rewriting a member some value of which names <c>this</c>. Every frame made
    /// for that member carries the instance, so that whichever frame a value ends up on can
    /// answer for the receiver as well as the names.
    /// </summary>
    private bool _needsSelf;

    /// <summary>The file's model for bodies that captured nothing, once one has been needed.</summary>
    private (ModelSymbol Symbol, List<Declaration> Members)? _loose;

    /// <summary>Members added to the type being rewritten so a lifted body can reach its parent.</summary>
    private List<Declaration> _thunks = [];

    /// <summary>The stand-in made for each parent member reached, so one serves every mention.</summary>
    private Dictionary<FunctionSymbol, FunctionSymbol> _reachedThroughBase = [];

    /// <summary>
    /// <para>Where each local function went, keyed by the symbol a name in the body still refers
    /// to.</para>
    /// <para>A place is taken as the declaration is reached, which is early enough because a
    /// local function is in scope only after it is declared — a call above the declaration is
    /// refused (<c>PC0200</c>) rather than reaching forward. Taking the place before writing the
    /// body is what lets the body call the function it is the body of.</para>
    /// </summary>
    private readonly Dictionary<FunctionSymbol, Placed> _lifted = [];

    private int _made;

    private ClosureConversion(SemanticModel model) => _model = model;

    /// <summary>Converts every file of a lowered compilation.</summary>
    public static IReadOnlyList<CompilationUnit> Convert(
        IReadOnlyList<CompilationUnit> units,
        SemanticModel model)
    {
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(model);

        // One converter across every file, because the number in a frame's name is what keeps
        // it apart from the others and a counter per file would mint two called '<frame$0>'.
        // They share one namespace, so the second would take the first's place there.
        ClosureConversion conversion = new(model);

        return [.. units.Select(conversion.ConvertUnit)];
    }

    /// <summary>Converts one file, which is a compilation of one.</summary>
    public static CompilationUnit Convert(CompilationUnit unit, SemanticModel model)
    {
        ArgumentNullException.ThrowIfNull(unit);

        return Convert([unit], model)[0];
    }

    private CompilationUnit ConvertUnit(CompilationUnit unit)
    {
        // A frame is declared in the file whose member made it, so what the last file left
        // behind is not carried into this one.
        _declared.Clear();
        _loose = null;

        List<Declaration> declarations = [.. unit.Declarations.Select(ConvertDeclaration)];

        // Frames sit beside the types whose members made them rather than inside one. Where
        // they sit is not observable — nothing names a frame — and the top level is the one
        // place reachable from every member, whatever namespace or model it was written in.
        declarations.AddRange(_declared);

        return new CompilationUnit(
            unit.Span, unit.Usings, unit.Imports, declarations, unit.Source);
    }

    private Declaration ConvertDeclaration(Declaration declaration)
    {
        switch (declaration)
        {
            case NamespaceDecl namespaceDecl:
                return Carry(declaration, new NamespaceDecl(
                    namespaceDecl.Span,
                    namespaceDecl.Name,
                    [.. namespaceDecl.Declarations.Select(ConvertDeclaration)],
                    namespaceDecl.IsFileScoped));

            case ModelDecl model:
                return WithinType(declaration, model.Members, members => Carry(
                    declaration,
                    new ModelDecl(
                        model.Span, model.Modifiers, model.Name, model.BaseTypeName, members)));

            case StructureDecl structure:
                return WithinType(declaration, structure.Members, members => Carry(
                    declaration,
                    new StructureDecl(
                        structure.Span, structure.Modifiers, structure.Name, members)));

            case FunctionDecl function:
                return ConvertMember(function);

            default:
                return declaration;
        }
    }

    /// <summary>
    /// Rewrites a type's members with that type recorded, so that a frame made inside one knows
    /// what <c>this</c> would have been. Nesting is restored on the way out, since a model may
    /// be declared inside another.
    /// </summary>
    private Declaration WithinType(
        Declaration declaration,
        IReadOnlyList<Declaration> source,
        Func<IReadOnlyList<Declaration>, Declaration> build)
    {
        DeclaredTypeSymbol? outerType = _currentType;
        List<Declaration> outerThunks = _thunks;
        Dictionary<FunctionSymbol, FunctionSymbol> outerReached = _reachedThroughBase;

        _currentType = _model.GetSymbol(declaration) as DeclaredTypeSymbol ?? outerType;
        _thunks = [];
        _reachedThroughBase = [];

        try
        {
            List<Declaration> members = [.. source.Select(ConvertDeclaration)];

            // Anything a lifted body needed its parent's version of is added here, after the
            // members that asked for it have been rewritten.
            members.AddRange(_thunks);

            return build(members);
        }
        finally
        {
            _currentType = outerType;
            _thunks = outerThunks;
            _reachedThroughBase = outerReached;
        }
    }

    // ---- One member ---------------------------------------------------------------------------

    /// <summary>
    /// Rewrites a member's body, if anything inside it captures. A member whose body holds no
    /// function value — which is nearly all of them — is given back untouched, so a program
    /// that never makes one is not rewritten at all.
    /// </summary>
    private FunctionDecl ConvertMember(FunctionDecl member)
    {
        if (member.Body is not { } body)
        {
            return member;
        }

        _captures = CaptureAnalysis.Analyze(member, _model);

        // A body holding no function value at all is given back as it stands, which is nearly
        // every member. One holding a value that captures nothing is still rewritten: that
        // value has no frame to go on, but it does have to stop being a lambda.
        if (_captures.Count == 0)
        {
            return member;
        }

        _movedTo.Clear();
        _open.Clear();
        _lifted.Clear();
        _inside = null;

        // Whether the instance has to travel is settled for the whole member before any of it
        // is rewritten, because a frame made deep inside may be the one that has to answer for
        // it and a frame's fields are fixed when it is made.
        _needsSelf = _currentType is not null
                     && !member.Modifiers.Has(DeclarationModifiers.Shared)
                     && _captures.Values.Any(c => c.CapturesReceiver);

        return Carry(member, new FunctionDecl(
            member.Span,
            member.Modifiers,
            member.ReturnType,
            member.Name,
            member.Parameters,
            ConvertBlock(body, member.Parameters, loop: null)));
    }

    /// <summary>A value that reaches for something, and so needs somewhere to reach it from.</summary>
    private static bool Convertible(CaptureSet captures) => !captures.IsEmpty;

    // ---- Blocks, and the frames they own ------------------------------------------------------

    /// <summary>
    /// <para>Rewrites a run of statements, making a frame first if this run declares names that
    /// something inside captures.</para>
    /// <para>The frame belongs to the run rather than to the member, which is the whole of how
    /// a loop works: the body of a loop is a run, so its frame is made afresh on every turn and
    /// a value made on one turn cannot see another turn's.</para>
    /// </summary>
    private List<Statement> ConvertBlock(
        IReadOnlyList<Statement> statements,
        IReadOnlyList<ParameterDecl> parameters,
        ForStmt? loop)
    {
        List<Symbol> owned = [.. Owned(statements, parameters, loop)];

        // A member whose values name 'this' but capture no name still needs one frame to put
        // the instance in, and the member's own body is where it goes.
        bool holdsSelf = _needsSelf && _open.Count == 0 && _inside is null;

        if (owned.Count == 0 && !holdsSelf)
        {
            Reserve(statements);
            return [.. statements.Select(ConvertStatement)];
        }

        Frame frame = MakeFrame(statements.Count > 0 ? statements[0].Span : default, owned);

        // Built before the new frame is open, so that reaching the one it links to is worked
        // out from where this code stands rather than from inside the frame being made.
        Statement? link = frame.Up is null
            ? null
            : new AssignmentStmt(
                frame.Creation.Span,
                ReadUp(frame.Creation.Span, frame),
                PathTo(frame.Creation.Span, frame.Parent!));

        foreach (Symbol name in owned)
        {
            _movedTo[name] = frame;
        }

        _open.Add(frame);

        List<Statement> result = [frame.Creation];

        if (link is not null)
        {
            result.Add(link);
        }

        Reserve(statements);

        // The instance is put in as the frame is made. Read from the frame around this one
        // where there is one, because inside a frame's function 'this' is that frame and no
        // longer the receiver the member was called on.
        if (frame.Self is not null)
        {
            SourceSpan at = frame.Creation.Span;

            result.Add(new AssignmentStmt(
                at,
                ReadSelf(at, frame),
                _inside is { } enclosing ? ReadSelf(at, enclosing) : Self(at)));
        }

        // A name already bound when the run begins — a parameter, or the counter of the loop
        // whose body this is — has its value copied into the frame at the top. A counter is
        // read-only inside the body and a fresh one each turn, so a copy of it is the same
        // thing as the counter itself, which is why this can be a copy where a local cannot.
        foreach (Symbol name in owned)
        {
            if (name is ParameterSymbol || (name is LocalSymbol { IsLoopVariable: true }))
            {
                result.Add(Store(frame, name, ReadOriginal(frame.Creation.Span, name)));
            }
        }

        result.AddRange(statements.Select(ConvertStatement));

        _open.RemoveAt(_open.Count - 1);

        return result;
    }

    /// <summary>
    /// <para>Takes a place on a model for every function this run declares, before any of the
    /// run is rewritten.</para>
    /// <para>Such a function is in scope for the whole run, so a call may be written above the
    /// declaration it reaches — settling where each name leads first is what lets that call be
    /// rewritten when it is met. Taking the place before writing any body also lets two of them
    /// call each other.</para>
    /// </summary>
    private void Reserve(IReadOnlyList<Statement> statements)
    {
        foreach (Statement statement in statements)
        {
            if (statement is not LocalDeclStmt { Declaration: FunctionDecl function }
                || _model.GetSymbol(function) is not FunctionSymbol symbol
                || _lifted.ContainsKey(symbol)
                || !_captures.TryGetValue(function, out CaptureSet? captures)
                || !TryHome(captures, out Frame? home))
            {
                continue;
            }

            _lifted[symbol] = Reserve(home, symbol.ReturnType, function.Parameters);
        }
    }

    /// <summary>
    /// The captured names this run introduces: those declared directly in it, the parameters
    /// where the run is a member's own body, and the counter where it is a loop's body.
    /// </summary>
    private IEnumerable<Symbol> Owned(
        IReadOnlyList<Statement> statements,
        IReadOnlyList<ParameterDecl> parameters,
        ForStmt? loop)
    {
        HashSet<Symbol> wanted = [];

        foreach (CaptureSet captures in _captures.Values.Where(Convertible))
        {
            wanted.UnionWith(captures.Names);
        }

        if (wanted.Count == 0)
        {
            yield break;
        }

        foreach (ParameterDecl parameter in parameters)
        {
            if (_model.GetSymbol(parameter) is { } symbol && wanted.Contains(symbol))
            {
                yield return symbol;
            }
        }

        if (loop is not null
            && _model.GetSymbol(loop) is { } counter
            && wanted.Contains(counter))
        {
            yield return counter;
        }

        foreach (Statement statement in statements)
        {
            if (statement is VarDeclStmt declaration
                && _model.GetSymbol(declaration) is { } local
                && wanted.Contains(local))
            {
                yield return local;
            }
        }
    }

    // ---- Statements ---------------------------------------------------------------------------

    private Statement ConvertStatement(Statement statement)
    {
        switch (statement)
        {
            // A declaration of a name that now lives in a frame becomes a write to the field.
            // With no initializer there is nothing to write and the statement goes: the field
            // starts empty, and definite assignment has already proved nothing reads it first.
            case VarDeclStmt declaration
                when _model.GetSymbol(declaration) is { } local && _movedTo.ContainsKey(local):
                return declaration.Initializer is { } start
                    ? Store(_movedTo[local], local, ConvertExpression(start))
                    : new BlockStmt(declaration.Span, []);

            case VarDeclStmt declaration:
                return Carry(declaration, new VarDeclStmt(
                    declaration.Span,
                    declaration.Type,
                    declaration.Name,
                    declaration.Initializer is null ? null : ConvertExpression(declaration.Initializer),
                    declaration.IsConstant));

            case BlockStmt block:
                return new BlockStmt(block.Span, ConvertBlock(block.Statements, [], loop: null));

            case ForStmt loop:
                return Carry(loop, new ForStmt(
                    loop.Span,
                    loop.VariableName,
                    ConvertExpression(loop.Start),
                    ConvertExpression(loop.Bound),
                    loop.IsInclusive,
                    loop.Step is null ? null : ConvertExpression(loop.Step),
                    ConvertBlock(loop.Body, [], loop)));

            case WalkStmt walk:
                return new WalkStmt(
                    walk.Span, ConvertExpression(walk.Sequence), ConvertStatement(walk.Body));

            case WhileStmt loop:
                return new WhileStmt(
                    loop.Span,
                    ConvertExpression(loop.Condition),
                    ConvertBlock(loop.Body, [], loop: null));

            case IfStmt branch:
                return new IfStmt(
                    branch.Span,
                    ConvertExpression(branch.Condition),
                    ConvertBlock(branch.ThenBody, [], loop: null),
                    [.. branch.ElseIfClauses.Select(c => new ElseIfClause(
                        c.Span, ConvertExpression(c.Condition), ConvertBlock(c.Body, [], loop: null)))],
                    branch.ElseBody is null ? null : ConvertBlock(branch.ElseBody, [], loop: null));

            case SwitchStmt switchStmt:
                return new SwitchStmt(
                    switchStmt.Span,
                    ConvertExpression(switchStmt.Subject),
                    [.. switchStmt.Cases.Select(c => new CaseGroup(
                        c.Span,
                        c.Labels,
                        ConvertBlock(c.Body, [], loop: null)))],
                    switchStmt.DefaultBody is null
                        ? null
                        : ConvertBlock(switchStmt.DefaultBody, [], loop: null));

            case TryStmt tryStmt:
                return new TryStmt(
                    tryStmt.Span,
                    ConvertBlock(tryStmt.Body, [], loop: null),
                    [.. tryStmt.Catches.Select(c => Carry(c, new CatchClause(
                        c.Span,
                        c.ExceptionType,
                        c.VariableName,
                        ConvertBlock(c.Body, [], loop: null))))],
                    tryStmt.FinallyBody is null
                        ? null
                        : ConvertBlock(tryStmt.FinallyBody, [], loop: null));

            case ThrowStmt throwStmt:
                return new ThrowStmt(throwStmt.Span, ConvertExpression(throwStmt.Exception));

            case YieldStmt yieldStmt:
                return new YieldStmt(
                    yieldStmt.Span,
                    yieldStmt.Value is null ? null : ConvertExpression(yieldStmt.Value));

            case ExpressionStmt expression:
                return new ExpressionStmt(expression.Span, ConvertExpression(expression.Expression));

            case AssignmentStmt assignment:
                return new AssignmentStmt(
                    assignment.Span,
                    ConvertExpression(assignment.Target),
                    ConvertExpression(assignment.Value));

            // A local function is moved onto a model and its declaration goes: it is a member
            // now, and every name that reached it reaches the member instead. Its place was
            // taken when the run began, which is what lets a call above this line find it.
            case LocalDeclStmt { Declaration: FunctionDecl function }
                when _model.GetSymbol(function) is FunctionSymbol was
                     && _lifted.TryGetValue(was, out Placed? placed):
                Write(placed, function.Span, function.Parameters, function.Body ?? []);
                return new BlockStmt(statement.Span, []);

            case LocalDeclStmt { Declaration: FunctionDecl function } local:
                return new LocalDeclStmt(
                    local.Span,
                    Carry(function, new FunctionDecl(
                        function.Span,
                        function.Modifiers,
                        function.ReturnType,
                        function.Name,
                        function.Parameters,
                        function.Body is null
                            ? null
                            : ConvertBlock(function.Body, function.Parameters, loop: null))));

            default:
                return statement;
        }
    }

    // ---- Expressions --------------------------------------------------------------------------

    private Expression ConvertExpression(Expression expression)
    {
        switch (expression)
        {
            // The name of something that has moved, read where it now lives.
            case IdentifierExpr identifier
                when _model.GetSymbol(identifier) is { } symbol
                     && _movedTo.TryGetValue(symbol, out Frame? frame):
                return Read(identifier.Span, frame, symbol);

            // Inside a frame's own function 'this' is the frame, so what the body meant by it
            // is the instance the frame is carrying. Outside one it still means the receiver.
            case ReceiverExpr { Receiver: ReceiverKind.This }
                when _inside is { Self: not null } frame:
                return ReadSelf(expression.Span, frame);

            // 'base.something' from inside a frame's function, where there is no parent to
            // reach: the frame extends nothing. A field is read off the instance the frame
            // carries, bound to the very field the name reached — fields are told apart by
            // which one they are rather than by name, so a parent's and a child's of the same
            // name stay separate. A function is not, because which one runs is decided by the
            // instance, so it is reached through a member of the type that does have a parent.
            case MemberExpr { Receiver: ReceiverExpr { Receiver: ReceiverKind.Base } } reached
                when _inside is { Self: not null } holder:
                return ThroughBase(reached, holder);

            // A name that stopped being a local function and became a member of one of these.
            case IdentifierExpr named
                when _model.GetSymbol(named) is FunctionSymbol was
                     && _lifted.TryGetValue(was, out Placed? placed):
                return NameOf(named.Span, placed, _model.GetType(named));

            case LambdaExpr lambda when _captures.TryGetValue(lambda, out CaptureSet? captures)
                                        && TryHome(captures, out Frame? home):
                return Lift(lambda, home);

            case LambdaExpr lambda:
                return Carry(lambda, lambda.IsExpressionBodied
                    ? LambdaExpr.Inline(
                        lambda.Span, lambda.Parameters, ConvertExpression(lambda.ExpressionBody!))
                    : LambdaExpr.Block(
                        lambda.Span, lambda.Parameters, ConvertBlock(lambda.Body!, lambda.Parameters, null)));

            case ConversionExpr conversion:
                return Carry(conversion, new ConversionExpr(
                    conversion.Span, ConvertExpression(conversion.Operand), conversion.Operation));

            case UnaryExpr unary:
                return Carry(unary, new UnaryExpr(
                    unary.Span, unary.Operator, ConvertExpression(unary.Operand)));

            case BinaryExpr binary:
                return Carry(binary, new BinaryExpr(
                    binary.Span,
                    ConvertExpression(binary.Left),
                    binary.Operator,
                    ConvertExpression(binary.Right)));

            case TypeTestExpr test:
                return Carry(test, new TypeTestExpr(
                    test.Span, ConvertExpression(test.Operand), test.TargetType));

            case TypeCastExpr cast:
                return Carry(cast, new TypeCastExpr(
                    cast.Span, ConvertExpression(cast.Operand), cast.TargetType));

            case IfExpr conditional:
                return Carry(conditional, new IfExpr(
                    conditional.Span,
                    ConvertExpression(conditional.Condition),
                    ConvertExpression(conditional.ThenValue),
                    ConvertExpression(conditional.ElseValue)));

            case CollectionExpr collection:
                return Carry(collection, new CollectionExpr(
                    collection.Span, [.. collection.Elements.Select(ConvertExpression)]));

            case NewExpr construction:
                return Carry(construction, new NewExpr(
                    construction.Span,
                    construction.TypeName,
                    [.. construction.Arguments.Select(ConvertExpression)]));

            case CallExpr call:
                return Carry(call, new CallExpr(
                    call.Span,
                    ConvertExpression(call.Callee),
                    [.. call.Arguments.Select(ConvertExpression)]));

            case IndexExpr index:
                return Carry(index, new IndexExpr(
                    index.Span, ConvertExpression(index.Receiver), ConvertExpression(index.Index)));

            case MemberExpr member:
                return Carry(member, new MemberExpr(
                    member.Span, ConvertExpression(member.Receiver), member.MemberName));

            default:
                return expression;
        }
    }

    /// <summary>
    /// <para>The frame a value's function is written onto, or null where there is none it could
    /// read everything from.</para>
    /// <para>Always the innermost frame open around the value, whichever of them the names came
    /// from: the frames are linked outward, so from the innermost every name in every run around
    /// it is reachable. What has to hold is that each name is in a frame still open — one from a
    /// run that has closed is not on the way out from here, and a value naming it stays a lambda.
    /// </para>
    /// </summary>
    private bool TryHome(CaptureSet captures, out Frame? home)
    {
        // Always the innermost frame open here, even for a body that captures nothing. A body
        // put on a shared model can reach no frame at all, so one that sits among frames has to
        // go on a frame too — otherwise a function lifted there could not call one beside it.
        home = _open.Count > 0 ? _open[^1] : null;

        if (captures.CapturesReceiver && home?.Self is null)
        {
            return false;
        }

        if (captures.Names.Count > 0 && home is null)
        {
            return false;
        }

        foreach (Symbol name in captures.Names)
        {
            if (!_movedTo.TryGetValue(name, out Frame? frame) || !_open.Contains(frame))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// <para>Moves a function value's body onto a model, and leaves that function named through
    /// the model in its place.</para>
    /// <para>With a <paramref name="home"/> the function is an instance one on that frame, so
    /// naming it binds it to the frame the names live in. With none the value captured nothing,
    /// so the function is shared and is named through a type: there is no instance because there
    /// is nothing for one to hold.</para>
    /// <para>The body is written as a block whichever form was used, because a function named
    /// through a value is run from its statements — the inline form has no separate life once
    /// it is a member. A value that yields nothing keeps its expression as a statement, so that
    /// what it does still happens and nothing is yielded from it.</para>
    /// </summary>
    private Expression Lift(LambdaExpr lambda, Frame? home)
    {
        TypeSymbol? yields = (_model.GetType(lambda) as FunctionType)?.ReturnType;

        // The inline form is first made into the one statement it means, then converted like
        // any other body rather than on its own. Its parameters are names too, and a value
        // written inside it may capture one — converting the expression directly would leave
        // that name with no frame to live in and the value naming it still a lambda.
        IReadOnlyList<Statement> written = lambda.IsExpressionBodied
            ? [
                yields is null
                    ? new ExpressionStmt(lambda.Span, lambda.ExpressionBody!)
                    : new YieldStmt(lambda.Span, lambda.ExpressionBody!),
            ]
            : lambda.Body!;

        Placed placed = Reserve(home, yields, lambda.Parameters);

        Write(placed, lambda.Span, lambda.Parameters, written);

        return NameOf(lambda.Span, placed, _model.GetType(lambda));
    }

    /// <summary>
    /// <para>Takes a place on a model for a body that has not been written yet.</para>
    /// <para>Reserved apart from writing because a local function may be called above the line
    /// that declares it: the name has to lead somewhere before the run is rewritten, and where
    /// it leads cannot depend on having converted the body first.</para>
    /// </summary>
    private Placed Reserve(Frame? home, TypeSymbol? yields, IReadOnlyList<ParameterDecl> parameters)
    {
        string name = $"<invoke${_made++}>";

        DeclarationModifiers modifiers = home is null
            ? DeclarationModifiers.Public | DeclarationModifiers.Shared
            : DeclarationModifiers.Public;

        FunctionSymbol symbol = new(
            name,
            yields,
            [.. parameters.Select(p => _model.GetSymbol(p)).OfType<ParameterSymbol>()],
            modifiers);

        (ModelSymbol owner, List<Declaration> members) = home is null ? Loose() : (home.Symbol, home.Members);
        owner.AddMember(symbol);

        return new Placed(home, owner, members, name, symbol, modifiers);
    }

    /// <summary>Converts a body and writes it into the place taken for it.</summary>
    private void Write(
        Placed placed,
        SourceSpan span,
        IReadOnlyList<ParameterDecl> parameters,
        IReadOnlyList<Statement> written)
    {
        Frame? outer = _inside;
        _inside = placed.Home;

        IReadOnlyList<Statement> body = ConvertBlock(written, parameters, loop: null);

        _inside = outer;

        FunctionDecl declaration = new(
            span, placed.Modifiers, returnType: null, placed.Name, parameters, body);

        _model.Bind(declaration, placed.Symbol);
        placed.Members.Add(declaration);
    }

    /// <summary>
    /// The function named through whatever holds it, which is what the language does for any
    /// function named rather than called: through a value it is bound to that value, and
    /// through a type name it is bound to nothing.
    /// </summary>
    private Expression NameOf(SourceSpan span, Placed placed, TypeSymbol? type)
    {
        MemberExpr reference = new(
            span,
            placed.Home is null ? TypeRef(span, placed.Owner) : PathTo(span, placed.Home),
            placed.Name);

        _model.Bind(reference, placed.Symbol);

        if (type is not null)
        {
            _model.BindType(reference, type);
        }

        return reference;
    }

    /// <summary>
    /// <para>The model holding the bodies of values that captured nothing, made once per file.
    /// </para>
    /// <para>Shared, because there is nothing for an instance to hold: a value that reaches for
    /// nothing outside itself is the same function every time it is written, and giving each one
    /// an object to be bound to would allocate for a difference that does not exist.</para>
    /// </summary>
    private (ModelSymbol Symbol, List<Declaration> Members) Loose()
    {
        if (_loose is not null)
        {
            return (_loose.Value.Symbol, _loose.Value.Members);
        }

        string name = $"<loose${_made++}>";

        ModelSymbol symbol = new(name, DeclarationModifiers.Public | DeclarationModifiers.Shared);
        List<Declaration> members = [];

        ModelDecl declaration = new(
            default, DeclarationModifiers.Public | DeclarationModifiers.Shared, name, null, members);

        _model.Bind(declaration, symbol);
        symbol.Container = _model.GlobalNamespace;
        _model.GlobalNamespace.Types[name] = symbol;
        _declared.Add(declaration);

        _loose = (symbol, members);

        return (symbol, members);
    }

    /// <summary>
    /// <para>What <c>base.something</c> becomes once its body sits on a frame.</para>
    /// <para>A frame extends nothing, so <c>base</c> written there would mean the root rather
    /// than the parent the body meant. What the body has instead is the instance, carried in
    /// the frame — and reading a field off it is enough, since a field is found by which field
    /// it is rather than by its name.</para>
    /// <para>A function is not enough, because <c>base.Speak()</c> means the parent's version
    /// whatever the instance turns out to be, and <c>&lt;self&gt;.Speak()</c> would run the
    /// overriding one. So the type that does have a parent gains a member which makes that call,
    /// and the frame calls that instead.</para>
    /// </summary>
    private Expression ThroughBase(MemberExpr reached, Frame holder)
    {
        Symbol? meant = _model.GetSymbol(reached);
        string name = reached.MemberName;

        if (meant is FunctionSymbol parent)
        {
            meant = StandInFor(parent, reached.Span);
            name = meant.Name;
        }

        MemberExpr replacement = new(reached.Span, ReadSelf(reached.Span, holder), name);

        if (meant is not null)
        {
            _model.Bind(replacement, meant);
        }

        if (_model.GetType(reached) is { } type)
        {
            _model.BindType(replacement, type);
        }

        return replacement;
    }

    /// <summary>
    /// <para>A member of the type being rewritten that calls its parent's version, made once
    /// per parent member reached.</para>
    /// <para>It is an ordinary function whose body is the <c>base</c> call the lifted body
    /// wanted, written where <c>base</c> still means something. Its parameters are its own, so
    /// what the caller passes reaches the parent unchanged.</para>
    /// </summary>
    private FunctionSymbol StandInFor(FunctionSymbol parent, SourceSpan span)
    {
        if (_reachedThroughBase.TryGetValue(parent, out FunctionSymbol? already))
        {
            return already;
        }

        string name = $"<base${_made++}>";

        List<ParameterDecl> written = [];
        List<ParameterSymbol> parameters = [];
        List<Expression> arguments = [];

        for (int index = 0; index < parent.Parameters.Count; index++)
        {
            ParameterSymbol parameter = new($"<given${index}>", parent.Parameters[index].Type);
            ParameterDecl declared = new(span, null, parameter.Name);
            _model.Bind(declared, parameter);

            IdentifierExpr passed = new(span, parameter.Name);
            _model.Bind(passed, parameter);
            _model.BindType(passed, parameter.Type);

            written.Add(declared);
            parameters.Add(parameter);
            arguments.Add(passed);
        }

        MemberExpr callee = new(span, new ReceiverExpr(span, ReceiverKind.Base), parent.Name);
        _model.Bind(callee, parent);

        CallExpr call = new(span, callee, arguments);

        if (parent.ReturnType is { } yields)
        {
            _model.BindType(callee, yields);
            _model.BindType(call, yields);
        }

        FunctionSymbol standIn = new(name, parent.ReturnType, parameters, DeclarationModifiers.Public);

        FunctionDecl declaration = new(
            span,
            DeclarationModifiers.Public,
            returnType: null,
            name,
            written,
            [
                parent.ReturnType is null
                    ? new ExpressionStmt(span, call)
                    : new YieldStmt(span, call),
            ]);

        _model.Bind(declaration, standIn);
        _currentType!.AddMember(standIn);

        _thunks.Add(declaration);
        _reachedThroughBase[parent] = standIn;

        return standIn;
    }

    /// <summary>A type named as a receiver, which is how a shared member is reached.</summary>
    private IdentifierExpr TypeRef(SourceSpan span, DeclaredTypeSymbol type)
    {
        IdentifierExpr reference = new(span, type.Name);
        _model.Bind(reference, type);
        return reference;
    }

    // ---- Building a frame ---------------------------------------------------------------------

    private Frame MakeFrame(SourceSpan span, IReadOnlyList<Symbol> names)
    {
        string typeName = $"<frame${_made++}>";
        Frame? parent = _open.Count > 0 ? _open[^1] : null;

        ModelSymbol type = new(typeName, DeclarationModifiers.Public);
        List<Declaration> members = [];
        Dictionary<Symbol, FieldSymbol> fields = [];

        foreach (Symbol name in names)
        {
            TypeSymbol held = name switch
            {
                LocalSymbol local => local.Type,
                ParameterSymbol parameter => parameter.Type,
                _ => ErrorType.Instance,
            };

            FieldSymbol field = new(name.Name, held, DeclarationModifiers.Public);
            FieldDecl declaration = new(
                span, DeclarationModifiers.Public, new NamedTypeSyntax(span, held.Name),
                name.Name, initializer: null);

            _model.Bind(declaration, field);
            type.AddMember(field);
            members.Add(declaration);
            fields[name] = field;
        }

        // Named with the same marks a frame is, so nothing a program could declare collides
        // with it and nothing reads it by accident.
        FieldSymbol? self = null;

        if (_needsSelf && _currentType is not null)
        {
            self = new FieldSymbol(SelfName, _currentType, DeclarationModifiers.Public);
            FieldDecl declaration = new(
                span, DeclarationModifiers.Public,
                new NamedTypeSyntax(span, _currentType.Name), SelfName, initializer: null);

            _model.Bind(declaration, self);
            type.AddMember(self);
            members.Add(declaration);
        }

        // The link outward. Only a frame with a frame around it has one, and it is what lets a
        // function written onto this frame read a name declared in a run further out.
        FieldSymbol? up = null;

        if (parent is not null)
        {
            up = new FieldSymbol(UpName, parent.Symbol, DeclarationModifiers.Public);
            FieldDecl declaration = new(
                span, DeclarationModifiers.Public,
                new NamedTypeSyntax(span, parent.Symbol.Name), UpName, initializer: null);

            _model.Bind(declaration, up);
            type.AddMember(up);
            members.Add(declaration);
        }

        ModelDecl model = new(span, DeclarationModifiers.Public, typeName, null, members);

        _model.Bind(model, type);
        type.Container = _model.GlobalNamespace;
        _model.GlobalNamespace.Types[typeName] = type;
        _declared.Add(model);

        // The frame is held in a local of its own, named so that nothing a program could write
        // collides with it.
        LocalSymbol holder = new($"<held${_made++}>", type, isConstant: false);

        NewExpr construction = new(span, typeName, []);
        _model.BindType(construction, type);

        VarDeclStmt creation = new(span, null, holder.Name, construction, isConstant: false);
        _model.Bind(creation, holder);

        return new Frame(type, holder, fields, members, creation, self)
        {
            Parent = parent,
            Up = up,
            DeclaredInside = _inside,
        };
    }

    // ---- Reaching a frame's fields --------------------------------------------------------------

    /// <summary>
    /// <para>How to reach a frame from where the code being written stands.</para>
    /// <para>A frame made in the same call is held by a local, and naming it is enough. One made
    /// further out is not: inside a frame's own function the only thing in hand is that frame,
    /// so the way to any frame around it is <c>this</c> and then <c>&lt;up&gt;</c> as many
    /// times as there are runs between them.</para>
    /// </summary>
    private Expression PathTo(SourceSpan span, Frame target)
    {
        if (ReferenceEquals(target.DeclaredInside, _inside))
        {
            IdentifierExpr held = new(span, target.Local.Name);
            _model.Bind(held, target.Local);
            _model.BindType(held, target.Symbol);
            return held;
        }

        Frame current = _inside!;

        ReceiverExpr start = new(span, ReceiverKind.This);
        _model.BindType(start, current.Symbol);

        Expression path = start;

        while (!ReferenceEquals(current, target) && current.Parent is { } parent)
        {
            MemberExpr up = new(span, path, UpName);
            _model.Bind(up, current.Up!);
            _model.BindType(up, parent.Symbol);

            path = up;
            current = parent;
        }

        return path;
    }

    /// <summary><c>this</c>, meaning whatever the receiver is where it is written.</summary>
    private ReceiverExpr Self(SourceSpan span)
    {
        ReceiverExpr self = new(span, ReceiverKind.This);

        if (_currentType is not null)
        {
            _model.BindType(self, _currentType);
        }

        return self;
    }

    /// <summary>The link outward, read off the frame that holds it.</summary>
    private MemberExpr ReadUp(SourceSpan span, Frame frame)
    {
        FieldSymbol field = frame.Up!;
        MemberExpr member = new(span, PathTo(span, frame), UpName);

        _model.Bind(member, field);
        _model.BindType(member, field.Type);

        return member;
    }

    /// <summary>The instance a frame is carrying, read off that frame.</summary>
    private MemberExpr ReadSelf(SourceSpan span, Frame frame)
    {
        FieldSymbol field = frame.Self!;
        MemberExpr member = new(span, PathTo(span, frame), SelfName);

        _model.Bind(member, field);
        _model.BindType(member, field.Type);

        return member;
    }

    private MemberExpr Read(SourceSpan span, Frame frame, Symbol name)
    {
        FieldSymbol field = frame.Fields[name];
        MemberExpr member = new(span, PathTo(span, frame), name.Name);

        _model.Bind(member, field);
        _model.BindType(member, field.Type);

        return member;
    }

    private Statement Store(Frame frame, Symbol name, Expression value) =>
        new AssignmentStmt(value.Span, Read(value.Span, frame, name), value);

    /// <summary>
    /// The name as it still stands, for copying a parameter or a loop counter into the frame
    /// at the top of the run that owns it. Read before the move, so it names the original.
    /// </summary>
    private IdentifierExpr ReadOriginal(SourceSpan span, Symbol name)
    {
        IdentifierExpr reference = new(span, name.Name);
        _model.Bind(reference, name);

        if (name switch
            {
                LocalSymbol local => local.Type,
                ParameterSymbol parameter => parameter.Type,
                _ => null,
            } is { } type)
        {
            _model.BindType(reference, type);
        }

        return reference;
    }

    private T Carry<T>(SyntaxNode original, T replacement)
        where T : SyntaxNode
    {
        if (_model.GetSymbol(original) is { } symbol)
        {
            _model.Bind(replacement, symbol);
        }

        if (_model.GetType(original) is { } type)
        {
            _model.BindType(replacement, type);
        }

        if (_model.GetBuiltIn(original) is { } builtIn)
        {
            _model.BindBuiltIn(replacement, builtIn);
        }

        if (_model.GetSettledTest(original) is { } settled)
        {
            _model.SettleTest(replacement, settled);
        }

        return replacement;
    }

    /// <summary>A place taken on a model for one body, before that body has been written.</summary>
    private sealed record Placed(
        Frame? Home,
        ModelSymbol Owner,
        List<Declaration> Members,
        string Name,
        FunctionSymbol Symbol,
        DeclarationModifiers Modifiers);

    /// <summary>Where the names of one run went, and the model they went into.</summary>
    private sealed record Frame(
        ModelSymbol Symbol,
        LocalSymbol Local,
        Dictionary<Symbol, FieldSymbol> Fields,
        List<Declaration> Members,
        VarDeclStmt Creation,
        FieldSymbol? Self)
    {
        /// <summary>The frame around this one, or null where this is the outermost.</summary>
        public Frame? Parent { get; init; }

        /// <summary>The field holding <see cref="Parent"/>, or null where there is none.</summary>
        public FieldSymbol? Up { get; init; }

        /// <summary>
        /// The frame whose function this one was made inside, or null where it was made in the
        /// member's own body. This is what says whether the local holding it is still in scope:
        /// a frame made outside the function being written is reachable only by following
        /// <see cref="Up"/>, because its local belongs to a call that is no longer in hand.
        /// </summary>
        public Frame? DeclaredInside { get; init; }
    }
}
