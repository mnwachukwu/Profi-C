using System.Collections;
using ProfiC.Compiler.Text;

namespace ProfiC.Compiler.Diagnostics;

/// <summary>
/// <para>Collects the diagnostics produced across a compilation.</para>
/// <para>One bag is shared by every phase rather than each phase owning its own, so that
/// nothing has to merge them at a phase boundary.</para>
/// </summary>
public sealed class DiagnosticBag : IReadOnlyCollection<Diagnostic>
{
    /// <summary>
    /// Reporting stops after this many diagnostics. A file that is badly malformed, or not
    /// Profi-C at all, would otherwise produce a diagnostic per character.
    /// </summary>
    public const int MaximumDiagnostics = 100;

    private readonly List<Diagnostic> _diagnostics = [];

    private SourceText? _source;

    /// <summary>The number of diagnostics collected.</summary>
    public int Count => _diagnostics.Count;

    /// <summary>
    /// <para>Marks the file that reports belong to until the returned scope is disposed.</para>
    /// <para>A pass walks one file at a time, so which file it is on is a property of where the
    /// pass has reached rather than of each report. Scoping it here keeps every reporting site
    /// unchanged now that a compilation spans several files.</para>
    /// </summary>
    public FileScope InFile(SourceText source)
    {
        ArgumentNullException.ThrowIfNull(source);

        FileScope scope = new(this, _source);
        _source = source;
        return scope;
    }

    /// <summary>Restores the previously reported-in file when disposed.</summary>
    public readonly struct FileScope(DiagnosticBag bag, SourceText? previous) : IDisposable
    {
        public void Dispose() => bag._source = previous;
    }

    /// <summary>True once any error has been reported. Warnings do not set this.</summary>
    public bool HasErrors { get; private set; }

    /// <summary>True once the cap has been reached and reporting has stopped.</summary>
    public bool IsFull => _diagnostics.Count >= MaximumDiagnostics;

    /// <summary>Reports a diagnostic. Ignored once the cap is reached.</summary>
    public void Report(DiagnosticDescriptor descriptor, SourceSpan span, params object?[] args)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        if (IsFull)
        {
            return;
        }

        Add(Diagnostic.Create(descriptor, span, _source, args));
    }

    /// <summary>Adds an already-constructed diagnostic. Ignored once the cap is reached.</summary>
    public void Add(Diagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        if (IsFull)
        {
            return;
        }

        _diagnostics.Add(diagnostic);

        if (diagnostic.Severity == DiagnosticSeverity.Error)
        {
            HasErrors = true;
        }
    }

    /// <summary>
    /// Returns the diagnostics ordered by file, then by position within it. With one file this
    /// is position order, as it was before a compilation could span several.
    /// </summary>
    public IReadOnlyList<Diagnostic> Sorted() =>
        [.. _diagnostics
            .OrderBy(d => d.FileName, StringComparer.Ordinal)
            .ThenBy(d => d.Span.Start.Offset)
            .ThenBy(d => d.Id, StringComparer.Ordinal)];

    public IEnumerator<Diagnostic> GetEnumerator() => _diagnostics.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
