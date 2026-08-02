using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Semantics;
using ProfiC.Compiler.Text;
using ProfiC.Interpreter;

namespace ProfiC.Tests.Interpreting;

/// <summary>
/// <para>Which file a debugger is talking about.</para>
/// <para>Everything here needs two files to say anything at all. With one file a line number
/// is a complete answer and every one of these tests passes without the file being carried
/// anywhere — which is exactly how a program spread across files ends up stopping in the wrong
/// one.</para>
/// </summary>
[TestFixture]
public sealed class DebugFilesTests
{
    private const string MainFile = "Program.pc";
    private const string OtherFile = "Books.pc";

    /// <summary>
    /// Calls into the other file from line 3, so that a stop inside <c>Doubled</c> has two
    /// different files on one stack.
    /// </summary>
    private const string MainSource = """
        shared model Program
            function Main()
                Console.WriteLine(Numbers.Doubled(21));
            end function
        end model
        """;

    /// <summary>
    /// <para>Written so that its interesting line is line 3 as well.</para>
    /// <para>Deliberate: the file-blind reading of a breakpoint is "line 3", and the two files
    /// agreeing on the number is what makes that reading visible when it is wrong.</para>
    /// </summary>
    private const string OtherSource = """
        shared model Numbers
            public integer function Doubled(integer n)
                yield n * 2;
            end function
        end model
        """;

    private static (IReadOnlyList<CompilationUnit> Lowered, SemanticModel Model) CompileBoth()
    {
        DiagnosticBag diagnostics = new();

        CompilationUnit main = Parser.Parse(new SourceText(MainSource, MainFile), diagnostics);
        CompilationUnit other = Parser.Parse(new SourceText(OtherSource, OtherFile), diagnostics);

        CompilationUnit[] units = [main, other];

        SemanticModel model = Resolver.Resolve(units, diagnostics, requireEntryPoint: true);
        TypeChecker.Check(units, model, diagnostics);
        DefiniteAssignment.Analyze(units, model, diagnostics);

        Assert.That(
            diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error),
            Is.Empty,
            "both files should compile before they are debugged");

        return (Lowering.Lower(units, model), model);
    }

    /// <summary>Records the stack at each stop, on the program's own thread.</summary>
    private sealed class Watcher : IDebugHost
    {
        private readonly StopPolicy _policy = new();

        public List<IReadOnlyList<CallFrame>> Stacks { get; } = [];

        public List<string> Files { get; } = [];

        public Watcher() => _policy.Resume(StepMode.Into, 0);

        public void Reached(ExecutionPoint point)
        {
            if (!_policy.ShouldStopAt(point))
            {
                return;
            }

            Stacks.Add(point.Stack);
            Files.Add(point.File);
            _policy.Resume(StepMode.Into, point.Depth);
        }
    }

    /// <summary>
    /// <para>Each frame says which file its call was written in.</para>
    /// <para>Per frame rather than per stop, because a call from one file into another is two
    /// files at once. An editor showing this stack opens each frame in the file that frame is
    /// actually in, and one file for the whole stack would take you to the wrong place for every
    /// frame but the innermost.</para>
    /// </summary>
    [Test]
    public void EachFrameNamesTheFileItsCallIsIn()
    {
        (IReadOnlyList<CompilationUnit> lowered, SemanticModel model) = CompileBoth();

        Watcher watcher = new();

        ProfiC.Interpreter.Interpreter.Run(
            lowered, model, new StringWriter(), TextReader.Null, watcher);

        IReadOnlyList<CallFrame> deepest = watcher.Stacks.MaxBy(stack => stack.Count) ?? [];

        Assert.Multiple(() =>
        {
            Assert.That(
                deepest.Select(frame => frame.Name),
                Is.EqualTo(new[] { "Doubled", "Main" }),
                "innermost first");

            Assert.That(
                deepest.Select(frame => frame.File),
                Is.EqualTo(new[] { OtherFile, MainFile }),
                "the called function's file, then the caller's");
        });
    }

    /// <summary>
    /// Stopping in the other file reports that file, and not the one the program started in.
    /// </summary>
    [Test]
    public void AStopReportsTheFileItStoppedIn()
    {
        (IReadOnlyList<CompilationUnit> lowered, SemanticModel model) = CompileBoth();

        Watcher watcher = new();

        ProfiC.Interpreter.Interpreter.Run(
            lowered, model, new StringWriter(), TextReader.Null, watcher);

        Assert.That(watcher.Files, Does.Contain(OtherFile),
                    "stepping into Doubled is a stop in the file Doubled is written in");
    }

    /// <summary>
    /// <para>A breakpoint belongs to one file, and does not fire on that line of another.</para>
    /// <para>Both files here have something on line 3, which is not a contrivance — line 3 is
    /// where the first statement of a small file lands, so a project of small files collides on
    /// it constantly. A breakpoint keyed on the number alone stops in whichever file the program
    /// reaches first, and there is no way to tell that apart from a breakpoint that works.</para>
    /// </summary>
    [Test]
    public void ABreakpointDoesNotFireOnTheSameLineOfAnotherFile()
    {
        (IReadOnlyList<CompilationUnit> lowered, SemanticModel model) = CompileBoth();

        StopPolicy policy = new();
        policy.BreakpointsAt(OtherFile, [3]);
        policy.Resume(StepMode.Run, 0);

        List<string> stopped = [];

        ProfiC.Interpreter.Interpreter.Run(
            lowered,
            model,
            new StringWriter(),
            TextReader.Null,
            new Watching(point =>
            {
                if (policy.ShouldStopAt(point))
                {
                    stopped.Add($"{point.File}:{point.Line}");
                    policy.Resume(StepMode.Run, point.Depth);
                }
            }));

        Assert.That(stopped, Is.EqualTo(new[] { $"{OtherFile}:3" }),
                    "only the file the breakpoint was set in");
    }

    /// <summary>
    /// <para>Setting the breakpoints of one file leaves another file's alone.</para>
    /// <para>Which is what the protocol requires: an editor sends the whole set for a file each
    /// time any one of them changes, and never mentions the files it is not talking about.
    /// Reading that as "these are all the breakpoints there are" clears every breakpoint outside
    /// the file last edited.</para>
    /// </summary>
    [Test]
    public void SettingOneFilesBreakpointsLeavesAnothersAlone()
    {
        (IReadOnlyList<CompilationUnit> lowered, SemanticModel model) = CompileBoth();

        StopPolicy policy = new();
        policy.BreakpointsAt(MainFile, [3]);
        policy.BreakpointsAt(OtherFile, [3]);
        policy.Resume(StepMode.Run, 0);

        List<string> stopped = [];

        ProfiC.Interpreter.Interpreter.Run(
            lowered,
            model,
            new StringWriter(),
            TextReader.Null,
            new Watching(point =>
            {
                if (policy.ShouldStopAt(point))
                {
                    stopped.Add($"{point.File}:{point.Line}");
                    policy.Resume(StepMode.Run, point.Depth);
                }
            }));

        Assert.That(stopped, Is.EqualTo(new[] { $"{MainFile}:3", $"{OtherFile}:3" }),
                    "both, in the order the program reaches them");
    }

    /// <summary>Watches a run with whatever the test wants done at each point.</summary>
    private sealed class Watching(Action<ExecutionPoint> at) : IDebugHost
    {
        public void Reached(ExecutionPoint point) => at(point);
    }
}
