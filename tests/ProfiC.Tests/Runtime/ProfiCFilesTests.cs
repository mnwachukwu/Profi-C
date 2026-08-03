using System.Text;
using ProfiC.Runtime;

namespace ProfiC.Tests.Runtime;

/// <summary>
/// <para>Files and folders, tested directly for the same reason the time types are: both engines
/// call these, so neither can catch a mistake in them.</para>
/// <para>Only the decisions. That a write followed by a read gives back what was written is the
/// platform's business; what is ours is that a missing file is an absence rather than a failure,
/// that nothing writes a byte-order mark, and that a listing comes back in a settled order.</para>
/// </summary>
[TestFixture]
public sealed class ProfiCFilesTests
{
    private string _folder = string.Empty;

    [SetUp]
    public void MakeAFolder()
    {
        _folder = Path.Combine(Path.GetTempPath(), $"profi-c-files-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_folder);
    }

    [TearDown]
    public void RemoveIt() => Directory.Delete(_folder, recursive: true);

    private string At(string name) => Path.Combine(_folder, name);

    /// <summary>
    /// <para>A file that is not there is an absence, not a failure.</para>
    /// <para>So the ordinary question needs no guard, and a program asking for a file it did not
    /// write is the ordinary case rather than something to wrap in a <c>try</c>.</para>
    /// </summary>
    [Test]
    public void AMissingFileAnswersWithNothing()
    {
        string missing = At("nowhere.txt");

        Assert.Multiple(() =>
        {
            Assert.That(ProfiCFiles.Read(missing).HasValue, Is.False);
            Assert.That(ProfiCFiles.ReadLines(missing).HasValue, Is.False);
            Assert.That(ProfiCFiles.Size(missing).HasValue, Is.False);
            Assert.That(ProfiCFiles.Changed(missing).HasValue, Is.False);
            Assert.That(ProfiCFiles.Files(At("nofolder")).HasValue, Is.False);
            Assert.That(ProfiCFiles.Folders(At("nofolder")).HasValue, Is.False);

            // And removing one that was never there says so rather than raising.
            Assert.That(ProfiCFiles.Delete(missing), Is.False);
            Assert.That(ProfiCFiles.DeleteFolder(At("nofolder")), Is.False);
        });
    }

    /// <summary>
    /// <para>Nothing writes a byte-order mark.</para>
    /// <para>A mark is invisible, travels into the first string a program reads back, and turns
    /// an equality that should hold into one that does not — which is a very long afternoon for
    /// a beginner.</para>
    /// </summary>
    [Test]
    public void NothingWritesAByteOrderMark()
    {
        string path = At("plain.txt");

        ProfiCFiles.Write(path, "hello");

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(Encoding.UTF8.GetBytes("hello")));
            Assert.That(ProfiCFiles.Read(path).Value, Is.EqualTo("hello"));
        });
    }

    /// <summary>
    /// <para>Each line is followed by a newline, including the last.</para>
    /// <para>So writing lines and reading them back gives what was written, and appending
    /// afterwards starts on a line of its own rather than joining the end of the last one.</para>
    /// </summary>
    [Test]
    public void EveryLineEndsWithANewline()
    {
        string path = At("lines.txt");

        ProfiCFiles.WriteLines(path, new ProfiCSet<string>(["bread", "milk"]));

        Assert.Multiple(() =>
        {
            Assert.That(ProfiCFiles.Read(path).Value, Is.EqualTo("bread\nmilk\n"));
            Assert.That(ProfiCFiles.ReadLines(path).Value, Has.Count.EqualTo(2));
        });
    }

    /// <summary>
    /// <para>A listing comes back in a settled order.</para>
    /// <para>A file system offers its own, which differs between machines and sometimes between
    /// two runs on one — and a program that prints a folder should print the same thing twice.
    /// </para>
    /// </summary>
    [Test]
    public void AListingIsSorted()
    {
        foreach (string name in new[] { "zebra.txt", "apple.txt", "mango.txt" })
        {
            ProfiCFiles.Write(At(name), string.Empty);
        }

        Assert.That(
            ProfiCFiles.Files(_folder).Value.Select(Path.GetFileName),
            Is.EqualTo(new[] { "apple.txt", "mango.txt", "zebra.txt" }));
    }

    /// <summary>
    /// The untyped forms the interpreter reads answer the same, with null for an absence — the
    /// same split every member that can answer with nothing takes.
    /// </summary>
    [Test]
    public void TheUntypedFormsAgreeWithTheTyped()
    {
        string path = At("both.txt");

        ProfiCFiles.Write(path, "one\ntwo\n");

        Assert.Multiple(() =>
        {
            Assert.That(ProfiCFiles.ReadUntyped(path), Is.EqualTo(ProfiCFiles.Read(path).Value));
            Assert.That(ProfiCFiles.ReadUntyped(At("gone.txt")), Is.Null);

            Assert.That(
                ((ProfiCSet<object?>)ProfiCFiles.ReadLinesUntyped(path)!).Count,
                Is.EqualTo(ProfiCFiles.ReadLines(path).Value.Count));
        });
    }
}
