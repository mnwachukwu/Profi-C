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

    /// <summary>
    /// A string written between triple quotes. Verbatim: no escape is read and no
    /// interpolation is looked for, so whatever is between the quotes is what it holds.
    /// </summary>
    BlockStringLiteral,

    // ---- The pieces of an interpolated string ---------------------------------------------
    //
    // A string holding no interpolation is one StringLiteral, exactly as before, so nothing
    // that never interpolates sees any of these. One that does interpolate is taken apart
    // here rather than left whole and picked over later: the expression in a hole is ordinary
    // Profi-C, and scanning it as ordinary tokens means the parser parses it with the code it
    // already has, every span inside it points at real source, and a mistake in one hole is
    // reported where it was written instead of somewhere in a literal.

    /// <summary>The opening quote of a string that holds at least one interpolation.</summary>
    InterpolatedStringStart,

    /// <summary>A run of ordinary text between the holes.</summary>
    InterpolatedStringText,

    /// <summary>The <c>{{</c> that opens a hole.</summary>
    InterpolationStart,

    /// <summary>
    /// The <c>:</c> and the pattern after it, when a hole says how to format its value.
    /// </summary>
    InterpolationFormat,

    /// <summary>The <c>}}</c> that closes a hole.</summary>
    InterpolationEnd,

    /// <summary>The closing quote.</summary>
    InterpolatedStringEnd,

    // ---- Identifier ---------------------------------------------------------------------

    Identifier,

    // ---- Reserved words, 54 -------------------------------------------------------------
    // Kept alphabetical so that a missing entry is easy to spot against the keyword table.
    // Note that "comment" is reserved but never produces a token: the scanner recognizes it
    // before tokenizing and skips what follows.

    Abstract,
    And,
    As,
    Base,
    Begin,
    Bitwise,
    Boolean,
    Break,
    Case,
    Catch,
    Character,
    Constant,
    Continue,
    Default,

    /// <summary>
    /// <para>Writes the type of a function, where <c>function</c> declares one.</para>
    /// <para>Two words rather than one because a type may follow a type: with only
    /// <c>function</c>, a parser meeting it after a result could not tell a nested type from
    /// the start of a declaration, and a function yielding a function had a type nothing could
    /// write down.</para>
    /// </summary>
    Delegate,

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
    Import,
    In,
    Integer,
    Internal,
    Is,
    Let,
    Model,
    Namespace,
    New,
    Not,
    Or,
    Override,
    Protected,
    Public,
    Real,
    Sealed,
    ShiftLeft,
    ShiftRight,
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
    Xor,
    Yield,

    // ---- Arithmetic operators -----------------------------------------------------------

    Plus,
    Minus,
    Star,
    Slash,
    Percent,
    Caret,

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
