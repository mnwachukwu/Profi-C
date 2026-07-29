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
            global model Program
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
    /// Neither direction is automatic. One third as a real is 0.33333333333333331 and one
    /// tenth as a fraction is 3602879701896397 over 36028797018963968; both are surprising
    /// enough that the program should ask.
    /// </summary>
    [Test]
    public void AFractionAndARealNeverConvertOnTheirOwn()
    {
        DiagnosticBag toReal = CheckBody("        real r = 1|2;");
        DiagnosticBag toFraction = CheckBody("        fraction f = 0.5;");

        Assert.Multiple(() =>
        {
            Assert.That(IdsOf(toReal), Is.EqualTo(new[] { "PFC0301" }));
            Assert.That(toReal.Single().Message, Does.Contain("ToReal()"));

            Assert.That(IdsOf(toFraction), Is.EqualTo(new[] { "PFC0301" }));
            Assert.That(toFraction.Single().Message, Does.Contain("ToFraction()"));
        });
    }

    [Test]
    public void TheExplicitConversionsWork()
    {
        Assert.That(IdsOf(CheckBody(
            """
                    let r = (1|2).ToReal();
                    let f = (0.5).ToFraction();
            """)), Is.Empty);
    }

    [Test]
    public void UnrelatedTypesDoNotConvert()
    {
        Assert.That(IdsOf(CheckBody("        integer x = \"text\";")),
                    Is.EqualTo(new[] { "PFC0300" }));
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
            Assert.That(IdsOf(diagnostics), Is.EqualTo(new[] { "PFC0329" }));
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

    [Test]
    public void AModelConvertsToItsAncestors()
    {
        Assert.That(IdsOf(Check(
            """
            model Shape
            end model

            model Square extends Shape
            end model

            global model Program
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

            global model Program
                function Main()
                    Square q = new Shape();
                end function
            end model
            """)), Is.EqualTo(new[] { "PFC0300" }));
    }

    /// <summary>
    /// Boxing is refused rather than deferred, so this stays an error in every version.
    /// </summary>
    [Test]
    public void AValueTypeNeverConvertsToModel()
    {
        Assert.That(IdsOf(CheckBody("        Model m = 1;")), Is.EqualTo(new[] { "PFC0300" }));
    }

    // ---- Operators -----------------------------------------------------------------------------

    [Test]
    public void ArithmeticNeedsNumbers()
    {
        Assert.That(IdsOf(CheckBody("        let x = true * 2;")),
                    Is.EqualTo(new[] { "PFC0303" }));
    }

    [Test]
    public void MixingAFractionAndARealInArithmeticIsRejected()
    {
        // They have no common type on purpose, so there is nothing for the operator to
        // produce without a conversion nobody asked for.
        Assert.That(IdsOf(CheckBody("        let x = 1|2 + 0.5;")),
                    Is.EqualTo(new[] { "PFC0303" }));
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
                    Is.EqualTo(new[] { "PFC0302", "PFC0302" }));
    }

    [Test]
    public void AConditionMustBeABoolean()
    {
        Assert.That(IdsOf(CheckBody(
            """
                    if 1
                        yield;
                    end if
            """)), Is.EqualTo(new[] { "PFC0302" }));
    }

    [Test]
    public void DividingByAnObviousZeroIsCaughtWhileCompiling()
    {
        Assert.Multiple(() =>
        {
            Assert.That(IdsOf(CheckBody("        let x = 1 / 0;")),
                        Is.EqualTo(new[] { "PFC0324" }));
            Assert.That(IdsOf(CheckBody("        let x = 1 % 0;")),
                        Is.EqualTo(new[] { "PFC0324" }));
        });
    }

    // ---- The conditional expression --------------------------------------------------------------

    [Test]
    public void ConditionalBranchesMustAgreeExactly()
    {
        // Finding a common type would make this a real, which is what neither branch says.
        DiagnosticBag diagnostics = CheckBody("        let x = if true then 1 else 2.5;");

        Assert.That(IdsOf(diagnostics), Is.EqualTo(new[] { "PFC0305" }));
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
                    Is.EqualTo(new[] { "PFC0314" }));
    }

    [Test]
    public void AnEmptySetNeedsItsTypeWritten()
    {
        Assert.Multiple(() =>
        {
            Assert.That(IdsOf(CheckBody("        let nothing = {};")),
                        Is.EqualTo(new[] { "PFC0313" }));
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
                        Is.EqualTo(new[] { "PFC0311" }));

            Assert.That(IdsOf(CheckBody(
                """
                        integer[] numbers = {1};
                        let bad = numbers["x"];
                """)), Is.EqualTo(new[] { "PFC0312" }));
        });
    }

    [Test]
    public void SetMembersAreAvailableWithoutBeingDeclared()
    {
        Assert.That(IdsOf(CheckBody(
            """
                    integer[] numbers = {1, 2};
                    let count = numbers.Count();
                    let has = numbers.Contains(1);
                    let removed = numbers.Remove(1);
                    numbers.Insert(3);
                    numbers.Clear();
            """)), Is.Empty);
    }

    [Test]
    public void AStringReportsItsLengthWithCountJustAsASetDoes()
    {
        Assert.That(IdsOf(CheckBody("        let n = \"abc\".Count();")), Is.Empty);
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

        Assert.That(IdsOf(diagnostics), Does.Contain("PFC0315"));
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
            """)), Is.EqualTo(new[] { "PFC0326" }));
    }

    // ---- Loops --------------------------------------------------------------------------------------

    [Test]
    public void ARangeLoopCountsWithIntegers()
    {
        Assert.That(IdsOf(CheckBody(
            """
                    for i = 1 to 2.5
                        yield;
                    end for
            """)), Is.EqualTo(new[] { "PFC0317" }));
    }

    [Test]
    public void IteratingWorksOverASetAndOverAString()
    {
        Assert.That(IdsOf(CheckBody(
            """
                    integer[] numbers = {1, 2};
                    for each n in numbers
                        integer copy = n;
                    end for
                    for each letter in "abc"
                        character c = letter;
                    end for
            """)), Is.Empty);
    }

    [Test]
    public void IteratingSomethingThatIsNotASequenceIsRejected()
    {
        Assert.That(IdsOf(CheckBody(
            """
                    for each x in 5
                        yield;
                    end for
            """)), Is.EqualTo(new[] { "PFC0316" }));
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
                """)), Is.EqualTo(new[] { "PFC0300" }));

            Assert.That(IdsOf(Check(
                """
                model M
                    function F()
                        yield 1;
                    end function
                end model
                """)), Is.EqualTo(new[] { "PFC0318" }));

            Assert.That(IdsOf(Check(
                """
                model M
                    integer function F()
                        yield;
                    end function
                end model
                """)), Is.EqualTo(new[] { "PFC0319" }));
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
                """)), Is.EqualTo(new[] { "PFC0321" }));
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
                    Does.Contain("PFC0322"));
    }

    [Test]
    public void AConstantModelIsRejectedForTheSameReason()
    {
        Assert.That(IdsOf(Check(
            """
            model Dog
            end model

            global model Program
                function Main()
                    constant Dog Pet = new Dog();
                end function
            end model
            """)), Does.Contain("PFC0322"));
    }

    [Test]
    public void AConstantMustBeGivenAValue()
    {
        Assert.That(IdsOf(CheckBody("        constant integer A;")),
                    Is.EqualTo(new[] { "PFC0320" }));
    }

    // ---- Calls -------------------------------------------------------------------------------------------

    [Test]
    public void ArgumentsMustFitTheParameters()
    {
        Assert.That(IdsOf(Check(
            """
            global model Program
                function Take(integer value)
                end function

                function Main()
                    Program.Take("text");
                end function
            end model
            """)), Is.EqualTo(new[] { "PFC0300" }));
    }

    [Test]
    public void TheArgumentCountMustMatch()
    {
        Assert.That(IdsOf(Check(
            """
            global model Program
                function Take(integer value)
                end function

                function Main()
                    Program.Take(1, 2);
                end function
            end model
            """)), Is.EqualTo(new[] { "PFC0308" }));
    }

    [Test]
    public void AnExactMatchWinsAmongOverloads()
    {
        Assert.That(IdsOf(Check(
            """
            global model Program
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
            global model Program
                function Take(real value)
                end function

                function Take(fraction value)
                end function

                function Main()
                    Program.Take(1);
                end function
            end model
            """)), Is.EqualTo(new[] { "PFC0310" }));
    }

    [Test]
    public void CallingSomethingThatIsNotAFunctionIsRejected()
    {
        Assert.That(IdsOf(CheckBody(
            """
                    integer x = 1;
                    x();
            """)), Is.EqualTo(new[] { "PFC0307" }));
    }

    [Test]
    public void AMemberThatDoesNotExistIsReported()
    {
        Assert.That(IdsOf(CheckBody(
            """
                    integer[] numbers = {1};
                    numbers.Nonexistent();
            """)), Is.EqualTo(new[] { "PFC0306" }));
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

            global model Program
                function Main()
                    let s = new Shape();
                end function
            end model
            """)), Is.EqualTo(new[] { "PFC0328" }));
    }

    [Test]
    public void AGlobalModelCannotBeInstantiated()
    {
        Assert.That(IdsOf(Check(
            """
            global model Utility
            end model

            global model Program
                function Main()
                    let u = new Utility();
                end function
            end model
            """)), Is.EqualTo(new[] { "PFC0328" }));
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

            global model Program
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

            global model Program
                function Main(Dog d)
                    let x = d is Fish;
                end function
            end model
            """)), Is.EqualTo(new[] { "PFC0327" }));
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
            """)), Is.EqualTo(new[] { "PFC0200" }));
    }

    // ---- Condition messages --------------------------------------------------------------------

    /// <summary>
    /// Each caller supplies its whole subject phrase, article included. Worth testing because
    /// message wording is what a reader sees and what nothing else checks.
    /// </summary>
    [TestCase("        if 1\n            yield;\n        end if", "An if condition")]
    [TestCase("        if true\n            yield;\n        else if 1\n            yield;\n        end if",
              "An else-if condition")]
    [TestCase("        while 1\n            yield;\n        end while", "A while condition")]
    [TestCase("        let f = if 1 then 2 else 3;", "An if expression's condition")]
    [TestCase("        let g = 1 and true;", "An operand of 'and' or 'or'")]
    public void AConditionMessageNamesItsSubjectCorrectly(string body, string expected)
    {
        DiagnosticBag diagnostics = CheckBody(body);

        Assert.Multiple(() =>
        {
            Assert.That(IdsOf(diagnostics), Does.Contain("PFC0302"));
            Assert.That(diagnostics.First(d => d.Id == "PFC0302").Message,
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
            "        while 1\n            yield;\n        end while",
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
    [TestCase("        let c = Console.WriteLine(\"hi\").Count();")]
    public void UsingTheResultOfAFunctionThatYieldsNothingIsRejected(string body) =>
        Assert.That(IdsOf(CheckBody(body)), Is.EqualTo(new[] { "PFC0332" }));

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
    [TestCase("        integer x = \"text\";", "PFC0300")]
    [TestCase("        let y = true * 2;", "PFC0303")]
    public void ARealMismatchIsUnaffected(string body, string expected) =>
        Assert.That(IdsOf(CheckBody(body)), Is.EqualTo(new[] { expected }));

    // ---- Fraction.Create ---------------------------------------------------------------------

    [Test]
    public void CreatingAFractionYieldsAFraction() =>
        Assert.That(IdsOf(CheckBody("        fraction f = Fraction.Create(1, 3);")), Is.Empty);

    [TestCase("        let f = Fraction.Create(1.0, 3);")]
    [TestCase("        let f = Fraction.Create(1, \"three\");")]
    public void CreatingAFractionNeedsTwoIntegers(string body) =>
        Assert.That(IdsOf(CheckBody(body)), Is.EqualTo(new[] { "PFC0300" }));

    /// <summary>
    /// A denominator of zero is the same mistake as dividing by zero, so it is reported the
    /// same way and in the same place when the compiler can see it.
    /// </summary>
    [Test]
    public void ALiteralZeroDenominatorIsCaughtWhileCompiling() =>
        Assert.That(IdsOf(CheckBody("        let f = Fraction.Create(1, 0);")),
                    Is.EqualTo(new[] { "PFC0324" }));

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
                    Is.EqualTo(new[] { "PFC0333" }));

    [Test]
    public void ANegativeExponentOnAFractionIsFine() =>
        Assert.That(IdsOf(CheckBody("        let x = (1|2) ^ -1;")), Is.Empty);

    // ---- Range loops -------------------------------------------------------------------------

    /// <summary>The counter is an integer by construction, so it needs no annotation to be one.</summary>
    [Test]
    public void TheCounterIsAnIntegerWithoutBeingDeclaredOne() =>
        Assert.That(
            IdsOf(CheckBody("""
                    for i = 1 to 10
                        integer doubled = i * 2;
                    end for
            """)),
            Is.Empty);

    /// <summary>The bounds are the only part left that can disagree.</summary>
    [TestCase("        for i = 1 to 2.5\n            yield;\n        end for")]
    [TestCase("        for i = 1|2 to 10\n            yield;\n        end for")]
    [TestCase("        for i = 1 to 10 step 0.5\n            yield;\n        end for")]
    public void ARangeLoopStillCountsWithIntegers(string body) =>
        Assert.That(IdsOf(CheckBody(body)), Is.EqualTo(new[] { "PFC0317" }));

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

                global model Program
                    global Shape[] Known = {new Rectangle(), new Circle()};

                    function Main()
                        Shape[] shapes = {new Rectangle(), new Circle()};
                        shapes = {new Circle()};
                        Console.WriteLine(Program.Make().Count());
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
            Is.EqualTo(new[] { "PFC0300" }),
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

                global model Program
                    function Main()
                        let guessed = {new Rectangle(), new Circle()};
                    end function
                end model
                """)),
            Is.EqualTo(new[] { "PFC0314" }));

    // ---- The members the language provides ------------------------------------------------------

    /// <summary>
    /// The language has no properties, so every member it provides is a function and every
    /// use of one is a call.
    /// </summary>
    [TestCase("        integer[] xs = {1};\n        let n = xs.Count;")]
    [TestCase("        let n = \"abc\".Count;")]
    [TestCase("        integer? maybe = 1;\n        let present = maybe.HasValue;")]
    public void ABuiltInMemberHasToBeCalled(string body) =>
        Assert.That(IdsOf(CheckBody(body)), Is.EqualTo(new[] { "PFC0330" }));

    [TestCase("        integer[] xs = {1};\n        let n = xs.Count();")]
    [TestCase("        integer? maybe = 1;\n        let present = maybe.HasValue();")]
    public void CallingItIsFine(string body) =>
        Assert.That(IdsOf(CheckBody(body)), Is.Empty);

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

                global model Program
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

    [Test]
    public void CheckingNeverThrows()
    {
        string[] hostile =
        [
            "", "model M end model", "model M function F() yield; end function end model",
            "global model Program function Main() let x = ; end function end model",
            "model M function F() this.x.y.z(); end function end model",
            "model M function F() let a = {}.Count(); end function end model",
        ];

        foreach (string source in hostile)
        {
            Assert.DoesNotThrow(() => Check(source), $"checking \"{source}\" threw");
        }
    }
}
