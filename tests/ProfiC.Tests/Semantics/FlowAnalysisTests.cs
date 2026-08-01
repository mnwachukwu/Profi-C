using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Semantics;
using ProfiC.Compiler.Text;

namespace ProfiC.Tests.Semantics;

/// <summary>
/// <para>Definite assignment, optional narrowing, and reaching a result.</para>
/// <para>Together these are what let the language do without null: a variable with no value
/// cannot be read, an optional cannot be read at all until presence is proven, and a function
/// that declares a result cannot finish without producing one. All three are the same forward
/// walk asked different questions.</para>
/// </summary>
[TestFixture]
public sealed class FlowAnalysisTests
{
    private static DiagnosticBag Check(string source)
    {
        DiagnosticBag diagnostics = new();
        CompilationUnit unit = Parser.Parse(new SourceText(source, "<test>"), diagnostics);
        SemanticModel model = Resolver.Resolve(unit, diagnostics);
        TypeChecker.Check(unit, model, diagnostics);
        DefiniteAssignment.Analyze(unit, model, diagnostics);
        return diagnostics;
    }

    private static string[] IdsOf(DiagnosticBag bag) => [.. bag.Sorted().Select(d => d.Id)];

    private static DiagnosticBag CheckBody(string body) =>
        Check($$"""
            shared model Program
                function Main(boolean flag)
            {{body}}
                end function
            end model
            """);

    // ---- Definite assignment ---------------------------------------------------------------

    [Test]
    public void ReadingAVariableBeforeItHasAValueIsRejected()
    {
        Assert.That(IdsOf(CheckBody(
            """
                    integer x;
                    let y = x;
            """)), Is.EqualTo(new[] { "PC0400" }));
    }

    [Test]
    public void AssigningFirstMakesItReadable()
    {
        Assert.That(IdsOf(CheckBody(
            """
                    integer x;
                    x = 1;
                    let y = x;
            """)), Is.Empty);
    }

    [Test]
    public void ParametersArriveHoldingValues()
    {
        Assert.That(IdsOf(CheckBody("        let copy = flag;")), Is.Empty);
    }

    [Test]
    public void AnInitializerCannotReadTheVariableItInitializes()
    {
        // Evaluation runs left to right, so the name is read before it holds anything.
        Assert.That(IdsOf(CheckBody(
            """
                    integer x;
                    x = x;
            """)), Is.EqualTo(new[] { "PC0400" }));
    }

    /// <summary>
    /// Only what every path guarantees survives a join, which is the whole point of the
    /// analysis.
    /// </summary>
    [Test]
    public void AssigningOnOnlyOneBranchIsNotEnough()
    {
        Assert.That(IdsOf(CheckBody(
            """
                    integer x;
                    if flag
                        x = 1;
                    end if
                    let y = x;
            """)), Is.EqualTo(new[] { "PC0401" }));
    }

    [Test]
    public void AssigningOnBothBranchesIsEnough()
    {
        Assert.That(IdsOf(CheckBody(
            """
                    integer x;
                    if flag
                        x = 1;
                    else
                        x = 2;
                    end if
                    let y = x;
            """)), Is.Empty);
    }

    [Test]
    public void EveryArmOfAnElseIfChainMustAssign()
    {
        Assert.Multiple(() =>
        {
            Assert.That(IdsOf(CheckBody(
                """
                        integer x;
                        if flag
                            x = 1;
                        else if not flag
                            x = 2;
                        else
                            x = 3;
                        end if
                        let y = x;
                """)), Is.Empty);

            // Without the else, nothing may have matched.
            Assert.That(IdsOf(CheckBody(
                """
                        integer x;
                        if flag
                            x = 1;
                        else if not flag
                            x = 2;
                        end if
                        let y = x;
                """)), Is.EqualTo(new[] { "PC0401" }));
        });
    }

    /// <summary>A loop body may run no times at all, so nothing it assigns can be relied on.</summary>
    [Test]
    public void ALoopBodyMayNotRun()
    {
        Assert.That(IdsOf(CheckBody(
            """
                    integer x;
                    loop while flag
                        x = 1;
                    end loop
                    let y = x;
            """)), Is.EqualTo(new[] { "PC0401" }));
    }

