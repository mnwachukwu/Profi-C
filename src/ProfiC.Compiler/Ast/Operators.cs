using ProfiC.Compiler.Lexing;

namespace ProfiC.Compiler.Ast;

/// <summary>An operator written before its single operand.</summary>
public enum UnaryOperator
{
    /// <summary>Arithmetic negation, which binds tighter than any arithmetic but not than `^`.</summary>
    Negate,

    /// <summary>Logical negation, which binds looser than a comparison.</summary>
    Not,
}

/// <summary>An operator written between two operands.</summary>
public enum BinaryOperator
{
    Or,
    And,
    BitwiseOr,
    Xor,
    BitwiseAnd,
    Equal,
    NotEqual,
    LessThan,
    GreaterThan,
    LessThanOrEqual,
    GreaterThanOrEqual,
    LeftShift,
    RightShift,
    Add,
    Subtract,
    Multiply,
    Divide,
    Remainder,
    Power,
}

/// <summary>
/// Mapping between operator tokens and operator nodes, together with the binding powers the
/// expression parser climbs. Keeping the table here rather than in the parser means the
/// precedence documented in the grammar has exactly one representation in code.
/// </summary>
public static class Operators
{
    /// <summary>The prefix operator a token introduces, if it is one.</summary>
    public static UnaryOperator? PrefixFrom(TokenType type) => type switch
    {
        TokenType.Minus => UnaryOperator.Negate,
        TokenType.Not => UnaryOperator.Not,
        _ => null,
    };

    /// <summary>The infix operator a token introduces, if it is one.</summary>
    public static BinaryOperator? InfixFrom(TokenType type) => type switch
    {
        TokenType.Or => BinaryOperator.Or,
        TokenType.And => BinaryOperator.And,
        TokenType.EqualEqual => BinaryOperator.Equal,
        TokenType.NotEqual => BinaryOperator.NotEqual,
        TokenType.LessThan => BinaryOperator.LessThan,
        TokenType.GreaterThan => BinaryOperator.GreaterThan,
        TokenType.LessThanOrEqual => BinaryOperator.LessThanOrEqual,
        TokenType.GreaterThanOrEqual => BinaryOperator.GreaterThanOrEqual,
        TokenType.Plus => BinaryOperator.Add,
        TokenType.Minus => BinaryOperator.Subtract,
        TokenType.Star => BinaryOperator.Multiply,
        TokenType.Slash => BinaryOperator.Divide,
        TokenType.Percent => BinaryOperator.Remainder,
        TokenType.Caret => BinaryOperator.Power,

        // 'bitwise' is read with the word after it, so what arrives here is already settled.
        TokenType.Xor => BinaryOperator.Xor,
        TokenType.LeftShift => BinaryOperator.LeftShift,
        TokenType.RightShift => BinaryOperator.RightShift,
        _ => null,
    };

    /// <summary>The source spelling of a unary operator.</summary>
    public static string Spelling(this UnaryOperator op) => op switch
    {
        UnaryOperator.Negate => "-",
        UnaryOperator.Not => "not",
        _ => op.ToString(),
    };

    /// <summary>The source spelling of a binary operator.</summary>
    public static string Spelling(this BinaryOperator op) => op switch
    {
        BinaryOperator.Or => "or",
        BinaryOperator.And => "and",
        BinaryOperator.Equal => "==",
        BinaryOperator.NotEqual => "!=",
        BinaryOperator.LessThan => "<",
        BinaryOperator.GreaterThan => ">",
        BinaryOperator.LessThanOrEqual => "<=",
        BinaryOperator.GreaterThanOrEqual => ">=",
        BinaryOperator.Add => "+",
        BinaryOperator.Subtract => "-",
        BinaryOperator.Multiply => "*",
        BinaryOperator.Divide => "/",
        BinaryOperator.Remainder => "%",
        BinaryOperator.Power => "^",
        BinaryOperator.BitwiseAnd => "bitwise and",
        BinaryOperator.BitwiseOr => "bitwise or",
        BinaryOperator.Xor => "xor",
        BinaryOperator.LeftShift => "leftshift",
        BinaryOperator.RightShift => "rightshift",
        _ => op.ToString(),
    };

    // ---- Binding powers -----------------------------------------------------------------
    // These are the levels in the grammar's precedence table. A left power below the current
    // minimum stops the expression parser; the right power is what it recurses with, and
    // making it one higher than the left is what produces left associativity.

    /// <summary>The binding powers of an infix operator, or null if the token is not one.</summary>
    /// <remarks>
    /// The levels are spaced by twos so that a band can be inserted between two of them
    /// without renumbering the table, which is what adding the bitwise operators cost.
    /// </remarks>
    public static (int Left, int Right)? InfixBindingPower(TokenType type) => type switch
    {
        TokenType.Or => (2, 3),
        TokenType.And => (6, 7),

        // 'bitwise' alone cannot say which level it is on, since the word after it decides.
        // The Pratt loop asks BitwisePower instead; this arm is what lets the loop see that
        // an operator begins here at all, and its level is the loosest of the three.
        TokenType.Bitwise => (10, 11),
        TokenType.Xor => (14, 15),

        TokenType.EqualEqual or TokenType.NotEqual => (22, 23),
        TokenType.LessThan or TokenType.GreaterThan
            or TokenType.LessThanOrEqual or TokenType.GreaterThanOrEqual
            or TokenType.Is or TokenType.As => (26, 27),

        // A shift binds tighter than a comparison and looser than arithmetic, so
        // "x leftshift 1 + 1" shifts by two and "x leftshift 1 < y" compares the result.
        TokenType.LeftShift or TokenType.RightShift => (30, 31),

        TokenType.Plus or TokenType.Minus => (34, 35),
        TokenType.Star or TokenType.Slash or TokenType.Percent => (38, 39),

        // Exponentiation binds tighter than a leading minus, so "-2 ^ 2" is -(2 ^ 2)
        // as it is in mathematics, and its right power is the lower of the pair, which
        // is what makes it right associative: "2 ^ 3 ^ 2" is 2 ^ (3 ^ 2).
        TokenType.Caret => (43, 42),
        _ => null,
    };

    /// <summary>
    /// <para>The binding power of <c>bitwise and</c> or <c>bitwise or</c>, told apart by the
    /// word that follows.</para>
    /// <para>The three bitwise operations sit on three levels, as they do in C#: <c>or</c> is
    /// loosest, then <c>xor</c>, then <c>and</c>. So <c>a bitwise or b bitwise and c</c> is
    /// <c>a bitwise or (b bitwise and c)</c>, and a reader who learned the order once carries
    /// it across.</para>
    /// <para>Written for the parser rather than the table, because the level depends on a
    /// second token and the table sees one.</para>
    /// </summary>
    public static (int Left, int Right) BitwisePower(TokenType following) => following switch
    {
        TokenType.And => (18, 19),
        _ => (10, 11),
    };

    /// <summary>The binding power of a prefix operator, or null if the token is not one.</summary>
    public static int? PrefixBindingPower(TokenType type) => type switch
    {
        // Looser than a comparison, so "not a == b" asks whether they differ rather than
        // negating a first. Tighter than everything that works on bits, which it cannot mix
        // with anyway.
        TokenType.Not => 20,
        TokenType.Minus => 42,
        _ => null,
    };

    /// <summary>
    /// The binding power of a postfix operator: a call, an index, or a member access. All
    /// three sit at the tightest level.
    /// </summary>
    public static int? PostfixBindingPower(TokenType type) => type switch
    {
        TokenType.LeftParen or TokenType.LeftBracket or TokenType.Dot => 46,
        _ => null,
    };
}
