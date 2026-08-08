using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Text;

namespace ProfiC.Compiler.Semantics;

/// <summary>
/// <para>Rewrites a checked tree into a simpler one, so that later phases have fewer shapes
/// to handle.</para>
/// <para>Two jobs. Conversions the program did not write become real nodes, because the type
/// checker is the only pass that ever knew where they belonged. And <c>loop each</c> becomes an
/// index loop, so that iteration is implemented once here rather than separately in the
/// interpreter and again in the emitter.</para>
/// <para>Anything this pass builds is registered in the same semantic model as the tree it
/// came from, so nothing downstream meets a node it knows nothing about.</para>
/// </summary>
public sealed class Lowering
{
    private readonly SemanticModel _model;
    private int _temporaries;

    private Lowering(SemanticModel model) => _model = model;

    /// <summary>Lowers every file of a checked compilation.</summary>
    public static IReadOnlyList<CompilationUnit> Lower(
        IReadOnlyList<CompilationUnit> units,
        SemanticModel model)
    {
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(model);

        Lowering lowering = new(model);

        return [.. units.Select(unit => new CompilationUnit(
            unit.Span,
            unit.Usings,
            unit.Imports,
            [.. unit.Declarations.Select(lowering.LowerDeclaration)],
            unit.Source))];
    }

    /// <summary>Lowers one file, which is a compilation of one.</summary>
    public static CompilationUnit Lower(CompilationUnit unit, SemanticModel model)
    {
        ArgumentNullException.ThrowIfNull(unit);

        return Lower([unit], model)[0];
    }

    // ---- Declarations ---------------------------------------------------------------------

    private Declaration LowerDeclaration(Declaration declaration)
    {
        switch (declaration)
        {
            case NamespaceDecl namespaceDecl:
                return Carry(declaration, new NamespaceDecl(
                    namespaceDecl.Span,
                    namespaceDecl.Name,
                    [.. namespaceDecl.Declarations.Select(LowerDeclaration)],
                    namespaceDecl.IsFileScoped));

            case ModelDecl model:
                return Carry(declaration, new ModelDecl(
                    model.Span,
                    model.Modifiers,
                    model.Name,
                    model.BaseTypeName,
                    [.. model.Members.Select(LowerDeclaration)]));

            case StructureDecl structure:
                return Carry(declaration, new StructureDecl(
                    structure.Span,
                    structure.Modifiers,
                    structure.Name,
                    [.. structure.Members.Select(LowerDeclaration)]));

            case FunctionDecl function:
                return Carry(declaration, new FunctionDecl(
                    function.Span,
                    function.Modifiers,
                    function.ReturnType,
                    function.Name,
                    function.Parameters,
                    function.Body is { } body ? LowerStatements(body) : null));

            case FieldDecl field:
                return Carry(declaration, new FieldDecl(
                    field.Span,
                    field.Modifiers,
                    field.Type,
                    field.Name,
                    field.Initializer is null ? null : LowerExpression(field.Initializer)));

            default:
                return declaration;
        }
    }

    // ---- Statements -----------------------------------------------------------------------

    private List<Statement> LowerStatements(IReadOnlyList<Statement> statements) =>
        [.. statements.Select(LowerStatement)];

