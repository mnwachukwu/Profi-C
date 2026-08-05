using System.Text.Json.Nodes;
using ProfiC.Cli.LanguageServer;

namespace ProfiC.Tests.LanguageServer;

/// <summary>
/// <para>Where the editor said it is working, read out of what it sent.</para>
/// <para><b>Every URI here is written the way a client sends one</b>, not built by calling the
/// compiler's own encoder. Encoding a path and decoding it again agrees with itself whatever
/// either half does, and that agreement is exactly what let the server go months unable to open a
/// file on Windows — the escaped drive in <c>file:///d%3A/</c> is the shape VS Code sends and the
/// shape a round trip never produces.</para>
/// </summary>
[TestFixture]
public sealed class WorkspaceFoldersTests
{
    private static JsonObject Folder(string uri) => new() { ["uri"] = uri, ["name"] = "named" };

    /// <summary>
    /// <para>The folders read, compared the way the rest of the compiler compares a path.</para>
    /// <para>Not ordinally, and the difference is real rather than pedantic: an escaped drive
    /// decodes to <c>d:\</c> where the same folder written any other way is <c>D:\</c>, and
    /// nothing normalizes the letter because nothing can without asking the disk. Every path in
    /// the compiler is matched through <see cref="ProfiC.Cli.SourceDiscovery.PathComparer"/>,
    /// which follows the platform, so that is what a folder is the same as.</para>
    /// </summary>
    private static void AssertFolders(WorkspaceFolders open, params string[] expected) =>
        // Cast, because a StringComparer is both kinds of comparer at once and the constraint
        // takes either. Equality is the question being asked of two lists of folders.
        Assert.That(
            open.Folders,
            Is.EqualTo(expected).Using(
                (IEqualityComparer<string>)ProfiC.Cli.SourceDiscovery.PathComparer));

    private static readonly bool OnWindows = OperatingSystem.IsWindows();

    /// <summary>A URI and the path it names, in whichever shape this platform has.</summary>
    private static (string Uri, string Path) Somewhere =>
        OnWindows
            ? ("file:///d%3A/Repos/Profi-C", @"D:\Repos\Profi-C")
            : ("file:///home/matt/Profi-C", "/home/matt/Profi-C");

    private static (string Uri, string Path) Elsewhere =>
        OnWindows
            ? ("file:///d%3A/Repos/Other", @"D:\Repos\Other")
            : ("file:///home/matt/Other", "/home/matt/Other");

    // ---- What the editor sent ---------------------------------------------------------------

    [Test]
    public void TheCurrentFieldIsRead()
    {
        (string uri, string path) = Somewhere;

        AssertFolders(
            WorkspaceFolders.Opened(
                new JsonObject { ["workspaceFolders"] = new JsonArray(Folder(uri)) }),
            path);
    }

    /// <summary>
    /// Both older fields still answer, because which one arrives says what the client is rather
    /// than what the reader did. A client old enough to send only <c>rootPath</c> sends a plain
    /// path rather than a URI, which is the whole difference between the two.
    /// </summary>
    [Test]
    public void TheDeprecatedFieldsAreReadWhereTheCurrentOneIsAbsent()
    {
        (string uri, string path) = Somewhere;

        Assert.Multiple(() =>
        {
            AssertFolders(WorkspaceFolders.Opened(new JsonObject { ["rootUri"] = uri }), path);
            AssertFolders(WorkspaceFolders.Opened(new JsonObject { ["rootPath"] = path }), path);
        });
    }

    /// <summary>
    /// A client sending both an empty list and a root is contradicting itself, and the root is
    /// the half of that with a folder in it.
    /// </summary>
    [Test]
    public void AnEmptyListFallsThroughToTheOlderFields()
    {
        (string uri, string path) = Somewhere;

        AssertFolders(
            WorkspaceFolders.Opened(new JsonObject
            {
                ["workspaceFolders"] = new JsonArray(),
                ["rootUri"] = uri,
            }),
            path);
    }

