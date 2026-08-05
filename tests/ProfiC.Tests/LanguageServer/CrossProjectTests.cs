using System.Text;
using System.Text.Json.Nodes;
using ProfiC.Cli.LanguageServer;

namespace ProfiC.Tests.LanguageServer;

/// <summary>
/// <para>Renaming and finding uses across a project boundary.</para>
/// <para><b>The asymmetry this exists to remove.</b> Asking about a file compiles the project
/// claiming it plus everything that project references — never the projects that reference
/// <em>it</em>. So renaming <c>Book</c> from the program that uses it found both files, and
/// renaming the same type from the file declaring it found one: the declaration was changed and
/// every use of it in the other project was left behind. That is worse than not offering rename,
/// because it reports success and leaves a workspace that no longer builds.</para>
/// <para>Driven through the server over its own wire rather than by calling
/// <see cref="ProfiC.Cli.LanguageServer.Rename"/> directly, because what changed is which
/// compilations get asked. Called directly, the old behavior and the new are the same function
/// returning the same answer about the one compilation it was handed.</para>
/// </summary>
[TestFixture]
public sealed class CrossProjectTests
{
    /// <summary>
    /// Two projects, one referencing the other, laid out as a reader would lay them out — which
    /// is <c>samples/library</c> in miniature.
    /// </summary>
    private sealed class Workspace : IDisposable
    {
        public Workspace()
        {
            Root = Path.Combine(Path.GetTempPath(), $"profi-c-across-{Guid.NewGuid():N}");

            Directory.CreateDirectory(Path.Combine(Root, "books"));
            Directory.CreateDirectory(Path.Combine(Root, "library"));

            Write(
                Path.Combine("books", "books.pcp"),
                """
                project Books
                    source Book.pc
                end project
                """);

            Write(
                Path.Combine("books", "Book.pc"),
                """
                public model Book

                    public string title;

                    public function Book(string called)
                        this.title = called;
                    end function

                end model
                """);

            Write(
                Path.Combine("library", "library.pcp"),
                """
                project Library
                    reference ../books/books.pcp
                    source Program.pc
                end project
                """);

            Write(
                Path.Combine("library", "Program.pc"),
                """
                shared model Program

                    function Main()
                        Book held = new Book("Dune");
                        Console.WriteLine(held.title);
                    end function

                end model
                """);
        }

        public string Root { get; }

        public string At(params string[] parts) => Path.Combine([Root, .. parts]);

        public string UriOf(params string[] parts) => Conversions.UriOf(At(parts));

        private void Write(string name, string body) =>
            File.WriteAllText(Path.Combine(Root, name), body);

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    /// <summary>Where <c>Book</c> is declared, and where the other project writes it.</summary>
    private static (int Line, int Character) DeclaredAt => (0, 13);

    private static (int Line, int Character) UsedAt => (3, 8);

    private static byte[] Framed(params JsonObject[] messages)
    {
        List<byte> all = [];

        foreach (JsonObject message in messages)
        {
            byte[] payload = Encoding.UTF8.GetBytes(message.ToJsonString());

            all.AddRange(Encoding.UTF8.GetBytes($"Content-Length: {payload.Length}\r\n\r\n"));
            all.AddRange(payload);
        }

        return [.. all];
    }

    private static JsonObject Initialize(string root) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = 1,
        ["method"] = "initialize",
        ["params"] = new JsonObject
        {
            ["workspaceFolders"] = new JsonArray(
                new JsonObject { ["uri"] = Conversions.UriOf(root), ["name"] = "workspace" }),
        },
    };

