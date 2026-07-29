using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Lexing;
using ProfiC.Compiler.Text;

namespace ProfiC.Tests;

/// <summary>
/// Shared helpers for scanning snippets and locating the repository's sample corpus.
/// </summary>
public abstract class LexerTestBase
{
    /// <summary>Scans a snippet, asserting that it produced no diagnostics.</summary>
    protected static List<Token> Scan(string source)
    {
        DiagnosticBag diagnostics = new();
        List<Token> tokens = new Lexer(source, diagnostics).Scan();

        Assert.That(
            diagnostics.Select(d => d.ToString()),
            Is.Empty,
            "expected the snippet to scan without diagnostics");

        return tokens;
    }

    /// <summary>Scans a snippet and returns both the tokens and whatever was reported.</summary>
    protected static (List<Token> Tokens, DiagnosticBag Diagnostics) ScanRaw(string source)
    {
        DiagnosticBag diagnostics = new();
        List<Token> tokens = new Lexer(source, diagnostics).Scan();
        return (tokens, diagnostics);
    }

    /// <summary>
    /// Scans a snippet and returns its tokens without the trailing end-of-file token, which
    /// most assertions about token content do not care about.
    /// </summary>
    protected static List<Token> ScanWithoutEof(string source)
    {
        List<Token> tokens = Scan(source);
        Assert.That(tokens[^1].Type, Is.EqualTo(TokenType.EndOfFile));
        return tokens[..^1];
    }

    /// <summary>Asserts a snippet yields exactly one token, and returns it.</summary>
    protected static Token ScanSingle(string source)
    {
        List<Token> tokens = ScanWithoutEof(source);
        Assert.That(tokens, Has.Count.EqualTo(1), $"expected one token from \"{source}\"");
        return tokens[0];
    }

    // ---- Locating repository files ------------------------------------------------------

    private static string? _repositoryRoot;

    /// <summary>
    /// Walks up from the test assembly until it finds the solution file, so that tests can
    /// read the sample corpus regardless of build configuration or working directory.
    /// </summary>
    protected static string RepositoryRoot
    {
        get
        {
            if (_repositoryRoot is not null)
            {
                return _repositoryRoot;
            }

            DirectoryInfo? directory = new(AppContext.BaseDirectory);

            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Profi-C.sln")))
            {
                directory = directory.Parent;
            }

            Assert.That(directory, Is.Not.Null, "could not locate the repository root");
            _repositoryRoot = directory!.FullName;
            return _repositoryRoot;
        }
    }

    /// <summary>The repository root, for fixtures that do not derive from this class.</summary>
    public static string RepositoryRootForTests => RepositoryRoot;

    /// <summary>
    /// <para>Every single-file sample: the programs in <c>samples</c>, and the corpus in
    /// <c>samples/reference</c> that exercises the syntax without being a program.</para>
    /// <para>The two are kept apart because a folder is compiled as a unit. <c>tour.pc</c>
    /// declares a model of nearly every shape, which would collide with the programs beside it
    /// if they shared a folder. The multi-file samples and the negatives have their own
    /// fixtures.</para>
    /// </summary>
    protected static IEnumerable<string> SampleFiles =>
        new[] { "samples", Path.Combine("samples", "reference") }
            .Select(folder => Path.Combine(RepositoryRoot, folder))
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*.pc"))
            .OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal);

    /// <summary>Names of the sample files, used to label test cases readably.</summary>
    public static IEnumerable<string> SampleNames =>
        SampleFiles.Select(Path.GetFileName)!;

    /// <summary>Loads a sample by file name, from whichever of the two folders holds it.</summary>
    protected static SourceText LoadSample(string name) =>
        SourceText.FromFile(
            SampleFiles.First(path => string.Equals(
                Path.GetFileName(path), name, StringComparison.Ordinal)));
}
