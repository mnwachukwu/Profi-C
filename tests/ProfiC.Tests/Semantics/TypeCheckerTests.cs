using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Semantics;
using ProfiC.Compiler.Text;

namespace ProfiC.Tests.Semantics;

/// <summary>Type checking: what fits where, and what has to be written out.</summary>
[TestFixture]
public sealed class TypeCheckerTests
{
    private static DiagnosticBag Check(string source)
    {
        DiagnosticBag diagnostics = new();
        CompilationUnit unit = Parser.Parse(new SourceText(source, "<test>"), diagnostics);
        SemanticModel model = Resolver.Resolve(unit, diagnostics);
        TypeChecker.Check(unit, model, diagnostics);
        return diagnostics;
    }

    private static string[] IdsOf(DiagnosticBag bag) => [.. bag.Sorted().Select(d => d.Id)];

    /// <summary>Wraps statements in a program, which is the shape most cases need.</summary>
    private static DiagnosticBag CheckBody(string body) =>
        Check($$"""
            shared model Program
                function Main()
            {{body}}
                end function
            end model
            """);

    // ---- Conversions --------------------------------------------------------------------------

    [Test]
    public void WideningAnIntegerHappensOnItsOwn()
    {
        Assert.That(IdsOf(CheckBody(
            """
                    real r = 1;
                    fraction f = 2;
            """)), Is.Empty);
    }

    /// <summary>
    /// <para>Between a fraction and a real, one direction converts on its own and one does not,
    /// and exactness is what decides which.</para>
    /// <para>A real counts in tens, so it already is a fraction over a power of ten and widens
    /// unasked. A fraction going the other way is where exactness stops — a third has no decimal
    /// form that ends — so that one is written out.</para>
    /// </summary>
    [Test]
    public void ARealWidensToAFractionButNotTheOtherWayAround()
    {
        DiagnosticBag toReal = CheckBody("        real r = 1|2;");
        DiagnosticBag toFraction = CheckBody("        fraction f = 0.5;");

        Assert.Multiple(() =>
        {
            Assert.That(IdsOf(toReal), Is.EqualTo(new[] { "PC0301" }));
            Assert.That(toReal.Single().Message, Does.Contain("ToReal()"));

            Assert.That(IdsOf(toFraction), Is.Empty);
        });
    }

    /// <summary>
    /// A real too wide to be held as a fraction is refused where it is written, rather than
    /// left to fail when it runs. <c>PC0324</c> draws the same line around dividing by zero.
    /// </summary>
    [Test]
    public void ARealTooWideToBeAFractionIsRefusedWhereItIsWritten()
    {
        Assert.That(
            IdsOf(CheckBody("        fraction tiny = 0.0000000000000000001;")),
            Is.EqualTo(new[] { "PC0346" }));
    }

    [Test]
    public void TheExplicitConversionsWork()
    {
        // The two that lose nothing and are written out anyway, because each answer is
        // surprising: a third has no decimal form that ends, and what a float holds for a
        // tenth is not a tenth. A real needs no such member — it widens on its own.
        Assert.That(IdsOf(CheckBody(
            """
                    let r = (1|2).ToReal();
                    let f = (0.1f).ToFraction();
            """)), Is.Empty);
    }

    [Test]
    public void UnrelatedTypesDoNotConvert()
    {
        Assert.That(IdsOf(CheckBody("        integer x = \"text\";")),
                    Is.EqualTo(new[] { "PC0300" }));
    }

    [Test]
    public void AValueMayBeWrappedIntoAnOptional()
    {
        Assert.That(IdsOf(CheckBody("        integer? maybe = 1;")), Is.Empty);
    }

    /// <summary>
    /// Reading an optional as a plain value is never automatic. The message names the three
    /// members that do it, since that is the whole fix.
    /// </summary>
    [Test]
    public void AnOptionalMustBeUnwrappedAndTheMessageSaysHow()
    {
        DiagnosticBag diagnostics = CheckBody(
            """
                    integer? maybe;
                    integer definite = maybe;
            """);

        Assert.Multiple(() =>
        {
            Assert.That(IdsOf(diagnostics), Is.EqualTo(new[] { "PC0329" }));
            Assert.That(diagnostics.Single().Message, Does.Contain("HasValue()"));
            Assert.That(diagnostics.Single().Message, Does.Contain("Or("));
            Assert.That(diagnostics.Single().Message, Does.Contain("Value()"));
        });
    }

    [Test]
    public void AStringAndASetOfCharactersConvertBothWays()
    {
        Assert.That(IdsOf(CheckBody(
            """
                    character[] letters = "abc";
                    string rebuilt = letters;
            """)), Is.Empty);
    }

    /// <summary>
    /// <para>An optional reaches another optional wherever the values would.</para>
    /// <para>Absence is carried across rather than looked inside, so this settles what to do
    /// with a <c>string?</c> where a <c>character[]?</c> is wanted without softening anything:
    /// what comes out is still an optional.</para>
    /// </summary>
    [TestCase("        string? word = \"abc\";\n        character[]? letters = word;")]
    [TestCase("        character[]? letters = {'a'};\n        string? word = letters;")]
    [TestCase("        integer? count = 1;\n        real? measured = count;")]
    public void AnOptionalConvertsWhereItsValueWould(string body) =>
        Assert.That(IdsOf(CheckBody(body)), Is.Empty);

    /// <summary>The rule follows inheritance too, since that is a conversion like any other.</summary>
    [Test]
    public void AnOptionalChildReachesAnOptionalParent() =>
        Assert.That(IdsOf(Check("""
            model Shape
            end model

            model Square extends Shape
            end model

            shared model Program
                function Main()
                    Square? square = new Square();
                    Shape? shape = square;
                    Console.WriteLine(shape.HasValue());
                end function
            end model
            """)), Is.Empty);

    /// <summary>
    /// <para>And it softens nothing. Reaching a plain value still means proving there is one,
    /// whatever the two types would do to each other.</para>
    /// <para>This is the pairing worth pinning: the rule above says an optional travels, and
    /// this says it never arrives unwrapped.</para>
    /// </summary>
    [TestCase("        string? word = \"abc\";\n        character[] letters = word;")]
    [TestCase("        string? word = \"abc\";\n        string plain = word;")]
    [TestCase("        integer? count = 1;\n        real measured = count;")]
    public void AnOptionalStillDoesNotReachAPlainValue(string body) =>
        Assert.That(IdsOf(CheckBody(body)), Does.Contain("PC0329"));

    [Test]
    public void AModelConvertsToItsAncestors()
    {
        Assert.That(IdsOf(Check(
            """
            model Shape
            end model

            model Square extends Shape
            end model

            shared model Program
                function Main()
                    Shape s = new Square();
                end function
            end model
            """)), Is.Empty);
    }

    [Test]
    public void AnAncestorDoesNotConvertToADescendant()
    {
        Assert.That(IdsOf(Check(
            """
            model Shape
            end model

            model Square extends Shape
            end model

            shared model Program
                function Main()
                    Square q = new Shape();
                end function
            end model
            """)), Is.EqualTo(new[] { "PC0300" }));
    }

    /// <summary>
    /// Boxing is refused rather than deferred, so this stays an error in every version.
    /// </summary>
    [TestCase("        Model m = 1;", TestName = "a number")]
    [TestCase("        Model m = 'c';", TestName = "a character")]
    [TestCase("        Model m = true;", TestName = "a boolean")]
    public void AValueTypeNeverConvertsToModel(string body) =>
        Assert.That(IdsOf(CheckBody(body)), Is.EqualTo(new[] { "PC0300" }));

    [Test]
    public void AStructureNeverConvertsToModel() => Assert.That(
        IdsOf(Check("""
            structure Point
                public integer X;
            end structure

            enumeration Color
                Red
            end enumeration

            shared model Program
                function Main()
                    Model a = new Point();
                    Model b = Color.Red;
                end function
            end model
            """)),
        Is.EqualTo(new[] { "PC0300", "PC0300" }));

    /// <summary>
    /// <para>Every model reaches Model, whether or not it wrote the word.</para>
    /// <para>Both spellings, because a model that says <c>extends Model</c> and one that says
    /// nothing are the same model, and a rule that held for one and not the other would make
    /// writing the implicit thing out change what a program means.</para>
    /// </summary>
    [TestCase("model Thing extends Model\nend model", TestName = "written out")]
    [TestCase("model Thing\nend model", TestName = "left implicit")]
    public void EveryModelConvertsToModel(string declaration) => Assert.That(
        IdsOf(Check($$"""
            {{declaration}}

            shared model Program
                function Main()
                    Model held = new Thing();
                    Console.WriteLine(Program.Describe(new Thing()));
                    Console.WriteLine(Reference.Equals(held, held));
                end function

                string function Describe(Model m)
                    yield "got " + m;
                end function
            end model
            """)),
        Is.Empty);

