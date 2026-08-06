using System.Globalization;
using System.Text.Json;
using ProfiC.Compiler;
using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Documentation;
using ProfiC.Compiler.Emit;
using ProfiC.Compiler.Formatting;
using ProfiC.Compiler.Lexing;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Semantics;
using ProfiC.Compiler.Text;
using ProfiC.Interpreter;
using ProfiC.Runtime;
using ProfiC.Services;

namespace ProfiC.Cli;

/// <summary>
/// <para>Entry point for the <c>profi-c</c> command, and for the <c>pc</c> alias that shares
/// it.</para>
/// <para>Diagnostic formatting belongs here rather than in the compiler, so that the front
/// end stays free of console output and can later back a language server.</para>
/// </summary>
public static class Program
{
    private const string Version = "0.1.0";

    /// <summary>
    /// <para>The name this was invoked as, so that messages name the command the reader
    /// actually typed.</para>
    /// <para>Falls back to the long name when the process is the .NET host itself, which is
    /// what happens under <c>dotnet run</c>.</para>
    /// </summary>
    private static string ToolName
    {
        get
        {
            string? invoked = Path.GetFileNameWithoutExtension(System.Environment.ProcessPath);

            return string.IsNullOrEmpty(invoked)
                   || invoked.Equals("dotnet", StringComparison.OrdinalIgnoreCase)
                ? "profi-c"
                : invoked;
        }
    }

    /// <summary>
    /// <para>What the process leaves behind, so that whatever ran it can tell apart the ways
    /// this ends.</para>
    /// <para><b>One failure code answers "did it work", which is all a person at a terminal
    /// wants.</b> Anything scripting this wants more: a program with a mistake in it is the
    /// ordinary result of checking one, a command line that could not be read is the caller's own
    /// bug, and a compiler that asserted something impossible is nobody's program at all. Told
    /// apart, a build step can pass the first back to whoever wrote the code and shout about the
    /// last.</para>
    /// <para>Settled now rather than later because these are the half of an interface that
    /// nothing declares: the day somebody writes a script against them is the day changing them
    /// breaks it silently.</para>
    /// </summary>
    private const int Ok = 0;

    /// <summary>Something is wrong with the program, and it was said with a position.</summary>
    private const int Reported = 1;

    /// <summary>Something is wrong with the command line, so no program was read.</summary>
    private const int Misused = 2;

    /// <summary>The compiler asserted something it says cannot happen.</summary>
    private const int Broken = 3;

    /// <summary>
    /// <para>One command, as both the dispatch and the help read it.</para>
    /// <para><b>One table rather than a switch beside a list of lines.</b> Kept apart, the two
    /// drift in the direction that is hardest to notice: a command that works and is documented
    /// nowhere, which is exactly what happened to <c>format</c>.</para>
    /// </summary>
    /// <param name="Name">The word that selects it.</param>
    /// <param name="Takes">What follows the name in the usage line, empty for a command that
    /// takes nothing.</param>
    /// <param name="Says">The one line beside it in the list.</param>
    /// <param name="Run">What it does, given the whole command line.</param>
    /// <param name="Detail">Shown only when this command is the one being asked about.</param>
    private sealed record Command(
        string Name, string Takes, string Says, Func<string[], int> Run, string[] Detail);

