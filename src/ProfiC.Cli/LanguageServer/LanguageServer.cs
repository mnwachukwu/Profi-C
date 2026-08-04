using System.Text.Json.Nodes;
using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Documentation;
using ProfiC.Compiler.Formatting;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Semantics;
using ProfiC.Compiler.Text;

namespace ProfiC.Cli.LanguageServer;

/// <summary>
/// <para>Answers an editor's questions about Profi-C, for as long as the editor is open.</para>
/// <para><b>What makes this different from every other command.</b> Everything else the compiler
/// does reads a file from the disk, answers, and exits — which is the only thing a separate
/// process can do, and it forecloses every question worth asking about code as it is being
/// written. A file mid-edit is not valid Profi-C and has no version on disk to stand in for it.
/// This holds what the editor holds, and answers about that.</para>
/// <para>It also removes what dominates the cost. A whole-file re-analysis is a few milliseconds;
/// starting a process to do it is a few hundred. Nothing here is cached, deliberately: full
/// re-analysis is comfortably fast for anything a reader will write, and a cache built against no
/// measurement is a guess with a bug in it.</para>
/// <para>What this answers so far is diagnostics. Everything else an editor can ask — hover,
/// completion, where a name is declared — is a different question about the same model, and none
/// of it is possible until this part is right.</para>
/// </summary>
public sealed class LanguageServer : IDisposable
{
    private readonly LspConnection _wire;
    private readonly DocumentStore _documents = new();
    private readonly Analysis _analysis;

    /// <summary>
    /// <para>Which files were last told to have problems, so they can be told they have none.
    /// </para>
    /// <para>A diagnostic is cleared by publishing an empty list for the file, and nothing else
    /// clears it. Without this, fixing the last error in a file the reader never opened would
    /// leave it listed in the panel forever — the analysis simply stops mentioning it, and
    /// silence is not a message.</para>
    /// </summary>
    private readonly HashSet<string> _reported = new(SourceDiscovery.PathComparer);

    private readonly Lock _reporting = new();

    private bool _shuttingDown;

    /// <summary>
    /// Which hints the reader asked for, read from the editor's settings and replaced whenever
    /// they change. Set before anything can be asked, so the defaults stand for a client that
    /// sends none.
    /// </summary>
    private Hints.Wants _hints = Hints.Wants.Default;

    public LanguageServer(Stream input, Stream output, TimeSpan? quiet = null)
    {
        _wire = new LspConnection(input, output);
        _analysis = new Analysis(Analyze, quiet);
    }

    /// <summary>
    /// <para>Reads messages until the editor goes away.</para>
    /// <para>One thread, and requests answered in the order they arrive. That is what the
    /// protocol asks for and what keeps this simple; the work an edit implies is the only thing
    /// that runs off to the side, which is the whole of <see cref="Analysis"/>.</para>
    /// </summary>
    public void Run()
    {
        while (_wire.Read() is { } message)
        {
            try
            {
                Dispatch(message);
            }
            catch (Exception fault) when (fault is not OperationCanceledException)
            {
                // A fault answering one message is not a reason to stop answering the rest. An
                // editor whose server vanishes shows nothing at all, which is worse than one
                // request going unanswered and being said out loud.
                _wire.Log($"A message could not be handled: {fault.Message}");

                if (message["id"] is { } id && message["method"] is not null)
                {
                    _wire.Refuse(id, LspConnection.Fault.InternalError, fault.Message);
                }
            }
        }
    }

