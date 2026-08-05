using ProfiC.Compiler.Diagnostics;

namespace ProfiC.Cli;

/// <summary>
/// <para>Which project claims a file.</para>
/// <para>Asked by editors, which need it before they can decide what a Run or Build button
/// points at. It lives here for the reason <c>outline</c>, <c>platforms</c> and
/// <c>vocabulary</c> do: the answer depends on how a <c>.pcp</c> is read, and anything else
/// that wanted it would have to read one a second time. Two readers of one format agree until
/// the format gains a word, and this particular disagreement is silent — the editor would find
/// no project, say so, and run the file on its own.</para>
/// <para><b>Claiming is about what a project lists, not about where it sits.</b> A <c>.pcp</c>
/// above a file says what it builds, and a file it does not list is no more part of it than one
/// in another folder.</para>
/// </summary>
public static class ProjectSearch
{
    /// <summary>
    /// <para>The project that claims a file, and how many were read on the way.</para>
    /// <para>The count is what separates "there is no project here" from "there are projects and
    /// none of them wants this file". Those are different things to be told: the second has
    /// something to go and look at.</para>
    /// </summary>
    /// <param name="Project">The project claiming the file, or null where none does.</param>
    /// <param name="Searched">How many projects were read before the answer was settled.</param>
    public readonly record struct Claim(string? Project, int Searched);

    /// <summary>
    /// <para>Searches upward from a file for the project that builds it.</para>
    /// <para>Past the workspace rather than stopping at it, since a project may sit above the
    /// folder an editor happens to have open. Within a folder the projects are read in name
    /// order, so a folder holding two of them answers the same way every time.</para>
    /// </summary>
    public static Claim For(string file)
    {
        ArgumentNullException.ThrowIfNull(file);

        string full = Path.GetFullPath(file);

        // A project names itself, and there is nothing above it to look for.
        if (SourceDiscovery.PathComparer.Equals(
                Path.GetExtension(full), SourceDiscovery.ProjectExtension))
        {
            return new Claim(full, 0);
        }

        int searched = 0;

        for (string? folder = Path.GetDirectoryName(full);
             folder is not null;
             folder = Path.GetDirectoryName(folder))
        {
            foreach (string project in ProjectsIn(folder))
            {
                searched++;

                if (Claims(project, full, new HashSet<string>(SourceDiscovery.PathComparer)))
                {
                    return new Claim(project, searched);
                }
            }
        }

        return new Claim(null, searched);
    }