    private Statement LowerStatement(Statement statement)
    {
        switch (statement)
        {
            case ForEachStmt loop:
                return LowerForEach(loop);

            case BlockStmt block:
                return new BlockStmt(block.Span, LowerStatements(block.Statements));

            case VarDeclStmt declaration:
                return Carry(declaration, new VarDeclStmt(
                    declaration.Span,
                    declaration.Type,
                    declaration.Name,
                    declaration.Initializer is null ? null : LowerExpression(declaration.Initializer),
                    declaration.IsConstant));

            case LocalDeclStmt { Declaration: FunctionDecl function } local:
                return new LocalDeclStmt(local.Span, LowerDeclaration(function));

            case IfStmt branch:
                return new IfStmt(
                    branch.Span,
                    LowerExpression(branch.Condition),
                    LowerStatements(branch.ThenBody),
                    [.. branch.ElseIfClauses.Select(c => new ElseIfClause(
                        c.Span, LowerExpression(c.Condition), LowerStatements(c.Body)))],
                    branch.ElseBody is null ? null : LowerStatements(branch.ElseBody));

            case WhileStmt loop:
                return new WhileStmt(
                    loop.Span, LowerExpression(loop.Condition), LowerStatements(loop.Body));

            case LoopUntilStmt loop:
                return new LoopUntilStmt(
                    loop.Span, LowerStatements(loop.Body), LowerExpression(loop.Condition));

            case LoopForeverStmt loop:
                return new LoopForeverStmt(loop.Span, LowerStatements(loop.Body));

            case ForStmt loop:
                return Carry(loop, new ForStmt(
                    loop.Span,
                    loop.VariableName,
                    LowerExpression(loop.Start),
                    LowerExpression(loop.Bound),
                    loop.IsInclusive,
                    loop.Step is null ? null : LowerExpression(loop.Step),
                    LowerStatements(loop.Body)));

            case SwitchStmt switchStmt:
                return new SwitchStmt(
                    switchStmt.Span,
                    LowerExpression(switchStmt.Subject),
                    [.. switchStmt.Cases.Select(c => new CaseGroup(
                        c.Span,
                        [.. c.Labels.Select(LowerExpression)],
                        LowerStatements(c.Body)))],
                    switchStmt.DefaultBody is null ? null : LowerStatements(switchStmt.DefaultBody));

            case TryStmt tryStmt:
                return new TryStmt(
                    tryStmt.Span,
                    LowerStatements(tryStmt.Body),
                    [.. tryStmt.Catches.Select(c => Carry(c, new CatchClause(
                        c.Span, c.ExceptionType, c.VariableName, LowerStatements(c.Body))))],
                    tryStmt.FinallyBody is null ? null : LowerStatements(tryStmt.FinallyBody));

            case ThrowStmt throwStmt:
                return new ThrowStmt(throwStmt.Span, LowerExpression(throwStmt.Exception));

            case YieldStmt yieldStmt:
                return new YieldStmt(
                    yieldStmt.Span,
                    yieldStmt.Value is null ? null : LowerExpression(yieldStmt.Value));

            case ExpressionStmt expression:
                return new ExpressionStmt(expression.Span, LowerExpression(expression.Expression));

            case AssignmentStmt assignment:
                return new AssignmentStmt(
                    assignment.Span,
                    LowerExpression(assignment.Target),
                    LowerExpression(assignment.Value));

            default:
                return statement;
        }
    }