    private void Dispatch(JsonObject message)
    {
        string? method = (string?)message["method"];

        if (method is null)
        {
            // A response to something this server asked. Nothing is asked yet, so there is
            // nothing this could be answering.
            return;
        }

        JsonNode? id = message["id"];
        JsonObject? parameters = message["params"] as JsonObject;

        switch (method)
        {
            case "initialize":
                _hints = Hints.Wanted(parameters?["initializationOptions"] as JsonObject);
                _wire.Respond(id, Capabilities());
                break;

            // Settings changed while the editor was open. Read rather than ignored, so that
            // turning a hint off takes effect where it was turned off rather than at the next
            // restart — which is nowhere, for somebody trying the switch to see what it does.
            case "workspace/didChangeConfiguration":
                _hints = Hints.Wanted(parameters?["settings"] as JsonObject);
                break;

            // Sent once the editor has finished starting. Nothing to do, and answering it as
            // unknown would put an error in the log of every session.
            case "initialized":
                break;

            case "shutdown":
                _shuttingDown = true;
                _wire.Respond(id, null);
                break;

            case "exit":
                return;

            case "textDocument/didOpen":
                Opened(parameters);
                break;

            case "textDocument/didChange":
                Changed(parameters);
                break;

            case "textDocument/didSave":
                Saved(parameters);
                break;

            case "textDocument/didClose":
                Closed(parameters);
                break;

            // Answered on the reading thread rather than scheduled, because somebody is waiting
            // for each of these. None is debounced for the same reason: a question with an
            // answer owed is not something to defer.
            case "textDocument/documentSymbol":
                _wire.Respond(id, DocumentSymbols(parameters));
                break;

            case "textDocument/hover":
                _wire.Respond(id, HoverAt(parameters));
                break;

            case "textDocument/definition":
                _wire.Respond(id, DefinitionAt(parameters));
                break;

            case "textDocument/completion":
                _wire.Respond(id, CompletionAt(parameters));
                break;

            case "textDocument/signatureHelp":
                _wire.Respond(id, SignatureAt(parameters));
                break;

            case "textDocument/codeAction":
                _wire.Respond(id, FixesFor(parameters));
                break;

            case "textDocument/prepareRename":
                _wire.Respond(id, PrepareRenameAt(parameters));
                break;

            case "textDocument/rename":
                _wire.Respond(id, RenameAt(parameters));
                break;

            case "textDocument/semanticTokens/full":
                _wire.Respond(id, ColorsFor(parameters));
                break;

            case "textDocument/documentHighlight":
                _wire.Respond(id, OccurrencesAt(parameters));
                break;

            case "textDocument/references":
                _wire.Respond(id, ReferencesTo(parameters));
                break;

            case "textDocument/inlayHint":
                _wire.Respond(id, HintsIn(parameters));
                break;

            case "textDocument/formatting":
                _wire.Respond(id, LinedUp(parameters, whole: true));
                break;

            case "textDocument/rangeFormatting":
                _wire.Respond(id, LinedUp(parameters, whole: false));
                break;

            default:
                // Only a request is owed an answer. A notification this server does not know is
                // ignored in silence, which the protocol requires — an editor sends several that
                // no server has to implement.
                if (id is not null)
                {
                    _wire.Refuse(
                        id,
                        LspConnection.Fault.MethodNotFound,
                        $"'{method}' is not something this server does");
                }

                break;
        }
    }

    /// <summary>
    /// <para>What this server can do, which an editor asks before it asks anything else.</para>
    /// <para>Sync is 1, meaning full text on every change. The alternative sends the edited range
    /// and leaves the server to apply it — worth having when a file is large enough that copying
    /// it matters, and not worth the second implementation of "apply an edit" before then.</para>
    /// </summary>
    private static JsonObject Capabilities() => new()
    {
        ["capabilities"] = new JsonObject
        {
            ["textDocumentSync"] = new JsonObject
            {
                ["openClose"] = true,
                ["change"] = 1,
                ["save"] = true,
            },
            ["documentSymbolProvider"] = true,
            ["hoverProvider"] = true,
            ["definitionProvider"] = true,
            ["documentHighlightProvider"] = true,
            ["referencesProvider"] = true,

            // Where the program says no type, which in this language is 'let' and the two loop
            // bindings. Narrow enough that it is on rather than something to be configured.
            ["inlayHintProvider"] = true,
            ["documentFormattingProvider"] = true,
            ["documentRangeFormattingProvider"] = true,

            // The dot is what makes the editor ask without being prompted. It asks again on each
            // letter after it on its own, which is why nothing else needs to be named here.
            ["completionProvider"] = new JsonObject
            {
                ["triggerCharacters"] = new JsonArray("."),
            },

            // The open paren starts it and the comma moves to the next parameter, which is the
            // whole of when somebody wants to see what a function takes.
            ["signatureHelpProvider"] = new JsonObject
            {
                ["triggerCharacters"] = new JsonArray("(", ","),
            },

            ["codeActionProvider"] = true,

            // Prepared as well as done, so that a cursor somewhere nothing can be renamed says
            // so before the reader has typed a replacement.
            ["renameProvider"] = new JsonObject
            {
                ["prepareProvider"] = true,
            },

            // The legend has to arrive before any tokens do: what comes back is numbers, and
            // these two lists are what the numbers mean. An editor that asked without them would
            // have no way to read the answer.
            ["semanticTokensProvider"] = new JsonObject
            {
                ["legend"] = new JsonObject
                {
                    ["tokenTypes"] = new JsonArray([.. SemanticTokens.Kinds.Select(k => (JsonNode)k)]),
                    ["tokenModifiers"] =
                        new JsonArray([.. SemanticTokens.Traits.Select(t => (JsonNode)t)]),
                },
                ["full"] = true,
            },
        },
        ["serverInfo"] = new JsonObject
        {
            ["name"] = "profi-c",
            ["version"] = typeof(LanguageServer).Assembly.GetName().Version?.ToString() ?? "0.0.0",
        },
    };

