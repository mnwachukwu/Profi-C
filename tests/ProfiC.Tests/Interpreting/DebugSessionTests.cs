using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Semantics;
using ProfiC.Compiler.Text;
using ProfiC.Interpreter;

namespace ProfiC.Tests.Interpreting;

/// <summary>
/// <para>The call stack, and a session that really blocks.</para>
/// <para>The blocking is the part worth testing with two threads rather than a stub: a stop is
/// the program's thread not returning, and a design that only looked right single-threaded
/// would deadlock the first time an editor drove it.</para>
/// </summary>
[TestFixture]
public sealed class DebugSessionTests
{
    private const string Nested = """
        shared model Program
            function Main()
                Console.WriteLine(Program.Outer(2));
            end function

            integer function Outer(integer n)
                yield Program.Inner(n);
            end function

            integer function Inner(integer n)
                yield n * 2;
            end function
        end model
        """;

    /// <summary>The file every program here is said to be written in.</summary>
    private const string TheFile = "<test>";

    private static (CompilationUnit Lowered, SemanticModel Model) Compile(string source)
    {
        DiagnosticBag diagnostics = new();
        CompilationUnit unit = Parser.Parse(new SourceText(source, TheFile), diagnostics);
        SemanticModel model = Resolver.Resolve(unit, diagnostics, requireEntryPoint: true);
        TypeChecker.Check(unit, model, diagnostics);
        DefiniteAssignment.Analyze(unit, model, diagnostics);

        Assert.That(
            diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error),
            Is.Empty,
            "the program should compile before it is debugged");

        return (Lowering.Lower(unit, model), model);
    }

    /// <summary>Records the stack at each stop, on the program's own thread.</summary>
    private sealed class Watcher : IDebugHost
    {
        private readonly StopPolicy _policy = new();

        public List<IReadOnlyList<CallFrame>> Stacks { get; } = [];

        public Watcher() => _policy.Resume(StepMode.Into, 0);

        public void Reached(ExecutionPoint point)
        {
            if (_policy.WhyStopAt(point) is null)
            {
                return;
            }

            Stacks.Add(point.Stack);
            _policy.Resume(StepMode.Into, point.Depth);
        }
    }

    /// <summary>
    /// <para>The stack names every call in progress, innermost first.</para>
    /// <para>And every frame below the innermost shows the line it is waiting on, not the line
    /// it started at — which is what makes a stack trace tell you how you got here.</para>
    /// </summary>
    [Test]
    public void TheStackNamesEveryCallInProgress()
    {
        (CompilationUnit lowered, SemanticModel model) = Compile(Nested);

        Watcher watcher = new();

        ProfiC.Interpreter.Interpreter.Run(
            lowered, model, new StringWriter(), TextReader.Null, watcher);

        IReadOnlyList<CallFrame> deepest =
            watcher.Stacks.MaxBy(stack => stack.Count) ?? [];

        Assert.Multiple(() =>
        {
            Assert.That(
                deepest.Select(frame => frame.Name),
                Is.EqualTo(new[] { "Inner", "Outer", "Main" }),
                "innermost first, all three calls named");

            Assert.That(deepest[0].Line, Is.EqualTo(11), "Inner is on its yield");
            Assert.That(deepest[1].Line, Is.EqualTo(7), "Outer waits on the call it made");
            Assert.That(deepest[2].Line, Is.EqualTo(3), "Main waits on the call it made");
        });
    }

    /// <summary>
    /// A run with nothing watching keeps no stack at all, which is what lets the bookkeeping
    /// sit on the path every call takes.
    /// </summary>
    [Test]
    public void AnUnwatchedRunKeepsNoStack()
    {
        (CompilationUnit lowered, SemanticModel model) = Compile(Nested);

        StringWriter output = new();

        Assert.DoesNotThrow(() =>
            ProfiC.Interpreter.Interpreter.Run(lowered, model, output));

        Assert.That(output.ToString().Trim(), Is.EqualTo("4"));
    }

    /// <summary>
    /// <para>A stop really stops: the program's thread waits until something lets it go.</para>
    /// <para>Driven from another thread, as an editor would, because that is the only way to
    /// find out whether the waiting works. A stub host that returned immediately would pass a
    /// design that deadlocks.</para>
    /// </summary>
    [Test]
    public void AStopHoldsTheProgramUntilItIsReleased()
    {
        (CompilationUnit lowered, SemanticModel model) = Compile("""
            shared model Program
                function Main()
                    Console.WriteLine("one");
                    Console.WriteLine("two");
                end function
            end model
            """);

        using SemaphoreSlim stopped = new(0);
        using DebugSession session = new((_, _) => stopped.Release());

        // A session runs by default, so something has to ask it to stop. Breakpoints are set
        // before the program starts, which is the order the protocol's handshake enforces.
        session.BreakpointsAt(TheFile, [3]);

        StringWriter output = new();

        Task program = Task.Run(() =>
            ProfiC.Interpreter.Interpreter.Run(
                lowered, model, output, TextReader.Null, session));

        Assert.Multiple(() =>
        {
            Assert.That(stopped.Wait(TimeSpan.FromSeconds(5)), Is.True,
                        "the program should stop at the breakpoint on its first line");

            Assert.That(session.Where, Is.Not.Null);
            Assert.That(session.Where!.Line, Is.EqualTo(3));

            Assert.That(output.ToString(), Is.Empty,
                        "stopped before the first line ran, so nothing is printed yet");

            Assert.That(program.IsCompleted, Is.False, "the program is waiting, not finished");
        });

        session.StepOver();

        Assert.That(stopped.Wait(TimeSpan.FromSeconds(5)), Is.True, "and stops again on line 4");
        Assert.That(output.ToString().Trim(), Is.EqualTo("one"), "one line ran, then it stopped");

        session.Continue();

        Assert.That(program.Wait(TimeSpan.FromSeconds(5)), Is.True, "continuing runs it to the end");
        Assert.That(output.ToString().ReplaceLineEndings("\n"), Is.EqualTo("one\ntwo\n"));
    }

    /// <summary>
    /// <para>Detaching lets the program finish rather than killing it.</para>
    /// <para>A reader who closes the session leaves a program mid-statement on another thread.
    /// Stopping it there would leave whatever it was doing half-done, so it is let go
    /// unwatched — and nothing after the detach stops again, however many breakpoints are set.
    /// </para>
    /// </summary>
    [Test]
    public void DetachingLetsTheProgramRunToTheEnd()
    {
        (CompilationUnit lowered, SemanticModel model) = Compile("""
            shared model Program
                function Main()
                    Console.WriteLine("one");
                    Console.WriteLine("two");
                    Console.WriteLine("three");
                end function
            end model
            """);

        using SemaphoreSlim stopped = new(0);
        using DebugSession session = new((_, _) => stopped.Release());

        session.BreakpointsAt(TheFile, [4, 5]);

        StringWriter output = new();

        Task program = Task.Run(() =>
            ProfiC.Interpreter.Interpreter.Run(
                lowered, model, output, TextReader.Null, session));

        Assert.That(stopped.Wait(TimeSpan.FromSeconds(5)), Is.True, "stopped at the first statement");

        session.Detach();

        Assert.Multiple(() =>
        {
            Assert.That(program.Wait(TimeSpan.FromSeconds(5)), Is.True,
                        "the program should finish rather than hang on the breakpoints");

            Assert.That(output.ToString().ReplaceLineEndings("\n"),
                        Is.EqualTo("one\ntwo\nthree\n"));
        });
    }
}