    [Test]
    public void ARangeLoopVariableHoldsAValueInsideTheBody()
    {
        Assert.That(IdsOf(CheckBody(
            """
                    loop for i = 1 to 10
                        let copy = i;
                    end loop
            """)), Is.Empty);
    }

    [Test]
    public void ASwitchWithoutADefaultMayMatchNothing()
    {
        Assert.Multiple(() =>
        {
            Assert.That(IdsOf(CheckBody(
                """
                        integer code = 1;
                        integer x;
                        switch code
                            case 1:
                                x = 1;
                            case 2:
                                x = 2;
                        end switch
                        let y = x;
                """)), Is.EqualTo(new[] { "PC0401" }));

            Assert.That(IdsOf(CheckBody(
                """
                        integer code = 1;
                        integer x;
                        switch code
                            case 1:
                                x = 1;
                            default:
                                x = 2;
                        end switch
                        let y = x;
                """)), Is.Empty);
        });
    }

    [Test]
    public void APathThatCannotContinueDoesNotWeakenTheOther()
    {
        // The then-branch never falls through, so only the else-branch reaches the read.
        Assert.That(IdsOf(CheckBody(
            """
                    integer x;
                    if flag
                        yield;
                    else
                        x = 1;
                    end if
                    let y = x;
            """)), Is.Empty);
    }

    // ---- try, catch, finally ------------------------------------------------------------------

    /// <summary>
    /// The classic trap. An exception may be thrown before the assignment in the try ran, so
    /// a catch clause can rely only on what was known on the way in.
    /// </summary>
    [Test]
    public void ACatchClauseCannotRelyOnWhatTheTryAssigned()
    {
        Assert.That(IdsOf(CheckBody(
            """
                    integer x;
                    try
                        x = 1;
                    catch Exception problem
                        let y = x;
                    end try
            """)), Is.EqualTo(new[] { "PC0400" }));
    }

    /// <summary>
    /// The caught variable is the one thing a catch clause <em>can</em> rely on: catching is
    /// what gives it its value.
    /// </summary>
    [Test]
    public void TheCaughtVariableIsAssignedByTheCatch()
    {
        Assert.That(IdsOf(CheckBody(
            """
                    try
                        yield;
                    catch Exception problem
                        Console.WriteLine(problem.Message());
                    end try
            """)), Is.Empty);
    }

    [Test]
    public void AFinallyClauseCannotRelyOnWhatTheTryAssignedEither()
    {
        Assert.That(IdsOf(CheckBody(
            """
                    integer x;
                    try
                        x = 1;
                    finally
                        let y = x;
                    end try
            """)), Is.EqualTo(new[] { "PC0400" }));
    }

    /// <summary>
    /// The other half of the trap: a finally clause runs whichever way the try turned out, so
    /// what it assigns really is certain afterwards.
    /// </summary>
    [Test]
    public void WhatAFinallyClauseAssignsIsCertainAfterwards()
    {
        // The try body must be able to finish, or there is no afterwards to be certain about.
        Assert.That(IdsOf(CheckBody(
            """
                    integer x;
                    try
                        Console.WriteLine("trying");
                    finally
                        x = 1;
                    end try
                    let y = x;
            """)), Is.Empty);
    }

    [Test]
    public void AssigningInBothTryAndEveryCatchIsEnough()
    {
        Assert.That(IdsOf(CheckBody(
            """
                    integer x;
                    try
                        x = 1;
                    catch Exception problem
                        x = 2;
                    end try
                    let y = x;
            """)), Is.Empty);
    }

    [Test]
    public void AssigningInTheTryButNotEveryCatchIsNotEnough()
    {
        Assert.That(IdsOf(CheckBody(
            """
                    integer x;
                    try
                        x = 1;
                    catch ArgumentException a
                        x = 2;
                    catch Exception b
                        yield 0;
                    end try
                    let y = x;
            """)), Does.Contain("PC0400").Or.Contain("PC0318"));
    }

    // ---- Constructors -------------------------------------------------------------------------

