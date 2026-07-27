using System.Text;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Lexing;
using ProfiC.Compiler.Text;

namespace ProfiC.Tests.Lexing;

/// <summary>
/// <para>Pins the exact token stream produced for each sample against a checked-in file.</para>
/// <para>The recorded format is the one <c>profic tokens</c> prints, so a golden file is
/// simultaneously a regression test and a worked example of a shipped command. Keeping one
/// format rather than two is the whole reason this is hand-rolled instead of using a
/// snapshot library.</para>
/// <para>Set <c>PROFIC_UPDATE_GOLDEN=1</c> to rewrite the files after an intended change.</para>
/// </summary>
[TestFixture]
public sealed class GoldenTokenStreamTests : LexerTestBase
{
    private static bool UpdateRequested =>
        Environment.GetEnvironmentVariable("PROFIC_UPDATE_GOLDEN") == "1";

    private static string GoldenDirectory =>
        Path.Combine(RepositoryRoot, "tests", "ProfiC.Tests", "TestData", "Lexing", "Golden");

    [TestCaseSource(nameof(SampleNames))]
    public void Sample_MatchesItsRecordedTokenStream(string name)
    {
        SourceText source = LoadSample(name);
        DiagnosticBag diagnostics = new();
        List<Token> tokens = new Lexer(source, diagnostics).Scan();

        StringBuilder builder = new();

        foreach (Token token in tokens)
        {
            builder.Append($"{token.Line,4}:{token.Column,-4} {token.Type,-20} '{token.Lexeme}'")
                   .Append('\n');
        }

        string actual = builder.ToString();
        string goldenPath = Path.Combine(GoldenDirectory, Path.ChangeExtension(name, ".tokens"));

        if (UpdateRequested || !File.Exists(goldenPath))
        {
            Directory.CreateDirectory(GoldenDirectory);
            File.WriteAllText(goldenPath, actual);

            if (!UpdateRequested)
            {
                Assert.Fail(
                    $"No golden file for {name}; one was written to {goldenPath}. "
                    + "Review it and re-run.");
            }

            return;
        }

        string expected = File.ReadAllText(goldenPath).Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.That(actual, Is.EqualTo(expected),
                    $"token stream for {name} changed; re-run with PROFIC_UPDATE_GOLDEN=1 if intended");
    }
}
