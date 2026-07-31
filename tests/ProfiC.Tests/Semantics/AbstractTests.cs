using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Semantics;
using ProfiC.Compiler.Text;

namespace ProfiC.Tests.Semantics;

/// <summary>
/// <para>What <c>abstract</c> obliges, on a model and on a function.</para>
/// <para>On a model it forbids constructing. On a function it declares a name and a signature
/// with nothing behind them, which is only meaningful if two things hold: no one can construct
/// the model that carries it, and the obligation to write it travels down until it reaches a
/// model that <em>can</em> be constructed.</para>
/// <para>The surface spans four passes — the resolver settles the obligations, the type checker
/// refuses the construction, reaching a result is a flow question, and dispatch is the
/// interpreter's — so all four run here.</para>
/// </summary>
[TestFixture]
public sealed class AbstractTests
{
    private static string[] Check(string source)
    {
        DiagnosticBag diagnostics = new();
        CompilationUnit unit = Parser.Parse(new SourceText(source, "<test>"), diagnostics);
        SemanticModel model = Resolver.Resolve(unit, diagnostics);
        TypeChecker.Check(unit, model, diagnostics);
        DefiniteAssignment.Analyze(unit, model, diagnostics);

        return [.. diagnostics.Sorted().Select(d => d.Id)];
    }

    /// <summary>An abstract base, a descendant, and whatever the case under test needs.</summary>
    private static string[] CheckShape(string open, string descendant) => Check(
        $$"""
        abstract model Shape
        {{open}}
        end model

        model Circle extends Shape
        {{descendant}}
        end model
        """);

    private const string Open = "    public abstract real function Area();";

    private const string Written = """
            public override real function Area()
                yield 3.0;
            end function
        """;

    // ---- On a model ---------------------------------------------------------------------------

    [Test]
    public void AnAbstractModelCannotBeConstructed() => Assert.That(
        Check("""
            abstract model Shape
            end model

            global model Program
                function Main()
                    Shape s = new Shape();
                    Console.WriteLine(s);
                end function
            end model
            """),
        Is.EqualTo(new[] { "PC0328" }));

    [Test]
    public void SealedAndAbstractTogetherIsRejected() => Assert.That(
        Check("""
            sealed abstract model Shape
            end model
            """),
        Is.EqualTo(new[] { "PC0210" }));

    // ---- A function left open -----------------------------------------------------------------

    /// <summary>
    /// The declaration ends at a semicolon. It closes no block, so it wants no
    /// <c>end function</c>, and nothing asks it to reach a result it never promised to produce
    /// here.
    /// </summary>
    [Test]
    public void AnAbstractFunctionNeedsNoBody() =>
        Assert.That(CheckShape(Open, Written), Is.Empty);

    /// <summary>
    /// <c>abstract</c> is what offers the function for overriding, so no <c>virtual</c> is
    /// wanted beside it — and writing one says nothing further.
    /// </summary>
    [Test]
    public void AbstractOffersTheFunctionWithoutVirtual() =>
        Assert.That(CheckShape(Open, Written), Is.Empty);

    [Test]
    public void VirtualBesideAbstractIsAWarning() => Assert.That(
        CheckShape("    public abstract virtual real function Area();", Written),
        Is.EqualTo(new[] { "PC0242" }));

    [Test]
    public void AnAbstractFunctionMayNotHaveABody() => Assert.That(
        CheckShape(
            """
                public abstract real function Area()
                    yield 0.0;
                end function
            """,
            Written),
        Is.EqualTo(new[] { "PC0239" }));

    /// <summary>
    /// The other direction, and the reason a bodiless function is not simply allowed: without
    /// <c>abstract</c> nothing obliges anyone to write it, so the program would reach a
    /// function that does not exist.
    /// </summary>
    [Test]
    public void AFunctionThatIsNotAbstractStillNeedsABody() => Assert.That(
        Check("""
            model Shape
                public real function Area();
            end model
            """),
        Is.EqualTo(new[] { "PC0238" }));

    [Test]
    public void OnlyAnAbstractModelMayCarryOne() => Assert.That(
        Check("""
            model Shape
                public abstract real function Area();
            end model
            """),
        Is.EqualTo(new[] { "PC0240" }));

