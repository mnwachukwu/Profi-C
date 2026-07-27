namespace ProfiC.Compiler.Diagnostics;

/// <summary>
/// <para>Every diagnostic the compiler can report.</para>
/// <para>Identifiers are allocated in blocks by compilation phase so that a reader can tell
/// from the number alone which part of the compiler objected.</para>
/// </summary>
/// <remarks>
/// <list type="table">
///   <item><term>PFC0001-0099</term><description>Lexical</description></item>
///   <item><term>PFC0100-0199</term><description>Syntax</description></item>
///   <item><term>PFC0200-0299</term><description>Name resolution</description></item>
///   <item><term>PFC0300-0399</term><description>Type checking</description></item>
///   <item><term>PFC0400-0499</term><description>Definite assignment and flow</description></item>
///   <item><term>PFC0500-0599</term><description>Lowering and emit</description></item>
///   <item><term>PFC0900+</term><description>Internal compiler errors</description></item>
/// </list>
/// </remarks>
public static class DiagnosticDescriptors
{
    private static DiagnosticDescriptor Error(string id, string title, string format) =>
        new(id, DiagnosticSeverity.Error, title, format);

    // ---- Lexical, PFC0001 to PFC0099 ----------------------------------------------------

    public static readonly DiagnosticDescriptor UnrecognizedCharacter = Error(
        "PFC0001",
        "Unrecognized character",
        "Unrecognized character '{0}'.");

    public static readonly DiagnosticDescriptor UnterminatedString = Error(
        "PFC0002",
        "Unterminated string literal",
        "Unterminated string literal.");

    public static readonly DiagnosticDescriptor UnterminatedCharacter = Error(
        "PFC0003",
        "Unterminated character literal",
        "Unterminated character literal.");

    public static readonly DiagnosticDescriptor MalformedCharacterLiteral = Error(
        "PFC0004",
        "Malformed character literal",
        "A character literal must contain exactly one character.");

    public static readonly DiagnosticDescriptor UnterminatedBlockComment = Error(
        "PFC0005",
        "Unterminated block comment",
        "Unterminated block comment; expected 'end comment'.");

    /// <summary>
    /// Reported for operators a C# author reaches for that Profi-C does not have. Naming the
    /// replacement is the whole point; the alternative is either a bare "unrecognized
    /// character" or, worse, silently scanning "+=" as two separate tokens.
    /// </summary>
    public static readonly DiagnosticDescriptor NotAnOperator = Error(
        "PFC0006",
        "Not an operator in Profi-C",
        "'{0}' is not an operator in Profi-C. {1}");

    public static readonly DiagnosticDescriptor UnrecognizedEscape = Error(
        "PFC0007",
        "Unrecognized escape sequence",
        "Unrecognized escape sequence '\\{0}'.");

    public static readonly DiagnosticDescriptor MalformedUnicodeEscape = Error(
        "PFC0008",
        "Malformed Unicode escape sequence",
        "A Unicode escape must be '\\u' followed by four hexadecimal digits.");
}