    // ---- Operators -----------------------------------------------------------------------------

    [Test]
    public void ArithmeticNeedsNumbers()
    {
        Assert.That(IdsOf(CheckBody("        let x = true * 2;")),
                    Is.EqualTo(new[] { "PC0303" }));
    }

    [Test]
    public void MixingAFractionAndARealGivesAFraction()
    {
        // The real widens, so the pair has a common type and it is the exact one: a half and
        // a half really is one, and going through a real would only invite the rounding the
        // fraction was there to avoid.
        Assert.That(IdsOf(CheckBody("        let x = 1|2 + 0.5;")), Is.Empty);
    }

    [Test]
    public void AddingToAStringJoinsItWhicheverSideTheStringIsOn()
    {
        Assert.That(IdsOf(CheckBody(
            """
                    string a = "score: " + 42;
                    string b = 42 + " points";
            """)), Is.Empty);
    }

    [Test]
    public void LogicalOperatorsNeedBooleans()
    {
        Assert.That(IdsOf(CheckBody("        let x = 1 and 2;")),
                    Is.EqualTo(new[] { "PC0302", "PC0302" }));
    }

    [Test]
    public void AConditionMustBeABoolean()
    {
        Assert.That(IdsOf(CheckBody(
            """
                    if 1
                        yield;
                    end if
            """)), Is.EqualTo(new[] { "PC0302" }));
    }

    [Test]
    public void DividingByAnObviousZeroIsCaughtWhileCompiling()
    {
        Assert.Multiple(() =>
        {
            Assert.That(IdsOf(CheckBody("        let x = 1 / 0;")),
                        Is.EqualTo(new[] { "PC0324" }));
            Assert.That(IdsOf(CheckBody("        let x = 1 % 0;")),
                        Is.EqualTo(new[] { "PC0324" }));
        });
    }

    // ---- The conditional expression --------------------------------------------------------------

    [Test]
    public void ConditionalBranchesMustAgreeExactly()
    {
        // Finding a common type would make this a real, which is what neither branch says.
        DiagnosticBag diagnostics = CheckBody("        let x = if true then 1 else 2.5;");

        Assert.That(IdsOf(diagnostics), Is.EqualTo(new[] { "PC0305" }));
    }

    [Test]
    public void MatchingBranchesGiveTheirOwnType()
    {
        Assert.That(IdsOf(CheckBody("        string label = if true then \"a\" else \"b\";")),
                    Is.Empty);
    }

    // ---- Sets and indexing ---------------------------------------------------------------------

    [Test]
    public void SetElementsMustAgree()
    {
        Assert.That(IdsOf(CheckBody("        let mixed = {1, \"two\"};")),
                    Is.EqualTo(new[] { "PC0314" }));
    }

    [Test]
    public void AnEmptySetNeedsItsTypeWritten()
    {
        Assert.Multiple(() =>
        {
            Assert.That(IdsOf(CheckBody("        let nothing = {};")),
                        Is.EqualTo(new[] { "PC0313" }));
            Assert.That(IdsOf(CheckBody("        integer[] empty = {};")), Is.Empty);
        });
    }

    [Test]
    public void IndexingNeedsASetOrAStringAndAnIntegerIndex()
    {
        Assert.Multiple(() =>
        {
            Assert.That(IdsOf(CheckBody(
                """
                        integer[] numbers = {1, 2};
                        let first = numbers[0];
                        let letter = "abc"[1];
                """)), Is.Empty);

            Assert.That(IdsOf(CheckBody("        let bad = 5[0];")),
                        Is.EqualTo(new[] { "PC0311" }));

            Assert.That(IdsOf(CheckBody(
                """
                        integer[] numbers = {1};
                        let bad = numbers["x"];
                """)), Is.EqualTo(new[] { "PC0312" }));
        });
    }

    [Test]
    public void SetMembersAreAvailableWithoutBeingDeclared()
    {
        Assert.That(IdsOf(CheckBody(
            """
                    integer[] numbers = {1, 2};
                    let count = numbers.Count;
                    let has = numbers.Contains(1);
                    let removed = numbers.Remove(1);
                    numbers.Insert(3);
                    numbers.Clear();
            """)), Is.Empty);
    }

    [Test]
    public void AStringReportsItsLengthWithCountJustAsASetDoes()
    {
        Assert.That(IdsOf(CheckBody("        let n = \"abc\".Count;")), Is.Empty);
    }

    // ---- Optionals -------------------------------------------------------------------------------

    [Test]
    public void TheThreeOptionalMembersWork()
    {
        Assert.That(IdsOf(CheckBody(
            """
                    integer? maybe;
                    let present = maybe.HasValue();
                    let fallback = maybe.Or(0);
                    let insisted = maybe.Value();
            """)), Is.Empty);
    }

    [Test]
    public void OrChainsAndOnlyAPlainFallbackEndsTheChain()
    {
        Assert.That(IdsOf(CheckBody(
            """
                    integer? a;
                    integer? b;
                    integer definite = a.Or(b).Or(3);
            """)), Is.Empty);
    }

    // ---- Narrowing, and where it stops ----------------------------------------------------------

    /// <summary>
    /// What the analysis is for. Everything below says where it stops, and none of that is worth
    /// anything if it stopped everywhere.
    /// </summary>
    [Test]
    public void AGuardNarrowsWhatItProved()
    {
        Assert.That(IdsOf(CheckBody(
            """
                    integer? maybe;
                    if maybe.HasValue()
                        integer definite = maybe;
                        Console.WriteLine(definite);
                    end if
            """)), Is.Empty);
    }

    /// <summary>
    /// <para>A value stored one way through is not there the other way.</para>
    /// <para>Every form of block that may be skipped or repeated, because the join is a
    /// different one each time and getting it right for an <c>if</c> says nothing about a
    /// loop. The last two are the ones a body running at least once does not save: a
    /// <c>break</c> leaves from wherever it is written, which for narrowing is the same as
    /// the body never having finished.</para>
    /// </summary>
    [TestCase("if 1 == 2", "", "end if")]
    [TestCase("loop while 1 == 2", "", "end loop")]
    [TestCase("loop for i = 1 to 3", "", "end loop")]
    [TestCase("loop each item in xs", "", "end loop")]
    [TestCase("loop", "", "until 1 == 2")]
    [TestCase("loop", "break;", "end loop")]
    public void AStoreOnOneWayThroughIsNotKnownAfterTheJoin(
        string opens,
        string alsoDoes,
        string closes)
    {
        Assert.That(IdsOf(CheckBody(
            $$"""
                    integer[] xs = {1, 2};
                    integer? n;
                    {{opens}}
                        n = 5;
                        {{alsoDoes}}
                    {{closes}}
                    integer definite = n;
            """)), Is.EqualTo(new[] { "PC0329" }));
    }

    /// <summary>The other half of it: where every arm stores one, it is there afterwards.</summary>
    [Test]
    public void AStoreEveryArmMakesIsKnownAfterThem()
    {
        Assert.That(IdsOf(CheckBody(
            """
                    integer? n;
                    if 1 == 2
                        n = 5;
                    else
                        n = 7;
                    end if
                    integer definite = n;
            """)), Is.Empty);
    }

    /// <summary>
    /// <para>A turn after the first begins where the one before it ended, so what held at the
    /// top of the loop the first time round need not hold the second.</para>
    /// <para>This is the case a block checked once from the state it was reached in gets
    /// wrong, and it reads as correct until the loop runs twice.</para>
    /// </summary>
    [Test]
    public void WhatALoopStoresIsNotKnownAtTheTopOfTheNextTurn()
    {
        Assert.That(IdsOf(CheckBody(
            """
                    integer? maybe;
                    integer? n;
                    n = 5;
                    loop while 1 == 2
                        integer definite = n;
                        Console.WriteLine(definite);
                        n = maybe;
                    end loop
            """)), Is.EqualTo(new[] { "PC0329" }));
    }

