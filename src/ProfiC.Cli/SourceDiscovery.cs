using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Text;

namespace ProfiC.Cli;

/// <summary>
/// <para>Works out which files a command compiles, given the one the reader named.</para>
/// <para>This is the driver's business rather than the compiler's. The compiler is handed a
/// set of files and has no opinion about where they came from.</para>
/// </summary>
public static class SourceDiscovery
{
    /// <summary>The extension of a Profi-C source file.</summary>
    public const string SourceExtension = ".pc";

    /// <summary>The extension of a Profi-C project file.</summary>
    public const string ProjectExtension = ".pcp";

    /// <summary>The name reserved for the model that holds a program's entry point.</summary>
    private const string EntryPointName = "Program";

    /// <summary>
    /// <para>How two paths are compared for sameness, and how an extension is recognized.</para>
    /// <para>Windows and macOS treat the case of a name as decoration; Linux treats it as part
    /// of the name. Comparing the wrong way makes two different files look like one, which
    /// would drop a source from a compilation without saying so.</para>
    /// <para>This follows the platform rather than the volume, which is what the framework
    /// gives us to go on. A case-sensitive volume mounted on Windows would be read the
    /// forgiving way, and the cost of that is a file quietly left out rather than a wrong
    /// answer from one.</para>
    /// </summary>
    public static StringComparer PathComparer { get; } =
        OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// <para>The files a command compiles, and the name to label the compilation with.</para>
    /// <para><see cref="Units"/> is in the order the files are compiled. The first is always
    /// the one the reader named, when they named a source file.</para>
    /// </summary>
    public sealed record Compilation(string Label, IReadOnlyList<CompilationUnit> Units);

    /// <summary>A file a command was pointed at, and which of the two kinds it is.</summary>
    public readonly record struct FileTarget(string Path, bool IsProject);

    /// <summary>
    /// <para>Works out which file a written path names, or says why it names none.</para>
    /// <para>An extension may be left off: <c>Program</c> finds <c>Program.pc</c> or
    /// <c>Program.pcp</c>, whichever is there. Writing one keeps its exactness, which is the
    /// way to say which is meant when both exist — the only case where leaving it off is
    /// ambiguous, and one the reader is told about rather than guessed at.</para>
    /// <para>An extension that is neither is refused rather than read hopefully. A file is
    /// Profi-C because it says so, not because something tried.</para>
    /// </summary>
    public static FileTarget? Locate(string written, out string problem)
    {
        ArgumentNullException.ThrowIfNull(written);

        string extension = System.IO.Path.GetExtension(written);

        if (extension.Length > 0)
        {
            // Recognized the same way the file system matches "*.pc" when a folder is read, so
            // that a spelling accepted here is one the folder rule will also find.
            bool project = PathComparer.Equals(extension, ProjectExtension);
            bool source = PathComparer.Equals(extension, SourceExtension);

            if (!project && !source)
            {
                problem = "Not a valid Profi-C source or project file.";
                return null;
            }

            if (!File.Exists(written))
            {
                problem = $"file not found: {written}";
                return null;
            }

            problem = string.Empty;
            return new FileTarget(written, project);
        }

        string asSource = written + SourceExtension;
        string asProject = written + ProjectExtension;
        bool hasSource = File.Exists(asSource);
        bool hasProject = File.Exists(asProject);

        if (hasSource && hasProject)
        {
            problem =
                $"'{written}' could mean {System.IO.Path.GetFileName(asSource)} or "
                + $"{System.IO.Path.GetFileName(asProject)}, and both are here. "
                + "Write the extension of the one you mean.";

            return null;
        }

        problem = string.Empty;

        if (hasSource)
        {
            return new FileTarget(asSource, false);
        }

        if (hasProject)
        {
            return new FileTarget(asProject, true);
        }

        problem = $"file not found: {asSource}, and no {asProject} either";
        return null;
    }

    /// <summary>
    /// <para>Gathers the files to compile, parsing each one.</para>
    /// <para>Naming a source file compiles it together with the shared code beside it: every
    /// other <c>.pc</c> in the same folder that declares no <c>Program</c>. A file that does
    /// declare one is a program in its own right and is left alone, so a folder may hold as
    /// many programs as it likes without them colliding.</para>
    /// <para>Naming a project file compiles exactly what the project lists, across as many
    /// folders as it names.</para>
    /// </summary>
    public static Compilation? Gather(string path, DiagnosticBag diagnostics)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(diagnostics);

        return PathComparer.Equals(Path.GetExtension(path), ProjectExtension)
            ? GatherFromProject(path, diagnostics)
            : GatherFromFolder(path, diagnostics);
    }

    /// <summary>Parses one file on its own, for the commands that work a file at a time.</summary>
    public static CompilationUnit ParseOne(string path, DiagnosticBag diagnostics)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(diagnostics);

        return Parser.Parse(SourceText.FromFile(path), diagnostics);
    }

    // ---- A source file, and the shared code beside it -------------------------------------

    private static Compilation GatherFromFolder(string path, DiagnosticBag diagnostics)
    {
        CompilationUnit named = Parser.Parse(SourceText.FromFile(path), diagnostics);
        List<CompilationUnit> units = [named];

        // Enumerated through the folder as it was written rather than its full path, so that
        // every file in a compilation is named the way the reader named the first one. A file
        // named with no folder at all keeps its bare name instead of gaining a "./".
        string? written = Path.GetDirectoryName(path);
        bool bare = string.IsNullOrEmpty(written);
        string folder = bare ? "." : written!;
        string namedFullPath = Path.GetFullPath(path);

        IEnumerable<string> neighbors =
            Directory.EnumerateFiles(folder, "*" + SourceExtension)
                     .Where(other => !PathComparer.Equals(Path.GetFullPath(other), namedFullPath))
                     .Select(other => bare ? Path.GetFileName(other) : other)
                     .OrderBy(other => other, StringComparer.Ordinal);

        foreach (string neighbor in neighbors)
        {
            // Parsed apart so that a neighboring program's mistakes are its own. Only what is
            // shared code joins the compilation, and only then do its diagnostics.
            DiagnosticBag aside = new();
            CompilationUnit unit = Parser.Parse(SourceText.FromFile(neighbor), aside);

            if (DeclaresEntryPointModel(unit.Declarations))
            {
                continue;
            }

            units.Add(unit);

            foreach (Diagnostic diagnostic in aside)
            {
                diagnostics.Add(diagnostic);
            }
        }

        return new Compilation(Path.GetFileName(path), units);
    }

    /// <summary>
    /// Whether a file declares the model a program's entry point lives in, which is what makes
    /// it a program rather than shared code. Namespaces are searched too, since a declaration
    /// inside one is still a declaration the file makes.
    /// </summary>
    private static bool DeclaresEntryPointModel(IEnumerable<Declaration> declarations) =>
        declarations.Any(declaration => declaration switch
        {
            ModelDecl model => string.Equals(model.Name, EntryPointName, StringComparison.Ordinal),
            NamespaceDecl inner => DeclaresEntryPointModel(inner.Declarations),
            _ => false,
        });

    // ---- A project file -------------------------------------------------------------------

    private static Compilation? GatherFromProject(string path, DiagnosticBag diagnostics)
    {
        if (ProjectFile.Read(path, diagnostics) is not { } project)
        {
            return null;
        }

        List<CompilationUnit> units =
            [.. project.SourceFiles.Select(file => Parser.Parse(SourceText.FromFile(file), diagnostics))];

        return new Compilation(project.Name, units);
    }
}
