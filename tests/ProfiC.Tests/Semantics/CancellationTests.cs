using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Semantics;
using ProfiC.Compiler.Text;

namespace ProfiC.Tests.Semantics;

/// <summary>
/// <para>Every analysis pass stops when the answer stops being wanted.</para>
/// <para>What this is for: a language server re-analyzes when the reader pauses, and a reader who
/// starts typing again has made the running analysis worthless. Without a way to stop it, the
/// work runs to completion against text nobody is looking at any more, and the next one queues
/// behind it — so the editor falls further behind the faster somebody types.</para>
/// <para>The check sits at two places in each pass: once per declaration and once per statement.
/// Per declaration alone would make a single very long function uninterruptible, which is a shape
/// a beginner writes — everything in <c>Main</c> — and per node would put the check on every line
/// of every walk to buy latency nobody can perceive.</para>
/// <para>Nothing in the compiler signals one. A build passes no token at all, so what these hold
/// costs a comparison against a field there.</para>
/// </summary>
[TestFixture]
public sealed class CancellationTests
{
    /// <summary>
    /// Several declarations and several statements, so there is something to stop partway
    /// through rather than a program that is over before the first check.
    /// </summary>
    private const string Program = """
        model Counter
            integer total;

            public function Counter()
                this.total = 0;
            end function

            public function Add(integer amount)
                integer doubled = amount * 2;
                integer halved = amount / 2;
                this.total = this.total + doubled + halved;
            end function

            public integer function Total()
                yield this.total;
            end function
        end model

        shared model Program
            function Main()
                Counter counter = new Counter();
                counter.Add(4);
                counter.Add(6);
                Console.WriteLine(counter.Total());
            end function
        end model
        """;

    private static CompilationUnit Parsed(DiagnosticBag diagnostics) =>
        Parser.Parse(new SourceText(Program, "<test>"), diagnostics);

    /// <summary>A token that has already been signalled, which is the deterministic case.</summary>
    private static CancellationToken Cancelled()
    {
        CancellationTokenSource source = new();
        source.Cancel();

        return source.Token;
    }

    [Test]
    public void ResolvingStopsWhenCancelled()
    {
        DiagnosticBag diagnostics = new();
        CompilationUnit unit = Parsed(diagnostics);

        Assert.Throws<OperationCanceledException>(
            () => Resolver.Resolve(unit, diagnostics, cancellation: Cancelled()));
    }

    [Test]
    public void CheckingStopsWhenCancelled()
    {
        DiagnosticBag diagnostics = new();
        CompilationUnit unit = Parsed(diagnostics);
        SemanticModel model = Resolver.Resolve(unit, diagnostics);

        Assert.Throws<OperationCanceledException>(
            () => TypeChecker.Check(unit, model, diagnostics, Cancelled()));
    }

    [Test]
    public void AnalyzingAssignmentStopsWhenCancelled()
    {
        DiagnosticBag diagnostics = new();
        CompilationUnit unit = Parsed(diagnostics);
        SemanticModel model = Resolver.Resolve(unit, diagnostics);

        TypeChecker.Check(unit, model, diagnostics);

        Assert.Throws<OperationCanceledException>(
            () => DefiniteAssignment.Analyze(unit, model, diagnostics, Cancelled()));
    }

    /// <summary>
    /// <para>Stopping happens before the work, not after it.</para>
    /// <para>A pass that checked its token only on the way out would throw and pass the test
    /// above while still having done everything. What says otherwise is that nothing was
    /// recorded: a resolver that ran would have declared <c>Counter</c> and <c>Program</c>.</para>
    /// </summary>
    [Test]
    public void NothingIsResolvedOnceCancelled()
    {
        DiagnosticBag diagnostics = new();
        CompilationUnit unit = Parsed(diagnostics);

        SemanticModel? model = null;

        try
        {
            model = Resolver.Resolve(unit, diagnostics, cancellation: Cancelled());
        }
        catch (OperationCanceledException)
        {
        }

        Assert.That(model, Is.Null, "the walk ran to the end and only then noticed");
    }

    /// <summary>
    /// A token nobody signals changes nothing, which is every build and every other test in this
    /// suite. Held explicitly so that the default stays the one a compiler gets.
    /// </summary>
    [Test]
    public void ATokenNobodySignalsChangesNothing()
    {
        DiagnosticBag diagnostics = new();
        CompilationUnit unit = Parsed(diagnostics);

        SemanticModel model = Resolver.Resolve(unit, diagnostics, cancellation: CancellationToken.None);

        TypeChecker.Check(unit, model, diagnostics, CancellationToken.None);
        DefiniteAssignment.Analyze(unit, model, diagnostics, CancellationToken.None);

        Assert.That(
            diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error),
            Is.Empty,
            "the program compiles, and passing a token that is never signalled did not change it");
    }
}