    /// <summary>
    /// <para><c>abstract</c> written with no visibility beside it is protected, not private.
    /// </para>
    /// <para>A declaration with no word belongs to the smallest thing that could own it, and
    /// for this one the declaring type is not that thing — nothing there writes the function.
    /// The narrowest reach the word admits is the type and everything extending it, so that is
    /// what silence means.</para>
    /// </summary>
    [Test]
    public void AbstractAloneIsProtected() => Assert.That(
        CheckShape("    abstract real function Area();", Written),
        Is.Empty);

    /// <summary>Protected, not public: the implication reaches descendants and stops there.</summary>
    [Test]
    public void AbstractAloneIsNotReachableFromOutside() => Assert.That(
        Check("""
            abstract model Shape
                abstract real function Area();
            end model

            model Square extends Shape
                public override real function Area()
                    yield 4.0;
                end function
            end model

            global model Program
                function Main()
                    Shape s = new Square();
                    Console.WriteLine(s.Area());
                end function
            end model
            """),
        Is.EqualTo(new[] { "PC0339" }));

    // ---- Who has to write it ------------------------------------------------------------------

    [Test]
    public void AModelThatCanBeConstructedMustWriteIt() =>
        Assert.That(CheckShape(Open, ""), Is.EqualTo(new[] { "PC0241" }));

    /// <summary>One message naming every one of them, since the reader's job is to write them all.</summary>
    [Test]
    public void TheMessageNamesEveryFunctionLeftOpen()
    {
        DiagnosticBag diagnostics = new();

        CompilationUnit unit = Parser.Parse(
            new SourceText("""
                abstract model Shape
                    public abstract real function Area();
                    public abstract string function Name();
                end model

                model Blob extends Shape
                end model
                """, "<test>"),
            diagnostics);

        Resolver.Resolve(unit, diagnostics);

        Assert.That(
            diagnostics.Sorted().Single(d => d.Id == "PC0241").Message,
            Does.Contain("'Area' from 'Shape'").And.Contain("'Name' from 'Shape'"));
    }

    /// <summary>An abstract descendant passes the obligation on rather than discharging it.</summary>
    [Test]
    public void AnAbstractDescendantNeedNotWriteIt() => Assert.That(
        Check("""
            abstract model Shape
                public abstract real function Area();
            end model

            abstract model Round extends Shape
            end model
            """),
        Is.Empty);

    /// <summary>
    /// A model in the middle that writes it discharges the obligation for everything below, so
    /// a hierarchy fills a function once rather than at every level.
    /// </summary>
    [Test]
    public void AModelInTheMiddleDischargesItForThoseBelow() => Assert.That(
        Check("""
            abstract model Shape
                public abstract real function Area();
            end model

            abstract model Round extends Shape
                public override real function Area()
                    yield 3.0;
                end function
            end model

            model Circle extends Round
            end model
            """),
        Is.Empty);

    // ---- And it runs --------------------------------------------------------------------------

    /// <summary>
    /// The point of all of it. A function the base never wrote is reached through a base-typed
    /// reference and finds the descendant's, which is virtual dispatch doing what the word
    /// promised.
    /// </summary>
    [Test]
    public void TheDescendantsFunctionIsWhatRuns()
    {
        DiagnosticBag diagnostics = new();

        CompilationUnit unit = Parser.Parse(
            new SourceText("""
                abstract model Shape
                    protected string label;

                    public function Shape(string label)
                        this.label = label;
                    end function

                    public abstract real function Area();

                    public string function Describe()
                        yield this.label + " of area " + this.Area();
                    end function
                end model

                model Square extends Shape
                    real side;

                    public function Square(real side)
                        base("square");
                        this.side = side;
                    end function

                    public override real function Area()
                        yield this.side * this.side;
                    end function
                end model

                global model Program
                    function Main()
                        Shape s = new Square(3.0);
                        Console.WriteLine(s.Describe());
                    end function
                end model
                """, "<test>"),
            diagnostics);

        SemanticModel model = Resolver.Resolve(unit, diagnostics, requireEntryPoint: true);
        TypeChecker.Check(unit, model, diagnostics);
        DefiniteAssignment.Analyze(unit, model, diagnostics);

        Assert.That(diagnostics.Select(d => d.Id), Is.Empty, "the program should check cleanly");

        StringWriter output = new();
        ProfiC.Interpreter.Interpreter.Run(
            Lowering.Lower(unit, model), model, output, TextReader.Null);

        Assert.That(output.ToString().Trim(), Is.EqualTo("square of area 9"));
    }
}
