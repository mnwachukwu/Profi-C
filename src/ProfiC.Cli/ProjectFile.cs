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
        IReadOnlyList<Entry> references,
        string? entryPoint)
    {
        Name = name;
        Source = source;
        SourceFiles = sourceFiles;
        References = references;
        EntryPoint = entryPoint;
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
    /// <para>The <c>Program</c> the build begins at, or null where the project did not say.
    /// </para>
    /// <para>Needed only where the sources declare more than one, which namespaces made
    /// possible: <c>Tools.Program</c> and <c>App.Program</c> are two types, not a name used
    /// twice. Saying nothing is right for the ordinary project that has one.</para>
    /// <para>It belongs here rather than on the command line because an assembly holds one
    /// entry point in its metadata, so the choice is made when the thing is built however it
    /// is spelled — and on the command line it would be something to remember, repeat, and
    /// teach to whatever runs the build.</para>
    /// </summary>
    public string? EntryPoint { get; }

    /// <summary>What a project is being read for, which decides how much of it has to be right.</summary>
    public enum Reading
    {
        /// <summary>
        /// To build it. Anything wrong means there is nothing to build, and the mistake has
        /// already been reported.
        /// </summary>
        ToBuild,

        /// <summary>
        /// <para>To ask what it lists, without building it.</para>
        /// <para>A mistake elsewhere in a project does not change which files it names. An
        /// editor deciding what its Run button points at needs the answer either way — a project
        /// that lists the open file and then fails to build should report its own failure, rather
        /// than being passed over so quietly that the file runs on its own instead.</para>
        /// </summary>
        ToSeeWhatItLists,
    }

    /// <summary>
    /// Reads a project file, or returns null if it could not be read. Anything wrong is
    /// reported rather than thrown, so a mistake in a project reads like a mistake in a program.
    /// </summary>
    public static ProjectFile? Read(
        string path,
        DiagnosticBag diagnostics,
        Reading reading = Reading.ToBuild)
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

        return Parse(source, folder, diagnostics, reading);
    }

    private static ProjectFile? Parse(
        SourceText source,
        string folder,
        DiagnosticBag diagnostics,
        Reading reading)
    {
        // Errors rather than reports, because a project file may now say things that do not
        // stop it being read: an 'ignore' line naming the wrong thing is worth hearing and
        // leaves a perfectly good project behind it.
        int failuresBefore = diagnostics.Errors;

        string? name = null;
        bool closed = false;
        bool sawEntry = false;
        string? entryPoint = null;
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

                // Which Program begins. Only checked for shape here — whether it names one of
                // the sources' programs is a question about the compilation, and is answered
                // once every file has been read.
                case "entry" when name is not null:
                    sawEntry = true;

                    if (rest.Length == 0)
                    {
                        diagnostics.Report(DiagnosticDescriptors.ProjectEntryMissingName, span);
                        break;
                    }

                    if (entryPoint is not null)
                    {
                        diagnostics.Report(DiagnosticDescriptors.ProjectEntryRepeated, span);
                        break;
                    }

                    entryPoint = rest;
                    break;

                // What every file the project builds should stop being told. A project file
                // has no prose, so a word here that names neither a severity nor a diagnostic
                // is a mistake, where the same words in a comment would be a remark.
                // Not something to build, so it does not answer the question of whether the
                // project names anything: one made only of these is still empty.
                case "ignore" when name is not null:
                    Ignore(rest, source, span, diagnostics);
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
        if (!sawEntry && diagnostics.Errors == failuresBefore)
        {
            diagnostics.Report(DiagnosticDescriptors.ProjectHasNoSources, LineSpan(source, 0));
            return null;
        }

        if (files.Count == 0 && references.Count == 0)
        {
            return null;
        }

        // A reader only asking what the project names keeps what was read; one about to build it
        // does not, since a project with a mistake in it has nothing to hand a compilation.
        return reading == Reading.ToBuild && diagnostics.Errors != failuresBefore
            ? null
            : new ProjectFile(name, source, files, references, entryPoint);
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
    /// <para>Records one <c>ignore</c> entry, which reaches every file the project builds.</para>
    /// <para>Nothing here stops a build. A project that asks to stop hearing something and
    /// names it wrongly still compiles; it just keeps hearing it, and is told why.</para>
    /// </summary>
    private static void Ignore(
        string rest,
        SourceText source,
        SourceSpan span,
        DiagnosticBag diagnostics)
    {
        (string named, _) = SplitFirstWord(rest);

        if (named.Length == 0)
        {
            diagnostics.Report(DiagnosticDescriptors.IgnoreNamesNeither, span, "nothing");
            return;
        }

        if (!SuppressionDirective.TryReadTarget(
                named, out DiagnosticSeverity? severity, out string? id))
        {
            diagnostics.Report(DiagnosticDescriptors.IgnoreNamesNeither, span, named);
            return;
        }

        if (id is not null)
        {
            if (!DiagnosticDescriptors.ById.TryGetValue(id, out DiagnosticDescriptor? target))
            {
                diagnostics.Report(DiagnosticDescriptors.IgnoreNamesNoDiagnostic, span, id);
                return;
            }

            if (target.DefaultSeverity == DiagnosticSeverity.Error)
            {
                diagnostics.Report(DiagnosticDescriptors.IgnoreCannotSilenceAnError, span, id);
                return;
            }
        }

        // The project file is carried so that a line silencing nothing is reported where it
        // was written, even though a project-wide suppression belongs to no one file.
        diagnostics.Suppress(
            new Suppression(SuppressionScope.Project, source, 0, severity, id, span));
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