    [Test]
    public void AConstructorMustGiveEveryFieldAValue()
    {
        Assert.That(IdsOf(Check(
            """
            model Point
                integer x;
                integer y;

                public function Point(integer a)
                    this.x = a;
                end function
            end model
            """)), Is.EqualTo(new[] { "PC0402" }));
    }

    [Test]
    public void AConstructorThatAssignsEveryFieldIsFine()
    {
        Assert.That(IdsOf(Check(
            """
            model Point
                integer x;
                integer y;

                public function Point(integer a, integer b)
                    this.x = a;
                    this.y = b;
                end function
            end model
            """)), Is.Empty);
    }

    [Test]
    public void AFieldWithAnInitializerNeedsNothingFromTheConstructor()
    {
        Assert.That(IdsOf(Check(
            """
            model Counter
                integer count = 0;

                public function Counter()
                end function
            end model
            """)), Is.Empty);
    }

    /// <summary>
    /// The exemption that makes a self-referential model constructible. A Node whose 'next'
    /// had to be assigned would have no base case; an optional one is already a value.
    /// </summary>
    [Test]
    public void AnOptionalFieldNeedsNothingFromTheConstructor()
    {
        Assert.That(IdsOf(Check(
            """
            model Node
                integer value;
                Node? next;

                public function Node(integer v)
                    this.value = v;
                end function
            end model
            """)), Is.Empty);
    }

    // ---- Optional narrowing ----------------------------------------------------------------------

    [Test]
    public void AnOptionalCannotBeReadWithoutProvingPresence()
    {
        Assert.That(IdsOf(CheckBody(
            """
                    integer? maybe;
                    integer definite = maybe;
            """)), Is.EqualTo(new[] { "PC0329" }));
    }

    /// <summary>
    /// The point of the whole exercise: inside the guarded block the optional reads as its
    /// underlying type, with no unwrapping written anywhere.
    /// </summary>
    [Test]
    public void ProvingPresenceNarrowsInsideTheGuardedBlock()
    {
        Assert.That(IdsOf(CheckBody(
            """
                    integer? maybe;
                    if maybe.HasValue()
                        integer definite = maybe;
                    end if
            """)), Is.Empty);
    }

    [Test]
    public void NarrowingDoesNotEscapeTheBlockItWasProvenFor()
    {
        Assert.That(IdsOf(CheckBody(
            """
                    integer? maybe;
                    if maybe.HasValue()
                        integer inside = maybe;
                    end if
                    integer outside = maybe;
            """)), Is.EqualTo(new[] { "PC0329" }));
    }

    [Test]
    public void NegationNarrowsTheOtherBranch()
    {
        Assert.That(IdsOf(CheckBody(
            """
                    integer? maybe;
                    if not maybe.HasValue()
                        yield;
                    else
                        integer definite = maybe;
                    end if
            """)), Is.Empty);
    }

    [Test]
    public void AnAndCarriesBothChecksIntoTheBody()
    {
        Assert.That(IdsOf(CheckBody(
            """
                    integer? a;
                    integer? b;
                    if a.HasValue() and b.HasValue()
                        integer x = a;
                        integer y = b;
                    end if
            """)), Is.Empty);
    }

    [Test]
    public void AnOrProvesOnlyWhatBothSidesShare()
    {
        // Either check may have been the one that held, so neither is certain.
        Assert.That(IdsOf(CheckBody(
            """
                    integer? a;
                    integer? b;
                    if a.HasValue() or b.HasValue()
                        integer x = a;
                    end if
            """)), Is.EqualTo(new[] { "PC0329" }));
    }

    [Test]
    public void AWhileConditionNarrowsItsBody()
    {
        Assert.That(IdsOf(CheckBody(
            """
                    integer? maybe;
                    loop while maybe.HasValue()
                        integer definite = maybe;
                    end loop
            """)), Is.Empty);
    }

    [Test]
    public void TheConditionalExpressionNarrowsItsBranches()
    {
        Assert.That(IdsOf(CheckBody(
            """
                    integer? maybe;
                    integer value = if maybe.HasValue() then maybe else 0;
            """)), Is.Empty);
    }