    private static JsonObject Asking(
        int id, string method, string uri, (int Line, int Character) at, JsonObject? extra = null)
    {
        JsonObject parameters = new()
        {
            ["textDocument"] = new JsonObject { ["uri"] = uri },
            ["position"] = new JsonObject
            {
                ["line"] = at.Line,
                ["character"] = at.Character,
            },
        };

        foreach ((string name, JsonNode? value) in extra ?? [])
        {
            parameters[name] = value?.DeepClone();
        }

        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method,
            ["params"] = parameters,
        };
    }

    /// <summary>Runs the server over the messages and gives back the answer to the last one.</summary>
    private static JsonNode? Answer(int id, params JsonObject[] messages)
    {
        MemoryStream written = new();

        using (ProfiC.Cli.LanguageServer.LanguageServer server =
                   new(new MemoryStream(Framed(messages)), written))
        {
            server.Run();
        }

        string answered = Encoding.UTF8.GetString(written.ToArray());

        foreach (string payload in answered.Split("Content-Length: ").Skip(1))
        {
            int start = payload.IndexOf('{', StringComparison.Ordinal);

            if (start >= 0
                && JsonNode.Parse(payload[start..]) is JsonObject message
                && (int?)message["id"] == id)
            {
                return message["result"];
            }
        }

        return null;
    }

    // ---- Renaming -----------------------------------------------------------------------------

    /// <summary>
    /// <para>Renaming a type from the file declaring it reaches the project built on it.</para>
    /// <para>This is the direction that was broken, and the one a reader is most likely to take:
    /// the file you rename a type in is usually the file that declares it.</para>
    /// </summary>
    [Test]
    public void RenamingFromTheDeclarationReachesTheProjectBuiltOnIt()
    {
        using Workspace workspace = new();

        JsonNode? answered = Answer(
            2,
            Initialize(workspace.Root),
            Asking(2, "textDocument/rename", workspace.UriOf("books", "Book.pc"), DeclaredAt,
                   new JsonObject { ["newName"] = "Volume" }));

        JsonObject changes = (JsonObject)answered!["changes"]!;

        Assert.Multiple(() =>
        {
            Assert.That(changes.ContainsKey(workspace.UriOf("books", "Book.pc")), Is.True,
                        "the file declaring it");

            Assert.That(changes.ContainsKey(workspace.UriOf("library", "Program.pc")), Is.True,
                        "and the file in the project built on it");
        });
    }

    /// <summary>
    /// <para>Every edit is offered once.</para>
    /// <para>A project referencing another compiles that project's files too, so the declaring
    /// file comes back from both builds. Handed twice, an editor writes the new name over itself
    /// — which is not a rename that merely looks untidy, it is one that produces the wrong
    /// text.</para>
    /// </summary>
    [Test]
    public void EachEditIsOfferedOnce()
    {
        using Workspace workspace = new();

        JsonNode? answered = Answer(
            2,
            Initialize(workspace.Root),
            Asking(2, "textDocument/rename", workspace.UriOf("books", "Book.pc"), DeclaredAt,
                   new JsonObject { ["newName"] = "Volume" }));

        JsonArray declaring =
            (JsonArray)((JsonObject)answered!["changes"]!)[workspace.UriOf("books", "Book.pc")]!;

        // The model's name and the constructor's, which is every place Book.pc writes it.
        Assert.That(declaring, Has.Count.EqualTo(2), declaring.ToJsonString());

        Assert.That(
            declaring.Select(edit => edit!.ToJsonString()).Distinct().Count(),
            Is.EqualTo(declaring.Count),
            "no edit should be offered twice");
    }

    /// <summary>
    /// The direction that already worked, held so that reaching further did not cost it. Asked
    /// from the program, the declaration is in a project this one references, which
    /// <see cref="ProfiC.Cli.SourceDiscovery"/> was already gathering.
    /// </summary>
    [Test]
    public void RenamingFromTheUseStillReachesTheDeclaration()
    {
        using Workspace workspace = new();

        JsonNode? answered = Answer(
            2,
            Initialize(workspace.Root),
            Asking(2, "textDocument/rename", workspace.UriOf("library", "Program.pc"), UsedAt,
                   new JsonObject { ["newName"] = "Volume" }));

        JsonObject changes = (JsonObject)answered!["changes"]!;

        Assert.Multiple(() =>
        {
            Assert.That(changes.ContainsKey(workspace.UriOf("books", "Book.pc")), Is.True);
            Assert.That(changes.ContainsKey(workspace.UriOf("library", "Program.pc")), Is.True);
        });
    }

    // ---- Finding uses -------------------------------------------------------------------------

    /// <summary>
    /// <para>The same reach, for the question that only reads.</para>
    /// <para>Undercounting here is quieter than a broken rename and worse in one way: an answer
    /// that says a type is used twice, when it is used five times in a project nobody opened, is
    /// one somebody acts on.</para>
    /// </summary>
    [Test]
    public void FindingUsesFromTheDeclarationReachesTheProjectBuiltOnIt()
    {
        using Workspace workspace = new();

        JsonNode? answered = Answer(
            2,
            Initialize(workspace.Root),
            Asking(2, "textDocument/references", workspace.UriOf("books", "Book.pc"), DeclaredAt,
                   new JsonObject
                   {
                       ["context"] = new JsonObject { ["includeDeclaration"] = true },
                   }));

        string[] files =
        [
            .. ((JsonArray)answered!).Select(one => (string)one!["uri"]!).Distinct(),
        ];

        Assert.That(files, Does.Contain(workspace.UriOf("library", "Program.pc")));
        Assert.That(files, Does.Contain(workspace.UriOf("books", "Book.pc")));
    }

    /// <summary>
    /// <para>With no folder open, the answer is what one compilation can see.</para>
    /// <para>A single file opened on its own belongs to no workspace, and there is nowhere to
    /// look for what builds on it. Answering less is right; searching the whole disk to answer
    /// more is not.</para>
    /// </summary>
    [Test]
    public void WithNoWorkspaceTheAnswerStopsAtTheOneCompilation()
    {
        using Workspace workspace = new();

        JsonNode? answered = Answer(
            2,
            new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 1,
                ["method"] = "initialize",
                ["params"] = new JsonObject(),
            },
            Asking(2, "textDocument/rename", workspace.UriOf("books", "Book.pc"), DeclaredAt,
                   new JsonObject { ["newName"] = "Volume" }));

        JsonObject changes = (JsonObject)answered!["changes"]!;

        Assert.That(changes.ContainsKey(workspace.UriOf("library", "Program.pc")), Is.False,
                    "nothing said where to look");
    }
}