    /// <summary>
    /// <para>A guard written as an early exit narrows everything after it, since the only way to
    /// still be standing there is the case that did not leave.</para>
    /// <para>Both ways out of a function, because they are separate statements and only one of
    /// them looks like leaving.</para>
    /// </summary>
    [TestCase("yield;")]
    [TestCase("throw new Exception(\"none\");")]
    public void AnEarlyExitNarrowsEverythingAfterIt(string leaves)
    {
        Assert.That(IdsOf(CheckBody(
            $$"""
                    integer? n;
                    if not n.HasValue()
                        {{leaves}}
                    end if
                    integer definite = n;
                    Console.WriteLine(definite);
            """)), Is.Empty);
    }

    /// <summary>The same, for the two ways out of a turn of a loop rather than out of a function.</summary>
    [TestCase("break;")]
    [TestCase("continue;")]
    public void LeavingATurnEarlyNarrowsTheRestOfIt(string leaves)
    {
        Assert.That(IdsOf(CheckBody(
            $$"""
                    integer? n;
                    loop while 1 == 2
                        if not n.HasValue()
                            {{leaves}}
                        end if
                        integer definite = n;
                        Console.WriteLine(definite);
                    end loop
            """)), Is.Empty);
    }

    /// <summary>
    /// It is not only guards. An arm that leaves cannot disagree with one that stored a value,
    /// so what the arm that stayed knows is what holds afterwards.
    /// </summary>
    [Test]
    public void AnArmThatLeavesLetsTheOtherOnesStoreThrough()
    {
        Assert.That(IdsOf(CheckBody(
            """
                    integer? n;
                    if 1 == 2
                        n = 5;
                    else
                        yield;
                    end if
                    integer definite = n;
                    Console.WriteLine(definite);
            """)), Is.Empty);
    }

    /// <summary>
    /// Leaving on some turns is not leaving. An arm that may fall out of the bottom arrives like
    /// any other, and what it knew has to agree with the rest.
    /// </summary>
    [Test]
    public void AnArmThatMayFallThroughNarrowsNothingAfterIt()
    {
        Assert.That(IdsOf(CheckBody(
            """
                    integer? n;
                    if not n.HasValue()
                        if 1 == 2
                            yield;
                        end if
                    end if
                    integer definite = n;
            """)), Is.EqualTo(new[] { "PC0329" }));
    }

    /// <summary>
    /// <para>A local a closure assigns is never narrowed, however plainly it was proved.</para>
    /// <para>The closure holds the name and may be called at any point, so a proof made before a
    /// call says nothing about after it — the same reasoning that keeps a field out of the
    /// analysis, arrived at from the other side.</para>
    /// </summary>
    [Test]
    public void AStoreIsNotKnownWhereAClosureCanUndoIt()
    {
        Assert.That(IdsOf(Check(
            """
            shared model Program
                function Main()
                    integer? n;
                    n = 5;

                    delegate() clear = function()
                        n = Program.Nothing();
                    end function;

                    clear();

                    integer definite = n;
                    Console.WriteLine(definite);
                end function

                integer? function Nothing()
                    integer? empty;
                    yield empty;
                end function
            end model
            """)), Is.EqualTo(new[] { "PC0345" }));
    }

    /// <summary>
    /// <para>And a guard does not rescue it, which is why the message is its own rather than the
    /// usual advice to write one.</para>
    /// <para>Without this the reader is sent in a circle: told to check, they check, and are told
    /// to check.</para>
    /// </summary>
    [Test]
    public void CheckingOneAClosureCanUndoIsSaidToBeNoHelp()
    {
        DiagnosticBag reported = Check(
            """
            shared model Program
                function Main()
                    integer? n;
                    n = 5;

                    delegate() clear = function()
                        n = Program.Nothing();
                    end function;

                    clear();

                    if n.HasValue()
                        integer definite = n;
                        Console.WriteLine(definite);
                    end if
                end function

                integer? function Nothing()
                    integer? empty;
                    yield empty;
                end function
            end model
            """);

        Assert.Multiple(() =>
        {
            Assert.That(IdsOf(reported), Is.EqualTo(new[] { "PC0345" }));
            Assert.That(reported.Single().Message, Does.Contain("Copy it into a local"));
        });
    }

    /// <summary>
    /// The way out the message names has to work, or naming it is worse than saying nothing. A
    /// local nothing else holds sits still, and narrows as any other does.
    /// </summary>
    [Test]
    public void ACopyNothingElseHoldsNarrowsAsUsual()
    {
        Assert.That(IdsOf(Check(
            """
            shared model Program
                function Main()
                    integer? n;
                    n = 5;

                    delegate() clear = function()
                        n = Program.Nothing();
                    end function;

                    clear();

                    integer? held = n;

                    if held.HasValue()
                        integer definite = held;
                        Console.WriteLine(definite);
                    end if
                end function

                integer? function Nothing()
                    integer? empty;
                    yield empty;
                end function
            end model
            """)), Is.Empty);
    }

    /// <summary>
    /// A closure that only reads the name takes nothing away. What matters is assignment, not
    /// capture — otherwise every optional mentioned in a lambda would stop narrowing.
    /// </summary>
    [Test]
    public void AClosureThatOnlyReadsLeavesNarrowingAlone()
    {
        Assert.That(IdsOf(Check(
            """
            shared model Program
                function Main()
                    integer? n;
                    n = 5;

                    delegate() show = function()
                        Console.WriteLine(n.Or(0));
                    end function;

                    show();

                    integer definite = n;
                    Console.WriteLine(definite);
                end function
            end model
            """)), Is.Empty);
    }

    /// <summary>An exception leaves the body from anywhere in it, the store included.</summary>
    [Test]
    public void ACatchDoesNotTrustWhatTheBodyStored()
    {
        Assert.That(IdsOf(CheckBody(
            """
                    integer? n;
                    try
                        n = 5;
                        Console.WriteLine("working");
                    catch Exception problem
                        integer definite = n;
                        Console.WriteLine(definite);
                    end try
            """)), Is.EqualTo(new[] { "PC0329" }));
    }

    /// <summary>
    /// A lambda is written in one place and called in another, so what was proven where it was
    /// written says nothing about where it runs.
    /// </summary>
    [Test]
    public void NothingProvenOutsideALambdaIsKnownInsideIt()
    {
        Assert.That(IdsOf(CheckBody(
            """
                    integer? n;
                    n = 5;
                    integer delegate() read = function()
                        integer definite = n;
                        yield definite;
                    end function;
            """)), Is.EqualTo(new[] { "PC0329" }));
    }

    // ---- Switch ------------------------------------------------------------------------------------

    [TestCase("real", "1.5")]
    [TestCase("fraction", "1|2")]
    public void ASwitchCannotExamineATypeWhereEqualityIsUnreliable(string type, string value)
    {
        DiagnosticBag diagnostics = CheckBody(
            $"""
                    {type} subject = {value};
                    switch subject
                        default:
                            yield;
                    end switch
            """);

        Assert.That(IdsOf(diagnostics), Does.Contain("PC0315"));
    }

    [Test]
    public void ASwitchOnAnIntegerIsFine()
    {
        Assert.That(IdsOf(CheckBody(
            """
                    integer code = 1;
                    switch code
                        case 1:
                            yield;
                        case 2:
                            yield;
                        default:
                            yield;
                    end switch
            """)), Is.Empty);
    }

    [Test]
    public void ACaseValueHandledTwiceIsRejected()
    {
        Assert.That(IdsOf(CheckBody(
            """
                    integer code = 1;
                    switch code
                        case 1:
                            yield;
                        case 1:
                            yield;
                    end switch
            """)), Is.EqualTo(new[] { "PC0326" }));
    }

    /// <summary>
    /// <para>An enumeration switch that leaves members out and writes no default is warned
    /// about, naming the ones with no case.</para>
    /// <para>This is what makes adding a member safe: every switch that has to change says so,
    /// at the place it has to change.</para>
    /// </summary>
    [Test]
    public void ASwitchLeavingEnumerationMembersOutIsReported()
    {
        DiagnosticBag diagnostics = CheckSuit("""
                    switch s
                        case Suit.Hearts:
                            yield;
                    end switch
            """);

        Assert.Multiple(() =>
        {
            Assert.That(IdsOf(diagnostics), Is.EqualTo(new[] { "PC0337" }));
            Assert.That(diagnostics.Single().Severity, Is.EqualTo(DiagnosticSeverity.Warning));
            Assert.That(diagnostics.Single().Message, Does.Contain("Clubs and Spades"));
        });
    }

    /// <summary>One left out reads as one, not as a list of one.</summary>
    [Test]
    public void OneMemberLeftOutIsNamedInTheSingular() =>
        Assert.That(
            CheckSuit("""
                    switch s
                        case Suit.Hearts:
                        case Suit.Clubs:
                        case Suit.Spades:
                            yield;
                    end switch
            """).Single().Message,
            Does.Contain("Diamonds has no case"));

