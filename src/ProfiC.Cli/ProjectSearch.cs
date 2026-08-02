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
