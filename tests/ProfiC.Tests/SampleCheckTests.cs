using ProfiC.Compiler;
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
        FrontEnd.Check(unit, diagnostics);

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

    /// <summary>
    /// <para>No sample counts to one less than a bound, because that is what <c>until</c> is.</para>
    /// <para><c>loop for i = 0 to grid.Count - 1</c> and <c>loop for i = 0 until grid.Count</c>
    /// walk the same elements, and only the second says so. The samples are where somebody
    /// learns which word to reach for, and a dozen of them subtracting one taught the habit the
    /// language added <c>until</c> to remove — while the specification, a page away, was calling
    /// that subtraction the thing C# leaves you to remember.</para>
    /// <para>Counting down is not in question. A loop written <c>stepby -1</c> ends where it
    /// ends, and the <c>- 1</c> in <c>loop for i = values.Count - 1 to 0 stepby -1</c> is where
    /// it starts rather than where it stops.</para>
    /// <para>The negatives are left out: what they hold is mistakes, and one of them is this
    /// exact off-by-one written the other way, walking one past the end on purpose.</para>
    /// </summary>
    [Test]
    public void NoSampleCountsToOneLessThanABound()
    {
        List<string> subtracting = [];

        foreach (string path in EverySampleFile)
        {
            string[] lines = File.ReadAllLines(path);

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];

                if (!line.Contains("loop for ", StringComparison.Ordinal)
                    || line.Contains("stepby -", StringComparison.Ordinal))
                {
                    continue;
                }

                // The bound is what follows " to ", and a step written after it ends the bound.
                int to = line.IndexOf(" to ", StringComparison.Ordinal);
                if (to < 0)
                {
                    continue;
                }

                string bound = line[(to + 4)..];
                int step = bound.IndexOf(" stepby ", StringComparison.Ordinal);
                bound = (step < 0 ? bound : bound[..step]).Trim();

                if (bound.EndsWith("- 1", StringComparison.Ordinal))
                {
                    subtracting.Add($"{Path.GetFileName(path)} line {i + 1}: {line.Trim()}");
                }
            }
        }

        Assert.That(
            subtracting,
            Is.Empty,
            "samples counting 'to' one less than a bound, where 'until' says it directly");
    }
}
