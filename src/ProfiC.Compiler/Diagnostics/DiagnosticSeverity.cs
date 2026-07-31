namespace ProfiC.Compiler.Diagnostics;

/// <summary>
/// <para>How serious a diagnostic is.</para>
/// <para>The three are a ladder, and what separates them is how much is known about what the
/// program means. An error is reported where the meaning is genuinely unpredictable. A warning
/// is reported where the meaning is clear and unlikely to be what was intended: a
/// <c>switch</c> that omits members, a test whose answer is fixed, code nothing reaches. An
/// opinion is reported where the meaning is clear, intended and correct, and the language would
/// still write it differently.</para>
/// <para>Severity is the word for this rather than class, since the language spells its own
/// reference type <c>model</c> to keep <c>class</c> out of a beginner's vocabulary.</para>
/// </summary>
public enum DiagnosticSeverity
{
    /// <summary>
    /// Does not prevent compilation. The program is correct and does what its author meant;
    /// the language has a view about how it is written.
    /// </summary>
    Opinion,

    /// <summary>Does not prevent compilation.</summary>
    Warning,

    /// <summary>Prevents compilation.</summary>
    Error,
}
