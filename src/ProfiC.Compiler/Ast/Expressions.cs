using ProfiC.Compiler.Lexing;
using ProfiC.Compiler.Text;

namespace ProfiC.Compiler.Ast;

/// <summary>What kind of value a literal denotes.</summary>
public enum LiteralKind
{
    Integer,
    Real,
    Character,
    String,
    Fraction,
    Boolean,
}

/// <summary>
/// <para>A literal value.</para>
/// <para><see cref="Text"/> is the lexeme exactly as written, escapes and all. Decoding it
/// into a value belongs here rather than in the scanner, and happens when the literal is
/// bound.</para>
/// </summary>
public sealed class LiteralExpr(SourceSpan span, LiteralKind kind, string text)
    : Expression(span)
{
    public LiteralKind Kind { get; } = kind;

    /// <summary>The source text of the literal, undecoded.</summary>
    public string Text { get; } = text;

    public override IEnumerable<SyntaxNode> Children => [];

    public override void Accept(SyntaxVisitor visitor) => visitor.VisitLiteralExpr(this);

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
        visitor.VisitLiteralExpr(this);

    /// <summary>The literal kind a token denotes, or null if the token is not a literal.</summary>
    public static LiteralKind? KindFrom(TokenType type) => type switch
    {
        TokenType.IntegerLiteral => LiteralKind.Integer,
        TokenType.RealLiteral => LiteralKind.Real,
        TokenType.CharLiteral => LiteralKind.Character,
        TokenType.StringLiteral => LiteralKind.String,
        TokenType.FractionLiteral => LiteralKind.Fraction,
        TokenType.True or TokenType.False => LiteralKind.Boolean,
        _ => null,
    };
}

/// <summary>
/// A bare name. It can only resolve to a local or a parameter: everything else needs
/// <c>this.</c>, <c>base.</c>, <c>outer.</c>, or a type name in front of it.
/// </summary>
public sealed class IdentifierExpr(SourceSpan span, string name) : Expression(span)
{
    public string Name { get; } = name;

    public override IEnumerable<SyntaxNode> Children => [];

    public override void Accept(SyntaxVisitor visitor) => visitor.VisitIdentifierExpr(this);

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
        visitor.VisitIdentifierExpr(this);
}

/// <summary>Which implicit receiver an expression names.</summary>
public enum ReceiverKind
{
    /// <summary>The instance the enclosing function belongs to.</summary>
    This,

    /// <summary>The parent type, for calling a parent implementation or constructor.</summary>
    Base,
}

/// <summary>
/// <para><c>this</c> or <c>base</c>.</para>
/// <para>There is no third form. A nested model holds no reference to the instance it is
/// nested in, exactly as a C# nested class does not, so a nested model that needs its
/// enclosing instance takes one as a constructor argument.</para>
/// </summary>
public sealed class ReceiverExpr(SourceSpan span, ReceiverKind receiver) : Expression(span)
{
    public ReceiverKind Receiver { get; } = receiver;

    public override string NodeKind => $"{Receiver}Expr";

    public override IEnumerable<SyntaxNode> Children => [];

    public override void Accept(SyntaxVisitor visitor) => visitor.VisitReceiverExpr(this);

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
        visitor.VisitReceiverExpr(this);
}

/// <summary>
/// A parenthesized expression. Kept in the tree rather than discarded, so that the source can
/// be reconstructed and so that a diagnostic can point at the parentheses themselves.
/// </summary>
public sealed class ParenthesizedExpr(SourceSpan span, Expression inner) : Expression(span)
{
    public Expression Inner { get; } = inner;

    public override IEnumerable<SyntaxNode> Children => [Inner];

    public override void Accept(SyntaxVisitor visitor) => visitor.VisitParenthesizedExpr(this);

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
        visitor.VisitParenthesizedExpr(this);
}

