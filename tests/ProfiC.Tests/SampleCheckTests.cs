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

    /// <summary>
    /// <para>Every conversion the language performs unasked happens somewhere in the corpus.</para>
    /// <para>These exist only after lowering, so nothing that reads a parse tree can see them —
    /// and a conversion no sample provokes is one the emitter is never asked to perform. That is
    /// not a hypothetical: a fraction in the exponent of <c>^</c> is the one place a fraction
    /// becomes a real on its own, no sample wrote one, and the emitter had no sequence for it.
    /// It type-checked, interpreted correctly, and died on the way to CIL.</para>
    /// <para>Held with no exclusions. Every member of the enumeration is one the type checker
    /// chooses, so any that goes unwritten here is either a gap in the corpus or a member
    /// nothing produces, and both are worth being told about.</para>
    /// </summary>
    [Test]
    public void Corpus_PerformsEveryConversionTheLanguageMakes()
    {
        HashSet<ConversionOperation> performed = [];

        foreach (string name in SampleNames)
        {
            SourceText source = LoadSample(name);
            DiagnosticBag diagnostics = new();

            CompilationUnit unit = Parser.Parse(source, diagnostics);
            SemanticModel model = Resolver.Resolve(unit, diagnostics);
            TypeChecker.Check(unit, model, diagnostics);
            DefiniteAssignment.Analyze(unit, model, diagnostics);

            performed.UnionWith(
                Lowering.Lower(unit, model)
                        .Descendants()
                        .OfType<ConversionExpr>()
                        .Select(conversion => conversion.Operation));
        }

        Assert.That(
            Enum.GetValues<ConversionOperation>().Where(one => !performed.Contains(one)),
            Is.Empty,
            "conversions the language performs that no sample provokes");
    }
}