    private static readonly Command[] Commands =
    [
        new("run", "<file>", "Run a .pc program or a .pcp project", RunProgram,
        [
            "Checks the program and then executes it on the interpreter. Nothing runs",
            "until everything checks. No file is produced; 'build' is what writes one.",
        ]),
        new("build", "<file>", "Compile one to a .NET assembly, into bin", RunBuild,
        [
            "Writes four files into a 'bin' beside the program: the assembly, its runtime",
            "configuration, the Profi-C runtime, and a launcher you can start without",
            "naming dotnet. The machine it runs on still needs .NET installed.",
            "",
            "  --out <folder>       Write them somewhere else",
            "  --runtime <platform> Build for a machine that is not this one",
            "",
            "'platforms' prints what --runtime accepts here.",
        ]),
        new("check", "<file>", "Check a .pc program or a .pcp project", RunCheck, []),
        new("new", "<name>", "Start an empty program, ready to be written", RunNew,
        [
            "  --project            Write a folder with a .pcp and a program in it",
            "",
            "What it writes is the smallest legal program and nothing else: every line",
            "already in a new file is a line somebody has to read and then delete.",
            "'sample' writes one that does something, for a first look at the language.",
            "",
            "Refuses to write over anything that is already there.",
        ]),
        new("sample", "<name>", "Write a program that does something, to read and change",
            RunSample,
        [
            "  --project            Write a folder with a .pcp and a program in it",
            "",
            "It prints, and then it loops, because the first thing anybody does with one",
            "is change one of those and run it again. 'new' writes an empty one instead.",
            "",
            "Refuses to write over anything that is already there.",
        ]),
        new("lower", "<file>", "Print the simplified tree the back end sees", RunLower, []),
        new("tokens", "<file>", "Scan one .pc file and print its token stream", RunTokens, []),
        new("ast", "<file>", "Parse one .pc file and print its syntax tree", RunAst,
        [
            "  --positions          Write each node's span beside it",
        ]),
        new("format", "<file>", "Line one .pc file up and print it", RunFormat,
        [
            "  --write              Save it back instead of printing it",
            "  --check              Print nothing, and fail if it is not already formatted",
            "",
            "Indentation and spacing only, so a comment cannot be lost — and a file that",
            "does not parse is formatted anyway.",
        ]),
        new("outline", "<file>", "Print what one .pc file declares, as JSON", RunOutline, []),
        new("project", "<file>", "Print the .pcp that builds a file, as JSON", RunProject, []),
        new("vocabulary", "", "Print every word the language reserves, as JSON",
            _ => RunVocabulary(), []),
        new("platforms", "", "Print the platforms --runtime accepts, as JSON",
            _ => RunPlatforms(), []),
        new("debug", "", "Debug a program, spoken to by an editor", _ => RunDebug(),
        [
            "Speaks the Debug Adapter Protocol on its own standard input and output, and",
            "is meant to be started by an editor rather than typed. Run by hand it waits",
            "for a request that never comes, which is correct and looks like a hang.",
        ]),
        new("lsp", "", "Answer an editor's questions while it is open", _ => RunLanguageServer(),
        [
            "Speaks the Language Server Protocol on its own standard input and output,",
            "and is the only one of these that stays open. Meant to be started by an",
            "editor rather than typed.",
        ]),
    ];

    private static int Main(string[] args) => Run(args);

    /// <summary>Runs the command. Public so that the <c>pc</c> alias can forward to it.</summary>
    public static int Run(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
        {
            WriteUsage();
            return Ok;
        }

        if (args[0] is "--version" or "-v")
        {
            return WriteVersion();
        }

        if (Commands.FirstOrDefault(c => c.Name == args[0]) is not { } command)
        {
            return UnknownCommand(args[0]);
        }

        // Asked about rather than run. Answered before anything reads the rest of the line,
        // since somebody asking what a command takes is somebody who does not know yet.
        if (args.Skip(1).Any(argument => argument is "--help" or "-h"))
        {
            WriteUsage(command);
            return Ok;
        }

        try
        {
            return command.Run(args);
        }
        catch (InvalidOperationException assertion)
        {
            // The type the compiler's own assertions throw, and only that type. A missing file
            // or a folder that cannot be written is the environment's fault and is answered
            // where it happens; calling either a fault in the compiler would be wrong.
            return ReportInternalError(assertion);
        }
    }

    /// <summary>
    /// <para>Reports a fault the compiler asserts cannot happen, and exits the way a failed
    /// build exits.</para>
    /// <para>Written with no position because there is no reliable one: an assertion fires deep
    /// in a pass, and what it knows is what it was doing rather than where in the program it
    /// was.</para>
    /// <para>The stack trace follows the message rather than replacing it. It is the only thing
    /// that says where the compiler actually went wrong, so it is printed in full — and the
    /// message above it says whose problem this is, so nobody reads the trace as something they
    /// were meant to act on.</para>
    /// </summary>
    private static int ReportInternalError(Exception assertion)
    {
        DiagnosticDescriptor descriptor = DiagnosticDescriptors.InternalError;

        Console.Error.WriteLine(
            $"{ToolName}: error {descriptor.Id}: "
            + string.Format(
                CultureInfo.InvariantCulture, descriptor.MessageFormat, assertion.Message));

        Console.Error.WriteLine();
        Console.Error.WriteLine(assertion);
        return Broken;
    }

