using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Semantics;
using ProfiC.Compiler.Text;

namespace ProfiC.Tests.Semantics;

/// <summary>
/// <para>What a function value reaches for outside itself.</para>
/// <para>Every case here is stated as the names captured, in order, because that list is what
/// closure conversion turns into fields — a name missing from it is a name the converted
/// program cannot read, and a name wrongly in it is a field nothing ever fills.</para>
/// </summary>
[TestFixture]
public sealed class CaptureAnalysisTests : LexerTestBase
{
    /// <summary>
    /// The captures of every function value in <c>Main</c>, innermost values included, keyed by
    /// the order the values appear in the source.
    /// </summary>
    private static IReadOnlyList<CaptureSet> CapturesIn(string body)
    {
        DiagnosticBag diagnostics = new();
        CompilationUnit unit = Parser.Parse(
            new SourceText(
                $$"""
                shared model Program
                    function Main()
                {{body}}
                    end function
                end model
                """,
                "<test>"),
            diagnostics);

        SemanticModel model = Resolver.Resolve(unit, diagnostics);
        TypeChecker.Check(unit, model, diagnostics);

        Assert.That(diagnostics.Select(d => d.Message), Is.Empty, "the snippet should check cleanly");

        FunctionDecl main = unit.Descendants()
            .OfType<FunctionDecl>()
            .First(f => string.Equals(f.Name, "Main", StringComparison.Ordinal));

        IReadOnlyDictionary<SyntaxNode, CaptureSet> captures =
            CaptureAnalysis.Analyze(main, model);

        // Source order, so a test can say "the first lambda" and mean the first one written.
        return
        [
            .. main.Descendants()
                   .Where(captures.ContainsKey)
                   .Select(node => captures[node]),
        ];
    }

    private static IReadOnlyList<string> NamesOf(CaptureSet captures) =>
        [.. captures.Names.Select(symbol => symbol.Name)];

    // ---- Nothing to capture ------------------------------------------------------------------

    [Test]
    public void ALambdaUsingOnlyItsParametersCapturesNothing()
    {
        IReadOnlyList<CaptureSet> captures = CapturesIn(
            "        integer delegate(integer) twice = (n) yield n * 2;");

        Assert.That(captures, Has.Count.EqualTo(1));
        Assert.That(captures[0].IsEmpty, Is.True, "nothing outside was named");
    }

    /// <summary>
    /// A shared member is reached through its type's name rather than as a bare name, so
    /// naming one is not a capture. This is the rule that keeps most lambdas capturing nothing.
    /// </summary>
    [Test]
    public void ALambdaNamingASharedMemberCapturesNothing()
    {
        IReadOnlyList<CaptureSet> captures = CapturesIn(
            """
                    delegate() announce = () yield Console.WriteLine("hello");
            """);

        Assert.That(captures[0].IsEmpty, Is.True);
    }

    // ---- One level out -----------------------------------------------------------------------

    [Test]
    public void ALambdaNamingALocalCapturesIt()
    {
        IReadOnlyList<CaptureSet> captures = CapturesIn(
            """
                    integer total = 1;
                    integer delegate() read = () yield total;
            """);

        Assert.That(NamesOf(captures[0]), Is.EqualTo(new[] { "total" }));
    }

    [Test]
    public void ALambdaNamingAParameterOfTheMemberCapturesIt()
    {
        DiagnosticBag diagnostics = new();
        CompilationUnit unit = Parser.Parse(
            new SourceText(
                """
                shared model Program
                    integer delegate() function AdderOf(integer by)
                        yield () yield by;
                    end function

                    function Main()
                    end function
                end model
                """,
                "<test>"),
            diagnostics);

        SemanticModel model = Resolver.Resolve(unit, diagnostics);
        TypeChecker.Check(unit, model, diagnostics);

        Assert.That(diagnostics.Select(d => d.Message), Is.Empty);

        FunctionDecl adder = unit.Descendants()
            .OfType<FunctionDecl>()
            .First(f => string.Equals(f.Name, "AdderOf", StringComparison.Ordinal));

        CaptureSet captured = CaptureAnalysis.Analyze(adder, model).Values.Single();

        Assert.That(NamesOf(captured), Is.EqualTo(new[] { "by" }));
    }

    /// <summary>
    /// The same name twice is one capture. Fields are made from this list, and a name listed
    /// twice would declare the field twice.
    /// </summary>
    [Test]
    public void ANameReadTwiceIsCapturedOnce()
    {
        IReadOnlyList<CaptureSet> captures = CapturesIn(
            """
                    integer total = 1;
                    integer delegate() twice = () yield total + total;
            """);

        Assert.That(NamesOf(captures[0]), Is.EqualTo(new[] { "total" }));
    }

    [Test]
    public void CapturesAreOrderedByFirstMention()
    {
        IReadOnlyList<CaptureSet> captures = CapturesIn(
            """
                    integer beta = 1;
                    integer alpha = 2;
                    integer delegate() sum = () yield beta + alpha;
            """);

        Assert.That(
            NamesOf(captures[0]),
            Is.EqualTo(new[] { "beta", "alpha" }),
            "order follows the source, not the alphabet, so a built tree is the same every run");
    }