    [Test]
    public void SeveralFoldersAreAllKept()
    {
        (string first, string firstPath) = Somewhere;
        (string second, string secondPath) = Elsewhere;

        // In the order the editor named them, which is the order a search across them takes.
        AssertFolders(
            WorkspaceFolders.Opened(new JsonObject
            {
                ["workspaceFolders"] = new JsonArray(Folder(first), Folder(second)),
            }),
            firstPath,
            secondPath);
    }

    /// <summary>
    /// An editor may hold one folder twice over — a workspace file listing it, and the folder
    /// itself. A duplicate would mean every search across the folders did that one twice.
    /// </summary>
    [Test]
    public void OneFolderNamedTwiceIsKeptOnce()
    {
        (string uri, string path) = Somewhere;

        AssertFolders(
            WorkspaceFolders.Opened(new JsonObject
            {
                ["workspaceFolders"] = new JsonArray(Folder(uri), Folder(uri)),
            }),
            path);
    }

    /// <summary>
    /// A client that named nothing leaves nothing. A single file opened on its own belongs to no
    /// folder, and there is nothing to invent for it.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public void AClientThatNamedNoFolderLeavesNone(bool sentAnything) => Assert.That(
        WorkspaceFolders.Opened(sentAnything ? new JsonObject() : null).Folders,
        Is.Empty);

    /// <summary>
    /// Something that is not a file is not a folder. An editor can open a buffer that lives
    /// nowhere, and the scheme is how it says so.
    /// </summary>
    [Test]
    public void SomethingThatNamesNoPlaceOnDiskIsNotAFolder() => Assert.That(
        WorkspaceFolders.Opened(new JsonObject
        {
            ["workspaceFolders"] = new JsonArray(Folder("untitled:Untitled-1")),
        }).Folders,
        Is.Empty);

    // ---- Whether a file is in one of them ----------------------------------------------------

    /// <summary>
    /// <para>The case that a text comparison gets wrong.</para>
    /// <para><c>D:\Repos\Profi</c> begins the same way as <c>D:\Repos\Profi-C</c> and is not
    /// inside it. A prefix test answers yes, and would put a neighbouring checkout inside the one
    /// being searched.</para>
    /// </summary>
    [Test]
    public void AFolderBeginningTheSameWayIsNotInside()
    {
        (_, string path) = Somewhere;

        WorkspaceFolders open = new([path]);

        Assert.Multiple(() =>
        {
            Assert.That(open.Holds(Path.Combine(path, "samples", "hello.pc")), Is.True);
            Assert.That(open.Holds(path), Is.True, "the folder itself is inside it");
            Assert.That(open.Holds(path + "-Editors"), Is.False);
        });
    }

    [Test]
    public void SomethingAboveOrBesideTheFolderIsOutside()
    {
        (_, string inside) = Somewhere;
        (_, string beside) = Elsewhere;

        WorkspaceFolders open = new([inside]);

        Assert.Multiple(() =>
        {
            Assert.That(open.Holds(beside), Is.False);

            Assert.That(open.Holds(Path.GetDirectoryName(inside)!), Is.False,
                        "the folder above is not in the folder");
        });
    }

    /// <summary>
    /// With no folder open nothing qualifies, which is the reading that keeps a caller from
    /// treating "the reader opened a single file" as "everything counts".
    /// </summary>
    [Test]
    public void NoFolderHoldsNothing() =>
        Assert.That(WorkspaceFolders.None.Holds(Somewhere.Path), Is.False);

    // ---- Folders added and removed while the editor is open ----------------------------------

    [Test]
    public void AFolderAddedIsKept()
    {
        (string uri, string path) = Elsewhere;
        (_, string already) = Somewhere;

        AssertFolders(
            new WorkspaceFolders([already]).After(
                new JsonObject { ["added"] = new JsonArray(Folder(uri)) }),
            already,
            path);
    }

    [Test]
    public void AFolderRemovedIsGone()
    {
        (string uri, string path) = Somewhere;
        (_, string kept) = Elsewhere;

        AssertFolders(
            new WorkspaceFolders([path, kept]).After(
                new JsonObject { ["removed"] = new JsonArray(Folder(uri)) }),
            kept);
    }

    /// <summary>
    /// A folder named on both sides is how an editor says one moved, so removals are applied
    /// first and it ends up present rather than absent.
    /// </summary>
    [Test]
    public void AFolderOnBothSidesOfTheChangeStays()
    {
        (string uri, string path) = Somewhere;

        AssertFolders(
            new WorkspaceFolders([path]).After(new JsonObject
            {
                ["removed"] = new JsonArray(Folder(uri)),
                ["added"] = new JsonArray(Folder(uri)),
            }),
            path);
    }

    [Test]
    public void AChangeThatSaysNothingChangesNothing()
    {
        (_, string path) = Somewhere;

        AssertFolders(new WorkspaceFolders([path]).After(null), path);
    }

    // ---- The server reading them off the wire ------------------------------------------------

    /// <summary>Messages framed the way the protocol frames them, ready to be read.</summary>
    private static Stream Framed(params JsonObject[] messages)
    {
        MemoryStream script = new();

        foreach (JsonObject message in messages)
        {
            byte[] payload = System.Text.Encoding.UTF8.GetBytes(message.ToJsonString());
            byte[] header = System.Text.Encoding.UTF8.GetBytes(
                $"Content-Length: {payload.Length}\r\n\r\n");

            script.Write(header);
            script.Write(payload);
        }

        script.Position = 0;
        return script;
    }

    /// <summary>The folders the server ended up holding, after being sent these messages.</summary>
    private static WorkspaceFolders HeldAfter(params JsonObject[] messages)
    {
        using ProfiC.Cli.LanguageServer.LanguageServer server =
            new(Framed(messages), Stream.Null);

        server.Run();

        return server.OpenFolders;
    }

    /// <summary>
    /// <para>The server reads them, which the tests above do not establish.</para>
    /// <para>Everything above asks whether a message can be read; this asks whether the server
    /// reads it — a field parsed correctly and never assigned would satisfy all of them and leave
    /// the server knowing nothing about where the reader is working.</para>
    /// </summary>
    [Test]
    public void TheServerHoldsWhatInitializeNamed()
    {
        (string uri, string path) = Somewhere;

        AssertFolders(
            HeldAfter(new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 1,
                ["method"] = "initialize",
                ["params"] = new JsonObject
                {
                    ["workspaceFolders"] = new JsonArray(Folder(uri)),
                },
            }),
            path);
    }

    /// <summary>
    /// And keeps them current. A set of folders that was right when the session began and wrong
    /// for the rest of it is the failure a reader cannot see: they open the folder they wanted
    /// and nothing about it can be answered, with nothing saying why.
    /// </summary>
    [Test]
    public void TheServerFollowsAFolderOpenedLater()
    {
        (string first, string firstPath) = Somewhere;
        (string second, string secondPath) = Elsewhere;

        AssertFolders(
            HeldAfter(
                new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = 1,
                    ["method"] = "initialize",
                    ["params"] = new JsonObject
                    {
                        ["workspaceFolders"] = new JsonArray(Folder(first)),
                    },
                },
                new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["method"] = "workspace/didChangeWorkspaceFolders",
                    ["params"] = new JsonObject
                    {
                        ["event"] = new JsonObject
                        {
                            ["added"] = new JsonArray(Folder(second)),
                            ["removed"] = new JsonArray(),
                        },
                    },
                }),
            firstPath,
            secondPath);
    }

    /// <summary>
    /// A client sends the folders only to a server that says it supports them, and the change
    /// notification only to one that says it wants it. Reading both is worth nothing if neither
    /// is ever sent.
    /// </summary>
    [Test]
    public void TheServerAsksToBeToldAboutFolders()
    {
        MemoryStream written = new();

        using (ProfiC.Cli.LanguageServer.LanguageServer server = new(
                   Framed(new JsonObject
                   {
                       ["jsonrpc"] = "2.0",
                       ["id"] = 1,
                       ["method"] = "initialize",
                       ["params"] = new JsonObject(),
                   }),
                   written))
        {
            server.Run();
        }

        string answered = System.Text.Encoding.UTF8.GetString(written.ToArray());

        Assert.Multiple(() =>
        {
            Assert.That(answered, Does.Contain("\"supported\":true"));
            Assert.That(answered, Does.Contain("\"changeNotifications\":true"));
        });
    }
}