    /// <summary>
    /// A default handles the rest, and saying so is the whole point of writing one. Handling
    /// every member reaches the same silence by the other route.
    /// </summary>
    [TestCase("""
                    switch s
                        case Suit.Hearts:
                            yield;
                        default:
                            yield;
                    end switch
            """)]
    [TestCase("""
                    switch s
                        case Suit.Hearts:
                        case Suit.Diamonds:
                        case Suit.Clubs:
                        case Suit.Spades:
                            yield;
                    end switch
            """)]
    public void ASwitchThatCoversEverythingIsSilent(string body) =>
        Assert.That(IdsOf(CheckSuit(body)), Is.Empty);

    /// <summary>
    /// Two members may name one value, and a case for either handles both — so the check
    /// compares what a member carries rather than what it is called.
    /// </summary>
    [Test]
    public void MembersSharingAValueAreHandledTogether() =>
        Assert.That(
            IdsOf(Check("""
                enumeration Level
                    Low = 1,
                    Bottom = 1,
                    High = 2
                end enumeration

                shared model Program
                    function Main()
                        Level level = Level.Low;
                        switch level
                            case Level.Low:
                            case Level.High:
                                Console.WriteLine("known");
                        end switch
                    end function
                end model
                """)),
            Is.Empty);

    /// <summary>
    /// A label that did not land leaves nothing to judge exhaustiveness from, so the switch is
    /// not also reported as incomplete. The label has been named once; a second message about
    /// the switch as a whole would point at the same mistake from further away.
    /// </summary>
    [TestCase("""
                    switch s
                        case "hearts":
                            yield;
                    end switch
            """)]
    [TestCase("""
                    integer notConstant = 1;
                    switch s
                        case notConstant:
                            yield;
                    end switch
            """)]
    public void ALabelThatDidNotLandSuppressesTheExhaustivenessWarning(string body) =>
        Assert.That(IdsOf(CheckSuit(body)), Does.Not.Contain("PC0337"));

    private static DiagnosticBag CheckSuit(string body) => Check($$"""
        enumeration Suit
            Hearts, Diamonds, Clubs, Spades
        end enumeration

        shared model Program
            function Main()
                Suit s = Suit.Hearts;
        {{body}}
            end function
        end model
        """);

    // ---- Loops --------------------------------------------------------------------------------------

    [Test]
    public void ARangeLoopCountsWithIntegers()
    {
        Assert.That(IdsOf(CheckBody(
            """
                    loop for i = 1 to 2.5
                        yield;
                    end loop
            """)), Is.EqualTo(new[] { "PC0317" }));
    }

    [Test]
    public void IteratingWorksOverASetAndOverAString()
    {
        Assert.That(IdsOf(CheckBody(
            """
                    integer[] numbers = {1, 2};
                    loop each n in numbers
                        integer copy = n;
                    end loop
                    loop each letter in "abc"
                        character c = letter;
                    end loop
            """)), Is.Empty);
    }

    [Test]
    public void IteratingSomethingThatIsNotASequenceIsRejected()
    {
        Assert.That(IdsOf(CheckBody(
            """
                    loop each x in 5
                        yield;
                    end loop
            """)), Is.EqualTo(new[] { "PC0316" }));
    }

    // ---- Yield ----------------------------------------------------------------------------------------

    [Test]
    public void YieldMustMatchWhatTheFunctionDeclares()
    {
        Assert.Multiple(() =>
        {
            Assert.That(IdsOf(Check(
                """
                model M
                    integer function F()
                        yield "text";
                    end function
                end model
                """)), Is.EqualTo(new[] { "PC0300" }));

            Assert.That(IdsOf(Check(
                """
                model M
                    function F()
                        yield 1;
                    end function
                end model
                """)), Is.EqualTo(new[] { "PC0318" }));

            Assert.That(IdsOf(Check(
                """
                model M
                    integer function F()
                        yield;
                    end function
                end model
                """)), Is.EqualTo(new[] { "PC0319" }));
        });
    }

    // ---- Constants -------------------------------------------------------------------------------------

    [Test]
    public void AConstantNeedsAValueKnownWhileCompiling()
    {
        Assert.Multiple(() =>
        {
            Assert.That(IdsOf(CheckBody("        constant integer A = 2 + 3;")), Is.Empty);

            Assert.That(IdsOf(CheckBody(
                """
                        integer runtime = 1;
                        constant integer B = runtime;
                """)), Is.EqualTo(new[] { "PC0321" }));
        });
    }

    /// <summary>
    /// A constant is permitted only where an immutable binding really means an unchanging
    /// value, which rules out anything that could change behind it.
    /// </summary>
    [Test]
    public void AConstantSetIsRejectedBecauseTheBindingCouldNotHoldItStill()
    {
        Assert.That(IdsOf(CheckBody("        constant integer[] Numbers = {1, 2};")),
                    Does.Contain("PC0322"));
    }

    [Test]
    public void AConstantModelIsRejectedForTheSameReason()
    {
        Assert.That(IdsOf(Check(
            """
            model Dog
            end model

            shared model Program
                function Main()
                    constant Dog Pet = new Dog();
                end function
            end model
            """)), Does.Contain("PC0322"));
    }

    [Test]
    public void AConstantMustBeGivenAValue()
    {
        Assert.That(IdsOf(CheckBody("        constant integer A;")),
                    Is.EqualTo(new[] { "PC0320" }));
    }

    // ---- Calls -------------------------------------------------------------------------------------------

    [Test]
    public void ArgumentsMustFitTheParameters()
    {
        Assert.That(IdsOf(Check(
            """
            shared model Program
                function Take(integer value)
                end function

                function Main()
                    Program.Take("text");
                end function
            end model
            """)), Is.EqualTo(new[] { "PC0300" }));
    }

    [Test]
    public void TheArgumentCountMustMatch()
    {
        Assert.That(IdsOf(Check(
            """
            shared model Program
                function Take(integer value)
                end function

                function Main()
                    Program.Take(1, 2);
                end function
            end model
            """)), Is.EqualTo(new[] { "PC0308" }));
    }

    [Test]
    public void AnExactMatchWinsAmongOverloads()
    {
        Assert.That(IdsOf(Check(
            """
            shared model Program
                function Take(integer value)
                end function

                function Take(real value)
                end function

                function Main()
                    Program.Take(1);
                    Program.Take(1.5);
                end function
            end model
            """)), Is.Empty);
    }

    /// <summary>
    /// Two versions reachable only by conversion is a tie, and a tie is reported rather than
    /// broken: choosing silently would make which one runs depend on rules nobody remembers.
    /// </summary>
    [Test]
    public void ATieAmongConversionsIsReportedRatherThanBroken()
    {
        Assert.That(IdsOf(Check(
            """
            shared model Program
                function Take(real value)
                end function

                function Take(fraction value)
                end function

                function Main()
                    Program.Take(1);
                end function
            end model
            """)), Is.EqualTo(new[] { "PC0310" }));
    }

    [Test]
    public void CallingSomethingThatIsNotAFunctionIsRejected()
    {
        Assert.That(IdsOf(CheckBody(
            """
                    integer x = 1;
                    x();
            """)), Is.EqualTo(new[] { "PC0307" }));
    }

    [Test]
    public void AMemberThatDoesNotExistIsReported()
    {
        Assert.That(IdsOf(CheckBody(
            """
                    integer[] numbers = {1};
                    numbers.Nonexistent();
            """)), Is.EqualTo(new[] { "PC0306" }));
    }

    [Test]
    public void ABaseConstructorCallChecksItsArguments()
    {
        Assert.That(IdsOf(Check(
            """
            model Shape
                public function Shape(string label)
                end function
            end model

            model Square extends Shape
                public function Square()
                    base("square");
                end function
            end model
            """)), Is.Empty);
    }

    // ---- Instantiation ---------------------------------------------------------------------------------------

    [Test]
    public void AnAbstractModelCannotBeInstantiated()
    {
        Assert.That(IdsOf(Check(
            """
            abstract model Shape
            end model

            shared model Program
                function Main()
                    let s = new Shape();
                end function
            end model
            """)), Is.EqualTo(new[] { "PC0328" }));
    }

    [Test]
    public void ASharedModelCannotBeInstantiated()
    {
        Assert.That(IdsOf(Check(
            """
            shared model Utility
            end model

            shared model Program
                function Main()
                    let u = new Utility();
                end function
            end model
            """)), Is.EqualTo(new[] { "PC0328" }));
    }

