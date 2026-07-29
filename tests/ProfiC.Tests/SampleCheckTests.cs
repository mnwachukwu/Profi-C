using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Semantics;
using ProfiC.Compiler.Text;

namespace ProfiC.Tests;

/// <summary>
/// <para>Every sample must pass the whole front end, not merely scan and parse.</para>
/// <para>This gap was real: the recorded token streams and syntax trees cover all fifteen
/// samples, but nothing type-checked the ones that declare no entry point, because running
/// them is what the other fixture does and they cannot be run. A mistake in
/// <c>operators.pfc</c> therefore sat unreported until a new diagnostic happened to find it —
/// it bound a name to the result of a function that yields nothing.</para>
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
