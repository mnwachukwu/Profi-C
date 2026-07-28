using ProfiC.Compiler.Lexing;

namespace ProfiC.Compiler.Ast;

/// <summary>An operator written before its single operand.</summary>
public enum UnaryOperator
{
    /// <summary>Arithmetic negation, at binding level 8.</summary>
    Negate,

    /// <summary>Logical negation, at binding level 3 — looser than comparison.</summary>
    Not,
}

/// <summary>An operator written between two operands.</summary>
public enum BinaryOperator
{
    Or,
    And,
    Equal,
    NotEqual,
    LessThan,
    GreaterThan,
    LessThanOrEqual,
    GreaterThanOrEqual,
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
        _ => op.ToString(),
    };

    // ---- Binding powers -----------------------------------------------------------------
    // These are the levels in the grammar's precedence table. A left power below the current
    // minimum stops the expression parser; the right power is what it recurses with, and
    // making it one higher than the left is what produces left associativity.

    /// <summary>The binding powers of an infix operator, or null if the token is not one.</summary>
    public static (int Left, int Right)? InfixBindingPower(TokenType type) => type switch
    {
        TokenType.Or => (1, 2),
        TokenType.And => (3, 4),
        TokenType.EqualEqual or TokenType.NotEqual => (6, 7),
        TokenType.LessThan or TokenType.GreaterThan
            or TokenType.LessThanOrEqual or TokenType.GreaterThanOrEqual
            or TokenType.Is or TokenType.As => (8, 9),
        TokenType.Plus or TokenType.Minus => (10, 11),
        TokenType.Star or TokenType.Slash or TokenType.Percent => (12, 13),

        // Exponentiation binds tighter than a leading minus, so "-2 ^ 2" is -(2 ^ 2)
        // as it is in mathematics, and its right power is the lower of the pair, which
        // is what makes it right associative: "2 ^ 3 ^ 2" is 2 ^ (3 ^ 2).
        TokenType.Caret => (15, 14),
        _ => null,
    };

    /// <summary>The binding power of a prefix operator, or null if the token is not one.</summary>
    public static int? PrefixBindingPower(TokenType type) => type switch
    {
        TokenType.Not => 5,
        TokenType.Minus => 14,
        _ => null,
    };

    /// <summary>
    /// The binding power of a postfix operator: a call, an index, or a member access. All
    /// three sit at the tightest level.
    /// </summary>
    public static int? PostfixBindingPower(TokenType type) => type switch
    {
        TokenType.LeftParen or TokenType.LeftBracket or TokenType.Dot => 16,
        _ => null,
    };
}