    // ---- What the editor says about documents ------------------------------------------------

    private void Opened(JsonObject? parameters)
    {
        if (Document(parameters) is not { } document)
        {
            return;
        }

        _documents.Set(document.Path, (string?)document.Node["text"] ?? string.Empty, Version(document.Node));

        // Not debounced. Nothing is on screen for this file yet, and waiting shows a blank panel
        // over code the reader is looking straight at.
        _ = _analysis.Now(document.Path);
    }

    private void Changed(JsonObject? parameters)
    {
        if (Document(parameters) is not { } document)
        {
            return;
        }

        // Full text, which is what the capability asked for: the last change carries the whole
        // document rather than a range to apply.
        if (parameters?["contentChanges"] is JsonArray changes
            && changes.Count > 0
            && (string?)changes[^1]?["text"] is { } text)
        {
            // Immediately, and never deferred. Every other question is answered from this, so a
            // store lagging behind the reader would make all of them quietly wrong.
            _documents.Set(document.Path, text, Version(document.Node));
        }

        _ = _analysis.Schedule(document.Path);
    }

    /// <summary>Saving is the reader saying they have stopped, so it is not waited on.</summary>
    private void Saved(JsonObject? parameters)
    {
        if (Document(parameters) is { } document)
        {
            _ = _analysis.Now(document.Path);
        }
    }

    private void Closed(JsonObject? parameters)
    {
        if (Document(parameters) is not { } document)
        {
            return;
        }

        _analysis.Forget(document.Path);
        _documents.Close(document.Path);

        // What was said about it stands: the file is still on disk and still has whatever
        // problems it had. Only a file that has stopped having them is cleared, and that is
        // Publish's business.
    }

    /// <summary>The document a notification is about, or null where it names none.</summary>
    private static (string Path, JsonObject Node)? Document(JsonObject? parameters)
    {
        if (parameters?["textDocument"] is not JsonObject node)
        {
            return null;
        }

        // Null for an editor's untitled buffer, which names no file. There is nothing to compile
        // and nowhere to compile it with — a program is a compilation, and a buffer belongs to no
        // folder until it is saved into one.
        return Conversions.PathOf((string?)node["uri"]) is { } path ? (path, node) : null;
    }

    private static int Version(JsonObject node) => (int?)node["version"] ?? 0;

    // ---- What the editor asks about a place --------------------------------------------------

    /// <summary>
    /// <para>What one file declares.</para>
    /// <para>Parsed alone rather than gathered, because an outline is about the file rather than
    /// the program: nothing in it depends on what another file declares, and a file that will not
    /// resolve still has a shape worth showing.</para>
    /// </summary>
    private JsonArray? DocumentSymbols(JsonObject? parameters)
    {
        if (Document(parameters) is not { } document)
        {
            return null;
        }

        DiagnosticBag aside = new();
        SourceText source = _documents.Reader(document.Path);

        return Answers.Symbols(Parser.Parse(source, aside), source);
    }

    private JsonObject? HoverAt(JsonObject? parameters)
    {
        if (Compiled(parameters) is not var (units, model, unit, offset))
        {
            return null;
        }

        return Answers.Hover(units, unit, model, unit.Source, offset);
    }

    /// <summary>
    /// <para>What could come after the dot before the cursor.</para>
    /// <para>Answered from the text alone rather than through <see cref="Compiled"/>, because
    /// what a reader has typed here does not parse — the whole difficulty of the question, and
    /// why <see cref="Completion"/> does its own compiling with a name put where the member
    /// will go.</para>
    /// </summary>
    private JsonArray? CompletionAt(JsonObject? parameters)
    {
        if (Document(parameters) is not { } document)
        {
            return null;
        }

        SourceText source = _documents.Reader(document.Path);

        if (Conversions.OffsetOf(parameters?["position"] as JsonObject, source) is not { } offset)
        {
            return null;
        }

        // Two questions, and which one is being asked is settled by whether a dot precedes the
        // cursor. Both are answered here rather than in one place that branches, because what a
        // member access offers and what a bare name offers have nothing in common but the shape
        // of the reply.
        return Completion.After(document.Path, source, offset, _documents.Reader)
            ?? Completion.Bare(document.Path, source, offset, _documents.Reader);
    }