    // ---- More than one level out -------------------------------------------------------------

    /// <summary>
    /// <para>A name reached from two levels in is captured by both, not only the inner one.
    /// </para>
    /// <para>The middle value has to carry it: the inner one can only read from what the value
    /// it sits inside holds, so a middle that captured nothing would leave the inner with
    /// nowhere to look.</para>
    /// </summary>
    [Test]
    public void AnInnerLambdaMakesEveryLambdaBetweenCaptureTheName()
    {
        IReadOnlyList<CaptureSet> captures = CapturesIn(
            """
                    integer total = 1;
                    integer delegate() delegate() outerValue = () yield () yield total;
            """);

        Assert.That(captures, Has.Count.EqualTo(2), "an outer lambda and an inner one");

        Assert.Multiple(() =>
        {
            Assert.That(NamesOf(captures[0]), Is.EqualTo(new[] { "total" }), "the outer carries it");
            Assert.That(NamesOf(captures[1]), Is.EqualTo(new[] { "total" }), "the inner reads it");
        });
    }

    /// <summary>A lambda naming only what it declares itself captures nothing, however deep.</summary>
    [Test]
    public void AnInnerLambdaNamingTheOutersParameterCapturesOnlyThat()
    {
        IReadOnlyList<CaptureSet> captures = CapturesIn(
            """
                    integer delegate() delegate(integer) make = (a) yield () yield a;
            """);

        Assert.Multiple(() =>
        {
            Assert.That(captures[0].IsEmpty, Is.True, "'a' is the outer lambda's own parameter");
            Assert.That(NamesOf(captures[1]), Is.EqualTo(new[] { "a" }));
        });
    }

    // ---- Loop variables ----------------------------------------------------------------------

    /// <summary>
    /// <para>The counter of a range loop is captured like any other name.</para>
    /// <para>What makes <c>samples/looping.pc</c> print 1 2 3 rather than 3 3 3 is that each
    /// turn binds a fresh counter, so conversion has to make a fresh place per turn as well —
    /// this test pins only that the counter is seen, which is what that depends on.</para>
    /// </summary>
    [Test]
    public void ALambdaMadeInALoopCapturesTheCounter()
    {
        IReadOnlyList<CaptureSet> captures = CapturesIn(
            """
                    integer delegate()[] made = {};

                    loop for i = 1 to 3
                        made.Insert(() yield i);
                    end loop
            """);

        Assert.That(NamesOf(captures[0]), Is.EqualTo(new[] { "i" }));
    }

