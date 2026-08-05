namespace ProfiC.Compiler.Diagnostics;

/// <summary>
/// <para>The unchanging part of a diagnostic: its identifier, severity, and message
/// template.</para>
/// <para>Identifiers are stable and are what tests, editor tooling, and any future
/// suppression mechanism match on. Message text is free to change without breaking
/// any of them.</para>
/// </summary>
/// <param name="Id">A stable identifier of the form <c>PC</c> followed by four digits.</param>
/// <param name="DefaultSeverity">Severity applied unless something overrides it.</param>
/// <param name="Title">A short description of the rule, independent of any occurrence.</param>
/// <param name="MessageFormat">A composite format string filled in per occurrence.</param>
public sealed record DiagnosticDescriptor(
    string Id,
    DiagnosticSeverity DefaultSeverity,
    string Title,
    string MessageFormat)
{
    /// <summary>
    /// <para>Whether this reports something the program does not use.</para>
    /// <para>An editor shows one of these faded rather than underlined, keeping whatever color
    /// the name already had — so it still reads as the field or the local it is, and reads as
    /// one nothing reaches. That is a different thing to say than "this may be wrong", which is
    /// what an underline says, and it is why severity alone cannot carry it: a local nothing
    /// reads is a warning and a redundant word is an opinion, and both fade.</para>
    /// <para>Not every opinion qualifies. Most say some written token has no effect, which is
    /// exactly this; but one says a loop has nothing to end it, where something is <em>missing</em>
    /// rather than spare, and fading the loop would point at the wrong thing entirely.</para>
    /// </summary>
    public bool Unused { get; init; }
}