    // ---- Casts -------------------------------------------------------------------------------------------------

    [Test]
    public void ACastYieldsAnOptional()
    {
        Assert.That(IdsOf(Check(
            """
            model Shape
            end model

            model Square extends Shape
            end model

            shared model Program
                function Main(Shape s)
                    Square? maybe = s as Square;
                    let test = s is Square;
                end function
            end model
            """)), Is.Empty);
    }

    [Test]
    public void TestingUnrelatedTypesIsRejected()
    {
        Assert.That(IdsOf(Check(
            """
            model Dog
            end model

            model Fish
            end model

            shared model Program
                function Main(Dog d)
                    let x = d is Fish;
                end function
            end model
            """)), Is.EqualTo(new[] { "PC0327" }));
    }

    // ---- Recovery ------------------------------------------------------------------------------------------------

    [Test]
    public void AnEarlierErrorDoesNotEchoThroughTheTypeChecker()
    {
        // One mistake, one diagnostic, all the way through.
        Assert.That(IdsOf(CheckBody(
            """
                    let x = nowhere;
                    let y = x + 1;
                    let z = y * 2;
            """)), Is.EqualTo(new[] { "PC0200" }));
    }

    // ---- Condition messages --------------------------------------------------------------------

    /// <summary>
    /// Each caller supplies its whole subject phrase, article included. Worth testing because
    /// message wording is what a reader sees and what nothing else checks.
    /// </summary>
    [TestCase("        if 1\n            yield;\n        end if", "An if condition")]
    [TestCase("        if true\n            yield;\n        else if 1\n            yield;\n        end if",
              "An else-if condition")]
    [TestCase("        loop while 1\n            yield;\n        end loop", "A while condition")]
    [TestCase("        let f = if 1 then 2 else 3;", "An if expression's condition")]
    [TestCase("        let g = 1 and true;", "An operand of 'and' or 'or'")]
    public void AConditionMessageNamesItsSubjectCorrectly(string body, string expected)
    {
        DiagnosticBag diagnostics = CheckBody(body);

        Assert.Multiple(() =>
        {
            Assert.That(IdsOf(diagnostics), Does.Contain("PC0302"));
            Assert.That(diagnostics.First(d => d.Id == "PC0302").Message,
                        Does.StartWith(expected));
        });
    }

    /// <summary>No message may begin with a doubled article.</summary>
    [Test]
    public void NoConditionMessageDoublesItsArticle()
    {
        string[] bodies =
        [
            "        if 1\n            yield;\n        end if",
            "        loop while 1\n            yield;\n        end loop",
            "        let f = if 1 then 2 else 3;",
            "        let g = 1 and true;",
        ];

        foreach (string body in bodies)
        {
            foreach (Diagnostic diagnostic in CheckBody(body))
            {
                Assert.That(diagnostic.Message, Does.Not.StartWith("A a"));
                Assert.That(diagnostic.Message, Does.Not.StartWith("A an"));
            }
        }
    }

    // ---- A call that yields nothing ------------------------------------------------------------

    /// <summary>
    /// Naming the type would describe the types correctly and the mistake badly, so this gets
    /// its own message rather than "cannot use a nothing where an integer is expected".
    /// </summary>
    [TestCase("        let x = Console.WriteLine(\"hi\");")]
    [TestCase("        integer y = Console.WriteLine(\"hi\");")]
    [TestCase("        let z = 1 + Console.WriteLine(\"hi\");")]

    // A call with no result has no members at all. Without this, ToString and Equals were
    // found on it — every type inherits them from Model — so the absence rendered as "empty".
    [TestCase("        let a = Console.WriteLine(\"hi\").ToString();")]
    [TestCase("        let b = Console.WriteLine(\"hi\").Equals(1);")]
    [TestCase("        let c = Console.WriteLine(\"hi\").Count;")]
    public void UsingTheResultOfAFunctionThatYieldsNothingIsRejected(string body) =>
        Assert.That(IdsOf(CheckBody(body)), Is.EqualTo(new[] { "PC0332" }));

    /// <summary>
    /// <para>Nothing else has this type, so it describes itself rather than naming a type no
    /// program can declare.</para>
    /// <para>These are the paths that report the type by name rather than intercepting it, and
    /// each has a different sentence shape, which is why the wording is checked in all of them.
    /// </para>
    /// </summary>
    [TestCase("        let a = Console.WriteLine(\"x\") == 1;")]
    [TestCase("        let b = Console.WriteLine(\"x\") < 1;")]
    [TestCase("        if Console.WriteLine(\"x\")\n            yield;\n        end if")]
    [TestCase("        let d = Console.WriteLine(\"x\")[0];")]
    [TestCase("        let e = not Console.WriteLine(\"x\");")]
    public void AVoidCallDescribesItselfInEveryMessage(string body)
    {
        DiagnosticBag diagnostics = CheckBody(body);

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics, Is.Not.Empty);

