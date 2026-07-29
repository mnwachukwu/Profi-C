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
    /// <para>The files a command compiles, and the name to label the compilation with.</para>
    /// <para><see cref="Units"/> is in the order the files are compiled. The first is always
    /// the one the reader named, when they named a source file.</para>
    /// </summary>
    public sealed record Compilation(string Label, IReadOnlyList<CompilationUnit> Units);

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

        return Path.GetExtension(path).Equals(ProjectExtension, StringComparison.OrdinalIgnoreCase)
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

        IEnumerable<string> neighbours =
            Directory.EnumerateFiles(folder, "*" + SourceExtension)
                     .Where(other => !string.Equals(
                         Path.GetFullPath(other), namedFullPath, StringComparison.OrdinalIgnoreCase))
                     .Select(other => bare ? Path.GetFileName(other) : other)
                     .OrderBy(other => other, StringComparer.Ordinal);

        foreach (string neighbour in neighbours)
        {
            // Parsed apart so that a neighbouring program's mistakes are its own. Only what is
            // shared code joins the compilation, and only then do its diagnostics.
            DiagnosticBag aside = new();
            CompilationUnit unit = Parser.Parse(SourceText.FromFile(neighbour), aside);

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
