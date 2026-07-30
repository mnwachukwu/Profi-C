using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Text;

namespace ProfiC.Cli;

/// <summary>
/// <para>Reads a Profi-C project file, the <c>.pcp</c> that names what a build is made of.</para>
/// <para>It is written to read like Profi-C — words rather than punctuation, and a closing
/// <c>end</c> — but it is not Profi-C and nothing in it compiles. A project describes a build;
/// a program describes a computation, and keeping the two apart is why this has its own small
/// reader rather than borrowing the language's.</para>
/// <para>The whole grammar:</para>
/// <code>
/// # The bank, across two folders, on top of a project of its own.
///
/// project Bank
///     reference ../Core/Core.pcp
///     source Program.pc
///     source models
///     source services/Ledger.pc
/// end project
/// </code>
/// <para>A <c>source</c> naming a folder takes every <c>.pc</c> directly inside it, and does
/// not descend — a nested folder is named by its own <c>source</c>, so what a project builds
/// can always be read off the file. A <c>reference</c> names another project, whose types this
/// one may then use. Paths are relative to the project file, and written with forward slashes
/// on every platform.</para>
/// </summary>
public sealed class ProjectFile
{
    private const string SourceExtension = SourceDiscovery.SourceExtension;
    private const string ProjectExtension = SourceDiscovery.ProjectExtension;

    private ProjectFile(
        string name,
        SourceText source,
        IReadOnlyList<Entry> sourceFiles,
        IReadOnlyList<Entry> references)
    {
        Name = name;
        Source = source;
        SourceFiles = sourceFiles;
        References = references;
    }

    /// <summary>
    /// One resolved entry, kept with the line that wrote it. Both kinds carry a line because
    /// what goes wrong with either is a mistake on that line and reads best pointed at.
    /// </summary>
    /// <param name="Written">The path as the project file wrote it, for messages.</param>
    /// <param name="Path">What it resolved to.</param>
    /// <param name="Span">The line it was written on.</param>
    public readonly record struct Entry(string Written, string Path, SourceSpan Span);

    /// <summary>The name the project gives itself.</summary>
    public string Name { get; }

    /// <summary>
    /// The project file's own text. It carries the path the project was named by, and is what
    /// a diagnostic reports against when the mistake is on one of its lines.
    /// </summary>
    public SourceText Source { get; }

    /// <summary>Every source file the project builds, in the order it listed them.</summary>
    public IReadOnlyList<Entry> SourceFiles { get; }

    /// <summary>Every project this one references, in the order it listed them.</summary>
    public IReadOnlyList<Entry> References { get; }

    /// <summary>
    /// Reads a project file, or returns null if it could not be read. Anything wrong is
    /// reported rather than thrown, so a mistake in a project reads like a mistake in a program.
    /// </summary>
    public static ProjectFile? Read(string path, DiagnosticBag diagnostics)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (!File.Exists(path))
        {
            diagnostics.Report(DiagnosticDescriptors.ProjectFileNotFound, default, path);
            return null;
        }

        SourceText source = SourceText.FromFile(path);

        // Sources are combined with the project's folder as it was written, so that a file the
        // project names reads the same way the reader named the project.
        string folder = Path.GetDirectoryName(path) is { Length: > 0 } written ? written : ".";

        using DiagnosticBag.FileScope reporting = diagnostics.InFile(source);

