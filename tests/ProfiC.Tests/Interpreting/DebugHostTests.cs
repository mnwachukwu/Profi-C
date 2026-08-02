using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Semantics;
using ProfiC.Compiler.Text;
using ProfiC.Interpreter;

namespace ProfiC.Tests.Interpreting;

/// <summary>
/// <para>The seam a debugger attaches to.</para>
/// <para>The interpreter's whole part in debugging is announcing where it is before each
/// statement and going on when the host returns. Everything a person would call debugging —
/// breakpoints, stepping, deciding that six statements on one line are one stop — is the host's,
/// and is deliberately not here.</para>
/// <para>So what these hold is the seam itself: that the announcement happens once per
/// statement, that it carries a usable line and depth, and that the scope it hands over
/// separates the names a program wrote from the ones lowering invented.</para>
/// </summary>
[TestFixture]
public sealed class DebugHostTests
{
    /// <summary>
    /// Records where the program went, and runs whatever the test wants done while it is still
    /// there — the only moment a point's locals mean anything.
    /// </summary>
    private sealed class Recorder(Action<ExecutionPoint>? whilePaused) : IDebugHost
    {
        public List<ExecutionPoint> Points { get; } = [];

        public void Reached(ExecutionPoint point)
        {
            Points.Add(point);
            whilePaused?.Invoke(point);
        }
    }

    private static Recorder Watch(string body, Action<ExecutionPoint>? whilePaused = null)
    {
        string source = $$"""
            shared model Program
                function Main()
            {{body}}
                end function

                integer function Doubled(integer n)
                    yield n * 2;
                end function
            end model
            """;

        DiagnosticBag diagnostics = new();
        CompilationUnit unit = Parser.Parse(new SourceText(source, "<test>"), diagnostics);
        SemanticModel model = Resolver.Resolve(unit, diagnostics, requireEntryPoint: true);
        TypeChecker.Check(unit, model, diagnostics);
        DefiniteAssignment.Analyze(unit, model, diagnostics);

        Assert.That(
            diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error),
            Is.Empty,
            "the program should compile before it is watched");

        Recorder recorder = new(whilePaused);

        ProfiC.Interpreter.Interpreter.Run(
            Lowering.Lower(unit, model), model, new StringWriter(), TextReader.Null, recorder);

        return recorder;
    }

    /// <summary>
    /// A host attached sees every statement, in the order they run — including the same one
    /// again on each turn of a loop, which is what makes a breakpoint inside one fire twice.
    /// </summary>
    [Test]
    public void AHostSeesEveryStatementAsItRuns()
    {
        Recorder watched = Watch("""
                    integer total = 0;

                    loop for i = 1 to 3
                        total = total + i;
                    end loop
            """);

        // Line 6 is the loop's body; line 5 is its header, announced once.
        Assert.Multiple(() =>
        {
            Assert.That(
                watched.Points.Count(point => point.Line == 6),
                Is.EqualTo(3),
                "a statement inside a loop is reached once per turn");

            Assert.That(
                watched.Points.Count(point => point.Line == 5),
                Is.EqualTo(1),
                "the loop's own statement is reached once, however many turns it takes");
        });
    }

    /// <summary>
    /// <para>Depth moves with calls, which is what "step over" and "step out" are about.</para>
    /// <para>A step over waits for a depth no greater than the one it started at; a step out for
    /// a smaller one. Neither is possible if the announcement does not carry it.</para>
    /// </summary>
    [Test]
    public void DepthRisesInsideACall()
    {
        Recorder watched = Watch("""
                    Console.WriteLine(Program.Doubled(21));
            """);

        Assert.Multiple(() =>
        {
            Assert.That(watched.Points, Is.Not.Empty);

            Assert.That(
                watched.Points.Select(point => point.Depth).Distinct().Count(),
                Is.GreaterThan(1),
                "the yield inside Doubled runs deeper than the call that reached it");
        });
    }

    /// <summary>
    /// <para>The scope handed over says which names the program wrote and which lowering
    /// invented.</para>
    /// <para>A <c>loop each</c> puts three invented names in scope beside the element. A
    /// variables pane showing <c>&lt;source$0&gt;</c> to a beginner would be worse than showing
    /// nothing, and this is what lets it not.</para>
    /// </summary>
    [Test]
    public void InventedNamesAreMarkedApartFromTheProgramsOwn()
    {
        Recorder watched = Watch("""
                    integer[] scores = {90, 72};
                    integer total = 0;

                    loop each score in scores
                        total = total + score;
                    end loop
            """);

        IReadOnlyList<Local> inside = watched.Points
            .Where(point => point.Line == 7)
            .Select(point => point.Locals())
            .First();

        Assert.Multiple(() =>
        {
            Assert.That(
                inside.Where(local => !local.Invented).Select(local => local.Name),
                Does.Contain("score").And.Contains("total"),
                "what the program wrote should be there and unmarked");

            Assert.That(
                inside.Where(local => local.Invented).Select(local => local.Name),
                Is.Not.Empty,
                "a 'loop each' invents names, and they should be marked as invented");

            Assert.That(
                inside.Where(local => local.Invented)
                      .Select(local => local.Name)
                      .Where(name => !name.StartsWith('<')),
                Is.Empty,
                "nothing should be called invented that a program could have written");
        });
    }

    /// <summary>
    /// <para>A local holds what it holds at the moment of the pause. The point of a variables
    /// pane.</para>
    /// <para>Read from inside <c>Reached</c>, because a point is a live view of a scope rather
    /// than a copy of one. Kept and asked afterwards, every one of these answers 6 — the value
    /// the loop finished at — which looks like a plausible reading and is a reading of the
    /// wrong moment. Written this way round deliberately: it is the mistake an adapter author
    /// will make first.</para>
    /// </summary>
    [Test]
    public void ALocalReadsAsItStandsAtThatMoment()
    {
        List<long> seen = [];

        Watch("""
                    integer total = 0;

                    loop for i = 1 to 3
                        total = total + i;
                    end loop
            """,
            point =>
            {
                if (point.Line == 6
                    && point.Locals().FirstOrDefault(local => local.Name == "total")?.Value
                        is long total)
                {
                    seen.Add(total);
                }
            });

        Assert.That(seen, Is.EqualTo(new long[] { 0, 1, 3 }),
                    "the value before each turn's addition: 0, then 1, then 1+2");
    }

    /// <summary>
    /// With no host attached nothing is announced, which is the ordinary way a program runs and
    /// the reason the gate can sit where every statement passes.
    /// </summary>
    [Test]
    public void NoHostMeansNoAnnouncement() => Assert.DoesNotThrow(() =>
    {
        DiagnosticBag diagnostics = new();

        CompilationUnit unit = Parser.Parse(
            new SourceText("""
                shared model Program
                    function Main()
                        Console.WriteLine("quiet");
                    end function
                end model
                """, "<test>"),
            diagnostics);

        SemanticModel model = Resolver.Resolve(unit, diagnostics, requireEntryPoint: true);
        TypeChecker.Check(unit, model, diagnostics);

        ProfiC.Interpreter.Interpreter.Run(Lowering.Lower(unit, model), model, new StringWriter());
    });
}