    private JsonObject? PrepareRenameAt(JsonObject? parameters)
    {
        if (Compiled(parameters) is not var (units, model, unit, offset))
        {
            return null;
        }

        _ = units;
        return Rename.Prepare(unit, model, offset);
    }

    private JsonObject? RenameAt(JsonObject? parameters)
    {
        if ((string?)parameters?["newName"] is not { Length: > 0 } newName)
        {
            return null;
        }

        return Compiled(parameters) is var (units, model, unit, offset)
            ? Rename.Edits(units, model, unit, offset, newName)
            : null;
    }

    private JsonObject? SignatureAt(JsonObject? parameters)
    {
        if (Compiled(parameters) is not var (units, model, unit, offset))
        {
            return null;
        }

        _ = units;
        return Answers.Signature(unit, model, unit.Source, offset);
    }

    /// <summary>
    /// <para>What can be done about the problems the editor is asking about.</para>
    /// <para>Compiled again rather than kept from the last analysis: the diagnostics that arrive
    /// with the question are the editor's copy of what was published, and what is needed is the
    /// compiler's, which carries the replacement. They are the same problems — the editor is
    /// asking about what it was told a moment ago.</para>
    /// </summary>
    private JsonArray? FixesFor(JsonObject? parameters)
    {
        if (Document(parameters) is not { } document)
        {
            return null;
        }

        DiagnosticBag found = new();
        SourceText source = _documents.Reader(document.Path);

        // Scanned rather than compiled. Every fix so far is the scanner's, and a scan is the
        // cheapest thing that could answer — this is asked whenever a cursor lands on a squiggle.
        _ = new ProfiC.Compiler.Lexing.Lexer(source, found).Scan();

        return Fixes.For(
            Conversions.UriOf(document.Path),
            parameters?["context"]?["diagnostics"] as JsonArray,
            [.. found]);
    }

    private JsonArray? DefinitionAt(JsonObject? parameters) =>
        Compiled(parameters) is var (units, model, unit, offset)
            ? Answers.Definition(units, model, unit, offset)
            : null;

    /// <summary>
    /// <para>What every name in the file is, for the editor to color by.</para>
    /// <para>The whole file rather than a place in it, and asked again after every change the
    /// editor thinks worth recoloring. That makes it the most-asked question here, which is why
    /// it is answered on the reading thread like the rest: a color arriving late is a file that
    /// flickers.</para>
    /// </summary>
    private JsonObject? ColorsFor(JsonObject? parameters) =>
        Checked(parameters) is var (_, model, unit)
            ? SemanticTokens.Of(unit, model, unit.Source)
            : null;

    /// <summary>
    /// <para>Every other place this file writes the name under the cursor.</para>
    /// <para>Asked whenever the caret moves, which makes it the second most-asked question here.
    /// It changes nothing, so unlike rename it answers for a name the language owns as well.
    /// </para>
    /// </summary>
    /// <summary>
    /// <para>The file lined up, as edits.</para>
    /// <para><b>One edit per line that changed, rather than one edit replacing the file.</b>
    /// Replacing the whole thing is a line shorter to write and would throw the reader's cursor
    /// to the top of the document, lose the folding, and put a whole-file change into their undo
    /// history for a run that altered three lines. An editor applies a list of small edits
    /// without moving anything it did not have to.</para>
    /// <para>The whole file is formatted either way. A range only decides which of the resulting
    /// edits are sent, because where a line belongs depends on every line above it — formatting
    /// a selection on its own would place it against whatever the selection happened to start
    /// with.</para>
    /// </summary>
    private JsonArray? LinedUp(JsonObject? parameters, bool whole)
    {
        if (Document(parameters) is not { } document)
        {
            return null;
        }

        SourceText source = _documents.Reader(document.Path);
        SourceText formatted = new(Formatter.Format(source), source.FileName);

        (int First, int Last) asked = whole
            ? (1, source.LineCount)
            : Lines(parameters?["range"] as JsonObject, source);

        JsonArray edits = [];

        for (int line = asked.First; line <= asked.Last && line <= source.LineCount; line++)
        {
            string was = source.GetLine(line).ToString().TrimEnd('\r', '\n');
            string now = formatted.GetLine(line).ToString().TrimEnd('\r', '\n');

            if (string.Equals(was, now, StringComparison.Ordinal))
            {
                continue;
            }

            edits.Add(new JsonObject
            {
                ["range"] = new JsonObject
                {
                    ["start"] = new JsonObject { ["line"] = line - 1, ["character"] = 0 },
                    ["end"] = new JsonObject { ["line"] = line - 1, ["character"] = was.Length },
                },
                ["newText"] = now,
            });
        }

        return edits;
    }

