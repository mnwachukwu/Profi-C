using ProfiC.Cli;
using ProfiC.Cli.LanguageServer;
using ProfiC.Compiler.Diagnostics;

namespace ProfiC.Tests.LanguageServer;

/// <summary>
/// <para>Compiling what the editor holds, rather than what was last saved.</para>
/// <para><b>The claim worth holding is the last one here</b>: a compilation gathered through the
/// store reads an open file from the store and a closed one from the disk. Everything above it is
/// bookkeeping; that one is the reason any of it exists, and it is the difference between a
/// language server and the process-per-question arrangement it replaces.</para>
/// <para>Both halves are needed. A program is a compilation, so pressing a key in
/// <c>Program.pc</c> re-analyzes the file beside it that only the disk knows — and an open file
/// must come from the store whatever the disk says, or nothing has changed.</para>
/// </summary>
[TestFixture]
public sealed class DocumentStoreTests
{
    /// <summary>Two files that compile together: one names a model the other declares.</summary>
    private sealed class Workspace : IDisposable
    {
        public Workspace()
        {
            Folder = Path.Combine(Path.GetTempPath(), $"profi-c-store-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Folder);

            Write(
                "Program.pc",
                """
                shared model Program
                    function Main()
                        Console.WriteLine(Greeting.Words());
                    end function
                end model
                """);

            Write(
                "Greeting.pc",
                """
                shared model Greeting
                    public shared string function Words()
                        yield "from the disk";
                    end function
                end model
                """);
        }

        public string Folder { get; }

        public string At(string name) => Path.Combine(Folder, name);

        public void Write(string name, string body) =>
            File.WriteAllText(Path.Combine(Folder, name), body);

        public void Dispose() => Directory.Delete(Folder, recursive: true);
    }

    [Test]
    public void AFileTheEditorHasNotOpenedIsRead()
    {
        DocumentStore store = new();

        Assert.Multiple(() =>
        {
            Assert.That(store.Find("nothing.pc"), Is.Null);
            Assert.That(store.Count, Is.Zero);
        });
    }

    [Test]
    public void AnOpenFileIsHeldAndFoundAgain()
    {
        DocumentStore store = new();

        store.Set("a.pc", "shared model Program\nend model\n", version: 1);

        Assert.Multiple(() =>
        {
            Assert.That(store.Find("a.pc")!.Text, Does.Contain("shared model Program"));
            Assert.That(store.Find("a.pc")!.Version, Is.EqualTo(1));
            Assert.That(store.Count, Is.EqualTo(1));
        });
    }

    /// <summary>
    /// A later edit replaces the earlier one rather than joining it. There is one current text
    /// per file and no history: what came before is the editor's business, not this.
    /// </summary>
    [Test]
    public void AnEditReplacesWhatWasThere()
    {
        DocumentStore store = new();

        store.Set("a.pc", "first", version: 1);
        store.Set("a.pc", "second", version: 4);

        Assert.Multiple(() =>
        {
            Assert.That(store.Find("a.pc")!.Text, Is.EqualTo("second"));
            Assert.That(store.Find("a.pc")!.Version, Is.EqualTo(4), "and versions need not run by one");
            Assert.That(store.Count, Is.EqualTo(1), "one file, however many edits");
        });
    }

    /// <summary>
    /// <para>The same file named two ways is one file.</para>
    /// <para>An editor sends an absolute path where a project may say a relative one. Held apart,
    /// a compilation would read one file as two and report every type in it declared twice.</para>
    /// </summary>
    [Test]
    public void OneFileNamedTwoWaysIsOneFile()
    {
        using Workspace workspace = new();

        DocumentStore store = new();

        store.Set(workspace.At("Program.pc"), "held", version: 1);
        store.Set(Path.Combine(workspace.Folder, ".", "Program.pc"), "held again", version: 2);

        Assert.Multiple(() =>
        {
            Assert.That(store.Count, Is.EqualTo(1));
            Assert.That(store.Find(workspace.At("Program.pc"))!.Text, Is.EqualTo("held again"));
        });
    }

