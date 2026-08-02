using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Semantics;
using ProfiC.Compiler.Text;
using ProfiC.Interpreter;

namespace ProfiC.Tests.Interpreting;

/// <summary>
/// <para>Where a debugger stops, and why.</para>
/// <para>Run against real programs rather than invented points, because the rules exist to cope
/// with what lowering actually produces — six statements sharing a <c>loop each</c>'s line, the
/// same statement reached once per turn of a loop — and a hand-made sequence of points would
/// only prove the rules agree with my idea of lowering.</para>
/// </summary>
[TestFixture]
public sealed class StopPolicyTests
{
    /// <summary>The file every program here is said to be written in.</summary>
    private const string TheFile = "<test>";

    /// <summary>
    /// Runs a program under a policy, resuming with the given mode after each stop, and gives
    /// back the line of every stop in order.
    /// </summary>
    private sealed class Stepper(StopPolicy policy, StepMode after) : IDebugHost
    {
        public List<int> Stops { get; } = [];

        /// <summary>Why each stop happened, in the same order as <see cref="Stops"/>.</summary>
        public List<StopReason> Reasons { get; } = [];

        public void Reached(ExecutionPoint point)
        {
            if (policy.WhyStopAt(point) is not { } why)
            {
                return;
            }

            Stops.Add(point.Line);
            Reasons.Add(why);
            policy.Resume(after, point.Depth);
        }
    }

    private static List<int> StopsIn(string body, StopPolicy policy, StepMode after) =>
        SteppingThrough(body, policy, after).Stops;

    private static Stepper SteppingThrough(string body, StopPolicy policy, StepMode after)
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
        CompilationUnit unit = Parser.Parse(new SourceText(source, TheFile), diagnostics);
        SemanticModel model = Resolver.Resolve(unit, diagnostics, requireEntryPoint: true);
        TypeChecker.Check(unit, model, diagnostics);
        DefiniteAssignment.Analyze(unit, model, diagnostics);

