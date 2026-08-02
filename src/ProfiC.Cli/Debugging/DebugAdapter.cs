using System.Text;
using System.Text.Json.Nodes;
using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Semantics;
using ProfiC.Interpreter;

namespace ProfiC.Cli.Debugging;

/// <summary>
/// <para>Speaks the Debug Adapter Protocol on behalf of a Profi-C program.</para>
/// <para>Translation, and nothing else. Everything about <em>how</em> to debug — where to stop,
/// what counts as one step, which names to show — was decided in
/// <see cref="StopPolicy"/> and <see cref="DebugSession"/>, and is deliberately not decided
/// again here. What this knows is the vocabulary an editor uses and how it maps onto that.
/// </para>
/// <para><b>The handshake is the part with an order that matters.</b> An editor sends
/// <c>initialize</c>, is told what this can do, and is then sent an <c>initialized</c> event —
/// which is its cue to send the breakpoints. Only when it says <c>configurationDone</c> does the
/// program start. That sequence is why a breakpoint on the first line is already in place before
/// the first line runs, and getting it wrong shows up as breakpoints that work on the second run
/// of a session and not the first.</para>
/// </summary>
public sealed class DebugAdapter : IDisposable
{
    /// <summary>Profi-C runs one program on one thread, so there is one to report.</summary>
    private const int OnlyThread = 1;

    /// <summary>What <c>scopes</c> hands back for a frame, and <c>variables</c> is then asked for.</summary>
    private const int LocalsReference = 1000;

    private readonly DapConnection _wire;
    private readonly DebugSession _session;

    public DebugAdapter(Stream input, Stream output)
    {
        _wire = new DapConnection(input, output);

        // Told on the program's own thread, before it is released — which is what makes the
        // stack and the locals readable when the editor asks about them a moment later.
        _session = new DebugSession(point => _wire.Event("stopped", new JsonObject
        {
            ["reason"] = "step",
            ["threadId"] = OnlyThread,
            ["line"] = point.Line,
            ["allThreadsStopped"] = true,
        }));
    }

    private IReadOnlyList<CompilationUnit>? _lowered;
    private SemanticModel? _model;
    private Thread? _program;
    private bool _running;

    /// <summary>
    /// <para>Reads requests until the editor disconnects or the stream ends.</para>
    /// <para>One thread reads and answers; the program, once started, runs on another. A stop is
    /// the program's thread waiting inside the session while this one carries on answering —
    /// which is what lets an editor ask for a stack trace while stopped.</para>
    /// </summary>
    public void Run()
    {
        while (_wire.Read() is { } request)
        {
            if (!Handle(request))
            {
                return;
            }
        }
    }

    /// <summary>Answers one request. False means the session is over.</summary>
    private bool Handle(JsonObject request)
    {
        string command = (string?)request["command"] ?? string.Empty;

        switch (command)
        {
            case "initialize":
                Initialize(request);
                return true;

            case "launch":
                Launch(request);
                return true;

            case "setBreakpoints":
                SetBreakpoints(request);
                return true;

            case "configurationDone":
                _wire.Respond(request);
                Start();
                return true;

            case "threads":
                Threads(request);
                return true;

            case "stackTrace":
                StackTrace(request);
                return true;

            case "scopes":
                Scopes(request);
                return true;

            case "variables":
                Variables(request);
                return true;

            case "continue":
                _wire.Respond(request, new JsonObject { ["allThreadsContinued"] = true });
                _session.Continue();
                return true;

            case "next":
                _wire.Respond(request);
                _session.StepOver();
                return true;

            case "stepIn":
                _wire.Respond(request);
                _session.StepInto();
                return true;

            case "stepOut":
                _wire.Respond(request);
                _session.StepOut();
                return true;

            case "disconnect":
            case "terminate":
                _wire.Respond(request);
                _session.Detach();
                return false;

            default:
                // Saying so is better than silence: an editor waiting on a reply that never
                // comes hangs, where one told no moves on.
                _wire.Refuse(request, $"'{command}' is not something this adapter does");
                return true;
        }
    }

