using System.Text;
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
    /// <para><see cref="Projects"/> says which project each file belongs to, which is how far
    /// an <c>internal</c> declared in it reaches. A file missing from it belongs to the one
    /// unnamed project every compilation has, which is what a build nobody divided is.</para>
    /// </summary>
    public sealed record Compilation(
        string Label,
        IReadOnlyList<CompilationUnit> Units,
        IReadOnlyDictionary<SourceText, string> Projects);

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

        Compilation? gathered = PathComparer.Equals(Path.GetExtension(path), ProjectExtension)
            ? GatherFromProject(path, diagnostics)
            : GatherFromFolder(path, diagnostics);

        if (gathered is null)
        {
            return null;
        }

        Dictionary<SourceText, string> projects = new(gathered.Projects);

        return gathered with
        {
            Units = FollowImports(gathered.Units, projects, diagnostics),
            Projects = projects,
        };
    }

    // ---- Files a file asks for --------------------------------------------------------------

    /// <summary>One import that resolved, as an edge from the file that wrote it to the file it names.</summary>
    private readonly record struct Reach(
        string From,
        string To,
        SourceText Source,
        ImportDirective Directive);

    /// <summary>
    /// <para>Adds every file reached by an <c>import</c>, and everything those reach in turn.
    /// </para>
    /// <para>Followed to closure rather than one level deep, because an imported file must be
    /// able to compile, and it cannot if what it imports is left out. That is the file carrying
    /// its own dependencies, not the program naming many.</para>
    /// <para>A file already present is not added again, whichever way it arrived. Reaching one
    /// twice is one file, not two, so it says nothing — a genuine duplicate is two different
    /// files declaring one type, and the resolver reports that.</para>
    /// <para>Every edge is kept even when the file at its end is already here, because whether
    /// a file arrives twice and whether the imports circle are separate questions, and only the
    /// edges answer the second.</para>
    /// </summary>
    private static IReadOnlyList<CompilationUnit> FollowImports(
        IReadOnlyList<CompilationUnit> seed,
        Dictionary<SourceText, string> projects,
        DiagnosticBag diagnostics)
    {
        List<CompilationUnit> all = [.. seed];
        List<Reach> reaches = [];
        HashSet<string> seen = new(PathComparer);

        foreach (CompilationUnit unit in seed)
        {
            seen.Add(Path.GetFullPath(unit.Source.FileName));
        }

        // Indexed rather than iterated, so a file added while walking is itself walked. That is
        // what makes imports transitive, and what is already seen is what stops a cycle looping
        // here — the circle is reported afterwards, from the edges this collects.
        for (int index = 0; index < all.Count; index++)
        {
            CompilationUnit unit = all[index];
            string from = Path.GetFullPath(unit.Source.FileName);

            foreach (ImportDirective import in unit.Imports)
            {
                if (ResolveImport(import, unit.Source, diagnostics) is not { } resolved)
                {
                    continue;
                }

                string to = Path.GetFullPath(resolved);
                reaches.Add(new Reach(from, to, unit.Source, import));

                if (seen.Add(to))
                {
                    CompilationUnit brought = Parser.Parse(SourceText.FromFile(resolved), diagnostics);
                    all.Add(brought);

                    // An imported file belongs to the project of whoever imported it. No project
                    // listed it, and the file that asked for it is the only claim there is.
                    if (projects.TryGetValue(unit.Source, out string? importing))
                    {
                        projects[brought.Source] = importing;
                    }
                }
            }
        }

        ReportCircles(all, reaches, diagnostics);

        return all;
    }

    /// <summary>
    /// <para>Reports every circle the imports draw.</para>
    /// <para>A depth-first walk, marking each file while it is on the path and again once it is
    /// finished with. An import reaching a file still on the path closes a circle, and the path
    /// from that file onwards is the circle itself — the files walked through to get there are
    /// not part of it and are left out of what is said.</para>
    /// <para>Each edge is examined once, so one circle is reported once however many files lead
    /// into it, and two circles sharing a file are still two.</para>
    /// </summary>
    private static void ReportCircles(
        IReadOnlyList<CompilationUnit> units,
        IReadOnlyList<Reach> reaches,
        DiagnosticBag diagnostics)
    {
        if (reaches.Count == 0)
        {
            return;
        }

        Dictionary<string, List<Reach>> outgoing = new(PathComparer);

        foreach (Reach reach in reaches)
        {
            if (!outgoing.TryGetValue(reach.From, out List<Reach>? fromHere))
            {
                outgoing[reach.From] = fromHere = [];
            }

            fromHere.Add(reach);
        }

        // False while a file is on the path, true once it is done with. Both readings are the
        // reason this is a dictionary of flags rather than a set: a file on the path closes a
        // circle, and one already finished with cannot.
        Dictionary<string, bool> visited = new(PathComparer);
        List<Reach> path = [];

        // Started from the compilation's files in the order they are compiled, so that what is
        // reported does not depend on how a dictionary happened to order its keys.
        foreach (CompilationUnit unit in units)
        {
            Walk(Path.GetFullPath(unit.Source.FileName));
        }

        void Walk(string file)
        {
            if (visited.ContainsKey(file))
            {
                return;
            }

            visited[file] = false;

            if (outgoing.TryGetValue(file, out List<Reach>? fromHere))
            {
                foreach (Reach reach in fromHere)
                {
                    if (visited.TryGetValue(reach.To, out bool done) && !done)
                    {
                        Report(reach);
                        continue;
                    }

                    path.Add(reach);
                    Walk(reach.To);
                    path.RemoveAt(path.Count - 1);
                }
            }

            visited[file] = true;
        }

        void Report(Reach closing)
        {
            // Where the circle begins: the step that first left the file this import reaches
            // back to. A file importing itself never took such a step, and its circle is the
            // one import.
            int opened = path.FindIndex(step => PathComparer.Equals(step.From, closing.To));
            List<Reach> circle = opened < 0 ? [closing] : [.. path[opened..], closing];

            using DiagnosticBag.FileScope reporting = diagnostics.InFile(closing.Source);

            diagnostics.Report(
                DiagnosticDescriptors.CircularImport,
                closing.Directive.Span,
                Describe(circle));
        }
    }

    /// <summary>
    /// Reads a circle back as a sentence: <c>A.pc imports B.pc, which imports A.pc</c>. Files
    /// are named without their folders, as every other import message names them, since the
    /// diagnostic already points at the import that says where to look.
    /// </summary>
    private static string Describe(IReadOnlyList<Reach> circle)
    {
        StringBuilder sentence = new(Path.GetFileName(circle[0].From));

        for (int step = 0; step < circle.Count; step++)
        {
            sentence.Append(step == 0 ? " imports " : ", which imports ")
                    .Append(Path.GetFileName(circle[step].To));
        }

        return sentence.ToString();
    }

    /// <summary>
    /// <para>Turns a written import into the file it names, or reports why it names none.</para>
    /// <para>The path is read relative to the file that wrote it, so a program and what it
    /// imports move together. Forward slashes are accepted everywhere and translated, so one
    /// spelling works on both platforms.</para>
    /// </summary>
    private static string? ResolveImport(
        ImportDirective import,
        SourceText importingFile,
        DiagnosticBag diagnostics)
    {
        // Reported against the file that wrote the import rather than the one it names, which
        // may not exist and in any case did nothing wrong.
        using DiagnosticBag.FileScope reporting = diagnostics.InFile(importingFile);

        string written = import.Path;
        string native = written.Replace('/', Path.DirectorySeparatorChar);

        if (Path.IsPathRooted(native))
        {
            diagnostics.Report(DiagnosticDescriptors.ImportPathIsAbsolute, import.Span, written);
        }

        if (!PathComparer.Equals(Path.GetExtension(native), SourceExtension))
        {
            diagnostics.Report(DiagnosticDescriptors.ImportNotSource, import.Span, written);
            return null;
        }

        string folder = Path.GetDirectoryName(Path.GetFullPath(importingFile.FileName)) ?? ".";

        // Collapsed rather than left as combined, so that a path climbing out of a folder names
        // the file it reached instead of the route it took. Diagnostics reported in an imported
        // file carry this name, and "lib/../other/B.pc" is a worse answer to where than "other/B.pc".
        string combined = Path.GetFullPath(Path.IsPathRooted(native) ? native : Path.Combine(folder, native));

        if (!File.Exists(combined))
        {
            diagnostics.Report(
                DiagnosticDescriptors.ImportNotFound,
                import.Span,
                written,
                Path.GetFileName(importingFile.FileName));

            return null;
        }

        return combined;
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

        // Every file here belongs to the one project a compilation nobody divided has, so
        // nothing is recorded: a file the map does not mention reads as that project, and the
        // map stays empty rather than repeating one name once per file.
        return new Compilation(Path.GetFileName(path), units, new Dictionary<SourceText, string>());
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

    /// <summary>
    /// <para>Gathers a project and every project it references, to closure.</para>
    /// <para>Referenced projects are compiled first, so a build reads in the order it depends:
    /// what a project is built on is already there by the time the project itself arrives.
    /// </para>
    /// </summary>
    private static Compilation? GatherFromProject(string path, DiagnosticBag diagnostics)
    {
        if (ProjectFile.Read(path, diagnostics) is not { } root)
        {
            return null;
        }

        Dictionary<string, ProjectFile> byPath = new(PathComparer)
        {
            [Path.GetFullPath(path)] = root,
        };

        if (!ReadReferenced(root, byPath, diagnostics))
        {
            return null;
        }

        ReportProjectCircles(root, byPath, diagnostics);

        List<CompilationUnit> units = [];
        Dictionary<SourceText, string> projects = [];

        // Which project claimed a file, so that two claims on one file are told apart from one
        // project naming a file twice — which its own reader already caught.
        Dictionary<string, string> owner = new(PathComparer);

        foreach (ProjectFile project in InReferenceOrder(root, byPath))
        {
            foreach (ProjectFile.Entry source in project.SourceFiles)
            {
                string full = Path.GetFullPath(source.Path);

                if (owner.TryGetValue(full, out string? first))
                {
                    using DiagnosticBag.FileScope reporting = diagnostics.InFile(project.Source);

                    diagnostics.Report(
                        DiagnosticDescriptors.SourceBelongsToTwoProjects,
                        source.Span,
                        Path.GetFileName(source.Path),
                        first,
                        project.Name);

                    continue;
                }

                owner[full] = project.Name;

                CompilationUnit unit = Parser.Parse(SourceText.FromFile(source.Path), diagnostics);
                units.Add(unit);
                projects[unit.Source] = project.Name;
            }
        }

        return new Compilation(root.Name, units, projects);
    }

    /// <summary>
    /// Reads every project reachable by <c>reference</c>, keyed by full path. False when one of
    /// them could not be read, since a build missing a project it was told to include is not a
    /// build whose remaining mistakes are worth listing.
    /// </summary>
    private static bool ReadReferenced(
        ProjectFile root,
        Dictionary<string, ProjectFile> byPath,
        DiagnosticBag diagnostics)
    {
        Queue<ProjectFile> pending = new([root]);
        bool whole = true;

        while (pending.Count > 0)
        {
            foreach (ProjectFile.Entry reference in pending.Dequeue().References)
            {
                if (byPath.ContainsKey(reference.Path))
                {
                    continue;
                }

                if (ProjectFile.Read(reference.Path, diagnostics) is not { } referenced)
                {
                    whole = false;
                    continue;
                }

                byPath[reference.Path] = referenced;
                pending.Enqueue(referenced);
            }
        }

        return whole;
    }

    /// <summary>
    /// <para>Every project in the build, each one after the projects it references.</para>
    /// <para>A circle has already been reported by the time this runs, and what is marked as
    /// finished with is what keeps it from looping here.</para>
    /// </summary>
    private static List<ProjectFile> InReferenceOrder(
        ProjectFile root,
        IReadOnlyDictionary<string, ProjectFile> byPath)
    {
        List<ProjectFile> ordered = [];
        HashSet<string> placed = new(PathComparer);

        Place(root);

        return ordered;

        void Place(ProjectFile project)
        {
            if (!placed.Add(Path.GetFullPath(project.Source.FileName)))
            {
                return;
            }

            foreach (ProjectFile.Entry reference in project.References)
            {
                if (byPath.TryGetValue(reference.Path, out ProjectFile? referenced))
                {
                    Place(referenced);
                }
            }

            ordered.Add(project);
        }
    }

    /// <summary>
    /// Reports every circle the references draw, the same walk the imports get and for the same
    /// reason: a reference reaching a project still waiting on this one closes a circle, and
    /// the path from that project onwards is the circle itself.
    /// </summary>
    private static void ReportProjectCircles(
        ProjectFile root,
        IReadOnlyDictionary<string, ProjectFile> byPath,
        DiagnosticBag diagnostics)
    {
        Dictionary<string, bool> visited = new(PathComparer);
        List<(ProjectFile From, ProjectFile.Entry Reference)> path = [];

        Walk(root);

        void Walk(ProjectFile project)
        {
            string here = Path.GetFullPath(project.Source.FileName);

            if (visited.ContainsKey(here))
            {
                return;
            }

            visited[here] = false;

            foreach (ProjectFile.Entry reference in project.References)
            {
                if (!byPath.TryGetValue(reference.Path, out ProjectFile? referenced))
                {
                    continue;
                }

                if (visited.TryGetValue(reference.Path, out bool done) && !done)
                {
                    Report(project, reference);
                    continue;
                }

                path.Add((project, reference));
                Walk(referenced);
                path.RemoveAt(path.Count - 1);
            }

            visited[here] = true;
        }

        void Report(ProjectFile from, ProjectFile.Entry closing)
        {
            int opened = path.FindIndex(step =>
                PathComparer.Equals(Path.GetFullPath(step.From.Source.FileName), closing.Path));

            List<(ProjectFile From, ProjectFile.Entry Reference)> circle =
                opened < 0 ? [(from, closing)] : [.. path[opened..], (from, closing)];

            StringBuilder sentence = new(circle[0].From.Name);

            for (int step = 0; step < circle.Count; step++)
            {
                sentence.Append(step == 0 ? " references " : ", which references ")
                        .Append(NameOf(circle[step].Reference.Path, byPath));
            }

            using DiagnosticBag.FileScope reporting = diagnostics.InFile(from.Source);

            diagnostics.Report(
                DiagnosticDescriptors.CircularProjectReference,
                closing.Span,
                sentence.ToString());
        }
    }

    /// <summary>
    /// What a project calls itself, for a message. The file name stands in where the project
    /// could not be read, so a circle still reads as a sentence when part of it is missing.
    /// </summary>
    private static string NameOf(string path, IReadOnlyDictionary<string, ProjectFile> byPath) =>
        byPath.TryGetValue(path, out ProjectFile? project)
            ? project.Name
            : Path.GetFileNameWithoutExtension(path);
}
