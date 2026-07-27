using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Lexing;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Semantics;
using ProfiC.Compiler.Text;

namespace ProfiC.Cli;

/// <summary>
/// <para>Entry point for the <c>profic</c> command.</para>
/// <para>Diagnostic formatting belongs here rather than in the compiler, so that the front
/// end stays free of console output and can later back a language server.</para>
/// </summary>
internal static class Program
{
    private const string Version = "0.1.0";

    private static int Main(string[] args)
    {
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
            _ => UnknownCommand(args[0]),
        };
    }

    private static void WriteUsage()
    {
        Console.WriteLine("profic - the Profi-C compiler");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  profic tokens <file>    Scan a .pfc file and print its token stream");
        Console.WriteLine("  profic ast <file>       Parse a .pfc file and print its syntax tree");
        Console.WriteLine("  profic check <file>     Parse and resolve a .pfc file, reporting problems");
        Console.WriteLine("  profic --version        Print the compiler version");
        Console.WriteLine("  profic --help           Print this message");
    }

    private static int WriteVersion()
    {
        Console.WriteLine(Version);
        return 0;
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"profic: unknown command '{command}'. Try 'profic --help'.");
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
            Console.Error.WriteLine("profic: 'tokens' requires a file path.");
            return 1;
        }

        string path = args[1];

        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"profic: file not found: {path}");
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
            Console.Error.WriteLine("profic: 'ast' requires a file path.");
            return 1;
        }

        string path = args[1];

        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"profic: file not found: {path}");
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
            Console.Error.WriteLine("profic: 'check' requires a file path.");
            return 1;
        }

        string path = args[1];

        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"profic: file not found: {path}");
            return 1;
        }

        SourceText source = SourceText.FromFile(path);
        DiagnosticBag diagnostics = new();

        CompilationUnit unit = Parser.Parse(source, diagnostics);
        SemanticModel model = Resolver.Resolve(unit, diagnostics);

        DiagnosticRenderer.WriteAll(source, diagnostics);

        if (!diagnostics.HasErrors)
        {
            int types = model.AllTypes().Count();
            string entry = model.EntryPoint is null ? "none" : "Program.Main";
            Console.WriteLine($"{source.FileName}: ok, {types} types, entry point {entry}.");
        }

        return diagnostics.HasErrors ? 1 : 0;
    }
}