        return Parse(source, folder, diagnostics);
    }

    private static ProjectFile? Parse(SourceText source, string folder, DiagnosticBag diagnostics)
    {
        int reportedBefore = diagnostics.Count;

        string? name = null;
        bool closed = false;
        bool sawEntry = false;
        List<Entry> files = [];
        List<Entry> references = [];
        HashSet<string> seen = new(SourceDiscovery.PathComparer);
        HashSet<string> referenced = new(SourceDiscovery.PathComparer);

        string[] lines = source.Text.ReplaceLineEndings("\n").Split('\n');

        bool inBlockComment = false;

        for (int number = 0; number < lines.Length; number++)
        {
            string line = lines[number].Trim();

            // Both of the language's comment forms, so that a project file is annotated the
            // same way the programs it builds are. A block closes on the line carrying the
            // next pair of marks, which is what makes a run of them a heading here too.
            if (inBlockComment)
            {
                inBlockComment = !line.Contains("##", StringComparison.Ordinal);
                continue;
            }

            if (line.StartsWith("##", StringComparison.Ordinal))
            {
                // One that closes on its own line leaves nothing open.
                inBlockComment = line.IndexOf("##", 2, StringComparison.Ordinal) < 0;
                continue;
            }

            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            SourceSpan span = LineSpan(source, number);
            (string word, string rest) = SplitFirstWord(line);

            switch (word)
            {
                case "project" when name is null:
                    if (rest.Length == 0)
                    {
                        diagnostics.Report(DiagnosticDescriptors.ProjectMissingName, span);
                        return null;
                    }

                    name = rest;
                    break;

                case "end" when rest == "project":
                    closed = true;
                    break;

                case "source" when name is not null:
                    sawEntry = true;

                    if (rest.Length == 0)
                    {
                        diagnostics.Report(DiagnosticDescriptors.ProjectSourceMissingPath, span);
                        break;
                    }

                    AddSource(rest, folder, span, files, seen, diagnostics);
                    break;

                case "reference" when name is not null:
                    sawEntry = true;

                    if (rest.Length == 0)
                    {
                        diagnostics.Report(DiagnosticDescriptors.ProjectReferenceMissingPath, span);
                        break;
                    }

                    AddReference(rest, folder, span, references, referenced, diagnostics);
                    break;

                default:
                    if (name is null)
                    {
                        diagnostics.Report(DiagnosticDescriptors.ProjectMissingHeader, span);
                        return null;
                    }

                    diagnostics.Report(DiagnosticDescriptors.ProjectUnknownEntry, span, word);
                    break;
            }

            if (closed)
            {
                break;
            }
        }

        if (name is null)
        {
            diagnostics.Report(DiagnosticDescriptors.ProjectMissingHeader, default);
            return null;
        }

        if (!closed)
        {
            diagnostics.Report(DiagnosticDescriptors.ProjectNotClosed, LineSpan(source, 0));
            return null;
        }

        // Only when the project named nothing at all and nothing else went wrong. A project
        // whose sources were rejected, or whose entries were not understood, has already said
        // why, and saying it builds nothing on top of that explains nothing. Referencing counts
        // as naming something: a project made only of others is composition, not emptiness.
        if (!sawEntry && diagnostics.Count == reportedBefore)
        {
            diagnostics.Report(DiagnosticDescriptors.ProjectHasNoSources, LineSpan(source, 0));
            return null;
        }

        return (files.Count == 0 && references.Count == 0) || diagnostics.Count != reportedBefore
            ? null
            : new ProjectFile(name, source, files, references);
    }

    /// <summary>
    /// Resolves one <c>reference</c> entry. Only the path is settled here; whether the project
    /// it names can be read, and whether the references circle, is for whoever walks them.
    /// </summary>
    private static void AddReference(
        string written,
        string folder,
        SourceSpan span,
        List<Entry> references,
        HashSet<string> referenced,
        DiagnosticBag diagnostics)
    {
        string combined = Path.GetFullPath(
            Path.Combine(folder, written.Replace('/', Path.DirectorySeparatorChar)));

        if (!SourceDiscovery.PathComparer.Equals(Path.GetExtension(combined), ProjectExtension))
        {
            diagnostics.Report(
                DiagnosticDescriptors.ProjectReferenceIsNotAProject, span, written);

            return;
        }

        if (!File.Exists(combined))
        {
            diagnostics.Report(DiagnosticDescriptors.ProjectReferenceNotFound, span, written);
            return;
        }

        // Sameness on the full path, as a source is, so that two spellings of one project are
        // caught while the message keeps the spelling that was written.
        if (!referenced.Add(combined))
        {
            diagnostics.Report(DiagnosticDescriptors.ProjectReferencedTwice, span, written);
            return;
        }

        references.Add(new Entry(written, combined, span));
    }

    /// <summary>
    /// Resolves one <c>source</c> entry. A path naming a folder contributes every source file
    /// directly inside it; a path naming a file contributes itself.
    /// </summary>
    private static void AddSource(
        string written,
        string folder,
        SourceSpan span,
        List<Entry> files,
        HashSet<string> seen,
        DiagnosticBag diagnostics)
    {
        string combined = Path.Combine(folder, written.Replace('/', Path.DirectorySeparatorChar));

        if (Directory.Exists(combined))
        {
            List<string> inside =
                [.. Directory.EnumerateFiles(combined, "*" + SourceExtension)
                             .OrderBy(file => file, StringComparer.Ordinal)];

            if (inside.Count == 0)
            {
                diagnostics.Report(DiagnosticDescriptors.ProjectFolderIsEmpty, span, written);
                return;
            }

            foreach (string file in inside)
            {
                Take(file, written, span, files, seen, diagnostics);
            }

            return;
        }

        if (!File.Exists(combined))
        {
            diagnostics.Report(DiagnosticDescriptors.ProjectSourceNotFound, span, written);
            return;
        }

        if (!SourceDiscovery.PathComparer.Equals(Path.GetExtension(combined), SourceExtension))
        {
            diagnostics.Report(DiagnosticDescriptors.ProjectSourceWrongExtension, span, written);
            return;
        }

        Take(combined, written, span, files, seen, diagnostics);
    }

    /// <summary>
    /// Records one file. Sameness is decided on the full path, so two entries reaching the same
    /// file by different routes are caught, while the recorded path stays the readable one.
    /// </summary>
    private static void Take(
        string path,
        string written,
        SourceSpan span,
        List<Entry> files,
        HashSet<string> seen,
        DiagnosticBag diagnostics)
    {
        if (!seen.Add(Path.GetFullPath(path)))
        {
            diagnostics.Report(DiagnosticDescriptors.ProjectSourceListedTwice, span, written);
            return;
        }

        files.Add(new Entry(written, path, span));
    }

    // ---- Reading a line -------------------------------------------------------------------

    private static (string Word, string Remainder) SplitFirstWord(string line)
    {
        int space = line.IndexOf(' ', StringComparison.Ordinal);

        return space < 0
            ? (line, string.Empty)
            : (line[..space], line[(space + 1)..].Trim());
    }

    /// <summary>The span covering a whole line, which is the grain this reader reports at.</summary>
    private static SourceSpan LineSpan(SourceText source, int lineNumber)
    {
        if (lineNumber >= source.LineCount)
        {
            return default;
        }

        ReadOnlySpan<char> line = source.GetLine(lineNumber + 1);
        int offset = source.OffsetOfLine(lineNumber + 1);

        return source.SpanAt(offset, line.Length);
    }
}
