using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
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
        Console.WriteLine($"  {ToolName} --version        Print the compiler version");
        Console.WriteLine($"  {ToolName} --help           Print this message");
        Console.WriteLine();
        Console.WriteLine("Naming a .pc file compiles it together with the shared code beside");
        Console.WriteLine("it: every other .pc in the same folder that declares no Program.");
        Console.WriteLine("A .pcp project compiles exactly what it lists, across any folders.");
    }

    /// <summary>
    /// Checks the argument list and reports what is missing. Returns the path, or null when the
    /// command cannot proceed.
    /// </summary>
    private static string? PathArgument(string[] args, string command)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine($"{ToolName}: '{command}' requires a file path.");
            return null;
        }

        if (!File.Exists(args[1]))
        {
            Console.Error.WriteLine($"{ToolName}: file not found: {args[1]}");
            return null;
        }

        return args[1];
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
    private static int RunTokens(string[] args)
    {
        if (PathArgument(args, "tokens") is not { } path)
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
        if (PathArgument(args, "ast") is not { } path)
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
        if (PathArgument(args, "check") is not { } path)
        {
            return 1;
        }

        DiagnosticBag diagnostics = new();

        if (Compile(path, diagnostics, requireEntryPoint: false) is not var (compilation, model))
        {
            DiagnosticRenderer.WriteAll(diagnostics);
            return 1;
        }

        DiagnosticRenderer.WriteAll(diagnostics);

        if (diagnostics.HasErrors)
        {
            return 1;
        }

        int types = model.AllTypes().Count();
        string entry = model.EntryPoint is null ? "none" : "Program.Main";
        string files = compilation.Units.Count == 1 ? "1 file" : $"{compilation.Units.Count} files";

        Console.WriteLine(
            $"{compilation.Label}: ok, {files}, {types} types, entry point {entry}.");

        return 0;
    }

    /// <summary>
    /// <para>Gathers the files a path names and takes them through the whole front end.</para>
    /// <para>Returns null only when the files could not be gathered at all, which is a broken
    /// project file. Everything else is reported into the bag and left for the caller to
    /// decide about, since some commands have something worth printing even so.</para>
    /// </summary>
    private static (SourceDiscovery.Compilation Compilation, SemanticModel Model)? Compile(
        string path,
        DiagnosticBag diagnostics,
        bool requireEntryPoint)
    {
        if (SourceDiscovery.Gather(path, diagnostics) is not { } compilation)
        {
            return null;
        }

        SemanticModel model = Resolver.Resolve(compilation.Units, diagnostics, requireEntryPoint);
        TypeChecker.Check(compilation.Units, model, diagnostics);
        DefiniteAssignment.Analyze(compilation.Units, model, diagnostics);

        return (compilation, model);
    }

    /// <summary>
    /// Checks a file and prints the simplified tree that the interpreter and the emitter
    /// actually work from, with conversions made explicit and iteration rewritten.
    /// </summary>
    private static int RunLower(string[] args)
    {
        if (PathArgument(args, "lower") is not { } path)
        {
            return 1;
        }

        DiagnosticBag diagnostics = new();

        if (Compile(path, diagnostics, requireEntryPoint: false) is not var (compilation, model)
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
        if (PathArgument(args, "run") is not { } path)
        {
            return 1;
        }

        DiagnosticBag diagnostics = new();

        if (Compile(path, diagnostics, requireEntryPoint: true) is not var (compilation, model))
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
