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

    private readonly List<Suppression> _suppressions = [];

    private IReadOnlyList<Diagnostic>? _visible;

    private bool _reportedUnused;

    private SourceText? _source;

    /// <summary>The number of diagnostics anything will see.</summary>
    public int Count => Visible().Count;

    /// <summary>
    /// <para>The number of diagnostics reported, before suppression removes any.</para>
    /// <para>For a pass asking whether it said anything, which is a question about what it
    /// reported rather than about what survives. <see cref="Count"/> is not that question: it
    /// falls as suppressions take effect, and read part-way through a compilation it would
    /// also count a dead-directive report that only the end can settle.</para>
    /// </summary>
    public int Reported => _diagnostics.Count;

    /// <summary>
    /// The number of errors reported. For a pass asking whether what it just read is usable,
    /// which nothing short of an error decides.
    /// </summary>
    public int Errors { get; private set; }

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

    /// <summary>
    /// True once any error has been reported. Neither a warning nor an opinion sets this, so a
    /// program carrying only those compiles and runs.
    /// </summary>
    public bool HasErrors { get; private set; }

    /// <summary>True once the cap has been reached and reporting has stopped.</summary>
    public bool IsFull { get; private set; }

    /// <summary>Reports a diagnostic. Ignored once the cap is reached.</summary>
    public void Report(DiagnosticDescriptor descriptor, SourceSpan span, params object?[] args)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        Add(Diagnostic.Create(descriptor, span, _source, args));
    }

    /// <summary>
    /// <para>Reports a diagnostic that one substitution would settle, carrying the text to
    /// substitute.</para>
    /// <para>Named apart from <see cref="Report"/> rather than given a default, because a
    /// <c>params</c> list has to come last and an optional before it could not be passed by
    /// name. The separation reads well enough: most diagnostics have no such text, and the ones
    /// that do are saying something more.</para>
    /// </summary>
    public void ReportFixable(
        DiagnosticDescriptor descriptor,
        SourceSpan span,
        string? fixedBy,
        params object?[] args)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        Add(Diagnostic.CreateFixable(descriptor, span, _source, fixedBy, args));
    }

    /// <summary>
    /// <para>Adds an already-constructed diagnostic. Ignored once the cap is reached.</para>
    /// <para>Reaching the cap is itself reported, in the place reporting stopped, so that a
    /// truncated list says it is truncated and says it with an identifier like everything
    /// else. It is the last thing the bag accepts.</para>
    /// </summary>
    public void Add(Diagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        if (IsFull)
        {
            return;
        }

        if (_diagnostics.Count >= MaximumDiagnostics)
        {
            IsFull = true;

            _diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.TooManyErrors,
                diagnostic.Span,
                diagnostic.Source,
                MaximumDiagnostics));

            HasErrors = true;
            Errors++;
            _visible = null;
            return;
        }

        _diagnostics.Add(diagnostic);
        _visible = null;

        if (diagnostic.Severity == DiagnosticSeverity.Error)
        {
            HasErrors = true;
            Errors++;
        }
    }

    /// <summary>
    /// <para>Records a request to stop reporting something.</para>
    /// <para>Suppression is applied where the bag is read rather than where a report is made,
    /// because whether a suppression silenced anything is knowable only once everything has
    /// reported — and that question has to be answerable.</para>
    /// </summary>
    public void Suppress(Suppression suppression)
    {
        ArgumentNullException.ThrowIfNull(suppression);

        _suppressions.Add(suppression);
        _visible = null;
    }

    /// <summary>
    /// <para>Every directive recorded here.</para>
    /// <para>For a caller moving one bag's contents into another: a file parsed apart carries
    /// both what it reported and what it asked not to be reported, and the two only mean what
    /// they were written to mean together.</para>
    /// </summary>
    public IReadOnlyList<Suppression> Suppressions => _suppressions;

    /// <summary>
    /// <para>Reports every directive that named a diagnostic and silenced none, which is the
    /// last thing a compilation has to say.</para>
    /// <para><b>It is a pass, run once when nothing more will report.</b> Whether a directive
    /// silenced anything cannot be answered before then: after the scanner alone, every
    /// directive naming something a later pass reports looks dead, and an editor scanning a
    /// file on every keystroke would say so on every keystroke. Leaving it out of a partial
    /// compilation costs a report that a full one still makes; putting it in would mean
    /// reporting on lines that are working.</para>
    /// <para>Only the by-identifier form is reported. Naming one asserts a particular
    /// diagnostic is there, while naming a severity claims nothing and is silent with nothing
    /// to silence.</para>
    /// </summary>
    public void ReportUnusedSuppressions()
    {
        if (_reportedUnused)
        {
            return;
        }

        _reportedUnused = true;

        HashSet<Suppression> used = [];

        // Every suppression covering a diagnostic is used, not only the first. Two that
        // overlap are both doing their job, and charging one of them with silencing nothing
        // would be reporting on a line that works.
        foreach (Diagnostic diagnostic in _diagnostics)
        {
            foreach (Suppression suppression in _suppressions)
            {
                if (suppression.Covers(diagnostic))
                {
                    used.Add(suppression);
                }
            }
        }

        // A directive naming PC0024 is passed over. One that could silence this report would
        // have to name it, and an unused one of those would report that it silenced nothing —
        // about itself, without end.
        foreach (Suppression dead in _suppressions.Where(
                     s => s.Id is not null
                          && !string.Equals(
                              s.Id,
                              DiagnosticDescriptors.IgnoreSilencedNothing.Id,
                              StringComparison.Ordinal)
                          && !used.Contains(s)))
        {
            Add(Diagnostic.Create(
                DiagnosticDescriptors.IgnoreSilencedNothing, dead.Span, dead.Source, dead.Id));
        }
    }

    /// <summary>
    /// <para>The diagnostics that survive suppression.</para>
    /// <para>Every reader goes through here, so nothing has to remember to apply it. An error
    /// is never removed: <see cref="Suppression.Covers"/> refuses one before looking at
    /// anything else, and <see cref="HasErrors"/> is set where a report is made rather than
    /// here, so a suppressed compilation still fails for the reasons it should.</para>
    /// </summary>
    private IReadOnlyList<Diagnostic> Visible()
    {
        if (_visible is not null)
        {
            return _visible;
        }

        if (_suppressions.Count == 0)
        {
            return _visible = _diagnostics;
        }

        return _visible =
            [.. _diagnostics.Where(d => !_suppressions.Any(s => s.Covers(d)))];
    }

    /// <summary>
    /// Returns the diagnostics ordered by file, then by position within it. With one file this
    /// is position order, as it was before a compilation could span several.
    /// </summary>
    public IReadOnlyList<Diagnostic> Sorted() =>
        [.. Visible()
            .OrderBy(d => d.FileName, StringComparer.Ordinal)
            .ThenBy(d => d.Span.Start.Offset)
            .ThenBy(d => d.Id, StringComparer.Ordinal)];

    public IEnumerator<Diagnostic> GetEnumerator() => Visible().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