    /// <summary>
    /// <para>What this adapter can do, and then the cue for breakpoints.</para>
    /// <para>Only what is true is claimed. An adapter that says it supports something and then
    /// refuses it leaves an editor showing a button that does nothing, which reads as the
    /// debugger being broken rather than the feature being absent.</para>
    /// </summary>
    private void Initialize(JsonObject request)
    {
        _wire.Respond(request, new JsonObject
        {
            ["supportsConfigurationDoneRequest"] = true,
            ["supportsTerminateRequest"] = true,
        });

        _wire.Event("initialized");
    }

    /// <summary>
    /// <para>Compiles the program named, and says so if it will not compile.</para>
    /// <para>Compiled the same way <c>pc run</c> compiles it, down to the file discovery, so
    /// that debugging a program and running it are the same program. A debugger that saw only
    /// the file it was pointed at could not step into anything beside it, which is most of what
    /// a program of any size is made of.</para>
    /// <para>A failed launch is reported as a refusal with the diagnostics in it, rather than as
    /// a program that starts and immediately dies. The reader asked to debug something that does
    /// not build, and that is what they should be told.</para>
    /// </summary>
    private void Launch(JsonObject request)
    {
        string? written = (string?)request["arguments"]?["program"];

        if (string.IsNullOrEmpty(written))
        {
            _wire.Refuse(request, "a launch has to say which program to debug");
            return;
        }

        if (SourceDiscovery.Locate(written, out string problem) is not { } target)
        {
            _wire.Refuse(request, problem);
            return;
        }

        DiagnosticBag diagnostics = new();

        if (Program.Compile(target.Path, diagnostics, requireEntryPoint: true)
            is not var (compilation, model))
        {
            _wire.Refuse(request, Refusals(diagnostics));
            return;
        }

        if (diagnostics.HasErrors)
        {
            _wire.Refuse(request, Refusals(diagnostics));
            return;
        }

        _lowered = Lowering.Lower(compilation.Units, model);
        _model = model;

        _wire.Respond(request);
    }

    /// <summary>
    /// <para>Why a launch was refused, as one piece of text for an editor to show.</para>
    /// <para>Capped, because this lands in a message box rather than a scrolling terminal, and a
    /// hundred errors there is a wall nobody reads. The first few are the ones worth acting on
    /// anyway — the rest of a compilation's complaints usually follow from them.</para>
    /// </summary>
    private static string Refusals(DiagnosticBag diagnostics)
    {
        string[] errors =
        [
            .. diagnostics.Sorted()
                          .Where(d => d.Severity == DiagnosticSeverity.Error)
                          .Select(d => $"{d.Id}: {d.Message}")
                          .Take(10),
        ];

        return errors.Length > 0 ? string.Join("\n", errors) : "the program could not be compiled";
    }

    /// <summary>
    /// <para>The breakpoints in one file, which is how they arrive: an editor sends the whole
    /// set for a file each time any one of them changes, and says nothing about the others.
    /// </para>
    /// <para>A request naming no file is refused rather than guessed at. Guessing means picking
    /// a file, and picking the wrong one puts breakpoints where the reader did not set them —
    /// which looks like the debugger stopping at random.</para>
    /// </summary>
    private void SetBreakpoints(JsonObject request)
    {
        string? file = (string?)request["arguments"]?["source"]?["path"];

        if (string.IsNullOrEmpty(file))
        {
            _wire.Refuse(request, "breakpoints have to say which file they are in");
            return;
        }

        JsonArray asked = request["arguments"]?["breakpoints"] as JsonArray ?? [];
        int[] lines = [.. asked.Select(b => (int?)b?["line"] ?? 0).Where(line => line > 0)];

        _session.BreakpointsAt(file, lines);

        // Every one is reported verified: a breakpoint may sit on any line, and the ones with no
        // statement behind them simply never fire. Claiming otherwise would mean deciding here
        // what lowering left where, which is the mapping's business rather than the protocol's.
        _wire.Respond(request, new JsonObject
        {
            ["breakpoints"] = new JsonArray(
                [.. lines.Select(line => (JsonNode)new JsonObject
                {
                    ["verified"] = true,
                    ["line"] = line,
                })]),
        });
    }

