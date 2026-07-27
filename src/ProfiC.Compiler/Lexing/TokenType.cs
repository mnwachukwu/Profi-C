namespace ProfiC.Compiler.Lexing;

/// <summary>
/// <para>The category of a scanned token.</para>
/// <para>There is one member per reserved word, so the keyword table and this enum must
/// stay in step; a test asserts that they do.</para>
/// </summary>
public enum TokenType
{
    // ---- Literals -----------------------------------------------------------------------

    IntegerLiteral,
    RealLiteral,
    CharLiteral,
    StringLiteral,
    FractionLiteral,

    // ---- Identifier ---------------------------------------------------------------------

    Identifier,

    // ---- Reserved words, 55 -------------------------------------------------------------
    // Kept alphabetical so that a missing entry is easy to spot against the keyword table.
    // Note that "comment" is reserved but never produces a token: the scanner recognizes it
    // before tokenizing and skips what follows.

    Abstract,
    And,
    As,
    Base,
    Begin,
    Boolean,
    Break,
    Case,
    Catch,
    Character,
    Constant,
    Continue,
    Default,
    Each,
    Else,
    End,
    Enumeration,
    Extends,
    False,
    Finally,
    For,
    Fraction,
    Function,
    Global,
    If,
    In,
    Integer,
    Is,
    Let,
    Model,
    Namespace,
    New,
    Not,
    Or,
    Outer,
    Override,
    Protected,
    Public,
    Real,
    Sealed,
    Step,
    String,
    Structure,
    Switch,
    Then,
    This,
    Throw,
    To,
    True,
    Try,
    Until,
    Using,
    Virtual,
    While,
    Yield,

    // ---- Arithmetic operators -----------------------------------------------------------

    Plus,
    Minus,
    Star,
    Slash,
    Percent,

    // ---- Comparison operators -----------------------------------------------------------

    EqualEqual,
    NotEqual,
    LessThan,
    GreaterThan,
    LessThanOrEqual,
    GreaterThanOrEqual,

    // ---- Assignment ---------------------------------------------------------------------

    Equal,

    // ---- Punctuation and other symbols --------------------------------------------------

    /// <summary>The "|" that separates a fraction's numerator and denominator.</summary>
    Pipe,

    /// <summary>The "?" optional type suffix.</summary>
    Question,

    /// <summary>The ":" that ends a switch case label.</summary>
    Colon,

    /// <summary>The "=>" of an expression lambda.</summary>
    Arrow,

    LeftParen,
    RightParen,
    LeftBrace,
    RightBrace,
    LeftBracket,
    RightBracket,
    Comma,
    Semicolon,
    Dot,

    // ---- End of input -------------------------------------------------------------------

    /// <summary>
    /// Always the final token. Its presence lets the parser look ahead unconditionally
    /// instead of bounds-checking at every call site, and gives "unexpected end of file"
    /// a real position to point at.
    /// </summary>
    EndOfFile,
}