    /// <summary>
    /// Narrowing is a convenience, not a removal. The optional's own members must stay
    /// reachable, so writing the unwrapping out anyway still works.
    /// </summary>
    [Test]
    public void TheOptionalMembersRemainAvailableInsideAGuardedBlock()
    {
        Assert.That(IdsOf(CheckBody(
            """
                    integer? maybe;
                    if maybe.HasValue()
                        integer definite = maybe.Value();
                    end if
            """)), Is.Empty);
    }

    [Test]
    public void AssigningAPlainValueProvesPresence()
    {
        Assert.That(IdsOf(CheckBody(
            """
                    integer? maybe;
                    maybe = 1;
                    integer definite = maybe;
            """)), Is.Empty);
    }

    /// <summary>
    /// A field is never narrowed. Any call in between could replace it, so a check made
    /// before one says nothing about after it. Kotlin declines to narrow mutable properties
    /// for the same reason.
    /// </summary>
    [Test]
    public void AFieldIsNeverNarrowed()
    {
        Assert.That(IdsOf(Check(
            """
            model Holder
                integer? maybe;

                function Run()
                    if this.maybe.HasValue()
                        integer definite = this.maybe;
                    end if
                end function
            end model
            """)), Is.EqualTo(new[] { "PC0329" }));
    }

    [Test]
    public void CopyingAFieldIntoALocalIsTheWayAround()
    {
        Assert.That(IdsOf(Check(
            """
            model Holder
                integer? maybe;

                function Run()
                    let copy = this.maybe;
                    if copy.HasValue()
                        integer definite = copy;
                    end if
                end function
            end model
            """)), Is.Empty);
    }

    // ---- Robustness -------------------------------------------------------------------------------

    [Test]
    public void AnalysisNeverThrows()
    {
        string[] hostile =
        [
            "", "model M end model",
            "model M function F() integer x; end function end model",
            "model M function F() try finally end try end function end model",
            "model M function F() switch 1 end switch end function end model",
            "shared model Program function Main() let x = ; end function end model",
        ];

        foreach (string source in hostile)
        {
            Assert.DoesNotThrow(() => Check(source), $"analyzing \"{source}\" threw");
        }
    }

    // ---- Reaching a result ---------------------------------------------------------------

    /// <summary>Wraps a member in a program, for the cases about one function's shape.</summary>
    private static DiagnosticBag CheckMember(string member) =>
        Check($$"""
            shared model Program
            {{member}}
                function Main()
                end function
            end model
            """);

    [Test]
    public void AFunctionThatDeclaresAResultMustReachOne() =>
        Assert.That(
            IdsOf(CheckMember("""
                    string function Describe()
                    end function
                """)),
            Is.EqualTo(new[] { "PC0404" }));

    // ---- A loop with no condition ------------------------------------------------------------

    /// <summary>
    /// <para>A loop left only by <c>yield</c> has no way of falling out of the bottom, so a
    /// function may end in one and still satisfy the rule that every path yields.</para>
    /// <para>This is the whole of what the construct buys beyond reading well, and it is why it
    /// is its own kind rather than a <c>while</c> whose condition happens to be <c>true</c>.
    /// </para>
    /// </summary>
    [Test]
    public void AConditionlessLoopLeftByYieldNeedsNothingAfterIt() =>
        Assert.That(
            IdsOf(CheckMember("""
                    integer function FirstBigEnough(integer[] values)
                        integer at = 0;

                        loop
                            if values[at] > 10
                                yield values[at];
                            end if

                            at = at + 1;
                        end loop
                    end function
                """)),
            Is.Empty);

    /// <summary>
    /// A <c>break</c> leaves the loop rather than the function, so what follows it runs and the
    /// function must still produce a result.
    /// </summary>
    [Test]
    public void AConditionlessLoopLeftByBreakStillHasToYieldAfterwards() =>
        Assert.That(
            IdsOf(CheckMember("""
                    integer function Counted()
                        loop
                            break;
                        end loop
                    end function
                """)),
            Is.EqualTo(new[] { "PC0404" }));