    /// <summary>
    /// <para>Rewrites <c>loop each x in sequence</c> as an index loop.</para>
    /// <para>The sequence is evaluated once into a temporary, which matters because the
    /// expression may have effects and must not run per iteration. The element is declared
    /// <em>inside</em> the loop body, which is what gives each iteration a fresh binding and
    /// removes the capture trap that a shared loop variable creates.</para>
    /// <para>A string works unchanged: it answers <c>Count()</c> and indexes to characters,
    /// exactly as a set does, which is why its members were made to mirror a set's.</para>
    /// </summary>
    private Statement LowerForEach(ForEachStmt loop)
    {
        SourceSpan span = loop.Span;

        TypeSymbol sequenceType = _model.GetType(loop.Sequence) ?? ErrorType.Instance;
        TypeSymbol elementType =
            (_model.GetSymbol(loop) as LocalSymbol)?.Type ?? ErrorType.Instance;

        // let <source> = <sequence>;
        LocalSymbol source = NewTemporary("source", sequenceType);
        VarDeclStmt sourceDecl = new(span, null, source.Name, LowerExpression(loop.Sequence), false);
        _model.Bind(sourceDecl, source);

        // for integer <index> = 0 until <source>.Count()
        LocalSymbol index = NewTemporary("index", PrimitiveType.Integer);

        IdentifierExpr sourceRef = Reference(span, source);
        MemberExpr countMember = new(span, sourceRef, "Count");
        _model.BindType(countMember, PrimitiveType.Integer);

        // Which Count this is depends on what is being iterated, and nothing later can work
        // that out: the synthesized node was never seen by the type checker.
        _model.BindBuiltIn(
            countMember,
            sequenceType is SetType ? BuiltInId.SetCount : BuiltInId.StringCount);

        CallExpr count = new(span, countMember, []);
        _model.BindType(count, PrimitiveType.Integer);

        // let <count> = <source>.Count();
        //
        // Held rather than asked for again on every turn. A range loop reads its bound each
        // time, and leaving the call there would make "loop each" follow whatever the sequence
        // grew to mid-loop; a sequence is taken as it stands when the loop begins. Writing the
        // snapshot into the lowered tree is what makes that visible rather than a rule.
        LocalSymbol limit = NewTemporary("count", PrimitiveType.Integer);
        VarDeclStmt limitDecl = new(span, null, limit.Name, count, false);
        _model.Bind(limitDecl, limit);

        LiteralExpr zero = new(span, LiteralKind.Integer, "0");
        _model.BindType(zero, PrimitiveType.Integer);

        // let x = <source>[<index>];
        //
        // Inferred rather than written, because "loop each" never states the element type;
        // the type it infers is the one the checker already worked out.
        IndexExpr element = new(span, Reference(span, source), Reference(span, index));
        _model.BindType(element, elementType);

        VarDeclStmt elementDecl = new(span, null, loop.VariableName, element, isConstant: false);

        if (_model.GetSymbol(loop) is { } elementSymbol)
        {
            _model.Bind(elementDecl, elementSymbol);
        }

        ForStmt indexLoop = new(
            span,
            index.Name,
            zero,
            Reference(span, limit),
            isInclusive: false,
            step: null,
            body: [elementDecl, .. LowerStatements(loop.Body)]);

        _model.Bind(indexLoop, index);

        // The loop is marked as a walk, which is what lets the sequence refuse to be changed
        // while it runs. Nothing else in the lowered tree says a walk is happening: by this
        // point a "loop each" is an index loop like any other.
        WalkStmt walk = new(span, Reference(span, source), indexLoop);

        // The temporaries live in a block of their own, so none escapes the loop.
        return new BlockStmt(span, [sourceDecl, limitDecl, walk]);
    }

    // ---- Expressions ------------------------------------------------------------------------

    /// <summary>
    /// Lowers an expression, then wraps it if the type checker recorded that a conversion
    /// belongs here.
    /// </summary>
    private Expression LowerExpression(Expression expression)
    {
        Expression lowered = LowerExpressionCore(expression);

        // In order, since a value may have two things to do and the second is written against
        // what the first produced — widening an integer to a real before wrapping the real.
        foreach ((ConversionOperation operation, TypeSymbol target)
                 in _model.GetConversion(expression))
        {
            ConversionExpr conversion = new(expression.Span, lowered, operation);

            // The conversion produces the target type, not the type the operand had.
            _model.BindType(conversion, target);

            lowered = conversion;
        }

        return lowered;
    }

    private Expression LowerExpressionCore(Expression expression)
    {
        switch (expression)
        {
            case ParenthesizedExpr parenthesized:
                // Parentheses said what to do first; the tree already records that, so they
                // are dropped rather than carried into every later pass.
                return LowerExpression(parenthesized.Inner);

            case InterpolatedStringExpr interpolated:
                return LowerInterpolatedString(interpolated);

            case UnaryExpr unary:
                return Carry(unary, new UnaryExpr(
                    unary.Span, unary.Operator, LowerExpression(unary.Operand)));

            case BinaryExpr binary:
                return LowerOrderedComparison(binary)
                       ?? Carry(binary, new BinaryExpr(
                           binary.Span,
                           LowerExpression(binary.Left),
                           binary.Operator,
                           LowerExpression(binary.Right)));

            case TypeTestExpr test:
                return Carry(test, new TypeTestExpr(
                    test.Span, LowerExpression(test.Operand), test.TargetType));

            case TypeCastExpr cast:
                return Carry(cast, new TypeCastExpr(
                    cast.Span, LowerExpression(cast.Operand), cast.TargetType));

            case IfExpr conditional:
                return Carry(conditional, new IfExpr(
                    conditional.Span,
                    LowerExpression(conditional.Condition),
                    LowerExpression(conditional.ThenValue),
                    LowerExpression(conditional.ElseValue)));

            case CollectionExpr collection:
                return Carry(collection, new CollectionExpr(
                    collection.Span, [.. collection.Elements.Select(LowerExpression)]));

            case NewExpr construction:
                return Carry(construction, new NewExpr(
                    construction.Span,
                    construction.TypeName,
                    [.. construction.Arguments.Select(LowerExpression)]));

            case CallExpr call:
                return Carry(call, new CallExpr(
                    call.Span,
                    LowerExpression(call.Callee),
                    [.. call.Arguments.Select(LowerExpression)]));

            case IndexExpr index:
                return Carry(index, new IndexExpr(
                    index.Span, LowerExpression(index.Receiver), LowerExpression(index.Index)));

            case MemberExpr member:
                return Carry(member, new MemberExpr(
                    member.Span, LowerExpression(member.Receiver), member.MemberName));

            case LambdaExpr lambda:
                return Carry(lambda, lambda.IsExpressionBodied
                    ? LambdaExpr.Inline(lambda.Span, lambda.Parameters,
                                       LowerExpression(lambda.ExpressionBody!))
                    : LambdaExpr.Block(lambda.Span, lambda.Parameters,
                                       LowerStatements(lambda.Body!)));

            default:
                return expression;
        }
    }