            foreach (Diagnostic diagnostic in diagnostics)
            {
                Assert.That(diagnostic.Message, Does.Contain("call that yields nothing"));

                // "nothing" alone named a type nobody can write, and read badly besides.
                Assert.That(diagnostic.Message, Does.Not.Contain("a nothing"));
            }
        });
    }

    /// <summary>A genuine type mismatch still reports as one.</summary>
    [TestCase("        integer x = \"text\";", "PC0300")]
    [TestCase("        let y = true * 2;", "PC0303")]
    public void ARealMismatchIsUnaffected(string body, string expected) =>
        Assert.That(IdsOf(CheckBody(body)), Is.EqualTo(new[] { expected }));

    // ---- Fraction.Create ---------------------------------------------------------------------

    [Test]
    public void CreatingAFractionYieldsAFraction() =>
        Assert.That(IdsOf(CheckBody("        fraction f = Fraction.Create(1, 3);")), Is.Empty);

    [TestCase("        let f = Fraction.Create(1.0, 3);")]
    [TestCase("        let f = Fraction.Create(1, \"three\");")]
    public void CreatingAFractionNeedsTwoIntegers(string body) =>
        Assert.That(IdsOf(CheckBody(body)), Is.EqualTo(new[] { "PC0300" }));

    /// <summary>
    /// A denominator of zero is the same mistake as dividing by zero, so it is reported the
    /// same way and in the same place when the compiler can see it.
    /// </summary>
    [Test]
    public void ALiteralZeroDenominatorIsCaughtWhileCompiling() =>
        Assert.That(IdsOf(CheckBody("        let f = Fraction.Create(1, 0);")),
                    Is.EqualTo(new[] { "PC0324" }));

    [Test]
    public void AVariableDenominatorIsLeftToRunTime() =>
        Assert.That(
            IdsOf(CheckBody("""
                    integer d = 0;
                    let f = Fraction.Create(1, d);
            """)),
            Is.Empty);

    // ---- Raising to a power ------------------------------------------------------------------

    /// <summary>
    /// The result follows the base, not a unification of both sides — an exponent counts
    /// multiplications rather than being a second value of the base's kind.
    /// </summary>
    [TestCase("        integer n = 2 ^ 10;")]
    [TestCase("        fraction f = 1|2 ^ 3;")]
    [TestCase("        fraction g = (1|2) ^ -3;")]
    [TestCase("        real r = 2.0 ^ 0.5;")]
    [TestCase("        real m = 2 ^ 0.5;")]
    public void RaisingToAPowerKeepsTheBaseType(string body) =>
        Assert.That(IdsOf(CheckBody(body)), Is.Empty);

    /// <summary>
    /// <para>Raising to m/n is the nth root of the mth power — ordinary arithmetic, and
    /// allowed. The answer is a real, because a root of a rational is usually irrational.</para>
    /// <para>This is the one place a fraction widens to a real without being asked. The rule
    /// against that elsewhere protects exactness that could have been kept; here there is
    /// none to keep, and <c>2 ^ (1|3)</c> says one third more faithfully than
    /// <c>2 ^ (1.0/3.0)</c> does.</para>
    /// </summary>
    [TestCase("        real x = (1|2) ^ (1|2);")]
    [TestCase("        real x = 2 ^ 1|2;")]
    [TestCase("        real x = 2.0 ^ 1|3;")]
    public void AFractionalExponentIsAllowedAndGivesAReal(string body) =>
        Assert.That(IdsOf(CheckBody(body)), Is.Empty);

    /// <summary>
    /// Two to the minus one is a half. Caught while compiling wherever the exponent can be
    /// seen; the same expression on a fraction base is exact and allowed.
    /// </summary>
    [Test]
    public void ANegativeExponentOnAnIntegerIsRejected() =>
        Assert.That(IdsOf(CheckBody("        let x = 2 ^ -1;")),
                    Is.EqualTo(new[] { "PC0333" }));

    [Test]
    public void ANegativeExponentOnAFractionIsFine() =>
        Assert.That(IdsOf(CheckBody("        let x = (1|2) ^ -1;")), Is.Empty);

    // ---- Range loops -------------------------------------------------------------------------

    /// <summary>The counter is an integer by construction, so it needs no annotation to be one.</summary>
    [Test]
    public void TheCounterIsAnIntegerWithoutBeingDeclaredOne() =>
        Assert.That(
            IdsOf(CheckBody("""
                    loop for i = 1 to 10
                        integer doubled = i * 2;
                    end loop
            """)),
            Is.Empty);

    /// <summary>The bounds are the only part left that can disagree.</summary>
    [TestCase("        loop for i = 1 to 2.5\n            yield;\n        end loop")]
    [TestCase("        loop for i = 1|2 to 10\n            yield;\n        end loop")]
    [TestCase("        loop for i = 1 to 10 stepby 0.5\n            yield;\n        end loop")]
    public void ARangeLoopStillCountsWithIntegers(string body) =>
        Assert.That(IdsOf(CheckBody(body)), Is.EqualTo(new[] { "PC0317" }));

    // ---- Collection literals take their type from what is wanted ---------------------------------

    /// <summary>
    /// The declared type decides, not whichever element came first. Without this a set of
    /// shapes cannot be written as the rectangles and circles it holds.
    /// </summary>
    [Test]
    public void ASetLiteralIsMeasuredAgainstTheDeclaredElementType() =>
        Assert.That(
            IdsOf(Check("""
                model Shape
                end model

                model Rectangle extends Shape
                end model

                model Circle extends Shape
                end model

                shared model Program
                    shared Shape[] Known = {new Rectangle(), new Circle()};

                    function Main()
                        Shape[] shapes = {new Rectangle(), new Circle()};
                        shapes = {new Circle()};
                        Console.WriteLine(Program.Make().Count);
                    end function

                    Shape[] function Make()
                        yield {new Rectangle(), new Circle()};
                    end function
                end model
                """)),
            Is.Empty,
            "a declaration, a field, an assignment and a yield all say what is wanted");

    /// <summary>
    /// Each element converts on its own, which is the thing inference could never produce:
    /// there is no single element type here that both 1 and 2 already have.
    /// </summary>
    [Test]
    public void EachElementConvertsOnItsOwn() =>
        Assert.That(IdsOf(CheckBody("        integer?[] maybes = {1, 2};")), Is.Empty);

    [Test]
    public void AnElementThatDoesNotFitIsStillRejected() =>
        Assert.That(
            IdsOf(CheckBody("        integer[] wrong = {1, \"two\"};")),
            Is.EqualTo(new[] { "PC0300" }),
            "measured against integer, not against the first element");

    /// <summary>With nothing to measure against, inference still needs one type.</summary>
    [Test]
    public void WithNoTargetTheElementsMustAgree() =>
        Assert.That(
            IdsOf(Check("""
                model Shape
                end model

                model Rectangle extends Shape
                end model

                model Circle extends Shape
                end model

                shared model Program
                    function Main()
                        let guessed = {new Rectangle(), new Circle()};
                    end function
                end model
                """)),
            Is.EqualTo(new[] { "PC0314" }));

    // ---- The members the language provides ------------------------------------------------------

    /// <summary>
    /// A member the language provides that is something to do is a function, and naming one
    /// without calling it is reported.
    /// </summary>
    [TestCase("        integer? maybe = 1;\n        let present = maybe.HasValue;")]
    [TestCase("        integer[] xs = {1};\n        let n = xs.Distinct;")]
    public void ABuiltInMemberHasToBeCalled(string body) =>
        Assert.That(IdsOf(CheckBody(body)), Is.EqualTo(new[] { "PC0330" }));

    /// <summary>
    /// <para>A member that is a value is read, and calling one is reported.</para>
    /// <para><c>Count</c> is the one this is really about. It reads as <c>.NET</c>'s does, which
    /// it did not always — and the mirror of the diagnostic above is what makes either mistake
    /// name itself rather than leaving a reader to guess which kind of member they have.</para>
    /// </summary>
    [TestCase("        integer[] xs = {1};\n        let n = xs.Count;", new string[0])]
    [TestCase("        let n = \"abc\".Count;", new string[0])]
    [TestCase("        integer[] xs = {1};\n        let n = xs.Count();", new[] { "PC0338" })]
    [TestCase("        let n = \"abc\".Count();", new[] { "PC0338" })]
    [TestCase("        integer? maybe = 1;\n        let present = maybe.HasValue();", new string[0])]
    public void AValueMemberIsReadRatherThanCalled(string body, string[] expected) =>
        Assert.That(IdsOf(CheckBody(body)), Is.EqualTo(expected));

    // ---- Exceptions -----------------------------------------------------------------------------

    /// <summary>
    /// The exceptions the language throws itself really do extend Exception, so one clause
    /// takes them all and Message is inherited rather than declared on each.
    /// </summary>
    [TestCase("DivideByZeroException")]
    [TestCase("IndexOutOfRangeException")]
    [TestCase("Exception")]
    public void EveryExceptionCarriesItsMessage(string type) =>
        Assert.That(
            IdsOf(CheckBody($"""
                    try
                        yield;
                    catch {type} problem
                        Console.WriteLine(problem.Message());
                    end try
            """)),
            Is.Empty);

    [Test]
    public void AModelExtendingExceptionInheritsMessage() =>
        Assert.That(
            IdsOf(Check("""
                model NotFoundException extends Exception
                end model

                shared model Program
                    function Main()
                        try
                            yield;
                        catch NotFoundException problem
                            Console.WriteLine(problem.Message());
                        end try
                    end function
                end model
                """)),
            Is.Empty);

    // ---- Function types --------------------------------------------------------------------

    /// <summary>
    /// <para>A function type that yields nothing can be given a value.</para>
    /// <para>A function type spells "yields nothing" as a null result. A lambda whose
    /// expression happens to produce no value must spell it the same way, or the two describe
    /// one idea in two forms and never match — which left every void function type declarable
    /// and unusable.</para>
    /// </summary>
    [TestCase("        delegate() f = () yield Console.WriteLine(\"x\");")]
    [TestCase("        delegate(string) f = (s) yield Console.WriteLine(s);")]
    [TestCase("        delegate() f = () yield Console.Write(\"x\");")]
    public void ALambdaThatProducesNoValueFitsAFunctionTypeThatYieldsNothing(string body) =>
        Assert.That(IdsOf(CheckBody(body)), Is.Empty);

    [Test]
    public void AVoidFunctionTypeWorksAsAParameter() =>
        Assert.That(
            IdsOf(Check("""
                shared model Program
                    function Run(delegate() action)
                        action();
                    end function

                    function Main()
                        Program.Run(() yield Console.WriteLine("ran"));
                    end function
                end model
                """)),
            Is.Empty);

    /// <summary>A result is still a result: one that yields a value does not fit one that does not.</summary>
    [Test]
    public void AFunctionThatYieldsAValueDoesNotFitOneThatYieldsNothing() =>
        Assert.That(
            IdsOf(CheckBody("        delegate() f = () yield 1;")),
            Is.EqualTo(new[] { "PC0300" }));

    // ---- A lambda parameter written without a type -------------------------------------------

    /// <summary>
    /// Every place the surrounding code says what a bare parameter name holds: a declared
    /// type, the element type of a set being built, a parameter of the function being called,
    /// and the result of the function doing the yielding.
    /// </summary>
    [TestCase("        integer delegate(integer) f = (a) yield a + 1;")]
    [TestCase("        integer delegate(integer, integer) f = (a, b) yield a + b;")]
    [TestCase("        integer delegate(integer) f = function(a) yield a + 1; end function;")]
    [TestCase("        integer delegate(integer)[] fs = { (a) yield a + 1 };")]
    [TestCase("        integer delegate(integer) f = (a) yield a; f = (a) yield a * 2;")]
    public void ABareParameterNameTakesItsTypeFromTheSurroundingCode(string body) =>
        Assert.That(IdsOf(CheckBody(body)), Is.Empty);

    [Test]
    public void AParameterOfTheFunctionBeingCalledSaysWhatABareNameHolds() =>
        Assert.That(
            IdsOf(Check("""
                shared model Program
                    integer function Apply(integer delegate(integer) f, integer n)
                        yield f(n);
                    end function

                    function Main()
                        Console.WriteLine(Program.Apply((n) yield n * 2, 5));
                    end function
                end model
                """)),
            Is.Empty);

    /// <summary>
    /// A bare name takes the type its own position was given, not the first one in the list,
    /// which is what makes a lambda over two unrelated types work.
    /// </summary>
    [Test]
    public void EachBareNameTakesTheTypeOfItsOwnPosition() =>
        Assert.That(
            IdsOf(CheckBody("""
                    string delegate(string, integer) f = (text, times) yield text + times;
                    Console.WriteLine(f("x", 3));
            """)),
            Is.Empty);

    /// <summary>
    /// Nothing says what the name holds, so it is reported rather than guessed. A 'let' is the
    /// plainest case: the lambda is the only thing on the right, so there is nothing to ask.
    /// </summary>
    [TestCase("        let f = (n) yield n + 1;")]
    [TestCase("        Console.WriteLine((n) yield n + 1);")]
    public void ABareParameterNameWithNothingToTakeATypeFromIsReported(string body) =>
        Assert.That(IdsOf(CheckBody(body)), Does.Contain("PC0336"));

    /// <summary>
    /// A count that disagrees settles no parameter, so each name is named. The types are not
    /// then compared on top of it: the lambda has no type worth measuring, and saying so twice
    /// would bury the one thing that has to be fixed.
    /// </summary>
    [Test]
    public void AMismatchedParameterCountNamesEachParameterAndStopsThere() =>
        Assert.That(
            IdsOf(CheckBody("        integer delegate(integer) f = (a, b) yield a;")),
            Is.EqualTo(new[] { "PC0336", "PC0336" }));

    /// <summary>
    /// <para>A type the surrounding code already fixed is reported wherever it is written:
    /// under a declared type, an element type, a parameter being passed to, or a result being
    /// yielded.</para>
    /// <para>One per parameter, since each is a type that could come out on its own.</para>
    /// </summary>
    [TestCase("        integer delegate(integer) f = (integer a) yield a;", 1)]
    [TestCase("        integer delegate(integer, integer) f = (integer a, integer b) yield a;", 2)]
    [TestCase("        integer delegate(integer) f = function(integer a) yield a; end function;", 1)]
    [TestCase("        integer delegate(integer)[] fs = { (integer a) yield a };", 1)]
    public void AWrittenTypeTheSurroundingCodeAlreadyGaveIsReported(string body, int count) =>
        Assert.That(
            IdsOf(CheckBody(body)),
            Is.EqualTo(Enumerable.Repeat("PC0115", count)));

    /// <summary>
    /// Mixing the two forms needs no rule of its own: the written one is reported for the
    /// same reason it would be on its own, and taking the advice leaves a list written one
    /// way. Nothing here ever suggests writing the other types out.
    /// </summary>
    [Test]
    public void AMixedListIsReportedOnlyForTheTypeThatWasWritten()
    {
        DiagnosticBag diagnostics =
            CheckBody("        integer delegate(integer, integer) f = (integer a, b) yield a + b;");

        Assert.Multiple(() =>
        {
            Assert.That(IdsOf(diagnostics), Is.EqualTo(new[] { "PC0115" }));
            Assert.That(diagnostics.Single().Message, Does.Contain("'a'"));
            Assert.That(diagnostics.Single().Message, Does.Not.Contain("every parameter"));
        });
    }

    /// <summary>
    /// An optional function type says what the parameters hold just as plainly as the bare
    /// form: the lambda is wrapped on the way in, so what it has to be is the type underneath.
    /// </summary>
    [TestCase("        integer delegate(integer)? f = (n) yield n + 1;", new string[0])]
    [TestCase("        integer delegate(integer)? f = (integer n) yield n + 1;", new[] { "PC0115" })]
    public void AnOptionalFunctionTypeIsStillATarget(string body, string[] expected) =>
        Assert.That(IdsOf(CheckBody(body)), Is.EqualTo(expected));

    /// <summary>
    /// <para>A <c>let</c> is the one place a lambda writes its own types, because it is the
    /// one place nothing else does.</para>
    /// <para>The two rules meet exactly here: written is silent and bare reports
    /// <c>PC0336</c>, while everywhere else it is the other way round. There is no third
    /// case, so a lambda always has exactly one spelling that says nothing twice and leaves
    /// nothing unsaid.</para>
    /// </summary>
    [Test]
    public void ALetIsWhereALambdaWritesItsOwnTypes() =>
        Assert.That(
            IdsOf(CheckBody("        let f = (integer n) yield n + 1;")),
            Is.Empty);

    /// <summary>
    /// A declared function has nothing to take a type from, so leaving one out there is a
    /// parse error rather than something the checker could settle.
    /// </summary>
    [Test]
    public void ADeclaredFunctionStillRequiresEveryParameterType() =>
        Assert.That(
            IdsOf(Check("""
                shared model Program
                    integer function Twice(n)
                        yield n * 2;
                    end function

                    function Main()
                        Console.WriteLine(Program.Twice(2));
                    end function
                end model
                """)),
            Is.Not.Empty);

    // ---- Function, the root of every function type -------------------------------------------

    /// <summary>
    /// <para>Every function fits <c>Function</c>, whatever it takes and yields — which is what
    /// lets one be held without its signature being named.</para>
    /// <para>The set case is the one worth having: a set holds one type, so before this there
    /// was no way to keep functions of different shapes together at all.</para>
    /// </summary>
    [TestCase("        Function f = (integer n) yield n + 1;")]
    [TestCase("        Function f = (string s) yield Console.WriteLine(s);")]
    [TestCase("        Function f = function(integer n) yield n; end function;")]
    [TestCase("        Function[] fs = { (integer n) yield n, (string s) yield s };")]
    public void EveryFunctionFitsTheBareFunctionType(string body) =>
        Assert.That(IdsOf(CheckBody(body)), Is.Empty);

    /// <summary>A Function is a Model, as every reference type is.</summary>
    [Test]
    public void AFunctionIsAModel() =>
        Assert.That(
            IdsOf(CheckBody("""
                    Function f = (integer n) yield n + 1;
                    Model m = f;
            """)),
            Is.Empty);

    /// <summary>
    /// Nothing but a function reaches it. Without this the name would read as "anything",
    /// which is what Model already means.
    /// </summary>
    [TestCase("        Function f = 1;")]
    [TestCase("        Function f = \"text\";")]
    [TestCase("        Function f = {1, 2};")]
    public void NothingElseReachesFunction(string body) =>
        Assert.That(IdsOf(CheckBody(body)), Does.Contain("PC0300"));

    /// <summary>
    /// It says nothing about what the parameters hold, so a lambda written into one has
    /// nothing to take a type from and writes its own.
    /// </summary>
    [Test]
    public void ABareParameterNameHasNothingToTakeFromAFunction() =>
        Assert.That(
            IdsOf(CheckBody("        Function f = (n) yield n + 1;")),
            Does.Contain("PC0336"));

    /// <summary>A program may not declare it, and may not extend it either.</summary>
    [TestCase("model Function\nend model")]
    [TestCase("model Mine extends Function\nend model")]
    public void FunctionCannotBeDeclaredOrExtended(string declaration) =>
        Assert.That(IdsOf(Check($$"""
            {{declaration}}

            shared model Program
                function Main()
                    Console.WriteLine("x");
                end function
            end model
            """)), Is.Not.Empty);

    // ---- Constructing ------------------------------------------------------------------------

    /// <summary>
    /// <para>A <c>new</c> is checked against the constructor it runs, the same way a call is
    /// checked against the function it calls.</para>
    /// <para>Left unresolved, a constructor accepted any arguments at all and dropped them,
    /// which reached as far as a string sitting in a field declared to hold an integer.</para>
    /// </summary>
    [TestCase("        Thing t = new Thing(\"text\");", "PC0300")]
    [TestCase("        Thing t = new Thing(1, 2);", "PC0308")]
    [TestCase("        Thing t = new Thing();", "PC0308")]
    public void ANewIsCheckedAgainstTheConstructorItRuns(string body, string expected) =>
        Assert.That(IdsOf(CheckWithThing(body)), Is.EqualTo(new[] { expected }));

    [Test]
    public void AConstructorThatFitsIsAccepted() =>
        Assert.That(IdsOf(CheckWithThing("        Thing t = new Thing(1);")), Is.Empty);

    /// <summary>A type declaring no constructor takes nothing, so only an empty new fits it.</summary>
    [TestCase("        Bare b = new Bare();", new string[0])]
    [TestCase("        Bare b = new Bare(1);", new[] { "PC0308" })]
    public void ATypeWithNoConstructorTakesNoArguments(string body, string[] expected) =>
        Assert.That(IdsOf(CheckWithThing(body)), Is.EqualTo(expected));

    /// <summary>
    /// An exception declares no constructor a program can see, but every one carries a
    /// message, so that one form is allowed through — as it is after <c>base</c>.
    /// </summary>
    [TestCase("        let e = new Exception(\"went wrong\");", new string[0])]
    [TestCase("        let e = new Exception(1, 2);", new[] { "PC0308" })]
    public void AnExceptionStillTakesItsMessage(string body, string[] expected) =>
        Assert.That(IdsOf(CheckWithThing(body)), Is.EqualTo(expected));

    /// <summary>
    /// <para>A type the language provides is constructible only if it says so.</para>
    /// <para>Without this the check fell through to the rule for a declared type with no
    /// constructor, which accepts an empty <c>new</c> — so <c>new Math()</c> passed and
    /// produced nothing at all.</para>
    /// </summary>
    [TestCase("        let m = new Math();")]
    [TestCase("        let c = new Console();")]
    [TestCase("        let f = new Fraction(1, 2);")]
    [TestCase("        let r = new Reference();")]
    public void ATypeTheLanguageProvidesIsNotConstructedUnlessItSaysSo(string body) =>
        Assert.That(IdsOf(CheckBody(body)), Does.Contain("PC0328"));

    /// <summary>The two that do say so, and the forms each accepts.</summary>
    [TestCase("        Random r = new Random();", new string[0])]
    [TestCase("        Random r = new Random(42);", new string[0])]
    [TestCase("        Random r = new Random(1, 2);", new[] { "PC0308" })]
    [TestCase("        DateTime d = new DateTime(2026, 7, 29);", new string[0])]
    [TestCase("        DateTime d = new DateTime(2026, 7, 29, 13, 0, 0);", new string[0])]
    [TestCase("        DateTime d = new DateTime(new Date(2026, 7, 29));", new string[0])]
    [TestCase("        DateTime d = new DateTime(new Date(2026, 7, 29), new Time(9, 0));",
              new string[0])]

    // A form does take one argument, so what is wrong here is the argument rather than the
    // count — which is why this reports a type and not PC0308.
    [TestCase("        DateTime d = new DateTime(2026);", new[] { "PC0300" })]
    [TestCase("        DateTime d = new DateTime(2026, 7);", new[] { "PC0300", "PC0300" })]
    public void TheConstructibleOnesTakeTheFormsTheyList(string body, string[] expected) =>
        Assert.That(IdsOf(CheckBody(body)), Is.EqualTo(expected));

    /// <summary>
    /// What .NET reads as a property is read as one here, so a moment's parts are written
    /// without parentheses and writing them is reported — the same pair of diagnostics that
    /// tells a reader which of the two anything is.
    /// </summary>
    [TestCase("        let y = new DateTime(2026, 7, 29).Year;", new string[0])]
    [TestCase("        let y = new DateTime(2026, 7, 29).Year();", new[] { "PC0338" })]
    [TestCase("        let n = new Random(1).Next();", new string[0])]
    [TestCase("        let n = new Random(1).Next;", new[] { "PC0330" })]
    public void AMomentsPartsAreReadWithoutParentheses(string body, string[] expected) =>
        Assert.That(IdsOf(CheckBody(body)), Is.EqualTo(expected));

    private static DiagnosticBag CheckWithThing(string body) => Check($$"""
        model Thing
            public integer v;

            public function Thing(integer v)
                this.v = v;
            end function
        end model

        model Bare
            public integer v;
        end model

        shared model Program
            function Main()
        {{body}}
            end function
        end model
        """);

    // ---- How a message reads ---------------------------------------------------------------

    /// <summary>
    /// A diagnostic is a sentence someone reads, so it agrees with itself however many things
    /// it is counting. The verb agrees with the function rather than with either number, which
    /// is what lets one wording serve every count.
    /// </summary>
    [TestCase("Program.None(1);", "'None' takes no arguments, but was given 1.")]
    [TestCase("Program.One();", "'One' takes 1 argument, but was given 0.")]
    [TestCase("Program.One(1, 2, 3);", "'One' takes 1 argument, but was given 3.")]
    [TestCase("Program.Two(1);", "'Two' takes 2 arguments, but was given 1.")]
    public void AWrongArgumentCountReadsAsASentence(string call, string expected)
    {
        DiagnosticBag diagnostics = Check($$"""
            shared model Program
                function None()
                end function

                function One(integer a)
                end function

                function Two(integer a, integer b)
                end function

                function Main()
                    {{call}}
                end function
            end model
            """);

        Assert.That(diagnostics.Sorted().Single().Message, Is.EqualTo(expected));
    }

    // ---- Signatures naming declared types -----------------------------------------------

    /// <summary>
    /// <para>A member's signature is read before the whole program has been seen, so a name it
    /// cannot yet place stands as the error type until every type is known. These hold the
    /// settling: a signature naming a declared type must be checked like any other.</para>
    /// <para>An unsettled signature is silent rather than loud — the error type suppresses
    /// cascades, so every one of these once compiled cleanly and was wrong.</para>
    /// </summary>
    [Test]
    public void AParameterOfADeclaredTypeIsChecked() =>
        Assert.That(
            IdsOf(Check("""
                model Item
                    public function Item()
                    end function
                end model

                shared model Program
                    function Take(Item held)
                    end function

                    function Main()
                        Program.Take(42);
                    end function
                end model
                """)),
            Is.EqualTo(new[] { "PC0300" }));

    [Test]
    public void AReturnTypeOfADeclaredTypeIsChecked() =>
        Assert.That(
            IdsOf(Check("""
                model Item
                    public function Item()
                    end function
                end model

                shared model Program
                    Item function Make()
                        yield new Item();
                    end function

                    function Main()
                        integer wrong = Program.Make();
                    end function
                end model
                """)),
            Is.EqualTo(new[] { "PC0300" }));

    [Test]
    public void AFieldOfADeclaredTypeIsChecked() =>
        Assert.That(
            IdsOf(Check("""
                model Item
                    public function Item()
                    end function
                end model

                model Box
                    Item held;

                    public function Box()
                        this.held = 42;
                    end function
                end model
                """)),
            Is.EqualTo(new[] { "PC0300" }));

    /// <summary>
    /// The members the language provides are found on a set of a declared type, which they are
    /// not when the element type never settled.
    /// </summary>
    [Test]
    public void ASetFieldOfADeclaredTypeHasTheMembersOfASet() =>
        Assert.That(
            IdsOf(Check("""
                model Item
                    public function Item()
                    end function
                end model

                model Box
                    Item[] held;

                    public function Box()
                        this.held = {};
                        this.held.Insert(new Item());
                    end function

                    public integer function Size()
                        yield this.held.Count;
                    end function
                end model
                """)),
            Is.Empty);

    /// <summary>A signature may name a type declared after it, which is why settling waits.</summary>
    [Test]
    public void ASignatureMayNameATypeDeclaredLater() =>
        Assert.That(
            IdsOf(Check("""
                model Box
                    Item[] held;

                    public function Box()
                        this.held = {};
                    end function

                    public function Add(Item one)
                        this.held.Insert(one);
                    end function
                end model

                model Item
                    public function Item()
                    end function
                end model
                """)),
            Is.Empty);

    /// <summary>A name nothing declares is reported once, not once per pass over it.</summary>
    [Test]
    public void AnUnknownTypeInASignatureIsReportedOnce() =>
        Assert.That(
            IdsOf(Check("""
                model Box
                    Missing held;

                    public function Take(Missing one)
                    end function
                end model
                """)),
            Is.EqualTo(new[] { "PC0201", "PC0201" }));

    [Test]
    public void CheckingNeverThrows()
    {
        string[] hostile =
        [
            "", "model M end model", "model M function F() yield; end function end model",
            "shared model Program function Main() let x = ; end function end model",
            "model M function F() this.x.y.z(); end function end model",
            "model M function F() let a = {}.Count; end function end model",
        ];

        foreach (string source in hostile)
        {
            Assert.DoesNotThrow(() => Check(source), $"checking \"{source}\" threw");
        }
    }
}
