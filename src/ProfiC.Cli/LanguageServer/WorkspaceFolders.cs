using System.Text.Json.Nodes;

namespace ProfiC.Cli.LanguageServer;

/// <summary>
/// <para>Which folders the editor has open, as it said when it started.</para>
/// <para><b>Nothing else in the compiler knows where the reader is working.</b> Every other
/// question is asked about a file and answered from that file outward:
/// <see cref="ProjectSearch.For"/> walks <em>up</em> from a file until a project claims it, and
/// deliberately walks past whatever an editor has open, because a project may sit above it.</para>
/// <para>A question asked the other way round — which files are near this one — has no answer
/// without somewhere to stop. The folders an editor opens are that boundary, and they are the
/// only honest one available: a search bounded by nothing reaches the root of the disk, and one
/// bounded by a guess is wrong on somebody's layout.</para>
/// <para>Several folders rather than one, because an editor may hold several at once and they
/// need not sit near each other on disk. Possibly none: a single file opened on its own belongs
/// to no folder, and there is nothing to invent for it.</para>
/// </summary>
/// <param name="Folders">Each folder open, as a full path, in the order the editor named them.</param>
public sealed record WorkspaceFolders(IReadOnlyList<string> Folders)
{
    /// <summary>What a client that named no folder leaves, and what stands until one does.</summary>
    public static WorkspaceFolders None { get; } = new([]);

    /// <summary>
    /// <para>Whether a path lies in one of the folders.</para>
    /// <para>Answered by working out the way from the folder to the file rather than by comparing
    /// the text of the two, since <c>D:\Repos\Profi</c> begins the same way as
    /// <c>D:\Repos\Profi-C</c> and is not inside it. A path that <em>is</em> the folder is inside
    /// it, which is what the empty way between them means.</para>
    /// <para>Always false where no folder is open, which is the reading that keeps a caller from
    /// quietly treating "the reader opened a single file" as "everything qualifies".</para>
    /// </summary>
    public bool Holds(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        string full = Path.GetFullPath(path);

        foreach (string folder in Folders)
        {
            string way = Path.GetRelativePath(folder, full);

            // Rooted means the two are on different drives, and a first step of '..' means the
            // path is above the folder. Either way it is outside.
            if (Path.IsPathRooted(way))
            {
                continue;
            }

            if (way != ".."
                && !way.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// <para>Reads the folders out of what the editor sent to <c>initialize</c>.</para>
    /// <para>Three fields say it, and the protocol has replaced the same answer twice:
    /// <c>workspaceFolders</c> is current, <c>rootUri</c> is deprecated, and <c>rootPath</c> is
    /// deprecated and is a plain path rather than a URI. All three are read, newest first, since
    /// which one arrives says what the client is rather than what the reader did.</para>
    /// <para>Falling through an empty <c>workspaceFolders</c> to the older fields is deliberate.
    /// A client sending both an empty list and a root is contradicting itself, and the root is
    /// the half of that with a folder in it.</para>
    /// </summary>
    public static WorkspaceFolders Opened(JsonObject? parameters)
    {
        if (parameters is null)
        {
            return None;
        }

        List<string> folders = [];

        foreach (string path in PathsIn(parameters["workspaceFolders"]))
        {
            Add(folders, path);
        }

        if (folders.Count == 0 && Conversions.PathOf((string?)parameters["rootUri"]) is { } root)
        {
            Add(folders, root);
        }

        if (folders.Count == 0 && (string?)parameters["rootPath"] is { Length: > 0 } written)
        {
            Add(folders, written);
        }

        return folders.Count == 0 ? None : new WorkspaceFolders(folders);
    }

    /// <summary>
    /// <para>The folders after the editor added or removed some.</para>
    /// <para>Removals first, so that a folder named on both sides — which is how an editor says
    /// one moved — ends up present rather than absent.</para>
    /// </summary>
    public WorkspaceFolders After(JsonObject? change)
    {
        if (change is null)
        {
            return this;
        }

        List<string> now = [.. Folders];

        foreach (string path in PathsIn(change["removed"]))
        {
            now.RemoveAll(already => SourceDiscovery.PathComparer.Equals(already, path));
        }

        foreach (string path in PathsIn(change["added"]))
        {
            Add(now, path);
        }

        return now.Count == 0 ? None : new WorkspaceFolders(now);
    }

    /// <summary>The folders a list of <c>{ uri, name }</c> names, as paths.</summary>
    private static IEnumerable<string> PathsIn(JsonNode? listed)
    {
        if (listed is not JsonArray folders)
        {
            yield break;
        }

        foreach (JsonNode? folder in folders)
        {
            if (Conversions.PathOf((string?)(folder?["uri"])) is { } path)
            {
                yield return path;
            }
        }
    }

    /// <summary>
    /// <para>Adds a folder, keeping the order the editor named them in and each of them once.
    /// </para>
    /// <para>An editor may hold one folder twice over — a workspace file listing it, and the
    /// folder itself — and a duplicate would mean every search across the folders did that one
    /// twice.</para>
    /// </summary>
    private static void Add(List<string> folders, string path)
    {
        string full;

        try
        {
            full = Path.GetFullPath(path);
        }
        catch (ArgumentException)
        {
            // A path no file system could hold. It arrived from outside, so it is not a folder
            // rather than a reason to stop starting up.
            return;
        }

        if (!folders.Any(already => SourceDiscovery.PathComparer.Equals(already, full)))
        {
            folders.Add(full);
        }
    }
}