        Assert.That(
            diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error),
            Is.Empty,
            "the program should compile before it is stepped through");

        Stepper stepper = new(policy, after);

        ProfiC.Interpreter.Interpreter.Run(
            Lowering.Lower(unit, model), model, new StringWriter(), TextReader.Null, stepper);

        return stepper;
    }

    /// <summary>
    /// <para>Arriving at a construct lowering rewrote is one stop, not six.</para>
    /// <para>This is the rule the whole policy exists for. A <c>loop each</c> becomes a block,
    /// two synthesized locals, a walk, an index loop and an element binding, all claiming the
    /// line it was written on. Stepping onto it should stop once and then move to the body.
    /// </para>
    /// <para>Returning to that line on a later turn is a different thing and is right: the next
    /// element is fetched there, and every debugger worth using shows the cursor go back to the
    /// loop's own line between turns. What is asserted is the arrival collapsing, which is why
    /// the check is on the order of the first stops rather than on a count.</para>
    /// </summary>
    [Test]
    public void ArrivingAtARewrittenConstructIsOneStop()
    {
        StopPolicy policy = new();
        policy.Resume(StepMode.Into, 0);

        List<int> stops = StopsIn("""
                    integer[] scores = {90, 72};
                    integer total = 0;

                    loop each score in scores
                        total = total + score;
                    end loop
            """, policy, StepMode.Into);

        Assert.Multiple(() =>
        {
            Assert.That(stops.Take(4), Is.EqualTo(new[] { 3, 4, 6, 7 }),
                        "the two declarations, then the loop once, then its body");

            Assert.That(stops.Count(line => line == 6), Is.LessThan(6),
                        "six lowered statements share that line and must not be six stops");
        });
    }

    /// <summary>
    /// <para>And a loop body is a stop on every turn.</para>
    /// <para>The other half of the same rule, and the reason it is written against statement
    /// identity rather than line number. By line these two cases are indistinguishable;
    /// collapsing the first without this would silently make a breakpoint in a loop fire
    /// once.</para>
    /// </summary>
    [Test]
    public void ALoopBodyStopsOnEveryTurn()
    {
        StopPolicy policy = new();
        policy.BreakpointsAt(TheFile, [6]);
        policy.Resume(StepMode.Run, 0);

        List<int> stops = StopsIn("""
                    integer total = 0;

                    loop for i = 1 to 3
                        total = total + i;
                    end loop
            """, policy, StepMode.Run);

        Assert.That(stops, Is.EqualTo(new[] { 6, 6, 6 }),
                    "a breakpoint in a loop body fires once per turn");
    }

    /// <summary>Running stops only where a breakpoint is, and nowhere else.</summary>
    [Test]
    public void RunningStopsOnlyAtBreakpoints()
    {
        StopPolicy policy = new();
        policy.BreakpointsAt(TheFile, [4]);
        policy.Resume(StepMode.Run, 0);

        List<int> stops = StopsIn("""
                    integer a = 1;
                    integer b = 2;
                    integer c = 3;
            """, policy, StepMode.Run);

        Assert.That(stops, Is.EqualTo(new[] { 4 }));
    }

    /// <summary>
    /// Stepping into a call stops inside it, which is what tells "into" from "over".
    /// </summary>
    [Test]
    public void SteppingIntoReachesTheCalledFunction()
    {
        StopPolicy policy = new();
        policy.Resume(StepMode.Into, 0);

        List<int> stops = StopsIn("""
                    Console.WriteLine(Program.Doubled(21));
            """, policy, StepMode.Into);

        Assert.That(stops, Does.Contain(7),
                    "the yield inside Doubled is on line 7 and should be stepped into");
    }

    /// <summary>
    /// <para>Stepping over the same call does not.</para>
    /// <para>The pair with the test above, on the same program: what differs is only what was
    /// asked for, which is the whole distinction.</para>
    /// </summary>
    [Test]
    public void SteppingOverDoesNotReachTheCalledFunction()
    {
        StopPolicy policy = new();
        policy.Resume(StepMode.Over, 1);

        List<int> stops = StopsIn("""
                    Console.WriteLine(Program.Doubled(21));
                    Console.WriteLine("after");
            """, policy, StepMode.Over);

        Assert.That(stops, Does.Not.Contain(7),
                    "stepping over a call should not stop inside it");
    }

    /// <summary>
    /// <para>A breakpoint is honored even while stepping over.</para>
    /// <para>Someone who set a breakpoint inside a function meant it, whatever they pressed
    /// afterwards. Skipping it because the step said "over" would be the debugger deciding it
    /// knew better.</para>
    /// </summary>
    [Test]
    public void ABreakpointIsHonoredWhileSteppingOver()
    {
        StopPolicy policy = new();
        policy.BreakpointsAt(TheFile, [7]);
        policy.Resume(StepMode.Over, 1);

        List<int> stops = StopsIn("""
                    Console.WriteLine(Program.Doubled(21));
            """, policy, StepMode.Over);

        Assert.That(stops, Does.Contain(7),
                    "a breakpoint inside the call should stop even though the step was 'over'");
    }

    // ---- Why it stopped ---------------------------------------------------------------

    /// <summary>Arriving at a breakpoint is reported as one.</summary>
    [Test]
    public void ArrivingAtABreakpointSaysSo()
    {
        StopPolicy policy = new();
        policy.BreakpointsAt(TheFile, [4]);
        policy.Resume(StepMode.Run, 0);

        Stepper stepper = SteppingThrough("""
                    integer a = 1;
                    integer b = 2;
            """, policy, StepMode.Run);

        Assert.That(stepper.Reasons, Is.EqualTo(new[] { StopReason.Breakpoint }));
    }

    /// <summary>And finishing a step is reported as a step.</summary>
    [Test]
    public void FinishingAStepSaysSo()
    {
        StopPolicy policy = new();
        policy.Resume(StepMode.Into, 0);

        Stepper stepper = SteppingThrough("""
                    integer a = 1;
                    integer b = 2;
            """, policy, StepMode.Into);

        Assert.That(stepper.Reasons, Is.All.EqualTo(StopReason.Step));
    }

    /// <summary>
    /// <para>Stepping onto a line that has a breakpoint is arriving at the breakpoint.</para>
    /// <para>The case the precedence exists for, and the one where getting it wrong is visible:
    /// an editor shows the reason above the call stack, so a reader who stepped onto their own
    /// breakpoint would be told "step" — an answer to a question they had already stopped
    /// asking.</para>
    /// </summary>
    [Test]
    public void SteppingOntoABreakpointReportsTheBreakpoint()
    {
        StopPolicy policy = new();
        policy.BreakpointsAt(TheFile, [4]);
        policy.Resume(StepMode.Into, 0);

        Stepper stepper = SteppingThrough("""
                    integer a = 1;
                    integer b = 2;
            """, policy, StepMode.Into);

        int arrival = stepper.Stops.IndexOf(4);

        Assert.Multiple(() =>
        {
            Assert.That(arrival, Is.GreaterThanOrEqualTo(0), "line 4 should be reached");

            Assert.That(stepper.Reasons[arrival], Is.EqualTo(StopReason.Breakpoint),
                        "the breakpoint wins where a step would have stopped there anyway");

            Assert.That(stepper.Reasons, Does.Contain(StopReason.Step),
                        "and the other stops are still steps, or the test proves nothing");
        });
    }
}