    /// <summary>
    /// The projects directly inside a folder, in name order. A folder that cannot be listed holds
    /// none as far as this is concerned — walking upward from a file reaches directories nobody
    /// is allowed to read.
    /// </summary>
    private static IEnumerable<string> ProjectsIn(string folder)
    {
        try
        {
            return
            [
                .. Directory.EnumerateFiles(folder, "*" + SourceDiscovery.ProjectExtension)
                            .OrderBy(path => path, StringComparer.Ordinal),
            ];
        }
        catch (Exception unreadable) when (unreadable is IOException
                                                      or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// <para>Every project that builds a given one into itself, by referencing it directly or
    /// through others.</para>
    /// <para><b>The question <see cref="For"/> cannot answer, asked the other way round.</b>
    /// Walking up from a file finds the project that claims it, and following that project's
    /// references finds everything it builds on. Neither reaches what builds on <em>it</em> — and
    /// a reference is one-way in the file system as well as in meaning, so the only way to find
    /// the projects at the other end is to read them.</para>
    /// <para>Which is why this takes the folders rather than searching from the project outward:
    /// there is no upper bound on a file system, so a search with no boundary walks to the root
    /// of the disk. The folders an editor has open are the boundary a reader would recognize, and
    /// a project outside all of them is not one they are working on.</para>
    /// <para>Every project is read, including ones with mistakes in them, since which projects a
    /// project references is not a question about whether it builds — and a workspace almost
    /// always holds at least one that does not, at least while somebody is editing it.</para>
    /// <para>The order is the one the folders were named in and then the file system's, so an
    /// answer built from this does not shuffle between runs.</para>
    /// </summary>
    public static IReadOnlyList<string> ProjectsBuilding(
        string project, IEnumerable<string> folders)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(folders);

        string wanted = Path.GetFullPath(project);

        // What each project references, read once and kept in the order they were found. A
        // project may be looked at several times over while the answer is worked out, and reading
        // a file is the expensive part of this.
        List<string> candidates = [];
        Dictionary<string, List<string>> references = new(SourceDiscovery.PathComparer);

        foreach (string found in ProjectsUnder(folders))
        {
            if (references.ContainsKey(found))
            {
                continue;
            }

            // Thrown away rather than reported. Nothing here was asked to check the projects it
            // reads, and a folder of unrelated broken ones is not this question's problem.
            DiagnosticBag aside = new();

            candidates.Add(found);

            references[found] =
                ProjectFile.Read(found, aside, ProjectFile.Reading.ToSeeWhatItLists) is { } read
                    ? [.. read.References.Select(entry => Path.GetFullPath(entry.Path))]
                    : [];
        }

        // Outward from the project, one step at a time: whoever references it, then whoever
        // references them. A reference is transitive, so a project two steps away builds this
        // one's files just as directly as one step does.
        List<string> reaching = [];
        HashSet<string> reached = new(SourceDiscovery.PathComparer) { wanted };
        Queue<string> asking = new([wanted]);

        while (asking.Count > 0)
        {
            string target = asking.Dequeue();

            foreach (string candidate in candidates)
            {
                if (!reached.Contains(candidate)
                    && references[candidate].Any(
                        one => SourceDiscovery.PathComparer.Equals(one, target)))
                {
                    reached.Add(candidate);
                    reaching.Add(candidate);
                    asking.Enqueue(candidate);
                }
            }
        }

        return reaching;
    }

    /// <summary>
    /// Every project file anywhere inside the folders, each once. A folder that cannot be read
    /// holds none as far as this is concerned, the same answer a folder walked upward gives.
    /// </summary>
    private static IEnumerable<string> ProjectsUnder(IEnumerable<string> folders)
    {
        foreach (string folder in folders)
        {
            string[] inside;

            try
            {
                inside =
                [
                    .. Directory.EnumerateFiles(
                                    folder,
                                    "*" + SourceDiscovery.ProjectExtension,
                                    SearchOption.AllDirectories)
                                .Select(Path.GetFullPath)
                                .OrderBy(path => path, StringComparer.Ordinal),
                ];
            }
            catch (Exception unreadable) when (unreadable is IOException
                                                          or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (string project in inside)
            {
                yield return project;
            }
        }
    }

    /// <summary>
    /// <para>Whether a project builds a file, following what it references.</para>
    /// <para>A referenced project's sources are compiled into the build that reaches them, so a
    /// file reached that way is one this project builds — which is what makes running the project
    /// the right answer for it.</para>
    /// <para>Read even where the project has a mistake in it, because which files a project names
    /// is not a question about whether it builds. Passing over a broken project that plainly
    /// lists the file would run the file alone and never mention the project that wanted it.</para>
    /// </summary>
    private static bool Claims(string project, string file, HashSet<string> seen)
    {
        string full = Path.GetFullPath(project);

        // Projects may reference each other in a circle. The compiler reports that; this only
        // has to avoid following it forever.
        if (!seen.Add(full))
        {
            return false;
        }

        // Thrown away rather than reported. Nothing here was asked to check the projects it
        // reads, and a folder of unrelated broken ones is not this file's problem.
        DiagnosticBag aside = new();

        if (ProjectFile.Read(full, aside, ProjectFile.Reading.ToSeeWhatItLists) is not { } read)
        {
            return false;
        }

        foreach (ProjectFile.Entry source in read.SourceFiles)
        {
            if (SourceDiscovery.PathComparer.Equals(Path.GetFullPath(source.Path), file))
            {
                return true;
            }
        }

        foreach (ProjectFile.Entry reference in read.References)
        {
            if (Claims(reference.Path, file, seen))
            {
                return true;
            }
        }

        return false;
    }
}
