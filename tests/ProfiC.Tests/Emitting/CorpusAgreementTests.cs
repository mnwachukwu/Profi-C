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

    /// <summary>
    /// <para>The programs made of several files, entered the way a reader enters them.</para>
    /// <para>These were emitted and verified and never compared. Verification asks whether the
    /// CIL is well formed; it says nothing about whether it computes what the interpreter
    /// computes — and a program spread across units is where the emitter's symbol maps have to
    /// carry a type from one file into another, so it is the last place two engines should be
    /// left free to differ.</para>
    /// </summary>
    private static IEnumerable<string> MultiFilePrograms =>
        ProfiC.Tests.Interpreting.MultiFileSampleTests.EntryPoints;

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
    /// The same, for a program gathered from a path. Discovery does the gathering rather than
    /// this fixture, so what the two engines are handed is what a <c>pc build</c> of the same
    /// path would compile, folder rules and project file and all.
    /// </summary>
    private static (IReadOnlyList<CompilationUnit> Units, SemanticModel Model) FrontEndAt(
        string entry)
    {
        DiagnosticBag diagnostics = new();

        string path = Path.Combine(
            RepositoryRoot, "samples", entry.Replace('/', Path.DirectorySeparatorChar));

        ProfiC.Cli.SourceDiscovery.Compilation gathered =
            ProfiC.Cli.SourceDiscovery.Gather(path, diagnostics)!;

        Assert.That(gathered, Is.Not.Null, $"{entry} was not gathered");

        // Carried through as a build carries it. A project naming which of its programs begins
        // is compiled as though it had not when this is left out.
        SemanticModel model = Resolver.Resolve(
            gathered.Units,
            diagnostics,
            requireEntryPoint: true,
            projects: gathered.Projects,
            entryPoint: gathered.EntryPoint);

        TypeChecker.Check(gathered.Units, model, diagnostics);
        DefiniteAssignment.Analyze(gathered.Units, model, diagnostics);

        Assert.That(
            diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error),
            Is.Empty,
            $"{entry} should compile before either engine is asked to run it");

        return (ClosureConversion.Convert(Lowering.Lower(gathered.Units, model), model), model);
    }

    /// <summary>
    /// <para>One sample, run both ways.</para>
    /// <para>Every sample is a real comparison: the emitter declines nothing, so a sample that
    /// will not build is a failure rather than something to skip past.</para>
    /// </summary>
    [TestCaseSource(nameof(Programs))]
    public void Sample_MeansTheSameEmittedAsInterpreted(string name) =>
        AssertBothEnginesAgree(name, FrontEnd(name));

    /// <summary>The same question of a program that is more than one file.</summary>
    [TestCaseSource(nameof(MultiFilePrograms))]
    public void MultiFileSample_MeansTheSameEmittedAsInterpreted(string entry) =>
        AssertBothEnginesAgree(entry, FrontEndAt(entry));

    private static void AssertBothEnginesAgree(
        string name, (IReadOnlyList<CompilationUnit> Units, SemanticModel Model) compiled)
    {
        (IReadOnlyList<CompilationUnit> units, SemanticModel model) = compiled;

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