    [Test]
    public void ALambdaInsideACatchCapturesTheCaughtException()
    {
        IReadOnlyList<CaptureSet> captures = CapturesIn(
            """
                    delegate() report = () yield Console.WriteLine("none");

                    try
                        throw new Exception("x");
                    catch Exception problem
                        report = () yield Console.WriteLine(problem.Message());
                    end try
            """);

        Assert.That(captures, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(captures[0].IsEmpty, Is.True);
            Assert.That(NamesOf(captures[1]), Is.EqualTo(new[] { "problem" }));
        });
    }

    // ---- Local functions ---------------------------------------------------------------------

    /// <summary>
    /// A function declared among statements captures exactly as a lambda does. It is the same
    /// question — a body naming what surrounds it — and the answer cannot differ by which of
    /// the two spellings was used.
    /// </summary>
    [Test]
    public void ALocalFunctionCapturesWhatItNames()
    {
        IReadOnlyList<CaptureSet> captures = CapturesIn(
            """
                    integer total = 7;

                    integer function Doubled()
                        yield total * 2;
                    end function

                    Console.WriteLine(Doubled());
            """);

        Assert.That(NamesOf(captures[0]), Is.EqualTo(new[] { "total" }));
    }

    [Test]
    public void ALocalFunctionNamingOnlyItsOwnCapturesNothing()
    {
        IReadOnlyList<CaptureSet> captures = CapturesIn(
            """
                    integer function Doubled(integer n)
                        integer scaled = n * 2;
                        yield scaled;
                    end function

                    Console.WriteLine(Doubled(4));
            """);

        Assert.That(captures[0].IsEmpty, Is.True);
    }

    // ---- The receiver ------------------------------------------------------------------------

    /// <summary>
    /// <para><c>this</c> is carried too, and is recorded apart from the names.</para>
    /// <para>There is no symbol to move into a field: what the value needs is the instance the
    /// member was called on, so it is a fact about the function value rather than an entry in
    /// a list of variables.</para>
    /// </summary>
    [Test]
    public void ALambdaNamingThisCapturesTheReceiver()
    {
        DiagnosticBag diagnostics = new();
        CompilationUnit unit = Parser.Parse(
            new SourceText(
                """
                model Counter
                    integer count;

                    public function Counter()
                        this.count = 0;
                    end function

                    public integer delegate() function Reader()
                        yield () yield this.count;
                    end function
                end model

                shared model Program
                    function Main()
                    end function
                end model
                """,
                "<test>"),
            diagnostics);

        SemanticModel model = Resolver.Resolve(unit, diagnostics);
        TypeChecker.Check(unit, model, diagnostics);

        Assert.That(diagnostics.Select(d => d.Message), Is.Empty);

        FunctionDecl reader = unit.Descendants()
            .OfType<FunctionDecl>()
            .First(f => string.Equals(f.Name, "Reader", StringComparison.Ordinal));

        CaptureSet captured = CaptureAnalysis.Analyze(reader, model).Values.Single();

        Assert.Multiple(() =>
        {
            Assert.That(captured.CapturesReceiver, Is.True);
            Assert.That(captured.Names, Is.Empty, "a field is reached through the receiver");
            Assert.That(captured.IsEmpty, Is.False);
        });
    }

    /// <summary>
    /// <para><c>base</c> is recorded apart from <c>this</c>.</para>
    /// <para>Both need the receiver carried, but <c>base.Speak()</c> also means the parent's
    /// version whatever the instance turns out to be — so the call has to travel too, and
    /// closure conversion has to be able to tell which of the two it is looking at.</para>
    /// </summary>
    [Test]
    public void ALambdaNamingBaseIsRecordedApartFromOneNamingThis()
    {
        DiagnosticBag diagnostics = new();
        CompilationUnit unit = Parser.Parse(
            new SourceText(
                """
                model Animal
                    public virtual string function Speak()
                        yield "...";
                    end function
                end model

                model Dog extends Animal
                    public string delegate() function Quieter()
                        yield () yield base.Speak();
                    end function
                end model

                shared model Program
                    function Main()
                    end function
                end model
                """,
                "<test>"),
            diagnostics);

        SemanticModel model = Resolver.Resolve(unit, diagnostics);
        TypeChecker.Check(unit, model, diagnostics);

        Assert.That(diagnostics.Select(d => d.Message), Is.Empty);

        FunctionDecl quieter = unit.Descendants()
            .OfType<FunctionDecl>()
            .First(f => string.Equals(f.Name, "Quieter", StringComparison.Ordinal));

        CaptureSet captured = CaptureAnalysis.Analyze(quieter, model).Values.Single();

        Assert.Multiple(() =>
        {
            Assert.That(captured.CapturesReceiver, Is.True);
            Assert.That(captured.CapturesBase, Is.True);
        });
    }

    [Test]
    public void ALambdaNamingOnlyThisDoesNotCaptureBase()
    {
        DiagnosticBag diagnostics = new();
        CompilationUnit unit = Parser.Parse(
            new SourceText(
                """
                model Counter
                    integer count;

                    public function Counter()
                        this.count = 0;
                    end function

                    public integer delegate() function Reader()
                        yield () yield this.count;
                    end function
                end model

                shared model Program
                    function Main()
                    end function
                end model
                """,
                "<test>"),
            diagnostics);

        SemanticModel model = Resolver.Resolve(unit, diagnostics);
        TypeChecker.Check(unit, model, diagnostics);

        Assert.That(diagnostics.Select(d => d.Message), Is.Empty);

        FunctionDecl reader = unit.Descendants()
            .OfType<FunctionDecl>()
            .First(f => string.Equals(f.Name, "Reader", StringComparison.Ordinal));

        CaptureSet captured = CaptureAnalysis.Analyze(reader, model).Values.Single();

        Assert.Multiple(() =>
        {
            Assert.That(captured.CapturesReceiver, Is.True);
            Assert.That(captured.CapturesBase, Is.False);
        });
    }

    // ---- Over the corpus ---------------------------------------------------------------------

    /// <summary>
    /// <para>Every function value in every sample is answered for, and nothing throws.</para>
    /// <para>The analysis walks shapes the targeted cases above do not reach — a lambda in a
    /// collection literal, one inside a switch, one handed straight to a call — and the corpus
    /// is where those live.</para>
    /// </summary>
    [TestCaseSource(nameof(SampleNames))]
    public void EveryFunctionValueInTheCorpusIsAnswered(string name)
    {
        DiagnosticBag diagnostics = new();
        CompilationUnit unit = Parser.Parse(LoadSample(name), diagnostics);
        SemanticModel model = Resolver.Resolve(unit, diagnostics);
        TypeChecker.Check(unit, model, diagnostics);

        Assert.That(diagnostics.Errors, Is.Zero, "the sample should check cleanly");

        foreach (FunctionDecl member in unit.Descendants().OfType<FunctionDecl>())
        {
            IReadOnlyDictionary<SyntaxNode, CaptureSet> captures =
                CaptureAnalysis.Analyze(member, model);

            foreach (SyntaxNode value in member.Descendants()
                                               .Where(n => n is LambdaExpr or FunctionDecl))
            {
                if (ReferenceEquals(value, member))
                {
                    continue;
                }

                Assert.That(
                    captures.ContainsKey(value),
                    Is.True,
                    $"a function value in {name} was not answered for");
            }
        }
    }
}
