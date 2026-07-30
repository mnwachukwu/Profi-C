using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Semantics;
using ProfiC.Compiler.Text;

namespace ProfiC.Tests.Interpreting;

/// <summary>
/// <para>Runs every sample that is a whole program and pins what it printed.</para>
/// <para>A sample is documentation people are invited to trust, so the assertion is on the
/// answers rather than on merely finishing. A wrong answer is the failure worth catching: a
/// sample that crashes is noticed immediately, and one that quietly prints the wrong number
/// is not.</para>
/// <para>Set <c>PROFIC_UPDATE_GOLDEN=1</c> to rewrite the files after an intended change.</para>
/// </summary>
[TestFixture]
public sealed class SampleProgramTests : LexerTestBase
{
    private static bool UpdateRequested =>
        Environment.GetEnvironmentVariable("PROFIC_UPDATE_GOLDEN") == "1";

    private static string GoldenDirectory =>
        Path.Combine(RepositoryRoot, "tests", "ProfiC.Tests", "TestData", "Running");

    /// <summary>
    /// The samples that are whole programs. The rest exist to exercise the scanner and
    /// declare no entry point, so there is nothing in them to run.
    /// </summary>
    public static IEnumerable<string> RunnableSampleNames =>
        SampleFiles.Select(Path.GetFileName)
                   .Where(name => name is not null && HasEntryPoint(name))!;

    private static bool HasEntryPoint(string name)
    {
        DiagnosticBag diagnostics = new();
        SourceText source = LoadSample(name);
        CompilationUnit unit = Parser.Parse(source, diagnostics);

        return Resolver.Resolve(unit, diagnostics).EntryPoint is not null;
    }

    [TestCaseSource(nameof(RunnableSampleNames))]
    public void Sample_RunsAndPrintsWhatItRecorded(string name)
    {
        SourceText source = LoadSample(name);
        DiagnosticBag diagnostics = new();

        CompilationUnit unit = Parser.Parse(source, diagnostics);
        SemanticModel model = Resolver.Resolve(unit, diagnostics, requireEntryPoint: true);
        TypeChecker.Check(unit, model, diagnostics);
        DefiniteAssignment.Analyze(unit, model, diagnostics);

        Assert.That(
            diagnostics.Select(d => $"{d.Id}: {d.Message}"),
            Is.Empty,
            $"{name} should check cleanly");

        StringWriter output = new();
        // Nothing to read, so a sample that asks a question gets the same answer here as it
        // would from a pipe with nothing in it. Left to the test host's own input, what a
        // sample printed would depend on how the suite happened to be started.
        ProfiC.Interpreter.Interpreter.Run(
            Lowering.Lower(unit, model), model, output, TextReader.Null);

        string actual = output.ToString().ReplaceLineEndings("\n");
        string goldenPath = Path.Combine(GoldenDirectory, Path.ChangeExtension(name, ".out"));

        if (UpdateRequested || !File.Exists(goldenPath))
        {
            Directory.CreateDirectory(GoldenDirectory);
            File.WriteAllText(goldenPath, actual);

            if (!UpdateRequested)
            {
                Assert.Fail(
                    $"No recorded output for {name}; one was written to {goldenPath}. "
                    + "Review it and re-run.");
            }

            return;
        }

        string expected = File.ReadAllText(goldenPath).Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.That(actual, Is.EqualTo(expected),
                    $"output of {name} changed; re-run with PROFIC_UPDATE_GOLDEN=1 if intended");
    }
}