    /// <summary>
    /// Every command in one list, laid out from the table that dispatches them — so a command
    /// that runs is a command that is written down.
    /// </summary>
    private static void WriteUsage()
    {
        int width = Commands.Max(c => c.Name.Length + c.Takes.Length) + 2;

        Console.WriteLine($"{ToolName} - the Profi-C compiler");
        Console.WriteLine();
        Console.WriteLine("Usage:");

        foreach (Command command in Commands)
        {
            string written = $"{command.Name} {command.Takes}".TrimEnd();

            Console.WriteLine($"  {ToolName} {written.PadRight(width)}{command.Says}");
        }

        Console.WriteLine($"  {ToolName} {"--version".PadRight(width)}Print the compiler version");
        Console.WriteLine($"  {ToolName} {"--help".PadRight(width)}Print this message");
        Console.WriteLine();
        Console.WriteLine($"'{ToolName} <command> --help' says more about one of them.");
        Console.WriteLine();
        Console.WriteLine("Naming a .pc file compiles it together with the shared code beside");
        Console.WriteLine("it: every other .pc in the same folder that declares no Program.");
        Console.WriteLine("A .pcp project compiles exactly what it lists, across any folders.");
        Console.WriteLine();
        Console.WriteLine("The extension may be left off: 'run Program' finds Program.pc or");
        Console.WriteLine("Program.pcp. Write it when both are there and you mean one.");
    }

    /// <summary>What one command takes and what it is for, for somebody who asked about it.</summary>
    private static void WriteUsage(Command command)
    {
        Console.WriteLine($"Usage: {ToolName} {command.Name} {command.Takes}".TrimEnd());
        Console.WriteLine();
        Console.WriteLine(command.Says);

        if (command.Detail.Length > 0)
        {
            Console.WriteLine();

            foreach (string line in command.Detail)
            {
                Console.WriteLine(line);
            }
        }
    }

    /// <summary>
    /// Finds the file a command was pointed at, reporting what is wrong with the argument if
    /// anything is. Null means the command cannot proceed and has already said why.
    /// </summary>
    private static SourceDiscovery.FileTarget? Target(string[] args, string command)
    {
        if (Named(args, command) is not { } named)
        {
            return null;
        }

        if (SourceDiscovery.Locate(named, out string problem) is not { } target)
        {
            Console.Error.WriteLine($"{ToolName}: {problem}");
            return null;
        }

        return target;
    }

