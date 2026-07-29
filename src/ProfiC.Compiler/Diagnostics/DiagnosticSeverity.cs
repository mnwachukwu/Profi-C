namespace ProfiC.Compiler.Diagnostics;

/// <summary>
/// <para>How serious a diagnostic is.</para>
/// <para>Profi-C has exactly two severities. A <c>switch</c> over an enumeration that omits
/// members is the only warning in the language; everything else is an error or is silent.</para>
/// </summary>
public enum DiagnosticSeverity
{
    /// <summary>Does not prevent compilation.</summary>
    Warning,

    /// <summary>Prevents compilation.</summary>
    Error,
}
