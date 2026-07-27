using ProfiC.Compiler.Lexing;

namespace ProfiC.Cli;

/// <summary>
/// <para>Entry point for the <c>profic</c> command.</para>
/// <para>
/// Diagnostic formatting belongs here rather than in the compiler, so that the front
/// end stays free of console output and can later back a language server.
/// </para>
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h")
        {
            Console.WriteLine("profic - the Profi-C compiler");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  profic tokens <file>    Scan a .pfc file and print its token stream");
            Console.WriteLine("  profic --version        Print the compiler version");
            return 0;
        }

        if (args[0] == "--version")
        {
            Console.WriteLine(ThisAssembly.Version);
            return 0;
        }

        if (args[0] == "tokens")
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("profic: 'tokens' requires a file path.");
                return 1;
            }

            return PrintTokens(args[1]);
        }

        Console.Error.WriteLine($"profic: unknown command '{args[0]}'. Try 'profic --help'.");
        return 1;
    }

    private static int PrintTokens(string path)
    {
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"profic: file not found: {path}");
            return 1;
        }

        string source = File.ReadAllText(path);
        List<Token> tokens = new Lexer(source).Scan();

        foreach (Token token in tokens)
        {
            Console.WriteLine(token);
        }

        return 0;
    }
}

/// <summary>Version information for the CLI.</summary>
internal static class ThisAssembly
{
    public const string Version = "0.1.0";
}
