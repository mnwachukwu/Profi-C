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
/// <para>Entry point for the <c>profi-c</c> command, and for the <c>pfc</c> alias that shares
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

    /// <summary>Runs the command. Public so that the <c>pfc</c> alias can forward to it.</summary>
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
        Console.WriteLine($"  {ToolName} run <file>       Run a .pfc program");
        Console.WriteLine($"  {ToolName} tokens <file>    Scan a .pfc file and print its token stream");
        Console.WriteLine($"  {ToolName} ast <file>       Parse a .pfc file and print its syntax tree");
        Console.WriteLine($"  {ToolName} check <file>     Check a .pfc file and report any problems");
        Console.WriteLine($"  {ToolName} lower <file>     Print the simplified tree the back end sees");
        Console.WriteLine($"  {ToolName} --version        Print the compiler version");
        Console.WriteLine($"  {ToolName} --help           Print this message");
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
        if (args.Length < 2)
        {
            Console.Error.WriteLine($"{ToolName}: 'tokens' requires a file path.");
            return 1;
        }

        string path = args[1];

        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"{ToolName}: file not found: {path}");
            return 1;
        }

        SourceText source = SourceText.FromFile(path);
        DiagnosticBag diagnostics = new();
        List<Token> tokens = new Lexer(source, diagnostics).Scan();

        Console.Out.Write(TokenPrinter.Print(tokens));

        if (diagnostics.Count > 0)
        {
            DiagnosticRenderer.WriteAll(source, diagnostics);
        }

        return diagnostics.HasErrors ? 1 : 0;
    }

    /// <summary>
    /// Parses a file and prints its syntax tree. Like scanning, parsing recovers, so a file
    /// with a mistake in it still produces a tree worth looking at.
    /// </summary>
    private static int RunAst(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine($"{ToolName}: 'ast' requires a file path.");
            return 1;
        }

        string path = args[1];

        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"{ToolName}: file not found: {path}");
            return 1;
        }

        bool withPositions = args.Contains("--positions");

        SourceText source = SourceText.FromFile(path);
        DiagnosticBag diagnostics = new();
        CompilationUnit unit = Parser.Parse(source, diagnostics);

        Console.Out.Write(AstPrinter.Print(unit, withPositions));

        if (diagnostics.Count > 0)
        {
            DiagnosticRenderer.WriteAll(source, diagnostics);
        }

        return diagnostics.HasErrors ? 1 : 0;
    }

    /// <summary>
    /// Parses and resolves a file, reporting everything found. This is as far as the compiler
    /// goes today; type checking follows.
    /// </summary>
    private static int RunCheck(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine($"{ToolName}: 'check' requires a file path.");
            return 1;
        }

        string path = args[1];

        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"{ToolName}: file not found: {path}");
            return 1;
        }

        SourceText source = SourceText.FromFile(path);
        DiagnosticBag diagnostics = new();

        CompilationUnit unit = Parser.Parse(source, diagnostics);
        SemanticModel model = Resolver.Resolve(unit, diagnostics);
        TypeChecker.Check(unit, model, diagnostics);
        DefiniteAssignment.Analyze(unit, model, diagnostics);

        DiagnosticRenderer.WriteAll(source, diagnostics);

        if (!diagnostics.HasErrors)
        {
            int types = model.AllTypes().Count();
            string entry = model.EntryPoint is null ? "none" : "Program.Main";
            Console.WriteLine($"{source.FileName}: ok, {types} types, entry point {entry}.");
        }

        return diagnostics.HasErrors ? 1 : 0;
    }

    /// <summary>
    /// Checks a file and prints the simplified tree that the interpreter and the emitter
    /// actually work from, with conversions made explicit and iteration rewritten.
    /// </summary>
    private static int RunLower(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine($"{ToolName}: 'lower' requires a file path.");
            return 1;
        }

        string path = args[1];

        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"{ToolName}: file not found: {path}");
            return 1;
        }

        SourceText source = SourceText.FromFile(path);
        DiagnosticBag diagnostics = new();

        CompilationUnit unit = Parser.Parse(source, diagnostics);
        SemanticModel model = Resolver.Resolve(unit, diagnostics);
        TypeChecker.Check(unit, model, diagnostics);
        DefiniteAssignment.Analyze(unit, model, diagnostics);

        if (diagnostics.HasErrors)
        {
            DiagnosticRenderer.WriteAll(source, diagnostics);
            return 1;
        }

        CompilationUnit lowered = Lowering.Lower(unit, model);
        Console.Out.Write(AstPrinter.Print(lowered));

        return 0;
    }

    /// <summary>
    /// <para>Checks a program and runs it.</para>
    /// <para>Nothing runs until everything checks, so a program that reaches execution has
    /// already been proved free of the mistakes the front end can see.</para>
    /// </summary>
    private static int RunProgram(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine($"{ToolName}: 'run' requires a file path.");
            return 1;
        }

        string path = args[1];

        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"{ToolName}: file not found: {path}");
            return 1;
        }

        SourceText source = SourceText.FromFile(path);
        DiagnosticBag diagnostics = new();

        CompilationUnit unit = Parser.Parse(source, diagnostics);
        SemanticModel model = Resolver.Resolve(unit, diagnostics, requireEntryPoint: true);
        TypeChecker.Check(unit, model, diagnostics);
        DefiniteAssignment.Analyze(unit, model, diagnostics);

        if (diagnostics.Count > 0)
        {
            DiagnosticRenderer.WriteAll(source, diagnostics);
        }

        if (diagnostics.HasErrors)
        {
            return 1;
        }

        CompilationUnit lowered = Lowering.Lower(unit, model);

        try
        {
            // Qualified because the class shares its name with its namespace.
            return ProfiC.Interpreter.Interpreter.Run(lowered, model);
        }
        catch (Exception failure)
            when (DiagnosticRenderer.DescribeFailure(source, failure) is { } description)
        {
            // Something the program did. A fault in the compiler is not described, and travels.
            Console.Error.WriteLine(description);
            return 1;
        }
    }
}
