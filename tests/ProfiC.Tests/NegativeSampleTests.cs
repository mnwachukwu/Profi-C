using ProfiC.Cli;
using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Semantics;
using ProfiC.Compiler.Text;

namespace ProfiC.Tests;

/// <summary>
/// <para>Runs the samples that are meant to fail, and pins how they fail.</para>
/// <para>A compiler is only as good as its refusals. A program under <c>samples/negatives</c>
/// is wrong on purpose, and these tests hold two things: that it is still rejected, and that
/// the wording of the rejection has not drifted. Both matter — a mistake that stops being
/// caught is a hole, and one whose explanation decays is a hole for the reader.</para>
/// <para>Set <c>PROFIC_UPDATE_GOLDEN=1</c> to rewrite the recorded files after an intended
/// change.</para>
/// </summary>
[TestFixture]
public sealed class NegativeSampleTests : LexerTestBase
{
    private static bool UpdateRequested =>
        Environment.GetEnvironmentVariable("PROFIC_UPDATE_GOLDEN") == "1";

    private static string GoldenDirectory =>
        Path.Combine(RepositoryRoot, "tests", "ProfiC.Tests", "TestData", "Negatives");

    private static string NegativeDirectory(string kind) =>
        Path.Combine(RepositoryRoot, "samples", "negatives", kind);

    private static IEnumerable<string> NamesIn(string kind) =>
        Directory.EnumerateFiles(NegativeDirectory(kind), "*.pfc")
                 .Select(Path.GetFileName)
                 .OrderBy(name => name, StringComparer.Ordinal)!;

    public static IEnumerable<string> CompileFailureNames => NamesIn("compile");

    public static IEnumerable<string> RuntimeFailureNames => NamesIn("runtime");

    /// <summary>
    /// Loads a negative sample under its bare file name, so that the diagnostics it produces
    /// read the same on every machine.
    /// </summary>
    private static SourceText Load(string kind, string name) =>
        new(File.ReadAllText(Path.Combine(NegativeDirectory(kind), name)), name);

    // ---- Programs that must not compile -------------------------------------------------

    [TestCaseSource(nameof(CompileFailureNames))]
    public void CompileFailure_IsRejectedWithTheDiagnosticsItRecorded(string name)
    {
        SourceText source = Load("compile", name);
        DiagnosticBag diagnostics = new();

        CompilationUnit unit = Parser.Parse(source, diagnostics);
        SemanticModel model = Resolver.Resolve(unit, diagnostics);
        TypeChecker.Check(unit, model, diagnostics);
        DefiniteAssignment.Analyze(unit, model, diagnostics);

        Assert.That(diagnostics.HasErrors, Is.True, $"{name} was supposed to be rejected");

        string actual = string.Concat(
            diagnostics.Sorted().Select(d => DiagnosticRenderer.Format(source, d) + "\n"));

        AssertMatchesGolden(actual, Path.ChangeExtension(name, ".errors"), name);
    }

    // ---- Programs that compile and then fail --------------------------------------------

    [TestCaseSource(nameof(RuntimeFailureNames))]
    public void RuntimeFailure_CompilesCleanlyThenFailsAsItRecorded(string name)
    {
        SourceText source = Load("runtime", name);
        DiagnosticBag diagnostics = new();

        CompilationUnit unit = Parser.Parse(source, diagnostics);
        SemanticModel model = Resolver.Resolve(unit, diagnostics, requireEntryPoint: true);
        TypeChecker.Check(unit, model, diagnostics);
        DefiniteAssignment.Analyze(unit, model, diagnostics);

        // The point of a runtime negative is that nothing was knowable earlier. One that fails
        // to compile is testing the wrong thing and belongs under compile instead.
        Assert.That(
            diagnostics.Sorted().Select(d => DiagnosticRenderer.Format(source, d)),
            Is.Empty,
            $"{name} should compile cleanly and fail only when run");

        StringWriter output = new();

        Exception? failure = Assert.Catch(
            () => ProfiC.Interpreter.Interpreter.Run(Lowering.Lower(unit, model), model, output),
            $"{name} was supposed to fail when run");

        string? description = DiagnosticRenderer.DescribeFailure(source, failure!);

        Assert.That(
            description,
            Is.Not.Null,
            $"{name} failed with {failure!.GetType().Name}, which is a fault in the compiler "
            + "rather than something the program did");

        string actual = output.ToString().ReplaceLineEndings("\n") + description + "\n";

        AssertMatchesGolden(actual, Path.ChangeExtension(name, ".out"), name);
    }

    // ---- The recorded expectations ------------------------------------------------------

    private static void AssertMatchesGolden(string actual, string goldenName, string sampleName)
    {
        string goldenPath = Path.Combine(GoldenDirectory, goldenName);

        if (UpdateRequested || !File.Exists(goldenPath))
        {
            Directory.CreateDirectory(GoldenDirectory);
            File.WriteAllText(goldenPath, actual);

            if (!UpdateRequested)
            {
                Assert.Fail(
                    $"No recorded failure for {sampleName}; one was written to {goldenPath}. "
                    + "Review it and re-run.");
            }

            return;
        }

        string expected = File.ReadAllText(goldenPath)
                              .Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.That(
            actual,
            Is.EqualTo(expected),
            $"how {sampleName} fails changed; re-run with PROFIC_UPDATE_GOLDEN=1 if intended");
    }
}
