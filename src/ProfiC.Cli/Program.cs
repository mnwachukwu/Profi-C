using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Documentation;
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

        return args[0] switch
        {
            "--version" or "-v" => WriteVersion(),
            "tokens" => RunTokens(args),
            "ast" => RunAst(args),
            "check" => RunCheck(args),
            "lower" => RunLower(args),
            "run" => RunProgram(args),
            "vocabulary" => RunVocabulary(),
            "debug" => RunDebug(),
            _ => UnknownCommand(args[0]),
        };
    }

    private static void WriteUsage()
    {
        Console.WriteLine($"{ToolName} - the Profi-C compiler");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine($"  {ToolName} run <file>       Run a .pc program or a .pcp project");
        Console.WriteLine($"  {ToolName} check <file>     Check a .pc program or a .pcp project");
        Console.WriteLine($"  {ToolName} lower <file>     Print the simplified tree the back end sees");
        Console.WriteLine($"  {ToolName} tokens <file>    Scan one .pc file and print its token stream");
        Console.WriteLine($"  {ToolName} ast <file>       Parse one .pc file and print its syntax tree");
        Console.WriteLine($"  {ToolName} vocabulary      Print every word the language reserves, as JSON");
        Console.WriteLine($"  {ToolName} debug           Debug a program, spoken to by an editor");
        Console.WriteLine($"  {ToolName} --version        Print the compiler version");
        Console.WriteLine($"  {ToolName} --help           Print this message");
        Console.WriteLine();
        Console.WriteLine("Naming a .pc file compiles it together with the shared code beside");
        Console.WriteLine("it: every other .pc in the same folder that declares no Program.");
        Console.WriteLine("A .pcp project compiles exactly what it lists, across any folders.");
        Console.WriteLine();
        Console.WriteLine("The extension may be left off: 'run Program' finds Program.pc or");
        Console.WriteLine("Program.pcp. Write it when both are there and you mean one.");
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
