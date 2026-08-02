using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Semantics;
using ProfiC.Compiler.Text;

namespace ProfiC.Tests.Semantics;

/// <summary>
/// <para>What a debugger will be able to stop on.</para>
/// <para>The interpreter runs the <em>lowered</em> tree, so a breakpoint set on a source line
/// can only be honored if some lowered statement still claims that line. Lowering rewrites
/// several constructs beyond recognition — a <c>loop each</c> becomes a block, two synthesized
/// locals, a walk, an index loop and an element binding — and nothing about that rewriting is
/// obliged to carry the original span.</para>
/// <para>It does carry them, measured 2026-08-01, and these hold it to that. Without them the
/// failure is silent and specific: a breakpoint on a <c>loop each</c> would simply never be
/// hit, and the reader would conclude the debugger was broken rather than the mapping.</para>
/// </summary>
[TestFixture]
public sealed class LoweredSpanTests
{
    /// <summary>
    /// Every construct whose lowering rewrites it, one per line, so a line number in a failure
    /// names the construct that lost its span.
    /// </summary>
    private const string Source = """
        shared model Program
            function Main()
                integer[] scores = {90, 72};
                string name = "Ada";
                integer total = 0;
                loop each score in scores
                    total = total + score;
                end loop
                switch total
                    case 162:
                        Console.WriteLine("all of them");
                    default:
                        Console.WriteLine("some of them");
                end switch
                Console.WriteLine("{{name}} scored {{total}}");
                loop for i = 1 to 2
                    Console.WriteLine(i);
                end loop
                loop
                    Console.WriteLine("once");
                until total > 0
            end function
        end model
        """;

    /// <summary>The lines above that a reader would expect to be able to stop on, 1-based.</summary>
    private static readonly int[] Executable =
    [
        3,   // integer[] scores = ...
        4,   // string name = ...
        5,   // integer total = ...
        6,   // loop each          <- rewritten into six statements
        7,   // total = total + score;
        9,   // switch total
        11,  // Console.WriteLine("all of them");
        13,  // Console.WriteLine("some of them");
        15,  // an interpolated string
        16,  // loop for
        17,  // Console.WriteLine(i);
        19,  // loop
        20,  // Console.WriteLine("once");
    ];

    private static Dictionary<int, List<Statement>> StopsByLine()
    {
        DiagnosticBag diagnostics = new();
        CompilationUnit unit = Parser.Parse(new SourceText(Source, "<test>"), diagnostics);
        SemanticModel model = Resolver.Resolve(unit, diagnostics, requireEntryPoint: true);
        TypeChecker.Check(unit, model, diagnostics);
        DefiniteAssignment.Analyze(unit, model, diagnostics);

        Assert.That(
            diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error),
            Is.Empty,
            "the fixture must compile before its spans mean anything");

        Dictionary<int, List<Statement>> stops = [];

        foreach (SyntaxNode node in Walk(Lowering.Lower(unit, model)))
        {
            if (node is Statement statement)
            {
                if (!stops.TryGetValue(statement.Span.Start.Line, out List<Statement>? here))
                {
                    here = [];
                    stops[statement.Span.Start.Line] = here;
                }

                here.Add(statement);
            }
        }

        return stops;

        static IEnumerable<SyntaxNode> Walk(SyntaxNode node)
        {
            yield return node;

            foreach (SyntaxNode child in node.Children)
            {
                foreach (SyntaxNode deeper in Walk(child))
                {
                    yield return deeper;
                }
            }
        }
    }

    /// <summary>
    /// Every line a reader would set a breakpoint on has a lowered statement that starts there.
    /// This is the whole basis of breakpoints, and it needs no span rewriting to be true.
    /// </summary>
    [Test]
    public void EveryExecutableLineKeepsAStatementThatStartsOnIt()
    {
        Dictionary<int, List<Statement>> stops = StopsByLine();

        Assert.That(
            Executable.Where(line => !stops.ContainsKey(line)),
            Is.Empty,
            "lines a breakpoint could never be hit on, because lowering moved their spans");
    }

    /// <summary>
    /// <para>And nothing claims a line that carries no statement.</para>
    /// <para>A closer, a case label, or a blank line offering a stop would let a breakpoint sit
    /// somewhere a reader cannot reason about — and the <c>until</c> of a bottom-tested loop is
    /// the sharpest of those, since the loop's own statement already claims the <c>loop</c>
    /// line above it.</para>
    /// </summary>
    [Test]
    public void NothingClaimsALineThatIsNotExecutable()
    {
        Dictionary<int, List<Statement>> stops = StopsByLine();

        Assert.That(
            stops.Keys.Where(line => !Executable.Contains(line)).Order(),
            Is.Empty,
            "lines offering a stop that a reader would not expect one on");
    }

    /// <summary>
    /// <para>A rewritten construct claims its line more than once, and a stepper has to know.
    /// </para>
    /// <para><c>loop each</c> lowers to six statements that all start on the one line it was
    /// written on. Stopping at each would report the same line six times for one step, and then
    /// again on every turn of the loop. Pinned because the number is a fact about lowering that
    /// a stepper written against it would otherwise silently depend on.</para>
    /// </summary>
    [Test]
    public void ARewrittenConstructClaimsItsLineMoreThanOnce()
    {
        Dictionary<int, List<Statement>> stops = StopsByLine();

        Assert.Multiple(() =>
        {
            Assert.That(stops[6], Has.Count.GreaterThan(1),
                        "a 'loop each' lowers to several statements on its own line");

            Assert.That(stops[7], Has.Count.EqualTo(1),
                        "an ordinary statement claims its line once");
        });
    }

    /// <summary>
    /// <para>The names lowering invents are recognizable as invented.</para>
    /// <para>A variables pane reads the scope the interpreter holds, and that scope carries
    /// <c>&lt;source$0&gt;</c>, <c>&lt;count$2&gt;</c> and <c>&lt;index$1&gt;</c> alongside the
    /// program's own names. Showing those to a beginner would be worse than showing nothing, so
    /// a debugger has to filter them — and can, because every one is wrapped in angle brackets
    /// and no name a program may write is.</para>
    /// </summary>
    [Test]
    public void EveryNameLoweringInventsIsAngleBracketed()
    {
        Dictionary<int, List<Statement>> stops = StopsByLine();

        string[] declared = [.. stops.Values
                                     .SelectMany(statements => statements)
                                     .OfType<VarDeclStmt>()
                                     .Select(declaration => declaration.Name)];

        Assert.Multiple(() =>
        {
            Assert.That(declared, Does.Contain("<source$0>"),
                        "the fixture should exercise a construct that invents a name");

            Assert.That(
                declared.Where(name => name.StartsWith('<') != name.EndsWith('>')),
                Is.Empty,
                "an invented name must be wrapped in angle brackets at both ends");
        });
    }
}