    /// <summary>
    /// <para>A forward slash names the same file as the platform's own separator.</para>
    /// <para>Worth its own test because the two sides disagree about what a path even is. An
    /// editor speaks URIs, which are always forward-slashed, while a project file and a folder
    /// walk speak whatever the platform uses. On Windows the two spellings must land on one
    /// entry; on Linux there is only ever one spelling and this asserts that nothing was
    /// normalized into a different file. Run on both, so neither reading passes alone.</para>
    /// </summary>
    [Test]
    public void AForwardSlashNamesTheSameFileAsTheSeparatorDoes()
    {
        using Workspace workspace = new();

        DocumentStore store = new();

        store.Set(workspace.At("Program.pc"), "held", version: 1);
        store.Set($"{workspace.Folder}/Program.pc", "held again", version: 2);

        Assert.Multiple(() =>
        {
            Assert.That(store.Count, Is.EqualTo(1), "one file, spelled two ways");
            Assert.That(store.Find(workspace.At("Program.pc"))!.Text, Is.EqualTo("held again"));
        });
    }

    /// <summary>
    /// <para>Case is part of a name where the platform says it is, and decoration where it is
    /// not.</para>
    /// <para>The comparer is <see cref="SourceDiscovery.PathComparer"/>, which the rest of the
    /// compiler already uses to decide whether two paths are one file. Holding open documents
    /// under a different rule would let a compilation and the store disagree about that — so
    /// what this asserts is that they agree, whichever way the platform answers.</para>
    /// </summary>
    [Test]
    public void CaseIsTreatedTheWayThisPlatformTreatsIt()
    {
        using Workspace workspace = new();

        DocumentStore store = new();

        store.Set(workspace.At("Program.pc"), "held", version: 1);
        store.Set(workspace.At("PROGRAM.PC"), "held again", version: 2);

        Assert.That(
            store.Count,
            Is.EqualTo(OperatingSystem.IsLinux() ? 2 : 1),
            "the store and a compilation must answer this the same way");
    }

    /// <summary>
    /// Closing hands the file back to the disk. An editor does not close one with unsaved edits
    /// without asking, so what is there is what the reader decided to keep.
    /// </summary>
    [Test]
    public void ClosingHandsAFileBackToTheDisk()
    {
        DocumentStore store = new();

        store.Set("a.pc", "held", version: 1);

        Assert.Multiple(() =>
        {
            Assert.That(store.Close("a.pc"), Is.True);
            Assert.That(store.Find("a.pc"), Is.Null);
            Assert.That(store.Close("a.pc"), Is.False, "and closing it twice says so");
        });
    }

    /// <summary>
    /// <para>A compilation reads an open file from the store and a closed one from the disk.
    /// </para>
    /// <para>The whole point, in one assertion. <c>Greeting.pc</c> is edited but never saved, and
    /// what compiles is the edit; <c>Program.pc</c> is never opened, and it still arrives.</para>
    /// </summary>
    [Test]
    public void ACompilationReadsTheEditorFirstAndTheDiskAfter()
    {
        using Workspace workspace = new();

        DocumentStore store = new();

        store.Set(
            workspace.At("Greeting.pc"),
            """
            shared model Greeting
                public shared string function Words()
                    yield "from the editor";
                end function
            end model
            """,
            version: 1);

        DiagnosticBag diagnostics = new();

        SourceDiscovery.Compilation gathered =
            SourceDiscovery.Gather(workspace.At("Program.pc"), diagnostics, store.Reader)!;

        string greeting = gathered.Units
            .Single(unit => unit.Source.FileName.EndsWith("Greeting.pc", StringComparison.Ordinal))
            .Source.Text;

        Assert.Multiple(() =>
        {
            Assert.That(gathered.Units, Has.Count.EqualTo(2), "the file nobody opened still arrives");
            Assert.That(greeting, Does.Contain("from the editor"));
            Assert.That(greeting, Does.Not.Contain("from the disk"), "the saved text was not read");
        });
    }

    /// <summary>
    /// With nothing open, gathering reads the disk — which is every command a reader types, and
    /// what the default reader has always done.
    /// </summary>
    [Test]
    public void WithNothingOpenTheDiskIsRead()
    {
        using Workspace workspace = new();

        DiagnosticBag diagnostics = new();

        SourceDiscovery.Compilation gathered =
            SourceDiscovery.Gather(workspace.At("Program.pc"), diagnostics, new DocumentStore().Reader)!;

        Assert.That(
            gathered.Units.Single(u => u.Source.FileName.EndsWith("Greeting.pc", StringComparison.Ordinal))
                          .Source.Text,
            Does.Contain("from the disk"));
    }
}
