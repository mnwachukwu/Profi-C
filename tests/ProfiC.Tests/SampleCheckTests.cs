using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Documentation;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Semantics;
using ProfiC.Compiler.Text;

namespace ProfiC.Tests;

/// <summary>
/// <para>Every sample must pass the whole front end, not merely scan and parse.</para>
/// <para>The recorded token streams and syntax trees cover every sample, and
/// <see cref="Interpreting.SampleProgramTests"/> runs the ones that declare an entry point.
/// Neither resolves or type-checks a sample that cannot be run, which is what this covers.
/// </para>
/// </summary>
[TestFixture]
public sealed class SampleCheckTests : LexerTestBase
{
    [TestCaseSource(nameof(SampleNames))]
    public void Sample_PassesTheWholeFrontEnd(string name)
    {
        SourceText source = LoadSample(name);
        DiagnosticBag diagnostics = new();

        CompilationUnit unit = Parser.Parse(source, diagnostics);
        SemanticModel model = Resolver.Resolve(unit, diagnostics);
        TypeChecker.Check(unit, model, diagnostics);
        DefiniteAssignment.Analyze(unit, model, diagnostics);
        DocumentationChecker.Check(unit, diagnostics);
        diagnostics.ReportUnusedSuppressions();

        Assert.That(
            diagnostics.Sorted().Select(d => $"{d.Id} at {d.Span.Start.Line}: {d.Message}"),
            Is.Empty,
            $"{name} does not check cleanly");
    }

    /// <summary>
    /// Lowering must also survive every sample. It runs after the front end on anything that
    /// executes, and a sample with no entry point would otherwise never reach it.
    /// </summary>
    [TestCaseSource(nameof(SampleNames))]
    public void Sample_Lowers(string name)
    {
        SourceText source = LoadSample(name);
        DiagnosticBag diagnostics = new();

        CompilationUnit unit = Parser.Parse(source, diagnostics);
        SemanticModel model = Resolver.Resolve(unit, diagnostics);
        TypeChecker.Check(unit, model, diagnostics);
        DefiniteAssignment.Analyze(unit, model, diagnostics);

        Assert.That(() => Lowering.Lower(unit, model), Throws.Nothing);
    }
}
