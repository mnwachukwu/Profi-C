namespace ProfiC.Compiler.Lexing;

/// <summary>
/// Represents the category of a scanned token.
/// </summary>
public enum TokenType
{
    // Literals
    IntegerLiteral,
    RealLiteral,
    CharLiteral,
    StringLiteral,
    FractionLiteral,

    // Composite literals; the lexer emits their signals and the parser assembles them.
    ArrayLiteral,

    // Identifier
    Identifier,

    // Types
    Integer,
    Real,
    Character,
    Bool,
    String,

    // Keywords
    If,
    Else,
    For,
    While,
    Yield,
    Let,
    Write,
    Read,
    Function,
    Model,
    Break,
    Continue,
    Begin,
    End,

    // Boolean literals
    True,
    False,

    // Arithmetic operators
    Plus,
    Minus,
    Star,
    Slash,
    Percent,

    // Comparison operators
    EqualEqual,
    NotEqual,
    LessThan,
    GreaterThan,
    LessThanOrEqual,
    GreaterThanOrEqual,

    // Assignment
    Equal,

    // Boolean operators (keyword-driven: "or", "and", "not")
    Or,
    And,
    Not,

    // Fraction pipe; the "|" character that signals a fraction such as 3|4
    Pipe,

    // Punctuation
    LeftParen,
    RightParen,
    LeftBrace,
    RightBrace,
    LeftBracket,
    RightBracket,
    Comma,
    Semicolon,
    Dot,

    // Quote delimiters; consumed by character and string literals, not emitted by lexer
    Quote,
    DoubleQuote,
}