/// <summary>A prefix operator applied to one operand.</summary>
public sealed class UnaryExpr(SourceSpan span, UnaryOperator op, Expression operand)
    : Expression(span)
{
    public UnaryOperator Operator { get; } = op;

    public Expression Operand { get; } = operand;

    public override IEnumerable<SyntaxNode> Children => [Operand];

    public override void Accept(SyntaxVisitor visitor) => visitor.VisitUnaryExpr(this);

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
        visitor.VisitUnaryExpr(this);
}

/// <summary>An infix operator applied to two operands.</summary>
public sealed class BinaryExpr(
    SourceSpan span,
    Expression left,
    BinaryOperator op,
    Expression right) : Expression(span)
{
    public Expression Left { get; } = left;

    public BinaryOperator Operator { get; } = op;

    public Expression Right { get; } = right;

    public override IEnumerable<SyntaxNode> Children => [Left, Right];

    public override void Accept(SyntaxVisitor visitor) => visitor.VisitBinaryExpr(this);

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
        visitor.VisitBinaryExpr(this);
}

/// <summary><c>x is Dog</c>, which yields a boolean without producing the value.</summary>
public sealed class TypeTestExpr(SourceSpan span, Expression operand, TypeSyntax targetType)
    : Expression(span)
{
    public Expression Operand { get; } = operand;

    public TypeSyntax TargetType { get; } = targetType;

    public override IEnumerable<SyntaxNode> Children => [Operand, TargetType];

    public override void Accept(SyntaxVisitor visitor) => visitor.VisitTypeTestExpr(this);

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
        visitor.VisitTypeTestExpr(this);
}

/// <summary>
/// <c>x as Dog</c>, which yields <c>Dog?</c> rather than failing. There is no null for it to
/// produce instead, so an optional is the natural result and no new machinery is needed.
/// </summary>
public sealed class TypeCastExpr(SourceSpan span, Expression operand, TypeSyntax targetType)
    : Expression(span)
{
    public Expression Operand { get; } = operand;

    public TypeSyntax TargetType { get; } = targetType;

    public override IEnumerable<SyntaxNode> Children => [Operand, TargetType];

    public override void Accept(SyntaxVisitor visitor) => visitor.VisitTypeCastExpr(this);

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
        visitor.VisitTypeCastExpr(this);
}

/// <summary>
/// <para><c>if c then a else b</c>, the value-producing conditional.</para>
/// <para>The <c>else</c> is mandatory and both branches must have the same type. This fills
/// the role of a ternary, which the language does not have because <c>a ? b : c</c> has no
/// reading-aloud form.</para>
/// </summary>
public sealed class IfExpr(
    SourceSpan span,
    Expression condition,
    Expression thenValue,
    Expression elseValue) : Expression(span)
{
    public Expression Condition { get; } = condition;

    public Expression ThenValue { get; } = thenValue;

    public Expression ElseValue { get; } = elseValue;

    public override IEnumerable<SyntaxNode> Children => [Condition, ThenValue, ElseValue];

    public override void Accept(SyntaxVisitor visitor) => visitor.VisitIfExpr(this);

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
        visitor.VisitIfExpr(this);
}

/// <summary>
/// A set literal, written in braces. Because a brace can never open a block, there is no
/// ambiguity between this and a statement block.
/// </summary>
public sealed class CollectionExpr(SourceSpan span, IReadOnlyList<Expression> elements)
    : Expression(span)
{
    public IReadOnlyList<Expression> Elements { get; } = elements;

    public override IEnumerable<SyntaxNode> Children => Elements;

    public override void Accept(SyntaxVisitor visitor) => visitor.VisitCollectionExpr(this);

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
        visitor.VisitCollectionExpr(this);
}

/// <summary>
/// Construction of a model or a structure. Sets are written as literals and are never
/// allocated with a size, so <c>new</c> never takes a length.
/// </summary>
public sealed class NewExpr(
    SourceSpan span,
    string typeName,
    IReadOnlyList<Expression> arguments) : Expression(span)
{
    public string TypeName { get; } = typeName;

    public IReadOnlyList<Expression> Arguments { get; } = arguments;

    public override IEnumerable<SyntaxNode> Children => Arguments;

    public override void Accept(SyntaxVisitor visitor) => visitor.VisitNewExpr(this);

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
        visitor.VisitNewExpr(this);
}