    // ---- Building new nodes -------------------------------------------------------------------

    /// <summary>Copies what was known about a node onto the node that replaces it.</summary>
    /// <summary>
    /// <para>Turns a string with holes into the concatenation it means.</para>
    /// <para><c>"a {{x}} b"</c> becomes <c>"a " + x + " b"</c>, and a hole that says how to
    /// format itself becomes <c>x.Format("F2")</c> first. Both are things the language already
    /// had, so nothing downstream learns a new shape: the interpreter runs the result with the
    /// code it uses for any other sum, and the emitter will too.</para>
    /// <para>Empty runs are dropped rather than added as empty strings — every literal here is
    /// one the source did not write, and leaving out the ones that say nothing keeps the
    /// lowered tree readable.</para>
    /// </summary>
    private Expression LowerInterpolatedString(InterpolatedStringExpr interpolated)
    {
        Expression? built = null;

        for (int i = 0; i < interpolated.Texts.Count; i++)
        {
            if (interpolated.Texts[i].Length > 0)
            {
                built = Append(built, TextLiteral(interpolated, interpolated.Texts[i]));
            }

            if (i < interpolated.Holes.Count)
            {
                built = Append(built, LowerHole(interpolated.Holes[i]));
            }
        }

        // A string with nothing in it at all, which is still a string.
        return built ?? TextLiteral(interpolated, string.Empty);

        Expression Append(Expression? left, Expression right)
        {
            if (left is null)
            {
                return right;
            }

            BinaryExpr sum = new(interpolated.Span, left, BinaryOperator.Add, right);
            _model.BindType(sum, PrimitiveType.String);
            return sum;
        }
    }

    /// <summary>
    /// <para>One hole, lowered to something that is already a string.</para>
    /// <para>Asking the value for its text here rather than leaving it to the <c>+</c> is what
    /// keeps this pass honest: a <c>+</c> that joins a number to a string only works because
    /// the checker recorded a conversion on the operand, and a tree built after the checker
    /// has run carries no such record. Calling <c>ToString</c> outright needs none, since the
    /// operands are then strings on both sides.</para>
    /// <para>A hole that named a pattern calls <c>Format</c> instead, which yields a string
    /// for the same reason.</para>
    /// </summary>
    private Expression LowerHole(InterpolationPart hole)
    {
        TypeSymbol? held = _model.GetType(hole.Value);
        Expression value = LowerExpression(hole.Value);

        string name = hole.Format is null ? "ToString" : "Format";

        IReadOnlyList<Expression> arguments = hole.Format is null
            ? []
            : [Text(hole, hole.Format)];

        MemberExpr member = new(hole.Span, value, name);
        CallExpr call = new(hole.Span, member, arguments);

        _model.BindType(member, PrimitiveType.String);
        _model.BindType(call, PrimitiveType.String);

        if (held is not null
            && BuiltInMembers.FindAll(held, name) is [{ Id: { } id }, ..])
        {
            _model.BindBuiltIn(member, id);
        }

        return call;

        Expression Text(SyntaxNode at, string text)
        {
            LiteralExpr literal = new(at.Span, LiteralKind.String, Quoted(text));
            _model.BindType(literal, PrimitiveType.String);
            return literal;
        }
    }