    /// <summary>The lines a range covers, counted as a reader counts them.</summary>
    private static (int First, int Last) Lines(JsonObject? range, SourceText source)
    {
        int first = (int?)range?["start"]?["line"] + 1 ?? 1;
        int last = (int?)range?["end"]?["line"] + 1 ?? source.LineCount;

        return (Math.Max(1, first), Math.Min(source.LineCount, Math.Max(first, last)));
    }

    private JsonArray? OccurrencesAt(JsonObject? parameters) =>
        Compiled(parameters) is var (_, model, unit, offset)
            ? Occurrences.In(unit, model, offset)
            : null;

    /// <summary>
    /// <para>Every use of the name under the cursor, across the whole program.</para>
    /// <para>Whether the declaration counts is the editor's to say, and it says so in the request.
    /// Missing, it counts — the protocol's own default, and the one that answers "where does this
    /// name appear" rather than "who calls it".</para>
    /// </summary>
    private JsonArray? ReferencesTo(JsonObject? parameters)
    {
        if (Compiled(parameters) is not var (units, model, unit, offset))
        {
            return null;
        }

        bool includingDeclaration =
            (bool?)parameters?["context"]?["includeDeclaration"] ?? true;

        return Occurrences.Across(units, model, unit, offset, includingDeclaration);
    }

    /// <summary>
    /// <para>The types a stretch of the file leaves unwritten.</para>
    /// <para>Asked about a range rather than a position, since this is the one question about a
    /// region of the file rather than about a point in it — an editor asks for what is on screen
    /// and asks again as it scrolls.</para>
    /// </summary>
    private JsonArray? HintsIn(JsonObject? parameters)
    {
        if (_hints.Nothing)
        {
            return [];
        }

        if (Checked(parameters) is not var (_, model, unit))
        {
            return null;
        }

        if (Conversions.OffsetOf(parameters?["range"]?["start"] as JsonObject, unit.Source)
                is not { } from
            || Conversions.OffsetOf(parameters?["range"]?["end"] as JsonObject, unit.Source)
                is not { } to)
        {
            return null;
        }

        return Hints.In(unit, model, unit.Source, from, to, _hints);
    }

    /// <summary>
    /// <para>The whole program around the file being asked about, checked, and where in it the
    /// question points.</para>
    /// <para>Compiled for the question rather than read from anything kept, which is the choice
    /// this server makes everywhere: the front end takes single-digit milliseconds on a realistic
    /// file, and a cache built before there is a measurement saying one is needed is a guess with
    /// a bug in it.</para>
    /// <para>Checked and not merely resolved, because the questions are about types: what a name's
    /// type is, and what an expression comes to, are the type checker's answers rather than the
    /// resolver's.</para>
    /// </summary>
    private (IReadOnlyList<CompilationUnit> Units, SemanticModel Model, CompilationUnit Unit, int Offset)?
        Compiled(JsonObject? parameters)
    {
        if (Checked(parameters) is not var (units, model, unit))
        {
            return null;
        }

        return Conversions.OffsetOf(parameters?["position"] as JsonObject, unit.Source) is { } offset
            ? (units, model, unit, offset)
            : null;
    }

    /// <summary>
    /// The same, for a question about a whole file rather than about a place in one. Held apart
    /// because a question with no position in it must not be refused for having none.
    /// </summary>
    private (IReadOnlyList<CompilationUnit> Units, SemanticModel Model, CompilationUnit Unit)?
        Checked(JsonObject? parameters)
    {
        if (Document(parameters) is not { } document)
        {
            return null;
        }

        DiagnosticBag aside = new();

        if (SourceDiscovery.Gather(document.Path, aside, _documents.Reader) is not { } compilation)
        {
            return null;
        }

        CompilationUnit? unit = compilation.Units.FirstOrDefault(
            u => SourceDiscovery.PathComparer.Equals(
                Path.GetFullPath(u.Source.FileName), document.Path));

        if (unit is null)
        {
            return null;
        }

        SemanticModel model = Resolver.Resolve(
            compilation.Units,
            aside,
            projects: compilation.Projects,
            entryPoint: compilation.EntryPoint);

        TypeChecker.Check(compilation.Units, model, aside);

        return (compilation.Units, model, unit);
    }

