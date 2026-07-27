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

    /// <summary>The number of diagnostics collected.</summary>
    public int Count => _diagnostics.Count;

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

        Add(Diagnostic.Create(descriptor, span, args));
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

    /// <summary>Returns the diagnostics ordered by source position.</summary>
    public IReadOnlyList<Diagnostic> Sorted() =>
        [.. _diagnostics.OrderBy(d => d.Span.Start.Offset).ThenBy(d => d.Id, StringComparer.Ordinal)];

    public IEnumerator<Diagnostic> GetEnumerator() => _diagnostics.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