    /// <summary>
    /// <para>Comparing two values of a type that orders itself, as the <c>CompareTo</c> it
    /// stands for: <c>a &lt; b</c> becomes <c>a.CompareTo(b) &lt; 0</c>.</para>
    /// <para>Done here rather than in either back end, because the operator is a spelling
    /// rather than an operation. There is no instruction that compares two moments, and both
    /// engines already know how to call <c>CompareTo</c> — so lowering leaves nothing for
    /// either of them to learn, and no way for them to learn it differently.</para>
    /// <para>The operator is kept and its right side becomes a zero, so the four
    /// relations need no cases of their own: what <c>&lt;=</c> means against zero is what it
    /// meant against the other value.</para>
    /// </summary>
    private Expression? LowerOrderedComparison(BinaryExpr binary)
    {
        if (binary.Operator is not (BinaryOperator.LessThan or BinaryOperator.GreaterThan
                or BinaryOperator.LessThanOrEqual or BinaryOperator.GreaterThanOrEqual))
        {
            return null;
        }

        if (_model.GetType(binary.Left) is not { } held || !BuiltInMembers.IsOrdered(held))
        {
            return null;
        }

        MemberExpr member = new(binary.Span, LowerExpression(binary.Left), "CompareTo");
        CallExpr call = new(binary.Span, member, [LowerExpression(binary.Right)]);

        _model.BindType(member, PrimitiveType.Integer);
        _model.BindType(call, PrimitiveType.Integer);

        if (BuiltInMembers.FindAll(held, "CompareTo") is [{ Id: { } id }, ..])
        {
            _model.BindBuiltIn(member, id);
        }

        LiteralExpr zero = new(binary.Span, LiteralKind.Integer, "0");
        _model.BindType(zero, PrimitiveType.Integer);

        BinaryExpr against = new(binary.Span, call, binary.Operator, zero);
        _model.BindType(against, PrimitiveType.Boolean);

        return against;
    }

    /// <summary>
    /// A run of text as the literal it stands for. The lexeme is rebuilt rather than sliced
    /// out of the source, because the text between two holes is not a literal anybody wrote
    /// and has no quotes of its own to keep.
    /// </summary>
    private LiteralExpr TextLiteral(SyntaxNode at, string text)
    {
        LiteralExpr literal = new(at.Span, LiteralKind.String, Quoted(text));
        _model.BindType(literal, PrimitiveType.String);
        return literal;
    }

    private static string Quoted(string text) =>
        "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal)
                   .Replace("\"", "\\\"", StringComparison.Ordinal)
             + "\"";

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

        // Which member the language provides a name resolved to is a decision only the type
        // checker can make, so it travels with the node rather than being worked out again
        // from a tree that no longer carries the receiver's declared type.
        if (_model.GetBuiltIn(original) is { } builtIn)
        {
            _model.BindBuiltIn(replacement, builtIn);
        }

        // A type test the types already answered travels with the node too. The back end could
        // not work it out again: a set does not carry its element type, nor a function its
        // signature.
        if (_model.GetSettledTest(original) is { } settled)
        {
            _model.SettleTest(replacement, settled);
        }

        return replacement;
    }

    /// <summary>
    /// Creates a variable the program did not write. The name uses a character no identifier
    /// may contain, so a synthesized name can never collide with one someone chose.
    /// </summary>
    private LocalSymbol NewTemporary(string purpose, TypeSymbol type) =>
        new($"<{purpose}${_temporaries++}>", type, isConstant: false);

    private IdentifierExpr Reference(SourceSpan span, LocalSymbol local)
    {
        IdentifierExpr reference = new(span, local.Name);
        _model.Bind(reference, local);
        _model.BindType(reference, local.Type);
        return reference;
    }

    private static NamedTypeSyntax IntegerTypeSyntax(SourceSpan span) => new(span, "integer");
}
