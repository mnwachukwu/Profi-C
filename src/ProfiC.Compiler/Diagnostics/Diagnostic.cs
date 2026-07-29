using System.Globalization;
using ProfiC.Compiler.Text;

namespace ProfiC.Compiler.Diagnostics;

/// <summary>
/// <para>One reported problem in one source location.</para>
/// <para>A diagnostic is data. It carries no formatting and writes nothing to a console;
/// rendering belongs to whatever is driving the compiler, which is what keeps a language
/// server possible.</para>
/// </summary>
public sealed record Diagnostic(
    DiagnosticDescriptor Descriptor,
    SourceSpan Span,
    string Message,
    SourceText? Source = null)
{
    /// <summary>The stable identifier of the rule that produced this diagnostic.</summary>
    public string Id => Descriptor.Id;

    /// <summary>How serious this diagnostic is.</summary>
    public DiagnosticSeverity Severity => Descriptor.DefaultSeverity;

    /// <summary>
    /// The name of the file this was reported in. A compilation spans several files, so a span
    /// alone does not say where a problem is.
    /// </summary>
    public string FileName => Source?.FileName ?? "<input>";

    /// <summary>Formats a diagnostic from its descriptor and the arguments for its message.</summary>
    public static Diagnostic Create(
        DiagnosticDescriptor descriptor,
        SourceSpan span,
        SourceText? source,
        params object?[] args)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        string message = args.Length == 0
            ? descriptor.MessageFormat
            : string.Format(CultureInfo.InvariantCulture, descriptor.MessageFormat, args);

        return new Diagnostic(descriptor, span, message, source);
    }

    public override string ToString() =>
        $"{Severity.ToString().ToLowerInvariant()} {Id}: {Message}";
}
