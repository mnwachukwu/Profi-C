using ProfiC.Cli;
using ProfiC.Compiler;
using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Documentation;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Semantics;

namespace ProfiC.Tests.Interpreting;

/// <summary>
/// <para>Runs the samples that are more than one file, and pins what they printed.</para>
/// <para>Each lives in its own folder under <c>samples</c>, and is entered the way a reader
/// would enter it: a project file if the folder has one, otherwise the <c>Program.pc</c> that
/// the folder rule gathers its neighbors around.</para>
/// <para>Set <c>PROFIC_UPDATE_GOLDEN=1</c> to rewrite the files after an intended change.</para>
/// </summary>
[TestFixture]
public sealed class MultiFileSampleTests : LexerTestBase
{
    private static bool UpdateRequested =>
        Environment.GetEnvironmentVariable("PROFIC_UPDATE_GOLDEN") == "1";

    private static string GoldenDirectory =>
        Path.Combine(RepositoryRoot, "tests", "ProfiC.Tests", "TestData", "Running");

    /// <summary>
    /// Every sample folder, by the path a reader would name. The negatives have their own
    /// fixture, since what is worth recording about them is how they fail.
    /// </summary>
    public static IEnumerable<string> EntryPoints
    {
        get
        {
            string samples = Path.Combine(RepositoryRoot, "samples");

            foreach (string folder in Directory.EnumerateDirectories(samples)
                                               .OrderBy(f => f, StringComparer.Ordinal))
            {
                if (Path.GetFileName(folder) == "negatives")
                {
                    continue;
                }

                string? project = Directory
                    .EnumerateFiles(folder, "*" + SourceDiscovery.ProjectExtension)
                    .OrderBy(f => f, StringComparer.Ordinal)
                    .FirstOrDefault();

                if (project is not null)
                {
                    yield return Path.GetFileName(folder) + "/" + Path.GetFileName(project);
                    continue;
                }

                if (File.Exists(Path.Combine(folder, "Program.pc")))
                {
                    yield return Path.GetFileName(folder) + "/Program.pc";
                }
            }
        }
    }

    /// <summary>
    /// <para>The claim <c>observatory.pcp</c> makes about itself: the line decides, and nothing
    /// else does.</para>
    /// <para>Its two programs are compiled from the same three files either way, so what
    /// separates them is one word in a project file. The recorded output above pins whichever
    /// the project names today; this pins that the other one is a whole program rather than
    /// something that merely parses, and that choosing it takes no edit to any <c>.pc</c>.</para>
    /// <para>Both are asked of the same gathered sources, which is what makes it the same
    /// build — a test that re-read the folder for each could differ for some other reason and
    /// still pass.</para>
    /// </summary>
    [TestCase("Observatory.Cataloging.Program", "== the catalog ==")]
    [TestCase("Observatory.Ranking.Program", "== the brightest ==")]
    public void TheEntryLineDecidesWhichProgramRuns(string program, string expected)
    {
        DiagnosticBag diagnostics = new();

        SourceDiscovery.Compilation gathered = SourceDiscovery.Gather(
            Path.Combine(RepositoryRoot, "samples", "observatory", "observatory.pcp"),
            diagnostics)!;

        SemanticModel model = FrontEnd.Check(
            gathered.Units, diagnostics, requireEntryPoint: true, gathered.Projects, program);

        Assert.That(
            diagnostics.Sorted().Select(DiagnosticRenderer.Format),
            Is.Empty,
            $"starting at {program} should check cleanly");

        StringWriter output = new();
        ProfiC.Interpreter.Interpreter.Run(
            Lowering.Lower(gathered.Units, model), model, output, TextReader.Null);

        Assert.That(output.ToString(), Does.StartWith(expected));
    }

    [TestCaseSource(nameof(EntryPoints))]
    public void MultiFileSample_RunsAndPrintsWhatItRecorded(string entry)
    {
        string path = Path.Combine(RepositoryRoot, "samples", entry.Replace('/', Path.DirectorySeparatorChar));

        DiagnosticBag diagnostics = new();
        SourceDiscovery.Compilation? compilation = SourceDiscovery.Gather(path, diagnostics);

        Assert.That(compilation, Is.Not.Null, $"{entry} could not be gathered");

        // The project's own entry point is carried through, as a build carries it. Left out, a
        // project naming which of its programs begins is compiled as though it had not, and
        // reports PC0234 about a question it answered.
        SemanticModel model = FrontEnd.Check(
            compilation!.Units,
            diagnostics,
            requireEntryPoint: true,
            compilation.Projects,
            compilation.EntryPoint);

        Assert.That(
            diagnostics.Sorted().Select(DiagnosticRenderer.Format),
            Is.Empty,
            $"{entry} should check cleanly");

        Assert.That(compilation.Units, Has.Count.GreaterThan(1),
                    $"{entry} is a multi-file sample and should gather more than one file");

        StringWriter output = new();
        IReadOnlyList<CompilationUnit> lowered = Lowering.Lower(compilation.Units, model);
        ProfiC.Interpreter.Interpreter.Run(lowered, model, output);

        string actual = output.ToString().ReplaceLineEndings("\n");
        string goldenPath = Path.Combine(
            GoldenDirectory, Path.GetDirectoryName(entry)!.Replace('\\', '/') + ".out");

        if (UpdateRequested || !File.Exists(goldenPath))
        {
            Directory.CreateDirectory(GoldenDirectory);
            File.WriteAllText(goldenPath, actual);

            if (!UpdateRequested)
            {
                Assert.Fail(
                    $"No recorded output for {entry}; one was written to {goldenPath}. "
                    + "Review it and re-run.");
            }

            return;
        }

        string expected = File.ReadAllText(goldenPath).Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.That(actual, Is.EqualTo(expected),
                    $"output of {entry} changed; re-run with PROFIC_UPDATE_GOLDEN=1 if intended");
    }
}
