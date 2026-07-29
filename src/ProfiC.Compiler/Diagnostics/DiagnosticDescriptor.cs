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
    string MessageFormat);