    /// <summary>
    /// <para>A loop nothing can end is an opinion, and it suppresses nothing.</para>
    /// <para>Both are reported because they say different things: <c>PC0406</c> is about the
    /// loop having no way out, and <c>PC0404</c> is about a function promising an integer and
    /// having no path that produces one. Silencing the second because of the first would let a
    /// broken promise compile clean.</para>
    /// </summary>
    [Test]
    public void ALoopNothingCanEndIsAnOpinionAndStillBreaksThePromise() =>
        Assert.That(
            IdsOf(CheckMember("""
                    integer function Never()
                        loop
                            Console.WriteLine("on and on");
                        end loop
                    end function
                """)),
            Is.EqualTo(new[] { "PC0404", "PC0406" }));

    /// <summary>
    /// The same loop in a function promising nothing is the opinion alone — which is the case
    /// a program that means to run until it is stopped from outside actually writes.
    /// </summary>
    [Test]
    public void ALoopNothingCanEndIsOnlyAnOpinionWhereNothingWasPromised() =>
        Assert.That(
            IdsOf(CheckMember("""
                    function Serve()
                        loop
                            Console.WriteLine("on and on");
                        end loop
                    end function
                """)),
            Is.EqualTo(new[] { "PC0406" }));

    /// <summary>
    /// A <c>break</c> belonging to a nested loop does not count as a way out of the outer one,
    /// which is the case that makes the check a walk rather than a search.
    /// </summary>
    [Test]
    public void ABreakBelongingToANestedLoopDoesNotCountAsAWayOut() =>
        Assert.That(
            IdsOf(CheckMember("""
                    function Serve()
                        loop
                            loop for i = 1 to 3
                                break;
                            end loop
                        end loop
                    end function
                """)),
            Is.EqualTo(new[] { "PC0406" }));

    [Test]
    public void YieldingOnOnlyOneBranchIsNotEnough() =>
        Assert.That(
            IdsOf(CheckMember("""
                    string function Grade(integer score)
                        if score >= 60
                            yield "pass";
                        end if
                    end function
                """)),
            Is.EqualTo(new[] { "PC0404" }));

    [Test]
    public void YieldingOnEveryBranchIsEnough() =>
        Assert.That(
            IdsOf(CheckMember("""
                    string function Grade(integer score)
                        if score >= 60
                            yield "pass";
                        else
                            yield "fail";
                        end if
                    end function
                """)),
            Is.Empty);

    /// <summary>A function that always throws never reaches its end, so it needs no yield.</summary>
    [Test]
    public void ThrowingInsteadOfYieldingIsEnough() =>
        Assert.That(
            IdsOf(CheckMember("""
                    string function Never()
                        throw new ArgumentException("no");
                    end function
                """)),
            Is.Empty);

    [Test]
    public void AFunctionThatDeclaresNoResultNeedsNoYield() =>
        Assert.That(
            IdsOf(CheckMember("""
                    function Shout()
                        Console.WriteLine("hi");
                    end function
                """)),
            Is.Empty);

    /// <summary>A constructor produces its instance rather than a result, so it is exempt.</summary>
    [Test]
    public void AConstructorNeedsNoYield() =>
        Assert.That(
            IdsOf(Check("""
                model Account
                    integer balance;

                    public function Account(integer opening)
                        this.balance = opening;
                    end function
                end model
                """)),
            Is.Empty);

    // ---- Nothing is not a value ------------------------------------------------------------

    /// <summary>
    /// <c>Console.WriteLine</c> takes a value of any kind, which still means a value. A call
    /// that produced none is refused rather than shown as though it had one.
    /// </summary>
    [Test]
    public void ACallThatProducedNothingCannotBeWritten() =>
        Assert.That(
            IdsOf(Check("""
                shared model Program
                    function Main()
                        integer[] xs = {1, 2};
                        Console.WriteLine(xs.InsertAt(0, 9));
                    end function
                end model
                """)),
            Is.EqualTo(new[] { "PC0332" }));

    [Test]
    public void ACallThatProducedNothingCannotBeAnArgument() =>
        Assert.That(
            IdsOf(Check("""
                shared model Program
                    function Shout()
                    end function

                    function Main()
                        Console.Write(Program.Shout());
                    end function
                end model
                """)),
            Is.EqualTo(new[] { "PC0332" }));
}
