using System.Text;
using System.Text.Json.Nodes;
using ProfiC.Cli.LanguageServer;

namespace ProfiC.Tests.LanguageServer;

/// <summary>
/// <para>The server answering an editor, driven by scripted messages over a pair of streams.</para>
/// <para>No editor, no subprocess. What is held is that a program the editor holds — not the one
/// on disk — is what gets compiled, and that what the compiler said comes back as diagnostics
/// against the right file at the right place.</para>
/// <para>The clearing half is here too, and it is the one that is easy to leave out and
/// impossible to notice: a diagnostic stays in the panel until an empty list arrives for its
/// file. A server that simply stopped mentioning a file it had fixed would leave the reader
/// looking at an error that is no longer there.</para>
/// </summary>
[TestFixture]
public sealed class LanguageServerTests
{
    private static readonly TimeSpan LongEnough = TimeSpan.FromSeconds(10);

    /// <summary>Two files that compile together, so a compilation is more than the one file.</summary>
    private sealed class Workspace : IDisposable
    {
        public Workspace()
        {
            Folder = Path.Combine(Path.GetTempPath(), $"profi-c-lsp-{Guid.NewGuid():N}");
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
                        yield "hello";
                    end function
                end model
                """);
        }

        public string Folder { get; }

        public string At(string name) => Path.Combine(Folder, name);

        public string UriOf(string name) => Conversions.UriOf(At(name));

        public void Write(string name, string body) =>
            File.WriteAllText(Path.Combine(Folder, name), body);

        public void Dispose() => Directory.Delete(Folder, recursive: true);
    }

    /// <summary>A stream that records what is written, readable while it is being written to.</summary>
    private sealed class Recording : Stream
    {
        private readonly List<byte> _written = [];
        private readonly Lock _guard = new();

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length
        {
            get
            {
                lock (_guard)
                {
                    return _written.Count;
                }
            }
        }

        public override long Position
        {
            get => Length;
            set => throw new NotSupportedException();
        }

        public string Text()
        {
            lock (_guard)
            {
                return Encoding.UTF8.GetString([.. _written]);
            }
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            lock (_guard)
            {
                _written.AddRange(buffer.AsSpan(offset, count).ToArray());
            }
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }

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

    private static JsonObject Open(string uri, string text) => new()
    {
        ["jsonrpc"] = "2.0",
        ["method"] = "textDocument/didOpen",
        ["params"] = new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "profi-c",
                ["version"] = 1,
                ["text"] = text,
            },
        },
    };

    /// <summary>
    /// Runs the server over the scripted messages and waits for the output to settle, or gives up.
    /// The server is left undisposed until the wait is over, since disposing cancels what is still
    /// being analyzed.
    /// </summary>
    private static string Answering(byte[] script, Func<string, bool> until)
    {
        Recording written = new();

        using ProfiC.Cli.LanguageServer.LanguageServer server =
            new(new MemoryStream(script), written, TimeSpan.FromMilliseconds(20));

        server.Run();

        DateTime giveUp = DateTime.UtcNow + LongEnough;

        while (DateTime.UtcNow < giveUp && !until(written.Text()))
        {
            Thread.Sleep(10);
        }

        return written.Text();
    }

    /// <summary>Every published set of diagnostics, by the file it was published for.</summary>
    private static Dictionary<string, JsonArray> Published(string answered)
    {
        Dictionary<string, JsonArray> byUri = [];

        foreach (string payload in answered.Split("Content-Length: ").Skip(1))
        {
            int start = payload.IndexOf('{', StringComparison.Ordinal);

            if (start < 0 || JsonNode.Parse(payload[start..]) is not JsonObject message)
            {
                continue;
            }

            if ((string?)message["method"] == "textDocument/publishDiagnostics"
                && message["params"] is JsonObject parameters
                && (string?)parameters["uri"] is { } uri)
            {
                byUri[uri] = (JsonArray)parameters["diagnostics"]!.DeepClone();
            }
        }

        return byUri;
    }

    [Test]
    public void ItSaysWhatItCanDo()
    {
        string answered = Answering(
            Framed(new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 1,
                ["method"] = "initialize",
                ["params"] = new JsonObject(),
            }),
            text => text.Contains("capabilities", StringComparison.Ordinal));

        Assert.Multiple(() =>
        {
            Assert.That(answered, Does.Contain("\"id\":1"));
            Assert.That(answered, Does.Contain("\"openClose\":true"));
            Assert.That(answered, Does.Contain("\"name\":\"profi-c\""));
        });
    }

    /// <summary>A request naming something this does not do is refused, rather than ignored.</summary>
    [Test]
    public void ARequestItDoesNotKnowIsRefused()
    {
        string answered = Answering(
            Framed(new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 7,

                // Something this genuinely does not do. Worth choosing carefully: the first
                // version of this named a method that was later implemented, and the test then
                // failed for having come true.
                ["method"] = "textDocument/foldingRange",
                ["params"] = new JsonObject(),
            }),
            text => text.Contains("error", StringComparison.Ordinal));

        Assert.Multiple(() =>
        {
            Assert.That(answered, Does.Contain("\"id\":7"));
            Assert.That(answered, Does.Contain(LspConnection.Fault.MethodNotFound.ToString(
                System.Globalization.CultureInfo.InvariantCulture)));
        });
    }

    /// <summary>
    /// <para>A notification it does not know is ignored in silence, which the protocol requires.
    /// </para>
    /// <para>An editor sends several no server has to implement. Refusing one would be answering
    /// something that carried no id to answer against.</para>
    /// </summary>
    [Test]
    public void ANotificationItDoesNotKnowIsIgnored()
    {
        string answered = Answering(
            Framed(new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = "workspace/didChangeWatchedFiles",
                ["params"] = new JsonObject(),
            }),
            _ => false);

        Assert.That(answered, Is.Empty);
    }

    /// <summary>
    /// <para>What the editor holds is what gets compiled.</para>
    /// <para>The whole point. The file on disk is fine; the buffer has a number too large to
    /// hold in it, and that is what is reported — at the place in the buffer where it sits,
    /// counted from zero as the protocol counts.</para>
    /// </summary>
    [Test]
    public void TheEditorsTextIsWhatIsCompiled()
    {
        using Workspace workspace = new();

        string answered = Answering(
            Framed(Open(
                workspace.UriOf("Program.pc"),
                """
                shared model Program
                    function Main()
                        integer n = 9223372036854775808;
                        Console.WriteLine(Greeting.Words());
                    end function
                end model
                """)),
            text => text.Contains("PC0026", StringComparison.Ordinal));

        JsonArray found = Published(answered)[workspace.UriOf("Program.pc")];

        Assert.Multiple(() =>
        {
            Assert.That(found, Has.Count.EqualTo(1));
            Assert.That((string?)found[0]!["code"], Is.EqualTo("PC0026"));
            Assert.That((int?)found[0]!["severity"], Is.EqualTo(1));
            Assert.That((string?)found[0]!["source"], Is.EqualTo("profi-c"));

            // Line 3 and column 21 as the compiler counts, which is 2 and 20 here.
            Assert.That((int?)found[0]!["range"]!["start"]!["line"], Is.EqualTo(2));
            Assert.That((int?)found[0]!["range"]!["start"]!["character"], Is.EqualTo(20));
        });
    }

    /// <summary>
    /// <para>A file with nothing wrong is published as having nothing wrong.</para>
    /// <para>Silence does not clear a panel. A file that has been fixed, or that never had
    /// anything, has to be told so with an empty list — including one the reader never opened,
    /// which arrives because a program is a compilation rather than a file.</para>
    /// </summary>
    [Test]
    public void EveryFileInTheProgramIsPublishedIncludingTheCleanOnes()
    {
        using Workspace workspace = new();

        string answered = Answering(
            Framed(Open(workspace.UriOf("Program.pc"), File.ReadAllText(workspace.At("Program.pc")))),
            text => text.Contains("Greeting.pc", StringComparison.Ordinal));

        Dictionary<string, JsonArray> published = Published(answered);

        Assert.Multiple(() =>
        {
            Assert.That(published[workspace.UriOf("Program.pc")], Is.Empty);
            Assert.That(
                published[workspace.UriOf("Greeting.pc")],
                Is.Empty,
                "the file nobody opened was published as clean");
        });
    }

    /// <summary>
    /// <para>Formatting answers with one edit per line that changed, not one for the file.</para>
    /// <para><b>The difference is what happens to the reader.</b> Replacing the whole document
    /// is shorter to write and throws the cursor to the top, collapses the folding, and puts a
    /// whole-file change in the undo history for a run that moved one line. A list of small
    /// edits leaves everything it did not have to touch.</para>
    /// </summary>
    [Test]
    public void FormattingEditsOnlyTheLinesThatMoved()
    {
        using Workspace workspace = new();

        string answered = Answering(
            Framed(
                Open(
                    workspace.UriOf("Program.pc"),
                    """
                    shared model Program
                        function Main()
                    Console.WriteLine(Greeting.Words());
                        end function
                    end model
                    """),
                new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = 9,
                    ["method"] = "textDocument/formatting",
                    ["params"] = new JsonObject
                    {
                        ["textDocument"] = new JsonObject
                        {
                            ["uri"] = workspace.UriOf("Program.pc"),
                        },
                        ["options"] = new JsonObject(),
                    },
                }),
            text => text.Contains("\"id\":9", StringComparison.Ordinal));

        JsonArray edits = (JsonArray)Answered(answered, id: 9)!["result"]!;

        Assert.Multiple(() =>
        {
            Assert.That(edits, Has.Count.EqualTo(1), "only the one line that was wrong");

            Assert.That((int?)edits[0]!["range"]!["start"]!["line"], Is.EqualTo(2));
            Assert.That(
                (string?)edits[0]!["newText"],
                Is.EqualTo("        Console.WriteLine(Greeting.Words());"));
        });
    }

    /// <summary>
    /// <para>A new line inside a bracket is placed as it is begun.</para>
    /// <para><b>The half of the layout rule an editor cannot work out for itself.</b> Left alone
    /// it copies the indent of the line above, which is right everywhere except inside an open
    /// bracket — and inside one it is wrong on every line, so a reader is shown where their line
    /// belongs only after they have finished typing it and run the formatter. Answering here is
    /// what makes the two the same rule rather than two rules that agree afterwards.</para>
    /// <para>The caret sits on an empty line, which is the case worth pinning: formatting a file
    /// writes an empty line empty, so an answer taken from that would send the reader to the left
    /// margin — further from where they belong than the editor's own guess.</para>
    /// </summary>
    [Test]
    public void ANewLineInsideABracketIsPlacedAsItIsTyped()
    {
        using Workspace workspace = new();

        // Enter pressed after the opening line of a call, so line 3 is empty and the caret is on
        // it. The bracket sits at column 25, so the line carrying it on belongs at 28.
        string answered = Answering(
            Framed(
                Open(
                    workspace.UriOf("Program.pc"),
                    "shared model Program\n"
                    + "    function Main()\n"
                    + "        Console.WriteLine(\"text \"\n"
                    + "\n"
                    + "    end function\n"
                    + "end model"),
                new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = 11,
                    ["method"] = "textDocument/onTypeFormatting",
                    ["params"] = new JsonObject
                    {
                        ["textDocument"] = new JsonObject
                        {
                            ["uri"] = workspace.UriOf("Program.pc"),
                        },
                        ["position"] = new JsonObject { ["line"] = 3, ["character"] = 0 },
                        ["ch"] = "\n",
                        ["options"] = new JsonObject(),
                    },
                }),
            text => text.Contains("\"id\":11", StringComparison.Ordinal));

        JsonArray edits = (JsonArray)Answered(answered, id: 11)!["result"]!;

        Assert.Multiple(() =>
        {
            Assert.That(edits, Has.Count.EqualTo(1), "the line the caret is on, and no other");

            Assert.That((int?)edits[0]!["range"]!["start"]!["line"], Is.EqualTo(3));
            Assert.That((int?)edits[0]!["range"]!["end"]!["line"], Is.EqualTo(3));

            Assert.That(
                (string?)edits[0]!["newText"],
                Is.EqualTo(new string(' ', 28)),
                "a line carrying another on takes the first tab stop past its bracket");
        });
    }

    /// <summary>
    /// <para>The questions about a place answer, asked the way an editor asks them.</para>
    /// <para><b>Every other test here builds its URI with <c>Conversions.UriOf</c>, which is this
    /// codebase talking to itself.</b> VS Code escapes the colon after a drive letter; nothing
    /// here ever did; and the whole suite stayed green while the server could not open a file on
    /// Windows at all. So this one writes the URI the way the editor writes it, and asks the four
    /// questions that go through the compile-per-question path rather than the publish path — the
    /// half that a test using our own URIs cannot tell is broken.</para>
    /// </summary>
    [Test]
    public void TheQuestionsAboutAPlaceAnswerForAnEditorsUri()
    {
        using Workspace workspace = new();

        // As VS Code sends it: lowercase drive, colon escaped.
        string uri = workspace.UriOf("Program.pc")
            .Replace("///C:", "///c%3A", StringComparison.OrdinalIgnoreCase)
            .Replace("///D:", "///d%3A", StringComparison.OrdinalIgnoreCase);

        JsonObject Asking(int id, string method, int line, int character) => new()
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method,
            ["params"] = new JsonObject
            {
                ["textDocument"] = new JsonObject { ["uri"] = uri },
                ["position"] = new JsonObject
                {
                    ["line"] = line,
                    ["character"] = character,
                },
            },
        };

        string answered = Answering(
            Framed(
                Open(uri, File.ReadAllText(workspace.At("Program.pc"))),

                // Line 3, on 'Greeting' in 'Console.WriteLine(Greeting.Words());'.
                Asking(20, "textDocument/hover", 2, 27),
                Asking(21, "textDocument/definition", 2, 27),
                Asking(22, "textDocument/documentHighlight", 2, 27),
                new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = 23,
                    ["method"] = "textDocument/semanticTokens/full",
                    ["params"] = new JsonObject
                    {
                        ["textDocument"] = new JsonObject { ["uri"] = uri },
                    },
                },

                // Just past the dot in 'Greeting.Words()', and again where a bare name goes.
                Asking(24, "textDocument/completion", 2, 35),
                Asking(25, "textDocument/completion", 2, 26)),
            text => text.Contains("\"id\":25", StringComparison.Ordinal));

        Assert.Multiple(() =>
        {
            Assert.That(
                Answered(answered, 20)?["result"],
                Is.Not.Null.And.Not.EqualTo(JsonValue.Create((object?)null)),
                "hover said nothing");

            Assert.That(
                (JsonArray?)Answered(answered, 21)?["result"],
                Is.Not.Null.And.Not.Empty,
                "definition led nowhere");

            Assert.That(
                (JsonArray?)Answered(answered, 22)?["result"],
                Is.Not.Null.And.Not.Empty,
                "nothing was marked");

            Assert.That(
                (JsonArray?)Answered(answered, 23)?["result"]?["data"],
                Is.Not.Null.And.Not.Empty,
                "nothing was colored");

            Assert.That(
                (JsonArray?)Answered(answered, 24)?["result"],
                Is.Not.Null.And.Not.Empty,
                "nothing was offered after the dot");

            Assert.That(
                (JsonArray?)Answered(answered, 25)?["result"],
                Is.Not.Null.And.Not.Empty,
                "nothing was offered for a bare name");
        });
    }

    /// <summary>
    /// <para>Completion through the server, on the buffer rather than on what is saved.</para>
    /// <para><b>Everything else about completion is tested by calling it directly, and that is the
    /// half that cannot fail the way it fails in an editor.</b> Called directly it reads a file
    /// from disk that parses. Through the server it reads a buffer that does not — a name half
    /// typed, on a line that is not a statement — and the file on disk still holds whatever was
    /// there before. Those are different programs, and only one of them is what somebody is
    /// looking at.</para>
    /// </summary>
    [Test]
    public void CompletionAnswersAboutTheBufferAndNotTheSavedFile()
    {
        using Workspace workspace = new();

        // On disk this file has none of these names in it. That is the point: what is offered has
        // to come from the buffer below, which is what the editor holds.
        string typing = """
            shared model Program
                function Main()
                    Random counter = new Random();

                    Console.WriteLine("one");

                    coun
                    counter.
                end function
            end model
            """;

        JsonObject Asking(int id, int line, int character) => new()
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = "textDocument/completion",
            ["params"] = new JsonObject
            {
                ["textDocument"] = new JsonObject { ["uri"] = workspace.UriOf("Program.pc") },
                ["position"] = new JsonObject { ["line"] = line, ["character"] = character },
            },
        };

        string answered = Answering(
            Framed(
                Open(workspace.UriOf("Program.pc"), typing),

                // Just past 'coun' on line 7, and just past the dot on line 8.
                Asking(30, 6, 12),
                Asking(31, 7, 16)),
            text => text.Contains("\"id\":31", StringComparison.Ordinal));

        string[] Labels(int id) =>
        [
            .. ((JsonArray?)Answered(answered, id)?["result"] ?? [])
                .Select(item => (string?)item!["label"] ?? string.Empty),
        ];

        Assert.Multiple(() =>
        {
            Assert.That(Labels(30), Does.Contain("counter"), "a bare name being typed");
            Assert.That(Labels(31), Does.Contain("Next"), "and what the local it names answers");
        });
    }

    /// <summary>The reply carrying an id, parsed back out of what was written.</summary>
    private static JsonObject? Answered(string answered, int id)
    {
        foreach (string payload in answered.Split("Content-Length: ").Skip(1))
        {
            int start = payload.IndexOf('{', StringComparison.Ordinal);

            if (start >= 0
                && JsonNode.Parse(payload[start..]) is JsonObject message
                && (int?)message["id"] == id)
            {
                return message;
            }
        }

        return null;
    }

    /// <summary>
    /// <para>A mistake in one file is reported against that file, wherever the reader is
    /// looking.</para>
    /// <para>A program is a compilation. Editing <c>Program.pc</c> can break <c>Greeting.pc</c>,
    /// and reporting it against the open file would point at a line that is not wrong.</para>
    /// </summary>
    [Test]
    public void AMistakeIsReportedAgainstTheFileItIsIn()
    {
        using Workspace workspace = new();

        workspace.Write(
            "Greeting.pc",
            """
            shared model Greeting
                public shared string function Words()
                    yield 42;
                end function
            end model
            """);

        string answered = Answering(
            Framed(Open(workspace.UriOf("Program.pc"), File.ReadAllText(workspace.At("Program.pc")))),
            text => text.Contains("PC03", StringComparison.Ordinal));

        Dictionary<string, JsonArray> published = Published(answered);

        Assert.Multiple(() =>
        {
            Assert.That(
                published[workspace.UriOf("Greeting.pc")],
                Is.Not.Empty,
                "the file with the mistake in it");

            Assert.That(
                published[workspace.UriOf("Program.pc")],
                Is.Empty,
                "and not the one the reader has open");
        });
    }
}
