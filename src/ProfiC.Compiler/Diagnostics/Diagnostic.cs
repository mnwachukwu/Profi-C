using System.Globalization;
using ProfiC.Compiler.Text;

namespace ProfiC.Compiler.Diagnostics;

/// <summary>
/// <para>One reported problem in one source location.</para>
/// <para>A diagnostic is data. It carries no formatting and writes nothing to a console;
/// rendering belongs to whatever is driving the compiler, which is what keeps a language
/// server possible.</para>
/// <para><c>FixedBy</c> is text that, written over <c>Span</c>, would settle this — or null
/// where nothing so simple would. Only where one substitution does the whole job:
/// <c>&amp;&amp;</c> becomes <c>and</c>, which is a swap, while <c>x += 1</c> becomes
/// <c>x = x + 1</c>, which needs to know what <c>x</c> is and is a rewrite. Held as data for the
/// reason the rest of this is: an editor offering the fix has to have the replacement, and
/// reading it back out of an English sentence would tie the fix to the wording.</para>
/// </summary>
public sealed record Diagnostic(
    DiagnosticDescriptor Descriptor,
    SourceSpan Span,
    string Message,
    SourceText? Source = null,
    string? FixedBy = null)
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
        params object?[] args) =>
        CreateFixable(descriptor, span, source, fixedBy: null, args);

    /// <summary>
    /// <para>The same, for a diagnostic one substitution would settle.</para>
    /// <para><b>Named apart from <see cref="Create"/> rather than overloading it.</b> An overload
    /// taking a string before a <c>params</c> list swallows any call whose first argument is a
    /// single string: the compiler binds it to the replacement, leaves the arguments empty, and
    /// the message goes out with its <c>{0}</c> still in it. Nothing about that fails to compile,
    /// and it happened here.</para>
    /// </summary>
    public static Diagnostic CreateFixable(
        DiagnosticDescriptor descriptor,
        SourceSpan span,
        SourceText? source,
        string? fixedBy,
        params object?[] args)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        string message = args.Length == 0
            ? descriptor.MessageFormat
            : string.Format(CultureInfo.InvariantCulture, descriptor.MessageFormat, args);

        return new Diagnostic(descriptor, span, message, source, fixedBy);
    }

    public override string ToString() =>
        $"{Severity.ToString().ToLowerInvariant()} {Id}: {Message}";
}
