using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;
using ProfiC.Cli.Debugging;

namespace ProfiC.Tests.Debugging;

/// <summary>
/// <para>A whole debugging session, driven the way an editor drives one.</para>
/// <para>Scripted rather than mocked: the requests below are the ones VS Code actually sends, in
/// the order it sends them, and what comes back is read off the wire. Anything less would test
/// that the adapter agrees with my idea of the protocol.</para>
/// </summary>
[TestFixture]
public sealed class DebugAdapterTests
{
    /// <summary>One file of a program, as it is written to disk before the session starts.</summary>
    private readonly record struct Written(string Name, string Source);

    /// <summary>Runs a session over a one-file program named <c>Program.pc</c>.</summary>
    private static List<JsonObject> Session(string program, params string[] requests) =>
        SessionOver([new Written("Program.pc", program)], requests);

    /// <summary>
    /// <para>Runs a session and gives back everything sent out.</para>
    /// <para>The requests go in through a live pipe rather than a buffer, and the ones written
    /// after <c>configurationDone</c> wait until the program has actually stopped. That is what
    /// an editor does — it acts on the <c>stopped</c> event — and a scripted buffer does not:
    /// the adapter would read and answer the whole script before the program reached its
    /// breakpoint, and every answer would be about a program that was still running.</para>
    /// <para>The files go in a folder of their own, because launching gathers the shared code
    /// beside what it was pointed at. Written straight into the temporary directory they would
    /// be compiled together with every other test's program that happened to be there.</para>
    /// <para><c>PROGRAM</c> in a request stands for the first file, which is the one launched,
    /// and <c>OTHER</c> for the second. Written as paths an editor would send: absolute, and
    /// escaped for JSON.</para>
    /// </summary>
    private static List<JsonObject> SessionOver(
        IReadOnlyList<Written> files,
        IReadOnlyList<string> requests)
    {
        string folder = Path.Combine(Path.GetTempPath(), $"profi-c-debug-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);

        try
        {
            string[] paths = [.. files.Select(file => Path.Combine(folder, file.Name))];

            for (int i = 0; i < files.Count; i++)
            {
                File.WriteAllText(paths[i], files[i].Source);
            }

            AnonymousPipeServerStream toAdapter = new(PipeDirection.Out);
            using AnonymousPipeClientStream adapterReads =
                new(PipeDirection.In, toAdapter.ClientSafePipeHandle);

            MemoryStream sent = new();
            using DebugAdapter adapter = new(adapterReads, sent);

            Thread answering = new(adapter.Run) { IsBackground = true };
            answering.Start();

            foreach (string request in requests)
            {
                string filled = Fill(request, paths);
                int settled = TimesSettled(sent);

                Write(toAdapter, filled);

                // Anything that lets the program move is waited on before the next request goes
                // in. Writing them back to back would let a later one overtake: a step and a
                // continue sent together are read together, and the continue changes what the
                // step was going to do before the step has done it.
                if (LetsTheProgramMove(filled))
                {
                    WaitForItToSettleAgain(sent, settled);
                }
            }

            toAdapter.Dispose();
            answering.Join(TimeSpan.FromSeconds(5));

            return ReadAll(sent.ToArray());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static string Fill(string request, IReadOnlyList<string> paths)
    {
        string filled = request.Replace("PROGRAM", ForJson(paths[0]), StringComparison.Ordinal);

        return paths.Count > 1
            ? filled.Replace("OTHER", ForJson(paths[1]), StringComparison.Ordinal)
            : filled;
    }

    private static string ForJson(string path) =>
        path.Replace("\\", "\\\\", StringComparison.Ordinal);

    private static void Write(Stream to, string request)
    {
        byte[] payload = Encoding.UTF8.GetBytes(request);

        to.Write(Encoding.UTF8.GetBytes($"Content-Length: {payload.Length}\r\n\r\n"));
        to.Write(payload);
        to.Flush();
    }

    /// <summary>The requests that release the program, after which it will stop or end again.</summary>
    private static bool LetsTheProgramMove(string request) =>
        new[] { "configurationDone", "continue", "next", "stepIn", "stepOut" }
            .Any(command => request.Contains($"\"{command}\"", StringComparison.Ordinal));

    /// <summary>
    /// How many times the program has come to rest, counting a stop and an ending alike — both
    /// are the program no longer moving, and either may be what a resume leads to.
    /// </summary>
    private static int TimesSettled(MemoryStream sent)
    {
        List<JsonObject> far = ReadAll(sent.ToArray());

        return EventsNamed(far, "stopped").Count() + EventsNamed(far, "terminated").Count();
    }

    /// <summary>
    /// <para>Waits for the program to come to rest once more than it had, or gives up.</para>
    /// <para>Counted rather than merely looked for, because after the first stop there is always
    /// one in the record. "Has it stopped" is answered yes by the stop before the one being
    /// waited on, which would let every later request go in while the program was still running.
    /// </para>
    /// </summary>
    private static void WaitForItToSettleAgain(MemoryStream sent, int settled)
    {
        DateTime giveUp = DateTime.UtcNow.AddSeconds(5);

        while (DateTime.UtcNow < giveUp && TimesSettled(sent) <= settled)
        {
            Thread.Sleep(20);
        }
    }

    private static List<JsonObject> ReadAll(byte[] bytes)
    {
        DapConnection reading = new(new MemoryStream(bytes), new MemoryStream());
        List<JsonObject> messages = [];

        while (reading.Read() is { } message)
        {
            messages.Add(message);
        }

        return messages;
    }

    private static JsonObject? ResponseTo(List<JsonObject> sent, string command) =>
        sent.FirstOrDefault(m =>
            (string?)m["type"] == "response" && (string?)m["command"] == command);

    private static IEnumerable<JsonObject> EventsNamed(List<JsonObject> sent, string name) =>
        sent.Where(m => (string?)m["type"] == "event" && (string?)m["event"] == name);

    private const string Counting = """
        shared model Program
            function Main()
                integer total = 0;
                total = total + 5;
                Console.WriteLine(total);
            end function
        end model
        """;

    /// <summary>
    /// <para>The handshake, in the order it happens.</para>
    /// <para><c>initialize</c> is answered with what the adapter can do, and then an
    /// <c>initialized</c> event goes out — which is the editor's cue to send breakpoints. Get
    /// that order wrong and breakpoints arrive after the program has started, which looks like
    /// a debugger that works on the second run of a session and not the first.</para>
    /// </summary>
    [Test]
    public void TheHandshakeAnswersThenAsksForBreakpoints()
    {
        List<JsonObject> sent = Session(
            Counting,
            """{"seq":1,"type":"request","command":"initialize","arguments":{}}""",
            """{"seq":2,"type":"request","command":"disconnect","arguments":{}}""");

        Assert.Multiple(() =>
        {
            JsonObject? answer = ResponseTo(sent, "initialize");

            Assert.That(answer, Is.Not.Null);
            Assert.That((bool?)answer!["body"]!["supportsConfigurationDoneRequest"], Is.True);

            Assert.That(EventsNamed(sent, "initialized").Count(), Is.EqualTo(1));

            Assert.That(
                sent.IndexOf(answer),
                Is.LessThan(sent.IndexOf(EventsNamed(sent, "initialized").First())),
                "the answer comes before the cue, or the editor has nothing to answer to");
        });
    }

    /// <summary>A program that will not compile is refused, with the diagnostics said.</summary>
    [Test]
    public void LaunchingSomethingThatWillNotCompileIsRefused()
    {
        List<JsonObject> sent = Session(
            """
            shared model Program
                integer function Main()
                end function
            end model
            """,
            """{"seq":1,"type":"request","command":"initialize","arguments":{}}""",
            """{"seq":2,"type":"request","command":"launch","arguments":{"program":"PROGRAM"}}""",
            """{"seq":3,"type":"request","command":"disconnect","arguments":{}}""");

        JsonObject? answer = ResponseTo(sent, "launch");

        Assert.Multiple(() =>
        {
            Assert.That((bool?)answer!["success"], Is.False);
            Assert.That((string?)answer["message"], Does.Contain("PC0404"));
        });
    }

    /// <summary>
    /// <para>Several complaints about one name are told apart.</para>
    /// <para>A name written five times and never declared is five errors whose messages are
    /// word for word identical — so without the position they render as one complaint printed
    /// five times, which reads as the debugger stuttering rather than as the program having
    /// five mistakes. The position is the only thing that distinguishes them, and it is also
    /// the thing a reader needs in order to go and fix them.</para>
    /// </summary>
    [Test]
    public void RefusalsAreToldApartByWhereTheyAre()
    {
        List<JsonObject> sent = Session(
            """
            shared model Program
                function Main()
                    Book first = new Book();
                    Book second = new Book();
                    Book third = new Book();
                end function
            end model
            """,
            """{"seq":1,"type":"request","command":"initialize","arguments":{}}""",
            """{"seq":2,"type":"request","command":"launch","arguments":{"program":"PROGRAM"}}""",
            """{"seq":3,"type":"request","command":"disconnect","arguments":{}}""");

        string message = (string?)ResponseTo(sent, "launch")!["message"] ?? string.Empty;

        string[] lines = [.. message.Split('\n', StringSplitOptions.RemoveEmptyEntries)];

        Assert.Multiple(() =>
        {
            Assert.That(lines, Has.Length.GreaterThan(1),
                        "three mentions of an undeclared type is more than one error");

            Assert.That(lines, Is.Unique,
                        "and no two of them read the same, or they cannot be acted on");

            Assert.That(lines[0], Does.Contain("Program.pc(").And.Contain("PC0201"),
                        "each says which file and where, as every other diagnostic does");
        });
    }

    /// <summary>
    /// <para>A breakpoint stops the program, and the stop names where it is.</para>
    /// <para>The whole point of the exercise, end to end: set a breakpoint, start, and be told.
    /// </para>
    /// </summary>
    [Test]
    public void ABreakpointStopsTheProgramAndSaysWhere()
    {
        List<JsonObject> sent = Session(
            Counting,
            """{"seq":1,"type":"request","command":"initialize","arguments":{}}""",
            """{"seq":2,"type":"request","command":"launch","arguments":{"program":"PROGRAM"}}""",
            """{"seq":3,"type":"request","command":"setBreakpoints","arguments":{"source":{"path":"PROGRAM"},"breakpoints":[{"line":5}]}}""",
            """{"seq":4,"type":"request","command":"configurationDone","arguments":{}}""");

        Assert.Multiple(() =>
        {
            Assert.That(
                (bool?)ResponseTo(sent, "setBreakpoints")!["body"]!["breakpoints"]![0]!["verified"],
                Is.True);

            Assert.That(EventsNamed(sent, "stopped"), Is.Not.Empty,
                        "the program should stop at the breakpoint");

            Assert.That(
                (string?)EventsNamed(sent, "stopped").First()["body"]!["reason"],
                Is.EqualTo("breakpoint"),
                "and say it was a breakpoint, which is what an editor shows above the stack");
        });
    }

    /// <summary>
    /// <para>A step reports itself as a step, which is the other half of the reason.</para>
    /// <para>Worth its own test rather than trusting the pair: a reason hard-coded to either
    /// word passes one of these two and fails the other, and a reason hard-coded to "breakpoint"
    /// is exactly the mistake that reads as correct while you are looking at a breakpoint.
    /// </para>
    /// </summary>
    [Test]
    public void AStepSaysItWasAStep()
    {
        List<JsonObject> sent = Session(
            Counting,
            """{"seq":1,"type":"request","command":"initialize","arguments":{}}""",
            """{"seq":2,"type":"request","command":"launch","arguments":{"program":"PROGRAM"}}""",
            """{"seq":3,"type":"request","command":"setBreakpoints","arguments":{"source":{"path":"PROGRAM"},"breakpoints":[{"line":3}]}}""",
            """{"seq":4,"type":"request","command":"configurationDone","arguments":{}}""",
            """{"seq":5,"type":"request","command":"next","arguments":{"threadId":1}}""",
            """{"seq":6,"type":"request","command":"continue","arguments":{}}""");

        string?[] reasons =
        [
            .. EventsNamed(sent, "stopped").Select(e => (string?)e["body"]!["reason"]),
        ];

        Assert.Multiple(() =>
        {
            Assert.That(reasons.First(), Is.EqualTo("breakpoint"), "arrived at the breakpoint");

            Assert.That(reasons, Does.Contain("step"),
                        "and stepping off it onto a line with no breakpoint is a step");
        });
    }

    /// <summary>
    /// <para>Stopped, an editor can ask what is where — the stack, the scopes, the values.</para>
    /// <para>Asked in the order an editor asks them, because each answer feeds the next: a stack
    /// frame's id goes into <c>scopes</c>, and that scope's reference into
    /// <c>variables</c>.</para>
    /// </summary>
    [Test]
    public void StoppedTheEditorCanAskWhatIsWhere()
    {
        List<JsonObject> sent = Session(
            Counting,
            """{"seq":1,"type":"request","command":"initialize","arguments":{}}""",
            """{"seq":2,"type":"request","command":"launch","arguments":{"program":"PROGRAM"}}""",
            """{"seq":3,"type":"request","command":"setBreakpoints","arguments":{"source":{"path":"PROGRAM"},"breakpoints":[{"line":5}]}}""",
            """{"seq":4,"type":"request","command":"configurationDone","arguments":{}}""",
            """{"seq":5,"type":"request","command":"threads","arguments":{}}""",
            """{"seq":6,"type":"request","command":"stackTrace","arguments":{"threadId":1}}""",
            """{"seq":7,"type":"request","command":"scopes","arguments":{"frameId":0}}""",
            """{"seq":8,"type":"request","command":"variables","arguments":{"variablesReference":1000}}""",
            """{"seq":9,"type":"request","command":"continue","arguments":{}}""");

        JsonArray frames = (JsonArray)ResponseTo(sent, "stackTrace")!["body"]!["stackFrames"]!;
        JsonArray variables = (JsonArray)ResponseTo(sent, "variables")!["body"]!["variables"]!;

        Assert.Multiple(() =>
        {
            Assert.That((string?)frames[0]!["name"], Is.EqualTo("Main"));
            Assert.That((int?)frames[0]!["line"], Is.EqualTo(5), "stopped on the breakpoint's line");

            Assert.That(
                (string?)ResponseTo(sent, "scopes")!["body"]!["scopes"]![0]!["name"],
                Is.EqualTo("Locals"));

            Assert.That(
                variables.Select(v => (string?)v!["name"]),
                Does.Contain("total"));

            Assert.That(
                (string?)variables.First(v => (string?)v!["name"] == "total")!["value"],
                Is.EqualTo("5"),
                "the value as it stands at the stop, after line 4 ran");

            Assert.That(
                variables.Select(v => (string?)v!["name"]).Where(n => n?.StartsWith('<') == true),
                Is.Empty,
                "nothing lowering invented should be shown");
        });
    }

    /// <summary>
    /// What the program prints arrives as it is printed, so a program stopped at a breakpoint
    /// has already shown what it printed on the way there.
    /// </summary>
    [Test]
    public void WhatTheProgramPrintsIsSentOut()
    {
        List<JsonObject> sent = Session(
            Counting,
            """{"seq":1,"type":"request","command":"initialize","arguments":{}}""",
            """{"seq":2,"type":"request","command":"launch","arguments":{"program":"PROGRAM"}}""",
            """{"seq":3,"type":"request","command":"setBreakpoints","arguments":{"source":{"path":"PROGRAM"},"breakpoints":[]}}""",
            """{"seq":4,"type":"request","command":"configurationDone","arguments":{}}""");

        string printed = string.Concat(
            EventsNamed(sent, "output").Select(e => (string?)e["body"]!["output"]));

        Assert.Multiple(() =>
        {
            Assert.That(printed, Does.Contain("5"));
            Assert.That(EventsNamed(sent, "terminated"), Is.Not.Empty, "and it ends");
        });
    }

    /// <summary>The program a two-file session launches, calling into the file beside it.</summary>
    private const string Calling = """
        shared model Program
            function Main()
                Console.WriteLine(Numbers.Doubled(21));
            end function
        end model
        """;

    /// <summary>
    /// Written so that its interesting line is line 3 as well, which is what makes a breakpoint
    /// that ignores the file indistinguishable from one that works.
    /// </summary>
    private const string Numbers = """
        shared model Numbers
            public integer function Doubled(integer n)
                yield n * 2;
            end function
        end model
        """;

    private static readonly Written[] TwoFiles =
        [new Written("Program.pc", Calling), new Written("Numbers.pc", Numbers)];

    /// <summary>
    /// <para>A breakpoint set in the file beside the program stops there, and every frame says
    /// which file it is in.</para>
    /// <para>The two together are what makes a stack navigable: the editor is told a path per
    /// frame, so clicking one opens that file at that line. Without it a stack can be shown and
    /// not followed, which is most of what a stack is for.</para>
    /// <para>Launching finds the second file at all only because it compiles what
    /// <c>pc run</c> compiles — the program named, and the shared code beside it.</para>
    /// </summary>
    [Test]
    public void AFrameSaysWhichFileItIsIn()
    {
        List<JsonObject> sent = SessionOver(
            TwoFiles,
            [
                """{"seq":1,"type":"request","command":"initialize","arguments":{}}""",
                """{"seq":2,"type":"request","command":"launch","arguments":{"program":"PROGRAM"}}""",
                """{"seq":3,"type":"request","command":"setBreakpoints","arguments":{"source":{"path":"OTHER"},"breakpoints":[{"line":3}]}}""",
                """{"seq":4,"type":"request","command":"configurationDone","arguments":{}}""",
                """{"seq":5,"type":"request","command":"stackTrace","arguments":{"threadId":1}}""",
                """{"seq":6,"type":"request","command":"continue","arguments":{}}""",
            ]);

        JsonArray frames = (JsonArray)ResponseTo(sent, "stackTrace")!["body"]!["stackFrames"]!;

        Assert.Multiple(() =>
        {
            Assert.That(
                frames.Select(frame => (string?)frame!["name"]),
                Is.EqualTo(new[] { "Doubled", "Main" }),
                "stopped inside the called function, in the other file");

            Assert.That(
                frames.Select(frame => (string?)frame!["source"]!["name"]),
                Is.EqualTo(new[] { "Numbers.pc", "Program.pc" }),
                "each frame names its own file, not the one the program started in");

            Assert.That(
                (string?)frames[0]!["source"]!["path"],
                Does.EndWith("Numbers.pc").And.Contains(Path.DirectorySeparatorChar),
                "an absolute path, since the editor resolves it against its own folder");
        });
    }

    /// <summary>
    /// <para>A breakpoint set in one file does not stop the program on that line of another.
    /// </para>
    /// <para>Both files here have a statement on line 3, which is where the first statement of a
    /// small file lands — so this is the ordinary case rather than a contrived one. A breakpoint
    /// keyed on the number alone stops in whichever file the program reaches first, and nothing
    /// in what the editor is shown tells that apart from a breakpoint that works.</para>
    /// </summary>
    [Test]
    public void ABreakpointBelongsToTheFileItWasSetIn()
    {
        List<JsonObject> sent = SessionOver(
            TwoFiles,
            [
                """{"seq":1,"type":"request","command":"initialize","arguments":{}}""",
                """{"seq":2,"type":"request","command":"launch","arguments":{"program":"PROGRAM"}}""",
                """{"seq":3,"type":"request","command":"setBreakpoints","arguments":{"source":{"path":"OTHER"},"breakpoints":[{"line":3}]}}""",
                """{"seq":4,"type":"request","command":"configurationDone","arguments":{}}""",
                """{"seq":5,"type":"request","command":"stackTrace","arguments":{"threadId":1}}""",
                """{"seq":6,"type":"request","command":"continue","arguments":{}}""",
            ]);

        JsonArray frames = (JsonArray)ResponseTo(sent, "stackTrace")!["body"]!["stackFrames"]!;

        Assert.Multiple(() =>
        {
            Assert.That(EventsNamed(sent, "stopped").Count(), Is.EqualTo(1),
                        "one breakpoint, one stop");

            Assert.That((string?)frames[0]!["source"]!["name"], Is.EqualTo("Numbers.pc"),
                        "stopped in the file the breakpoint was set in");
        });
    }

    /// <summary>
    /// Breakpoints that do not say which file they are in are refused. Picking one for the
    /// reader would put them somewhere they were never set, which reads as a debugger stopping
    /// at random.
    /// </summary>
    [Test]
    public void BreakpointsWithNoFileAreRefused()
    {
        List<JsonObject> sent = Session(
            Counting,
            """{"seq":1,"type":"request","command":"initialize","arguments":{}}""",
            """{"seq":2,"type":"request","command":"setBreakpoints","arguments":{"breakpoints":[{"line":5}]}}""",
            """{"seq":3,"type":"request","command":"disconnect","arguments":{}}""");

        Assert.That((bool?)ResponseTo(sent, "setBreakpoints")!["success"], Is.False);
    }

    /// <summary>
    /// A request this adapter does not answer is refused rather than ignored. An editor waiting
    /// on a reply that never comes hangs; one told no carries on.
    /// </summary>
    [Test]
    public void AnUnknownRequestIsRefusedRatherThanIgnored()
    {
        List<JsonObject> sent = Session(
            Counting,
            """{"seq":1,"type":"request","command":"setExpression","arguments":{}}""",
            """{"seq":2,"type":"request","command":"disconnect","arguments":{}}""");

        JsonObject? answer = ResponseTo(sent, "setExpression");

        Assert.Multiple(() =>
        {
            Assert.That(answer, Is.Not.Null);
            Assert.That((bool?)answer!["success"], Is.False);
        });
    }
}
