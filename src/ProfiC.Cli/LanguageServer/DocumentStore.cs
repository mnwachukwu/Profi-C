using System.Collections.Concurrent;
using ProfiC.Compiler.Text;

namespace ProfiC.Cli.LanguageServer;

/// <summary>
/// <para>The files an editor has open, as they are right now rather than as they were last
/// saved.</para>
/// <para><b>This is what makes a language server possible at all.</b> Every command a reader
/// types reads the disk, which is the only thing a separate process can do. A server is told
/// about each edit as it happens, so what it answers about is the buffer — and a buffer mid-edit
/// is usually not valid Profi-C and has no version on disk to stand in for it.</para>
/// <para>Held by full path rather than by the URI it arrived as. An editor may say
/// <c>file:///c%3A/work/Program.pc</c> where a project says <c>Program.pc</c>, and a compilation
/// that read one as two files would report every type in it declared twice.</para>
/// <para>Every document carries the version the editor gave it. Analysis is worth publishing
/// only against the text it actually read, and by the time it finishes the reader may have
/// typed again — so what it was looking at has to be recoverable afterwards.</para>
/// <para>Concurrent because the reading loop takes edits while analysis walks the text. Neither
/// waits for the other: an edit replaces an entry, and analysis already holds the
/// <see cref="Document"/> it started with.</para>
/// </summary>
public sealed class DocumentStore
{
    private readonly ConcurrentDictionary<string, Document> _open =
        new(SourceDiscovery.PathComparer);

    /// <summary>One open file: its text, and which edit of it this is.</summary>
    /// <param name="Path">The full path, which is how a compilation names it.</param>
    /// <param name="Text">What the editor holds, which may never have been saved.</param>
    /// <param name="Version">
    /// The editor's count of edits to this file. Rising, and not necessarily by one.
    /// </param>
    public sealed record Document(string Path, string Text, int Version)
    {
        /// <summary>The text as the compiler takes it, named by the path a diagnostic will show.</summary>
        public SourceText AsSource() => new(Text, Path);
    }

    /// <summary>How many files are open, which is what a server has to answer about.</summary>
    public int Count => _open.Count;

    /// <summary>
    /// Takes a file the editor has opened, or the whole new text of one already open. The same
    /// operation either way: an editor sends the full text on open and a server that treated the
    /// two differently would have two paths to the same state.
    /// </summary>
    public Document Set(string path, string text, int version)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(text);

        Document document = new(Path.GetFullPath(path), text, version);

        _open[document.Path] = document;
        return document;
    }

    /// <summary>
    /// <para>Forgets a file the editor has closed, and says whether it was open.</para>
    /// <para>Closing means the editor is no longer the authority on it, so what is on disk
    /// becomes the answer again — which is the saved text, since an editor does not close a file
    /// with unsaved edits without asking.</para>
    /// </summary>
    public bool Close(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return _open.TryRemove(Path.GetFullPath(path), out _);
    }

    /// <summary>What the editor holds for a file, or null where it holds none.</summary>
    public Document? Find(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return _open.TryGetValue(Path.GetFullPath(path), out Document? found) ? found : null;
    }

    /// <summary>Every file the editor has open, in no particular order.</summary>
    public IEnumerable<Document> All() => _open.Values;

    /// <summary>
    /// <para>Reading that answers from the editor where it can and the disk where it
    /// cannot.</para>
    /// <para>Both halves are needed, and neither on its own would do. A program is a compilation:
    /// pressing a key in <c>Program.pc</c> re-analyzes <c>Shelf.pc</c> beside it, which the
    /// reader may never have opened and which only the disk knows. And a file that is open must
    /// come from here whatever the disk says, or the whole point is lost.</para>
    /// <para>Handed to <see cref="SourceDiscovery.Gather"/> as its reader, which is the one
    /// place a compilation decides what any of its files say.</para>
    /// </summary>
    public SourceReader Reader => path => Find(path) is { } open
        ? open.AsSource()
        : SourceDiscovery.FromDisk(path);
}