    // ---- The analysis itself -----------------------------------------------------------------

    /// <summary>
    /// <para>Compiles what the editor holds and publishes what the compiler said.</para>
    /// <para>Gathered rather than parsed alone, so that what is checked is the program rather
    /// than the file: a name declared in the file beside this one has to resolve, or every
    /// multi-file program would be a wall of undefined names. That means diagnostics arrive for
    /// files the reader never opened, which is correct and is why they are published per file.
    /// </para>
    /// </summary>
    private Task Analyze(string path, CancellationToken stop)
    {
        DiagnosticBag diagnostics = new();

        SourceDiscovery.Compilation? compilation =
            SourceDiscovery.Gather(path, diagnostics, _documents.Reader);

        if (compilation is not null)
        {
            SemanticModel model = Resolver.Resolve(
                compilation.Units,
                diagnostics,
                projects: compilation.Projects,
                entryPoint: compilation.EntryPoint,
                cancellation: stop);

            TypeChecker.Check(compilation.Units, model, diagnostics, stop);
            DefiniteAssignment.Analyze(compilation.Units, model, diagnostics, stop);

            foreach (CompilationUnit unit in compilation.Units)
            {
                DocumentationChecker.Check(unit, diagnostics);
            }
        }

        stop.ThrowIfCancellationRequested();

        Publish(path, compilation, diagnostics);
        return Task.CompletedTask;
    }

    /// <summary>
    /// <para>Sends what was found, one message per file, and clears what is no longer
    /// found.</para>
    /// <para>The clearing is the half that is easy to leave out and impossible to notice: a
    /// diagnostic stays in the panel until an empty list arrives for its file, so a file that has
    /// been fixed has to be told so explicitly. Every file this compilation covers is published
    /// whether or not it had anything, and every file that was mentioned before and is not part
    /// of this compilation at all is emptied.</para>
    /// </summary>
    private void Publish(
        string path, SourceDiscovery.Compilation? compilation, DiagnosticBag diagnostics)
    {
        Dictionary<string, JsonArray> byFile = new(SourceDiscovery.PathComparer);
        Dictionary<string, SourceText> sources = new(SourceDiscovery.PathComparer);

        // Every file in the compilation, so one that is now clean is published as clean rather
        // than left saying what it said before.
        foreach (CompilationUnit unit in compilation?.Units ?? [])
        {
            string full = Path.GetFullPath(unit.Source.FileName);

            byFile[full] = [];
            sources[full] = unit.Source;
        }

        // The file asked about, even where nothing gathered — a file that will not parse enough
        // to be gathered still has something to say about why.
        byFile.TryAdd(Path.GetFullPath(path), []);

        foreach (Diagnostic diagnostic in diagnostics.Sorted())
        {
            string full = Path.GetFullPath(diagnostic.FileName ?? path);

            sources.TryGetValue(full, out SourceText? source);
            (byFile.TryGetValue(full, out JsonArray? found) ? found : byFile[full] = [])
                .Add(Conversions.DiagnosticOf(diagnostic, source));
        }

        lock (_reporting)
        {
            // Anything named last time and not this time has stopped being part of the program —
            // an import removed, a project edited — so it is emptied rather than left standing.
            foreach (string stale in _reported.Where(f => !byFile.ContainsKey(f)).ToArray())
            {
                Send(stale, []);
            }

            _reported.Clear();

            foreach ((string full, JsonArray found) in byFile)
            {
                Send(full, found);

                if (found.Count > 0)
                {
                    _reported.Add(full);
                }
            }
        }
    }

    private void Send(string path, JsonArray found) =>
        _wire.Notify("textDocument/publishDiagnostics", new JsonObject
        {
            ["uri"] = Conversions.UriOf(path),
            ["diagnostics"] = found,
        });

    /// <summary>Whether the editor asked this to stop, which the caller reports as its exit code.</summary>
    public bool AskedToStop => _shuttingDown;

    public void Dispose() => _analysis.Dispose();
}