    /// <summary>
    /// <para>The thing a command was pointed at, or null where the line does not name one.</para>
    /// <para><b>A word beginning with a dash is a flag and not a path.</b> Read as one,
    /// <c>build --help</c> answers "file not found: --help.pc" — which names a file nobody meant,
    /// about a question nobody asked, and reads as though the tool were broken rather than as
    /// though the line were.</para>
    /// </summary>
    private static string? Named(string[] args, string command)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine($"{ToolName}: '{command}' requires a file path.");
            return null;
        }

        if (args[1].StartsWith('-'))
        {
            Console.Error.WriteLine(
                $"{ToolName}: '{command}' takes a file first, and '{args[1]}' is not one. "
                + $"Try '{ToolName} {command} --help'.");

            return null;
        }

        return args[1];
    }

    /// <summary>
    /// Finds a source file for the commands that read one at a time. A project describes a
    /// build rather than being Profi-C, so there is nothing in one for them to show.
    /// </summary>
    private static string? SourceArgument(string[] args, string command)
    {
        if (Target(args, command) is not { } target)
        {
            return null;
        }

        if (target.IsProject)
        {
            Console.Error.WriteLine(
                $"{ToolName}: '{command}' reads one {SourceDiscovery.SourceExtension} file, and "
                + $"{target.Path} is a project.");

            return null;
        }

        return target.Path;
    }

    private static int WriteVersion()
    {
        Console.WriteLine(Version);
        return Ok;
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine(
            $"{ToolName}: unknown command '{command}'. Try '{ToolName} --help'.");
        return Misused;
    }

    /// <summary>
    /// Scans a file and prints its tokens. Exits non-zero if any error was reported, but
    /// still prints the tokens: the scanner recovers, so a file with a mistake in it still
    /// produces a usable stream.
    /// </summary>
    /// <summary>
    /// <para>Prints every word the language reserves and every type it provides, as JSON.</para>
    /// <para>Written for the tooling that has to know the language's vocabulary without being
    /// built against the compiler — a TextMate grammar, a language server's completion list, a
    /// syntax highlighter in another editor. Those live in their own repository, and asking
    /// them to reference this one would make every keyword a two-repository change.</para>
    /// <para>The answer is read straight from the tables the compiler itself uses, so it cannot
    /// describe a language other than the one that just printed it.</para>
    /// </summary>
    /// <summary>
    /// <para>Debugs a program, speaking the Debug Adapter Protocol on standard input and
    /// output.</para>
    /// <para>Takes no file. Which program to debug arrives in the protocol's <c>launch</c>
    /// request, because the editor is what knows — a reader presses a button beside a file
    /// rather than typing a path, and the same running adapter may be asked for a different
    /// program next.</para>
    /// <para><b>Nothing else may write to standard output while this runs.</b> The stream is the
    /// protocol, and a stray line of ordinary text is a framing error the editor cannot recover
    /// from. What the program being debugged prints goes out as <c>output</c> events instead,
    /// which is also what puts it in the editor's debug console rather than nowhere.</para>
    /// <para>Meant to be launched by an editor rather than typed. Run by hand it will sit
    /// waiting for a request that never comes, which is correct and looks like a hang.</para>
    /// </summary>
    private static int RunDebug()
    {
        using Stream input = Console.OpenStandardInput();
        using Stream output = Console.OpenStandardOutput();
        using Debugging.DebugAdapter adapter = new(input, output);

        adapter.Run();

        return Ok;
    }

    /// <summary>
    /// <para>Answers an editor's questions about Profi-C for as long as it stays open, speaking
    /// the Language Server Protocol over standard input and output.</para>
    /// <para><b>Every other command reads a file and exits, which is the thing this exists to
    /// stop.</b> A file being typed into is not on disk in the form the reader is looking at, and
    /// half of it is not valid Profi-C at any given moment — so questions about it can only be
    /// answered by something that holds it. This holds it.</para>
    /// <para>Takes no file. Which files matter arrives in the protocol, because the editor is
    /// what knows: it opens and closes them as the reader does.</para>
    /// <para><b>Nothing else may write to standard output while this runs.</b> The stream is the
    /// protocol, and a stray line of ordinary text is a framing error the editor cannot recover
    /// from. Anything worth saying goes out as a <c>window/logMessage</c>.</para>
    /// <para>Meant to be launched by an editor rather than typed. Run by hand it will sit waiting
    /// for a request that never comes, which is correct and looks like a hang.</para>
    /// </summary>
    private static int RunLanguageServer()
    {
        using Stream input = Console.OpenStandardInput();
        using Stream output = Console.OpenStandardOutput();
        using LanguageServer.LanguageServer server = new(input, output);

        server.Run();

        // A stream that ended without the editor asking is the editor having gone away
        // unexpectedly, which the protocol says to report as a failure rather than a clean stop.
        return server.AskedToStop ? 0 : 1;
    }

    private static int RunVocabulary()
    {
        Console.WriteLine(Vocabulary.AsJson());

        return Ok;
    }

    /// <summary>
    /// <para>The platforms a build can target here, and which one it targets by default.</para>
    /// <para>Written for an editor, which has to offer the choice and cannot work it out: what
    /// is available depends on which launchers the SDK installed and which any project has ever
    /// published for, and both of those are facts about the machine. Asking the compiler is the
    /// same bridge <c>vocabulary</c> is — one place knows, and everything else reads it rather
    /// than keeping a second list that drifts.</para>
    /// </summary>
    private static int RunPlatforms()
    {
        // Qualified: the interpreter has an Environment of its own, and a scope chain is not
        // the place to ask for a newline.
        string installed = string.Join(
            $",{System.Environment.NewLine}",
            AppHost.Installed().Select(rid => $"    \"{rid}\""));

        Console.WriteLine("{");
        Console.WriteLine($"  \"default\": \"{AppHost.ThisPlatform}\",");
        Console.WriteLine("  \"installed\": [");
        Console.WriteLine(installed);
        Console.WriteLine("  ]");
        Console.WriteLine("}");

        return Ok;
    }

    /// <summary>
    /// <para>What one file declares, for an editor's outline and breadcrumbs.</para>
    /// <para>Parsed and nothing more — not resolved, not checked. An outline is wanted most
    /// while a file is being written, which is exactly when it does not compile, and the parser
    /// recovers where the rest of the front end would refuse. Diagnostics are collected and
    /// dropped for the same reason: a reader looking at the shape of their file is not asking
    /// about its mistakes, and the editor reports those from a build.</para>
    /// </summary>
    private static int RunOutline(string[] args)
    {
        if (SourceArgument(args, "outline") is not { } path)
        {
            return Misused;
        }

        SourceText source = SourceText.FromFile(path);
        DiagnosticBag aside = new();

        Console.WriteLine(Outline.AsJson(Parser.Parse(source, aside), source));

        return Ok;
    }

    /// <summary>
    /// <para>Lines a file up, to the screen or over the file itself.</para>
    /// <para>Written back only when asked, so that the ordinary form of this command can be run
    /// against anything without wondering what it did. <c>--check</c> answers with an exit code
    /// instead, which is what a build step wants.</para>
    /// </summary>
    private static int RunFormat(string[] args)
    {
        if (SourceArgument(args, "format") is not { } path)
        {
            return Misused;
        }

        SourceText source = SourceText.FromFile(path);
        string formatted = Formatter.Format(source);

        if (args.Contains("--check"))
        {
            if (string.Equals(formatted, source.Text, StringComparison.Ordinal))
            {
                return Ok;
            }

            Console.Error.WriteLine($"{path}: not formatted.");
            return Reported;
        }

        if (args.Contains("--write"))
        {
            // Only where it would change something. Rewriting an unchanged file gives it a new
            // timestamp, which is enough to make a watcher rebuild and a backup tool copy it.
            if (!string.Equals(formatted, source.Text, StringComparison.Ordinal))
            {
                File.WriteAllText(path, formatted);
            }

            return Ok;
        }

        Console.Out.Write(formatted);
        return Ok;
    }

    /// <summary>
    /// <para>Which project builds a file, for an editor deciding what a button points at.</para>
    /// <para>Answered here rather than by whatever asks, because the answer depends on how a
    /// <c>.pcp</c> is read — and a second reader of that format would agree with this one until
    /// the format gained a word. That disagreement is silent: the other reader finds no project,
    /// says so, and runs the file on its own.</para>
    /// <para>Nothing is compiled and nothing is checked, so this stays fast enough to sit on the
    /// path between a click and what it does.</para>
    /// </summary>
    private static int RunProject(string[] args)
    {
        // Located rather than taken as written, so that 'project Program' answers about the same
        // file 'run Program' would compile.
        if (Target(args, "project") is not { } target)
        {
            return Misused;
        }

        ProjectSearch.Claim claim = ProjectSearch.For(target.Path);

        Console.WriteLine(JsonSerializer.Serialize(
            new
            {
                file = Path.GetFullPath(target.Path),
                project = claim.Project,
                searched = claim.Searched,
            },
            new JsonSerializerOptions { WriteIndented = true }));

        return Ok;
    }

    /// <summary>
    /// <para>What <c>new</c> writes: the smallest legal program, and nothing else.</para>
    /// <para><b>A new thing is empty.</b> Somebody who asked for a new program is about to write
    /// one, and every line already in the file is a line they have to read and then delete. What
    /// a first program can look like is a different question, asked by <c>sample</c>.</para>
    /// </summary>
    private const string Blank = """
        shared model Program
            function Main()
            end function
        end model

        """;

    /// <summary>
    /// <para>What <c>sample</c> writes: a program that does something when it is run.</para>
    /// <para>It prints, and then it loops, because the first thing anybody does with one of these
    /// is change one of those and run it again. The same program the README walks through,
    /// deliberately — two starting points that differ in small ways is two things to keep
    /// true.</para>
    /// </summary>
    private const string Sample = """
        shared model Program
            function Main()
                Console.WriteLine("Hello, World!");

                loop for i = 1 to 5
                    Console.WriteLine(i + " squared is " + (i * i));
                end loop
            end function
        end model

        """;

    private static int RunNew(string[] args) => Writing(args, "new", Blank);

    private static int RunSample(string[] args) => Writing(args, "sample", Sample);

    /// <summary>
    /// <para>Writes a program somebody can run, which is the only kind of command a beginner
    /// meets first.</para>
    /// <para><b>Nothing is written over.</b> A tool that scaffolds is a tool somebody will point
    /// at a folder they are already working in, and losing a file to a mistyped name is not
    /// something an undo can reach.</para>
    /// </summary>
    private static int Writing(string[] args, string command, string program)
    {
        if (Named(args, command) is not { } asked)
        {
            return Misused;
        }

        string name = Path.GetFileNameWithoutExtension(asked);

        if (name.Length == 0 || !name.All(c => char.IsLetterOrDigit(c) || c == '_'))
        {
            Console.Error.WriteLine(
                $"{ToolName}: '{asked}' is not a name a project or a file can take. "
                + "Letters, digits and underscores.");

            return Misused;
        }

        return args.Contains("--project")
            ? NewProject(asked, name, program)
            : NewProgram(asked + SourceDiscovery.SourceExtension, program);
    }

    /// <summary>One file, written where it was named.</summary>
    private static int NewProgram(string path, string program)
    {
        if (File.Exists(path))
        {
            Console.Error.WriteLine($"{ToolName}: {path} is already there.");
            return Misused;
        }

        if (Path.GetDirectoryName(Path.GetFullPath(path)) is { Length: > 0 } folder)
        {
            Directory.CreateDirectory(folder);
        }

        File.WriteAllText(path, program);

        Console.WriteLine($"Wrote {path}");
        Console.WriteLine($"Run it with: {ToolName} run {path}");

        return Ok;
    }

    /// <summary>
    /// <para>A folder holding a project and the program it builds.</para>
    /// <para>A folder rather than a project file beside whatever else is there, because a project
    /// names what it builds by path — so one written into an occupied folder either lists nothing
    /// or lists files somebody else's project already lists.</para>
    /// </summary>
    private static int NewProject(string path, string name, string program)
    {
        if (Directory.Exists(path) || File.Exists(path))
        {
            Console.Error.WriteLine($"{ToolName}: {path} is already there.");
            return Misused;
        }

        string project = Path.Combine(path, name + SourceDiscovery.ProjectExtension);
        string main = Path.Combine(path, "Program" + SourceDiscovery.SourceExtension);

        Directory.CreateDirectory(path);

        File.WriteAllText(
            project,
            $"""
             project {name}
                 source Program{SourceDiscovery.SourceExtension}
             end project

             """);

        File.WriteAllText(main, program);

        Console.WriteLine($"Wrote {project}");
        Console.WriteLine($"Wrote {main}");
        Console.WriteLine($"Run it with: {ToolName} run {project}");

        return Ok;
    }

    private static int RunTokens(string[] args)
    {
        if (SourceArgument(args, "tokens") is not { } path)
        {
            return Misused;
        }

        SourceText source = SourceText.FromFile(path);
        DiagnosticBag diagnostics = new();
        List<Token> tokens = new Lexer(source, diagnostics).Scan();

        Console.Out.Write(TokenPrinter.Print(tokens));
        DiagnosticRenderer.WriteAll(diagnostics);

        return diagnostics.HasErrors ? 1 : 0;
    }

    /// <summary>
    /// Parses a file and prints its syntax tree. Like scanning, parsing recovers, so a file
    /// with a mistake in it still produces a tree worth looking at.
    /// </summary>
    private static int RunAst(string[] args)
    {
        if (SourceArgument(args, "ast") is not { } path)
        {
            return Misused;
        }

        DiagnosticBag diagnostics = new();
        CompilationUnit unit = SourceDiscovery.ParseOne(path, diagnostics);

        Console.Out.Write(AstPrinter.Print(unit, args.Contains("--positions")));
        DiagnosticRenderer.WriteAll(diagnostics);

        return diagnostics.HasErrors ? 1 : 0;
    }

    /// <summary>
    /// Parses and resolves a file, reporting everything found. This is as far as the compiler
    /// goes today; type checking follows.
    /// </summary>
    private static int RunCheck(string[] args)
    {
        if (Target(args, "check") is not { } target)
        {
            return Misused;
        }

        DiagnosticBag diagnostics = new();

        if (Compile(target.Path, diagnostics, requireEntryPoint: false) is not var (compilation, model))
        {
            DiagnosticRenderer.WriteAll(diagnostics);
            return Reported;
        }

        DiagnosticRenderer.WriteAll(diagnostics);

        if (diagnostics.HasErrors)
        {
            return Reported;
        }

        string entry = model.EntryPoint is null ? "none" : "Program.Main";
        string files = Wording.Count(compilation.Units.Count, "file");
        string types = Wording.Count(model.AllTypes().Count(), "type");

        Console.WriteLine($"{compilation.Label}: ok, {files}, {types}, entry point {entry}.");

        return Ok;
    }

    /// <summary>
    /// <para>Gathers the files a path names and takes them through the whole front end.</para>
    /// <para>Returns null only when the files could not be gathered at all, which is a broken
    /// project file. Everything else is reported into the bag and left for the caller to
    /// decide about, since some commands have something worth printing even so.</para>
    /// </summary>
    internal static (SourceDiscovery.Compilation Compilation, SemanticModel Model)? Compile(
        string path,
        DiagnosticBag diagnostics,
        bool requireEntryPoint)
    {
        if (SourceDiscovery.Gather(path, diagnostics) is not { } compilation)
        {
            return null;
        }

        SemanticModel model = FrontEnd.Check(
            compilation.Units,
            diagnostics,
            requireEntryPoint,
            compilation.Projects,
            compilation.EntryPoint);

        return (compilation, model);
    }

    /// <summary>
    /// Checks a file and prints the simplified tree that the interpreter and the emitter
    /// actually work from, with conversions made explicit and iteration rewritten.
    /// </summary>
    private static int RunLower(string[] args)
    {
        if (Target(args, "lower") is not { } target)
        {
            return Misused;
        }

        DiagnosticBag diagnostics = new();

        if (Compile(target.Path, diagnostics, requireEntryPoint: false) is not var (compilation, model)
            || diagnostics.HasErrors)
        {
            DiagnosticRenderer.WriteAll(diagnostics);
            return Reported;
        }

        // One tree per file, each headed by the file it came from, since a compilation of
        // several would otherwise print as one undivided wall.
        IReadOnlyList<CompilationUnit> lowered = Lowering.Lower(compilation.Units, model);

        for (int index = 0; index < lowered.Count; index++)
        {
            if (lowered.Count > 1)
            {
                Console.WriteLine($"comment {lowered[index].Source.FileName}");
            }

            Console.Out.Write(AstPrinter.Print(lowered[index]));
        }

        return Ok;
    }

    /// <summary>
    /// <para>Checks a program and compiles it to a .NET assembly.</para>
    /// <para>Every program that checks is one that builds: the back end declines nothing, so the
    /// only thing that stops a build is a diagnostic from the front end.</para>
    /// <para>What is emitted is the closure-converted tree rather than the lowered one. The
    /// emitter is the reason that pass exists: it receives a tree with no captures left in it
    /// and never reasons about them.</para>
    /// <para>A compilation that declares no <c>Program</c> builds a library rather than being
    /// refused. That is what a project written to be referenced is, and there was no way to
    /// build one while every build demanded somewhere to begin. What was made is said either
    /// way, so a <c>Main</c> whose name went astray reads as a library rather than as
    /// silence.</para>
    /// </summary>
    private static int RunBuild(string[] args)
    {
        if (Target(args, "build") is not { } target)
        {
            return Misused;
        }

        // Read before anything is compiled. A line that could not be read is not a line to act
        // on, and finding that out after the work is done reports the program's mistakes to
        // somebody whose actual mistake was the option they misspelled.
        if (BuildOptions(args, target) is not { } options)
        {
            return Misused;
        }

        DiagnosticBag diagnostics = new();

        if (Compile(target.Path, diagnostics, requireEntryPoint: false) is not var (compilation, model))
        {
            DiagnosticRenderer.WriteAll(diagnostics);
            return Reported;
        }

        if (diagnostics.HasErrors)
        {
            DiagnosticRenderer.WriteAll(diagnostics);
            return Reported;
        }

        string name = Path.GetFileNameWithoutExtension(compilation.Label);
        string output = Path.Combine(
            WhereToWrite(options, compilation, target), name + ".dll");

        IReadOnlyList<CompilationUnit> emitting = ClosureConversion.Convert(
            Lowering.Lower(compilation.Units, model), model);

        CilEmitter.Emit(emitting, model, name, output);

        DiagnosticRenderer.WriteAll(diagnostics);

        if (model.EntryPoint is null)
        {
            Console.WriteLine($"{compilation.Label}: wrote {output}, a library.");

            // Said plainly, because the difference between the two is one declaration and the
            // reader who wanted a program is owed the reason they did not get one.
            Console.WriteLine(
                "Nothing here declares a 'shared model Program', so there is nowhere to begin "
                + "and nothing to run. Another build reaches these types with 'reference'.");

            return Ok;
        }

        Console.WriteLine($"{compilation.Label}: wrote {output}");

        if (AppHost.Create(output, options.Runtime, out string why) is not { } launcher)
        {
            // Said rather than failed. The assembly is built and runs; what is missing is only
            // the convenience of starting it without naming 'dotnet'.
            Console.Error.WriteLine($"{ToolName}: no launcher was made — {why}");

            // Written as a path from where the reader is standing, so the line can be pasted.
            Console.WriteLine($"Run it with: dotnet {Path.GetRelativePath(".", output)}");

            return Ok;
        }

        Console.WriteLine($"Run it with: {Path.GetRelativePath(".", launcher)}");

        if (!AppHost.IsWindows(options.Runtime) && OperatingSystem.IsWindows())
        {
            // A Windows file system has nowhere to record that a file may be run, so the bit
            // has to be set wherever it lands. Better said here than found there.
            Console.WriteLine(
                $"On {options.Runtime}, mark it runnable first: chmod +x {Path.GetFileName(launcher)}");
        }

        return Ok;
    }

    /// <summary>
    /// What a build was asked for on the command line. <c>Folder</c> is null where nothing was
    /// written there, which leaves the choice to the project, and the project's default to
    /// <see cref="WhereToWrite"/>.
    /// </summary>
    private readonly record struct Build(string? Folder, string Runtime);

    /// <summary>The folder a build writes into, when neither the command line nor a project says.</summary>
    private const string DefaultOutputFolder = "bin";

    /// <summary>
    /// <para>Where the assembly is written, from the three places that can decide it.</para>
    /// <para><c>--out</c> first, because somebody typed it just now for this one build.
    /// A project's <c>output</c> next, because a project is a build describing itself and holds
    /// for every run of it. Otherwise a <c>bin</c> beside whatever was named.</para>
    /// <para>That default is why <c>output</c> exists. A loose source file has nowhere to record
    /// a choice, so a folder holding several programs fills one <c>bin</c> with all of them —
    /// which is fine until it is not, and the way out is to write the project file that has
    /// somewhere to put the answer.</para>
    /// </summary>
    private static string WhereToWrite(
        Build options, SourceDiscovery.Compilation compilation, SourceDiscovery.FileTarget target)
    {
        if (options.Folder is { } written)
        {
            return written;
        }

        if (compilation.Output is { } declared)
        {
            return declared;
        }

        // Relative to what was named rather than to where the reader is standing, so that
        // building the same file from two directories puts the result in one place.
        return Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(target.Path)) ?? ".", DefaultOutputFolder);
    }

    /// <summary>
    /// <para>Reads what a build was asked for: where to put it, and what to build it for.</para>
    /// <para><c>bin</c> is a folder of its own rather than beside the source, because a build
    /// writes four files — the assembly, its runtime configuration, the runtime, and a launcher
    /// — and dropping those next to the program mixes what somebody wrote with what a tool
    /// made. The name because every .gitignore already knows it.</para>
    /// <para>Null where the arguments could not be read, having already said why.</para>
    /// </summary>
    private static Build? BuildOptions(string[] args, SourceDiscovery.FileTarget target)
    {
        string? folder = null;
        string? runtime = null;

        // From the third argument on: the first two are the command and the file it names.
        for (int i = 2; i < args.Length; i++)
        {
            string flag = args[i];

            if (flag is not ("--out" or "--runtime"))
            {
                Console.Error.WriteLine($"{ToolName}: 'build' does not take '{flag}'.");
                return null;
            }

            if (i + 1 >= args.Length)
            {
                Console.Error.WriteLine(
                    $"{ToolName}: '{flag}' needs "
                    + (flag == "--out" ? "a folder to write into." : "a platform to build for."));

                return null;
            }

            string value = args[++i];

            if (flag == "--out")
            {
                folder = value;
            }
            else
            {
                runtime = value;
            }
        }

        if (runtime is not null && !AppHost.CanTarget(runtime))
        {
            Console.Error.WriteLine(
                $"{ToolName}: nothing here can build for '{runtime}'. "
                + $"Available: {string.Join(", ", AppHost.Installed())}. "
                + $"'dotnet publish -r {runtime}' on any project fetches what is needed.");

            return null;
        }

        // A written folder is taken as the reader meant it — relative to where they are, which
        // is what every other tool does with a path typed on a command line.
        return new Build(
            folder is null ? null : Path.GetFullPath(folder),
            runtime ?? AppHost.ThisPlatform);
    }


    /// <summary>
    /// <para>Checks a program and runs it.</para>
    /// <para>Nothing runs until everything checks, so a program that reaches execution has
    /// already been proved free of the mistakes the front end can see.</para>
    /// </summary>
    private static int RunProgram(string[] args)
    {
        if (Target(args, "run") is not { } target)
        {
            return Misused;
        }

        DiagnosticBag diagnostics = new();

        if (Compile(target.Path, diagnostics, requireEntryPoint: true) is not var (compilation, model))
        {
            DiagnosticRenderer.WriteAll(diagnostics);
            return Reported;
        }

        DiagnosticRenderer.WriteAll(diagnostics);

        if (diagnostics.HasErrors)
        {
            return Reported;
        }

        try
        {
            // Qualified because the class shares its name with its namespace.
            return ProfiC.Interpreter.Interpreter.Run(
                Lowering.Lower(compilation.Units, model), model);
        }
        catch (Exception failure)
            when (DiagnosticRenderer.DescribeFailure(compilation.Label, failure) is { } description)
        {
            // Something the program did. A fault in the compiler is not described, and travels.
            Console.Error.WriteLine(description);
            return Reported;
        }
    }
}