    /// <summary>Starts the program on its own thread, so that this one can keep answering.</summary>
    private void Start()
    {
        if (_lowered is null || _model is null || _running)
        {
            return;
        }

        _running = true;

        _program = new Thread(() =>
        {
            try
            {
                ProfiC.Interpreter.Interpreter.Run(
                    _lowered, _model, new Reporting(_wire), TextReader.Null, _session);
            }
            catch (Exception failure)
            {
                _wire.Event("output", new JsonObject
                {
                    ["category"] = "stderr",
                    ["output"] = failure.Message + "\n",
                });
            }
            finally
            {
                _wire.Event("terminated");
            }
        })
        {
            IsBackground = true,
            Name = "profi-c program",
        };

        _program.Start();
    }

    private void Threads(JsonObject request) => _wire.Respond(request, new JsonObject
    {
        ["threads"] = new JsonArray(
            new JsonObject { ["id"] = OnlyThread, ["name"] = "program" }),
    });

    /// <summary>
    /// <para>The calls in progress, innermost first, which is the order an editor shows them
    /// in. A lambda has no name, so it is described rather than given an invented one.</para>
    /// <para>Each frame carries the file it is in, which is what makes the list navigable:
    /// clicking a frame opens that file at that line. A frame without one is still shown,
    /// because a stack missing its outer calls is more confusing than a frame that will not
    /// open.</para>
    /// </summary>
    private void StackTrace(JsonObject request)
    {
        IReadOnlyList<CallFrame> stack = _session.Where?.Stack ?? [];

        JsonArray frames = [];

        for (int i = 0; i < stack.Count; i++)
        {
            JsonObject frame = new()
            {
                ["id"] = i,
                ["name"] = stack[i].Name ?? "a function with no name",
                ["line"] = stack[i].Line,
                ["column"] = 1,
            };

            if (Source(stack[i].File) is { } source)
            {
                frame["source"] = source;
            }

            frames.Add(frame);
        }

        _wire.Respond(request, new JsonObject
        {
            ["stackFrames"] = frames,
            ["totalFrames"] = stack.Count,
        });
    }

    /// <summary>
    /// <para>A file, as an editor expects to be told about one.</para>
    /// <para>The path is made absolute because an editor resolves it against its own working
    /// directory rather than the adapter's, and those are not the same directory. Null where
    /// there is no file to name, so that the caller can leave the field off entirely — an empty
    /// source is a source an editor will try to open.</para>
    /// </summary>
    private static JsonObject? Source(string file) =>
        string.IsNullOrEmpty(file)
            ? null
            : new JsonObject
            {
                ["name"] = Path.GetFileName(file),
                ["path"] = Path.GetFullPath(file),
            };

    private void Scopes(JsonObject request) => _wire.Respond(request, new JsonObject
    {
        ["scopes"] = new JsonArray(new JsonObject
        {
            ["name"] = "Locals",
            ["variablesReference"] = LocalsReference,
            ["expensive"] = false,
        }),
    });

    /// <summary>
    /// <para>What is in scope, without the names lowering invented.</para>
    /// <para>A <c>loop each</c> puts three of those in scope beside the element. Showing
    /// <c>&lt;source$0&gt;</c> to a beginner would be worse than showing nothing — it is not
    /// theirs, they cannot write it, and it invites the question of what they did wrong.</para>
    /// </summary>
    private void Variables(JsonObject request)
    {
        IReadOnlyList<Local> locals = _session.Where?.Locals() ?? [];

        _wire.Respond(request, new JsonObject
        {
            ["variables"] = new JsonArray(
                [.. locals.Where(local => !local.Invented)
                          .Select(local => (JsonNode)new JsonObject
                          {
                              ["name"] = local.Name,
                              ["value"] = Runtime.ModelOperations.ToDisplayString(local.Value),
                              ["variablesReference"] = 0,
                          })]),
        });
    }

    public void Dispose()
    {
        _session.Detach();
        _session.Dispose();
    }

    /// <summary>
    /// <para>Sends everything the program prints to the editor as it is printed.</para>
    /// <para>Written through rather than collected, so that a program stopped at a breakpoint
    /// has already shown what it printed on the way there — which is most of how anybody
    /// actually debugs.</para>
    /// </summary>
    private sealed class Reporting(DapConnection wire) : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                wire.Event("output", new JsonObject
                {
                    ["category"] = "stdout",
                    ["output"] = value,
                });
            }
        }

        public override void Write(char value) => Write(value.ToString());
    }
}
