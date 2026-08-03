using System.Diagnostics;
using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Emit;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Semantics;
using ProfiC.Compiler.Text;

namespace ProfiC.Tests.Emitting;

/// <summary>
/// <para>Every sample that is a whole program, run both ways, and the two answers compared.</para>
/// <para><b>This is the claim the rest of the suite cannot make.</b> Elsewhere the two engines are
/// tested apart, and the emitter is tested on programs somebody sat down and wrote — so a
/// construct nobody thought to write is a construct the two are free to disagree about. That is
/// not hypothetical: <c>==</c> compared references in emitted code and held deeply in the
/// interpreter, for weeks, while a suite of some 2,700 tests passed. Every emitter test asserted
/// agreement, and not one of them compared two models.</para>
/// <para>What closes that gap is not more tests of the same kind but a different question: take
/// the programs that already exist to teach the language, and require the two engines to agree on
/// all of them. The corpus grows for its own reasons, and every sample added to it becomes another
/// thing the two cannot quietly differ about.</para>
/// <para>Deliberately not a recorded file. The interpreter is the oracle and the comparison is
/// between the engines; a golden would let both drift together and still pass.</para>
/// </summary>
[TestFixture]
public sealed class CorpusAgreementTests : LexerTestBase
{
    private static IEnumerable<string> Programs =>
        ProfiC.Tests.Interpreting.SampleProgramTests.RunnableSampleNames;

    /// <summary>Everything the front end produces for one sample, ready for either engine.</summary>
    private static (IReadOnlyList<CompilationUnit> Units, SemanticModel Model) FrontEnd(string name)
    {
        SourceText source = LoadSample(name);
        DiagnosticBag diagnostics = new();

        CompilationUnit unit = Parser.Parse(source, diagnostics);
        SemanticModel model = Resolver.Resolve(unit, diagnostics, requireEntryPoint: true);
        TypeChecker.Check(unit, model, diagnostics);
        DefiniteAssignment.Analyze(unit, model, diagnostics);

        Assert.That(
            diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error),
            Is.Empty,
            $"{name} should compile before either engine is asked to run it");

        return (ClosureConversion.Convert(Lowering.Lower([unit], model), model), model);
    }

    /// <summary>
    /// <para>One sample, run both ways.</para>
    /// <para>Every sample is a real comparison: the emitter declines nothing, so a sample that
    /// will not build is a failure rather than something to skip past.</para>
    /// </summary>
    [TestCaseSource(nameof(Programs))]
    public void Sample_MeansTheSameEmittedAsInterpreted(string name)
    {
        (IReadOnlyList<CompilationUnit> units, SemanticModel model) = FrontEnd(name);

        string folder = Path.Combine(Path.GetTempPath(), $"profi-c-corpus-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);

        try
        {
            string assembly = Path.Combine(folder, "Emitted.dll");

            CilEmitter.Emit(units, model, "Emitted", assembly);

            StringWriter interpreted = new();
            ProfiC.Interpreter.Interpreter.Run(units, model, interpreted, TextReader.Null);

            Assert.That(
                Run(assembly),
                Is.EqualTo(interpreted.ToString().ReplaceLineEndings("\n")),
                $"{name} means one thing interpreted and another emitted");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    /// Run in a process of its own rather than loaded here, so that the file is not left locked
    /// and a program that fails to start reads as exactly that.
    /// </summary>
    private static string Run(string assembly)
    {
        ProcessStartInfo start = new()
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
        };

        start.ArgumentList.Add(assembly);

        using Process running = Process.Start(start)!;

        // Nothing to read, matching what the interpreter is given, so a sample that asks a
        // question is answered the same way on both sides.
        running.StandardInput.Close();

        string output = running.StandardOutput.ReadToEnd();
        string failed = running.StandardError.ReadToEnd();

        running.WaitForExit();

        Assert.That(running.ExitCode, Is.Zero, failed);

        return output.ReplaceLineEndings("\n");
    }
}
