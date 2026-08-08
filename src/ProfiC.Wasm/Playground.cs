using System.Reflection;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json.Nodes;
using ProfiC.Compiler;
using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Formatting;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Semantics;
using ProfiC.Compiler.Text;
using ProfiC.Services;

namespace ProfiC.Wasm;

/// <summary>
/// <para>What a browser may ask of the compiler.</para>
/// <para><b>Two questions, and the same passes answer both.</b> Checking a program is the front
/// end; running one is the front end and then the interpreter. Neither is a re-implementation of
/// anything — a second answer to what a name means, or to whether a program is well typed, would
/// agree with the compiler right up until it did not, and the place it disagreed would be a
/// teaching site telling somebody their correct program is wrong.</para>
/// <para>Answers are JSON strings rather than objects, because the boundary between C# and
/// JavaScript carries strings cheaply and anything richer would mean a description of every shape
/// on both sides of it.</para>
/// <para>Marked as the browser's, because that is the only place it can run: the attribute that
/// exports these to JavaScript exists nowhere else, and saying so is what keeps the platform
/// analyzer from having to guess.</para>
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class Playground
{
    /// <summary>What a program is called here. It is never on a disk, and the name is only for
    /// diagnostics to point at.</summary>
    private const string FileName = "playground.pc";

    /// <summary>
    /// <para>Everything the compiler has to say about a program, without running it.</para>
    /// <para>This is what an editor asks on every keystroke, so it is the front end and nothing
    /// after it.</para>
    /// </summary>
    [JSExport]
    public static string Check(string source)
    {
        DiagnosticBag diagnostics = new();
        (CompilationUnit unit, SemanticModel model) = Parse(source, diagnostics);

        JsonArray listed = Reported(diagnostics);
        Refusals(unit, model, listed);

        return new JsonObject { ["diagnostics"] = listed }.ToJsonString();
    }

    /// <summary>
    /// <para>The parts of the standard library a browser has no answer for.</para>
    /// <para>Every one of them reads or writes a file. .NET in a page has a file system, but it is
    /// one the page invented and nothing else can see — so these would appear to work and would
    /// touch nothing, which is worse than refusing them. A reader following a lesson about files
    /// would watch their program write one and then be unable to find it.</para>
    /// <para>Written out rather than taken as a range of the enumeration, so that a member added
    /// between two of these is not swept in by where it happens to sit.</para>
    /// </summary>
    private static readonly HashSet<BuiltInId> Elsewhere =
    [
        BuiltInId.FileRead, BuiltInId.FileReadLines, BuiltInId.FileWrite,
        BuiltInId.FileWriteLines, BuiltInId.FileAppend, BuiltInId.FileExists,
        BuiltInId.FileDelete, BuiltInId.FileCopy, BuiltInId.FileMove,
        BuiltInId.FileSize, BuiltInId.FileChanged,
        BuiltInId.DirectoryExists, BuiltInId.DirectoryCreate, BuiltInId.DirectoryDelete,
        BuiltInId.DirectoryFiles, BuiltInId.DirectoryFolders, BuiltInId.DirectoryCurrent,
    ];

    /// <summary>
    /// <para>Marks every place the program reaches for a file, as an error.</para>
    /// <para><b>Said while it is being written rather than when it is run</b>, which is the rule
    /// the rest of the compiler follows: a mistake worth reporting is worth reporting before
    /// somebody presses a button. It reaches the editor as an ordinary diagnostic, so the line is
    /// underlined like any other.</para>
    /// <para>Given an id of its own rather than a <c>PC</c> number, because it is not one. Nothing
    /// is wrong with the program — it would run on a machine — and a code that looked like the
    /// compiler's would send a reader to an appendix that has never heard of it.</para>
    /// </summary>
    private static void Refusals(CompilationUnit unit, SemanticModel model, JsonArray listed)
    {
        foreach (SyntaxNode node in unit.Descendants())
        {
            // Only where the name is written. A call is bound to the same member as the name
            // inside it, so without this every use is marked twice — and the second mark covers
            // the arguments rather than the name, which is the same reason renaming needs it.
            if (!node.HasName
                || model.GetBuiltIn(node) is not { } member
                || !Elsewhere.Contains(member))
            {
                continue;
            }

            SourcePosition start = node.NameSpan.Start;

            JsonObject one = new()
            {
                ["id"] = "browser",
                ["severity"] = "error",
                ["title"] = "Files are not available here",
                ["message"] =
                    "This playground runs in a browser, which has no files to read or write. The "
                    + "program is fine — run it on your own machine with 'pc run' and this works.",
                ["line"] = start.Line - 1,
                ["column"] = start.Column - 1,
                ["length"] = node.NameSpan.Length,
            };

            listed.Add((JsonNode)one);
        }
    }

    /// <summary>
    /// <para>Runs a program and answers with what it printed.</para>
    /// <para><paramref name="input"/> is everything the program may read, since a page cannot be
    /// asked a question halfway through and answer it. A program reading past the end of it gets
    /// what any program reading past the end of its input gets.</para>
    /// <para>Nothing runs if the front end reported an error, which is the same rule the command
    /// line follows: a program that does not check is not a program to run.</para>
    /// <para><b>Nothing here can stop a program that will not stop.</b> A loop with no way out
    /// runs until whatever is hosting this gives up on it — which is why the page runs this in a
    /// worker it can discard, and why no time limit is pretended at here.</para>
    /// </summary>
    [JSExport]
    public static string Run(string source, string input)
    {
        DiagnosticBag diagnostics = new();
        (CompilationUnit unit, SemanticModel model) = Parse(source, diagnostics);

        JsonArray listed = Reported(diagnostics);
        int reported = listed.Count;

        Refusals(unit, model, listed);

        JsonObject answer = new() { ["diagnostics"] = listed };

        // Nothing runs if the front end objected, which is the rule the command line follows —
        // or if the program reaches for a file, which is this playground's own rule and has to
        // stop it just as firmly. Running anyway would touch a file system nobody can see.
        if (diagnostics.HasErrors || listed.Count > reported)
        {
            answer["output"] = string.Empty;
            answer["ran"] = false;

            return answer.ToJsonString();
        }

        Capped printed = new();
        answer["ran"] = true;

        try
        {
            ProfiC.Interpreter.Interpreter.Run(
                Lowering.Lower(unit, model), model, printed, new StringReader(input));

            answer["output"] = printed.Text;
        }
        catch (Capped.Full)
        {
            answer["output"] = printed.Text;
            answer["failure"] =
                $"Stopped after {Capped.Limit:N0} characters. A program printing this much is "
                + "usually a loop with no way out of it.";
        }
        catch (Exception failed)
        {
            // What the program did wrong while running, said the way the command line says it.
            // The partial output is kept: a program that printed three lines and then failed has
            // told the reader something, and throwing it away would hide where it got to.
            answer["output"] = printed.Text;
            answer["failure"] = Failure(failed);
        }

        return answer.ToJsonString();
    }

    /// <summary>
    /// <para>Somewhere to print that stops accepting.</para>
    /// <para><b>A page can stop a program that will not stop, and cannot take back the memory it
    /// spent first.</b> A loop printing a line each time round fills this faster than anybody
    /// reaches the button, and the tab is what pays — so the writer gives up rather than the
    /// machine. Nothing is lost that a reader would have read: a million characters of output is
    /// already past what any page will show.</para>
    /// <para>Given up by throwing, because that is the only way out of an interpreter that is
    /// several calls deep and asking to print. The caller knows this one from a program's own
    /// failure and says something different about it.</para>
    /// </summary>
    private sealed class Capped : StringWriter
    {
        /// <summary>How much a program may print before this stops taking it.</summary>
        internal const int Limit = 1_000_000;

        /// <summary>Raised when a program has printed more than <see cref="Limit"/>.</summary>
        internal sealed class Full : Exception;

        /// <summary>What was printed before it stopped.</summary>
        internal string Text => GetStringBuilder().ToString();

        public override void Write(char value)
        {
            Room(1);
            base.Write(value);
        }

        public override void Write(string? value)
        {
            Room(value?.Length ?? 0);
            base.Write(value);
        }

        private void Room(int wanted)
        {
            if (GetStringBuilder().Length + wanted > Limit)
            {
                throw new Full();
            }
        }
    }

    /// <summary>
    /// <para>The program, laid out the way <c>pc format</c> lays one out.</para>
    /// <para>The compiler's own formatter rather than anything written for a browser, which is the
    /// only way a page can promise that tidying here and tidying on somebody's machine agree — and
    /// it is what makes a playground able to write the <c>end</c> of a block at all, since which
    /// closer belongs to which opener is a thing only this knows.</para>
    /// <para>A program that will not parse is handed back unchanged. Laying out a tree that was
    /// never built means guessing, and rearranging somebody's half-written line while they are
    /// still writing it is worse than leaving it alone.</para>
    /// </summary>
    [JSExport]
    public static string Format(string source)
    {
        DiagnosticBag diagnostics = new();
        SourceText text = new(source, FileName);

        Parser.Parse(text, diagnostics);

        return diagnostics.HasErrors ? source : Formatter.Format(text);
    }

    /// <summary>
    /// <para>What could be written where the cursor is.</para>
    /// <para>The same list an editor shows, from the same code that builds it for one: what
    /// follows a dot is the members of whatever precedes it, and what follows nothing is the
    /// names in scope. Neither is worked out here — a second answer to "what is in scope" would
    /// be a second resolver, and the day it disagreed would be a page telling somebody a name
    /// they can write does not exist.</para>
    /// <para>An empty list where the cursor is somewhere no name can go, rather than nothing at
    /// all: a page has one thing to do with either, and the distinction an editor draws between
    /// them is about whether to leave a list already on screen alone.</para>
    /// </summary>
    [JSExport]
    public static string Complete(string source, int offset)
    {
        SourceText text = new(source, FileName);

        JsonArray offered =
            Completion.After(FileName, text, offset, OnThePage)
            ?? Completion.Bare(FileName, text, offset, OnThePage)
            ?? [];

        return offered.ToJsonString();
    }

    /// <summary>
    /// <para>What the compiler knows about the place the cursor is in: the thing under it, and the
    /// call it sits inside.</para>
    /// <para><b>Both at once, because they answer together and are asked together.</b> A cursor
    /// halfway through <c>Math.Round(</c> is over a name and inside a call, and a page wanting to
    /// show either would otherwise take the program through the front end twice to find out.
    /// </para>
    /// <para>Each is the shape an editor is sent, which is what makes the page able to do more
    /// with them rather than less: the hover carries markdown holding a line of Profi-C, and the
    /// signature carries its parameters as places in the label rather than as text. A page owns
    /// its own renderer for both, and can color the one and embolden the other — neither of which
    /// an editor's tooltip will do.</para>
    /// </summary>
    [JSExport]
    public static string Describe(string source, int offset)
    {
        SourceText text = new(source, FileName);

        if (OnThePage(FileName, text, CancellationToken.None)
            is not { Model: { } model, Unit: { } unit })
        {
            return "{}";
        }

        JsonObject answer = new();

        if (Answers.Hover([unit], unit, model, text, offset) is { } hover)
        {
            answer["hover"] = hover;
        }

        if (Answers.Signature([unit], unit, model, text, offset) is { } signature)
        {
            answer["signature"] = signature;
        }

        return answer.ToJsonString();
    }

    /// <summary>
    /// <para>The program a page holds, which is the one file in the editor on it.</para>
    /// <para>What the same question answers to on a machine is a folder or a project, read off a
    /// disk. There is no disk here and there are no files beside this one, so gathering is the
    /// whole of what changes between the two — and it is the only thing the language services ask
    /// their caller for.</para>
    /// <para>Resolved and checked, and nothing after that. The passes that follow report what is
    /// unassigned and what is unused, and a file somebody is halfway through typing trips both
    /// constantly — while none of it is anything a completion list or a hover reads.</para>
    /// </summary>
    private static Around OnThePage(string path, SourceText text, CancellationToken cancellation)
    {
        DiagnosticBag aside = new();
        CompilationUnit unit = Parser.Parse(text, aside);

        SemanticModel model = Resolver.Resolve(
            [unit], aside, requireEntryPoint: false, cancellation: cancellation);

        TypeChecker.Check([unit], model, aside, cancellation);

        return new Around(model, unit);
    }

    /// <summary>
    /// <para>Which stretches of the program fold away, and what each one holds.</para>
    /// <para><b>Parsed and nothing more.</b> Folding is wanted most in a long program being worked
    /// on, which is exactly when it does not compile — so this stops at the parse, which recovers,
    /// and the blocks around a mistake still fold.</para>
    /// <para>Each range carries what an editor can show in place of what it hid, since an editor
    /// left to itself draws a mark saying only that something is there.</para>
    /// </summary>
    [JSExport]
    public static string Fold(string source)
    {
        SourceText text = new(source, FileName);
        DiagnosticBag aside = new();

        JsonArray folded = [];

        foreach (Folding.Range range in Folding.Of(Parser.Parse(text, aside), text))
        {
            JsonObject one = new()
            {
                // Counted from zero, as everything else crossing this boundary is.
                ["line"] = range.Line - 1,
                ["endLine"] = range.EndLine - 1,
                ["kind"] = range.Kind,
                ["held"] = range.Held,
            };

            folded.Add((JsonNode)one);
        }

        return folded.ToJsonString();
    }

    /// <summary>
    /// <para>The compiler's version, so a page can say which one it is running.</para>
    /// <para>Read from the informational version, which is the one that follows the number set in
    /// <c>Directory.Build.props</c>. <b>The assembly version does not.</b> It is pinned at
    /// <c>1.0.0.0</c> for the whole of 1.x on purpose, so that a rebuild never changes what one
    /// assembly records about another — which means asking it would have this page reporting
    /// 1.0.0 at every release of 1.x, confidently and wrongly.</para>
    /// <para>Cut at the <c>+</c>, where the commit a build came from is written, for the same
    /// reason <c>pc --version</c> cuts it: that belongs in a build log.</para>
    /// </summary>
    [JSExport]
    public static string Version() =>
        (typeof(Playground).Assembly
                           .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                           ?.InformationalVersion ?? "0.0.0")
        .Split('+')[0];

    /// <summary>
    /// Takes a program through the whole front end. One file — a page holds one editor, and
    /// gathering the files beside it is a question about a disk that a browser does not have.
    /// </summary>
    private static (CompilationUnit Unit, SemanticModel Model) Parse(
        string source, DiagnosticBag diagnostics)
    {
        CompilationUnit unit = Parser.Parse(new SourceText(source, FileName), diagnostics);

        return (unit, FrontEnd.Check(unit, diagnostics, requireEntryPoint: true));
    }

    /// <summary>
    /// <para>Everything reported, in the shape an editor marks up.</para>
    /// <para>Positions are counted from zero here and from one in the compiler, which is the
    /// conversion every editor boundary makes. A span is given both ends so that a mark covers
    /// the thing it is about rather than starting at it.</para>
    /// </summary>
    private static JsonArray Reported(DiagnosticBag diagnostics)
    {
        JsonArray listed = [];

        foreach (Diagnostic reported in diagnostics.Sorted())
        {
            SourcePosition start = reported.Span.Start;

            JsonObject one = new()
            {
                ["id"] = reported.Id,
                ["severity"] = reported.Severity.ToString().ToLowerInvariant(),
                ["title"] = reported.Descriptor.Title,
                ["message"] = reported.Message,
                ["line"] = start.Line - 1,
                ["column"] = start.Column - 1,
                ["length"] = reported.Span.Length,
            };

            // Added as a node rather than through the generic overload, which is not safe to
            // trim: it accepts anything and would serialize a type whose members a trimmer had
            // already removed. This one takes a node, which is what it already is.
            listed.Add((JsonNode)one);
        }

        return listed;
    }

    /// <summary>
    /// <para>What to say about a program that failed while running.</para>
    /// <para>The two the language raises itself already carry a sentence somebody can read, and
    /// are passed through as they are — an exception nothing caught, and a failure like dividing
    /// by zero. Anything else is the compiler's problem rather than the program's, and says so
    /// rather than letting a reader believe their code caused it.</para>
    /// </summary>
    private static string Failure(Exception failed) =>
        failed is ProfiC.Interpreter.UncaughtProfiCException
                  or ProfiC.Interpreter.ProfiCRuntimeException
            ? failed.Message
            : $"The compiler failed while running this: {failed.Message}";
}
