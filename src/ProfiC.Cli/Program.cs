using System.Globalization;
using System.Text.Json;
using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Documentation;
using ProfiC.Compiler.Emit;
using ProfiC.Compiler.Lexing;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Semantics;
using ProfiC.Compiler.Text;
using ProfiC.Interpreter;
using ProfiC.Runtime;

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

    private static int Main(string[] args) => Run(args);

    /// <summary>Runs the command. Public so that the <c>pc</c> alias can forward to it.</summary>
    public static int Run(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
        {
            WriteUsage();
            return 0;
        }

        try
        {
            return args[0] switch
            {
                "--version" or "-v" => WriteVersion(),
                "tokens" => RunTokens(args),
                "ast" => RunAst(args),
                "check" => RunCheck(args),
                "lower" => RunLower(args),
                "run" => RunProgram(args),
                "build" => RunBuild(args),
                "vocabulary" => RunVocabulary(),
                "platforms" => RunPlatforms(),
                "outline" => RunOutline(args),
                "project" => RunProject(args),
                "debug" => RunDebug(),
                _ => UnknownCommand(args[0]),
            };
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
        return 1;
    }

    private static void WriteUsage()
    {
        Console.WriteLine($"{ToolName} - the Profi-C compiler");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine($"  {ToolName} run <file>       Run a .pc program or a .pcp project");
        Console.WriteLine($"  {ToolName} build <file>     Compile one to a .NET assembly, into bin");
        Console.WriteLine($"  {ToolName} check <file>     Check a .pc program or a .pcp project");
        Console.WriteLine($"  {ToolName} lower <file>     Print the simplified tree the back end sees");
        Console.WriteLine($"  {ToolName} tokens <file>    Scan one .pc file and print its token stream");
        Console.WriteLine($"  {ToolName} ast <file>       Parse one .pc file and print its syntax tree");
        Console.WriteLine($"  {ToolName} outline <file>   Print what one .pc file declares, as JSON");
        Console.WriteLine($"  {ToolName} project <file>   Print the .pcp that builds a file, as JSON");
        Console.WriteLine($"  {ToolName} vocabulary       Print every word the language reserves, as JSON");
        Console.WriteLine($"  {ToolName} platforms        Print the platforms --runtime accepts, as JSON");
        Console.WriteLine($"  {ToolName} debug            Debug a program, spoken to by an editor");
        Console.WriteLine($"  {ToolName} --version        Print the compiler version");
        Console.WriteLine($"  {ToolName} --help           Print this message");
        Console.WriteLine();
        Console.WriteLine("Naming a .pc file compiles it together with the shared code beside");
        Console.WriteLine("it: every other .pc in the same folder that declares no Program.");
        Console.WriteLine("A .pcp project compiles exactly what it lists, across any folders.");
        Console.WriteLine();
        Console.WriteLine("The extension may be left off: 'run Program' finds Program.pc or");
        Console.WriteLine("Program.pcp. Write it when both are there and you mean one.");
        Console.WriteLine();
        Console.WriteLine("'build' writes into a 'bin' beside the program, with a launcher you");
        Console.WriteLine("can run without naming dotnet. Two options change what it makes:");
        Console.WriteLine($"  {ToolName} build hello.pc --out dist");
        Console.WriteLine($"  {ToolName} build hello.pc --runtime linux-x64");
        Console.WriteLine();
        Console.WriteLine("The machine it runs on still needs .NET installed.");
    }

    /// <summary>
    /// Finds the file a command was pointed at, reporting what is wrong with the argument if
    /// anything is. Null means the command cannot proceed and has already said why.
    /// </summary>
    private static SourceDiscovery.FileTarget? Target(string[] args, string command)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine($"{ToolName}: '{command}' requires a file path.");
            return null;
        }

        if (SourceDiscovery.Locate(args[1], out string problem) is not { } target)
        {
            Console.Error.WriteLine($"{ToolName}: {problem}");
            return null;
        }

        return target;
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
        return 0;
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine(
            $"{ToolName}: unknown command '{command}'. Try '{ToolName} --help'.");
        return 1;
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

        return 0;
    }

    private static int RunVocabulary()
    {
        Console.WriteLine(Vocabulary.AsJson());

        return 0;
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

        return 0;
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
            return 1;
        }

        SourceText source = SourceText.FromFile(path);
        DiagnosticBag aside = new();

        Console.WriteLine(Outline.AsJson(Parser.Parse(source, aside), source));

        return 0;
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
        if (args.Length < 2)
        {
            Console.Error.WriteLine($"{ToolName}: 'project' requires a file path.");
            return 1;
        }

        // Located rather than taken as written, so that 'project Program' answers about the same
        // file 'run Program' would compile.
        if (SourceDiscovery.Locate(args[1], out string problem) is not { } target)
        {
            Console.Error.WriteLine($"{ToolName}: {problem}");
            return 1;
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

        return 0;
    }

    private static int RunTokens(string[] args)
    {
        if (SourceArgument(args, "tokens") is not { } path)
        {
            return 1;
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
            return 1;
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
            return 1;
        }

        DiagnosticBag diagnostics = new();

        if (Compile(target.Path, diagnostics, requireEntryPoint: false) is not var (compilation, model))
        {
            DiagnosticRenderer.WriteAll(diagnostics);
            return 1;
        }

        DiagnosticRenderer.WriteAll(diagnostics);

        if (diagnostics.HasErrors)
        {
            return 1;
        }

        string entry = model.EntryPoint is null ? "none" : "Program.Main";
        string files = Wording.Count(compilation.Units.Count, "file");
        string types = Wording.Count(model.AllTypes().Count(), "type");

        Console.WriteLine($"{compilation.Label}: ok, {files}, {types}, entry point {entry}.");

        return 0;
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

        SemanticModel model = Resolver.Resolve(
            compilation.Units,
            diagnostics,
            requireEntryPoint,
            compilation.Projects,
            compilation.EntryPoint);
        TypeChecker.Check(compilation.Units, model, diagnostics);
        DefiniteAssignment.Analyze(compilation.Units, model, diagnostics);

        foreach (CompilationUnit unit in compilation.Units)
        {
            DocumentationChecker.Check(unit, diagnostics);
        }

        // Last, because whether an 'ignore' silenced anything is a question only the finished
        // compilation can answer.
        diagnostics.ReportUnusedSuppressions();

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
            return 1;
        }

        DiagnosticBag diagnostics = new();

        if (Compile(target.Path, diagnostics, requireEntryPoint: false) is not var (compilation, model)
            || diagnostics.HasErrors)
        {
            DiagnosticRenderer.WriteAll(diagnostics);
            return 1;
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

        return 0;
    }

    /// <summary>
    /// <para>Checks a program and compiles it to a .NET assembly.</para>
    /// <para>Every program that checks is one that builds: the back end declines nothing, so the
    /// only thing that stops a build is a diagnostic from the front end.</para>
    /// <para>What is emitted is the closure-converted tree rather than the lowered one. The
    /// emitter is the reason that pass exists: it receives a tree with no captures left in it
    /// and never reasons about them.</para>
    /// </summary>
    private static int RunBuild(string[] args)
    {
        if (Target(args, "build") is not { } target)
        {
            return 1;
        }

        DiagnosticBag diagnostics = new();

        if (Compile(target.Path, diagnostics, requireEntryPoint: true) is not var (compilation, model))
        {
            DiagnosticRenderer.WriteAll(diagnostics);
            return 1;
        }

        if (diagnostics.HasErrors)
        {
            DiagnosticRenderer.WriteAll(diagnostics);
            return 1;
        }

        if (BuildOptions(args, target) is not { } options)
        {
            return 1;
        }

        string name = Path.GetFileNameWithoutExtension(compilation.Label);
        string output = Path.Combine(options.Folder, name + ".dll");

        IReadOnlyList<CompilationUnit> emitting = ClosureConversion.Convert(
            Lowering.Lower(compilation.Units, model), model);

        CilEmitter.Emit(emitting, model, name, output);

        DiagnosticRenderer.WriteAll(diagnostics);

        Console.WriteLine($"{compilation.Label}: wrote {output}");

        if (AppHost.Create(output, options.Runtime, out string why) is not { } launcher)
        {
            // Said rather than failed. The assembly is built and runs; what is missing is only
            // the convenience of starting it without naming 'dotnet'.
            Console.Error.WriteLine($"{ToolName}: no launcher was made — {why}");

            // Written as a path from where the reader is standing, so the line can be pasted.
            Console.WriteLine($"Run it with: dotnet {Path.GetRelativePath(".", output)}");

            return 0;
        }

        Console.WriteLine($"Run it with: {Path.GetRelativePath(".", launcher)}");

        if (!AppHost.IsWindows(options.Runtime) && OperatingSystem.IsWindows())
        {
            // A Windows file system has nowhere to record that a file may be run, so the bit
            // has to be set wherever it lands. Better said here than found there.
            Console.WriteLine(
                $"On {options.Runtime}, mark it runnable first: chmod +x {Path.GetFileName(launcher)}");
        }

        return 0;
    }

    /// <summary>What a build was asked for beyond the program itself.</summary>
    private readonly record struct Build(string Folder, string Runtime);

    /// <summary>The folder a build writes into, when nothing says otherwise.</summary>
    private const string DefaultOutputFolder = "bin";

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
        string beside = Path.GetDirectoryName(Path.GetFullPath(target.Path)) ?? ".";
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
        // is what every other tool does with a path typed on a command line. The default is
        // relative to the program instead, so that building the same file from two directories
        // puts the result in one place.
        return new Build(
            folder is null ? Path.Combine(beside, DefaultOutputFolder) : Path.GetFullPath(folder),
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
            return 1;
        }

        DiagnosticBag diagnostics = new();

        if (Compile(target.Path, diagnostics, requireEntryPoint: true) is not var (compilation, model))
        {
            DiagnosticRenderer.WriteAll(diagnostics);
            return 1;
        }

        DiagnosticRenderer.WriteAll(diagnostics);

        if (diagnostics.HasErrors)
        {
            return 1;
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
            return 1;
        }
    }
}