/// <summary>A call. The callee is an arbitrary expression, since a function is a value.</summary>
public sealed class CallExpr(
    SourceSpan span,
    Expression callee,
    IReadOnlyList<Expression> arguments) : Expression(span)
{
    public Expression Callee { get; } = callee;

    public IReadOnlyList<Expression> Arguments { get; } = arguments;

    public override IEnumerable<SyntaxNode> Children =>
        new SyntaxNode[] { Callee }.Concat(Arguments);

    public override void Accept(SyntaxVisitor visitor) => visitor.VisitCallExpr(this);

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
        visitor.VisitCallExpr(this);
}

/// <summary>Indexing into a set or a string.</summary>
public sealed class IndexExpr(SourceSpan span, Expression receiver, Expression index)
    : Expression(span)
{
    public Expression Receiver { get; } = receiver;

    public Expression Index { get; } = index;

    public override IEnumerable<SyntaxNode> Children => [Receiver, Index];

    public override void Accept(SyntaxVisitor visitor) => visitor.VisitIndexExpr(this);

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
        visitor.VisitIndexExpr(this);
}

/// <summary>Member access, as in <c>this.count</c> or <c>Math.Sqrt</c>.</summary>
public sealed class MemberExpr(SourceSpan span, Expression receiver, string memberName)
    : Expression(span)
{
    public Expression Receiver { get; } = receiver;

    public string MemberName { get; } = memberName;

    public override IEnumerable<SyntaxNode> Children => [Receiver];

    public override void Accept(SyntaxVisitor visitor) => visitor.VisitMemberExpr(this);

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
        visitor.VisitMemberExpr(this);
}

/// <summary>
/// <para>A function value, in either of its two forms: a block bodied
/// <c>function(…) … end function</c>, or an arrow <c>(…) =&gt; expression</c>.</para>
/// <para>Exactly one of <see cref="Body"/> and <see cref="ExpressionBody"/> is set.</para>
/// </summary>
public sealed class LambdaExpr : Expression
{
    private LambdaExpr(
        SourceSpan span,
        IReadOnlyList<ParameterDecl> parameters,
        IReadOnlyList<Statement>? body,
        Expression? expressionBody) : base(span)
    {
        Parameters = parameters;
        Body = body;
        ExpressionBody = expressionBody;
    }

    public IReadOnlyList<ParameterDecl> Parameters { get; }

    /// <summary>The statements of a block-bodied lambda, or null for the arrow form.</summary>
    public IReadOnlyList<Statement>? Body { get; }

    /// <summary>The expression of an arrow lambda, or null for the block form.</summary>
    public Expression? ExpressionBody { get; }

    /// <summary>True for the <c>(a, b) =&gt; a - b</c> form.</summary>
    public bool IsExpressionBodied => ExpressionBody is not null;

    /// <summary>Creates the <c>function(…) … end function</c> form.</summary>
    public static LambdaExpr Block(
        SourceSpan span,
        IReadOnlyList<ParameterDecl> parameters,
        IReadOnlyList<Statement> body) => new(span, parameters, body, null);

    /// <summary>Creates the <c>(…) =&gt; expression</c> form.</summary>
    public static LambdaExpr Arrow(
        SourceSpan span,
        IReadOnlyList<ParameterDecl> parameters,
        Expression body) => new(span, parameters, null, body);

    public override IEnumerable<SyntaxNode> Children =>
        Parameters.Concat<SyntaxNode>(Body ?? []).Concat(NonNull(ExpressionBody));

    public override void Accept(SyntaxVisitor visitor) => visitor.VisitLambdaExpr(this);

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
        visitor.VisitLambdaExpr(this);
}
