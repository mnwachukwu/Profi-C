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
/// comment The bank, across two folders.
///
/// project Bank
///     source Program.pc
///     source models
///     source services/Ledger.pc
/// end project
/// </code>
/// <para>A <c>source</c> naming a folder takes every <c>.pc</c> directly inside it, and does
/// not descend — a nested folder is named by its own <c>source</c>, so what a project builds
/// can always be read off the file. Paths are relative to the project file, and written with
/// forward slashes on every platform.</para>
/// </summary>
public sealed class ProjectFile
{
    private const string SourceExtension = SourceDiscovery.SourceExtension;

    private ProjectFile(string name, IReadOnlyList<string> sourceFiles)
    {
        Name = name;
        SourceFiles = sourceFiles;
    }

    /// <summary>The name the project gives itself.</summary>
    public string Name { get; }

    /// <summary>Every source file the project builds, in the order it listed them.</summary>
    public IReadOnlyList<string> SourceFiles { get; }

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
        bool sawSource = false;
        List<string> files = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        string[] lines = source.Text.ReplaceLineEndings("\n").Split('\n');

        bool inBlockComment = false;

        for (int number = 0; number < lines.Length; number++)
        {
            string line = lines[number].Trim();

            // Both of the language's comment forms, so that a project file is annotated the
            // same way the programs it builds are.
            if (inBlockComment)
            {
                inBlockComment = line != "end comment";
                continue;
            }

            if (line is "comment begin")
            {
                inBlockComment = true;
                continue;
            }

            if (line.Length == 0 || line.StartsWith("comment", StringComparison.Ordinal))
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
                    sawSource = true;

                    if (rest.Length == 0)
                    {
                        diagnostics.Report(DiagnosticDescriptors.ProjectSourceMissingPath, span);
                        break;
                    }

                    AddSource(rest, folder, span, files, seen, diagnostics);
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
        // why, and saying it builds nothing on top of that explains nothing.
        if (!sawSource && diagnostics.Count == reportedBefore)
        {
            diagnostics.Report(DiagnosticDescriptors.ProjectHasNoSources, LineSpan(source, 0));
            return null;
        }

        return files.Count == 0 || diagnostics.Count != reportedBefore
            ? null
            : new ProjectFile(name, files);
    }

    /// <summary>
    /// Resolves one <c>source</c> entry. A path naming a folder contributes every source file
    /// directly inside it; a path naming a file contributes itself.
    /// </summary>
    private static void AddSource(
        string written,
        string folder,
        SourceSpan span,
        List<string> files,
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

        if (!Path.GetExtension(combined).Equals(SourceExtension, StringComparison.OrdinalIgnoreCase))
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
        List<string> files,
        HashSet<string> seen,
        DiagnosticBag diagnostics)
    {
        if (!seen.Add(Path.GetFullPath(path)))
        {
            diagnostics.Report(DiagnosticDescriptors.ProjectSourceListedTwice, span, written);
            return;
        }

        files.Add(path);
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
