using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Semantics;
using ProfiC.Compiler.Text;
using ProfiC.Runtime;

namespace ProfiC.Tests.Interpreting;

/// <summary>
/// <para>The interpreter, tested the way a user meets it: give it a program, read what it
/// printed.</para>
/// <para>Assertions are on output rather than on internal state deliberately. The interpreter
/// exists to be the oracle the emitter is checked against, and an oracle is only useful if it
/// is tested through the same surface the emitter will be — what the program produced.</para>
/// </summary>
[TestFixture]
public sealed class InterpreterTests
{
    /// <summary>
    /// Compiles and runs a whole program, returning everything it wrote. Fails the test if the
    /// program does not check cleanly, so that a broken fixture never masquerades as a broken
    /// interpreter.
    /// </summary>
    /// <summary>
    /// Runs a program with lines waiting to be read, as though they had been typed or piped
    /// in. Reading past the last one yields nothing, which is what the end of input means.
    /// </summary>
    private static string RunReading(string source, params string[] lines) =>
        Run(source, new StringReader(string.Concat(lines.Select(line => line + "\n"))));

    private static string Run(string source, TextReader? input = null)
    {
        DiagnosticBag diagnostics = new();
        CompilationUnit unit = Parser.Parse(new SourceText(source, "<test>"), diagnostics);
        SemanticModel model = Resolver.Resolve(unit, diagnostics, requireEntryPoint: true);
        TypeChecker.Check(unit, model, diagnostics);
        DefiniteAssignment.Analyze(unit, model, diagnostics);

        Assert.That(
            diagnostics.Select(d => $"{d.Descriptor.Id}: {d.Message}"),
            Is.Empty,
            "the program should check cleanly before it is run");

        StringWriter output = new();
        ProfiC.Interpreter.Interpreter.Run(
            Lowering.Lower(unit, model), model, output, input ?? TextReader.Null);

        return output.ToString().ReplaceLineEndings("\n");
    }

    // ---- Constructing up a chain --------------------------------------------------------

    /// <summary>
    /// <para>A declared exception extending a declared exception, which is two <c>base(...)</c>
    /// calls in a row.</para>
    /// <para>Each one means the parent of the type whose constructor wrote it. Read off the
    /// instance instead, it would mean the parent of what is being made — the same answer at
    /// every level — and the second call would run the constructor that made it, forever.</para>
    /// </summary>
    [Test]
    public void ConstructionWalksUpADeclaredChain() => Assert.That(
        Run("""
            model TooBig extends Exception
                public function TooBig(string message)
                    base(message);
                end function
            end model

            model WayTooBig extends TooBig
                public function WayTooBig(string message)
                    base(message);
                end function
            end model

            shared model Program
                function Main()
                    try
                        throw new WayTooBig("far too big");
                    catch TooBig problem
                        Console.WriteLine("caught: " + problem.Message());
                    end try
                end function
            end model
            """),
        Is.EqualTo("caught: far too big\n"));

    /// <summary>The same for ordinary models, where the fields each level sets must all stick.</summary>
    [Test]
    public void EachConstructorInAChainRunsOnce() => Assert.That(
        Run("""
            model Root
                public string Trail;

                public function Root()
                    this.Trail = "root";
                end function
            end model

            model Middle extends Root
                public function Middle()
                    base();
                    this.Trail = this.Trail + " middle";
                end function
            end model

            model Leaf extends Middle
                public function Leaf()
                    base();
                    this.Trail = this.Trail + " leaf";
                end function
            end model

            shared model Program
                function Main()
                    Console.WriteLine(new Leaf().Trail);
                end function
            end model
            """),
        Is.EqualTo("root middle leaf\n"));

    // ---- Reading what somebody typed --------------------------------------------------

    /// <summary>
    /// <para>What <c>Console.Read</c> does, which nothing exercised until now.</para>
    /// <para>It yields an optional because the end of input is an answer rather than a fault,
    /// and these are the three cases that matter: a line is there, the input has run out, and
    /// what was there does not read as what was wanted.</para>
    /// </summary>
    [Test]
    public void ReadGivesBackTheLineThatWasTyped() => Assert.That(
        RunReading(
            """
            shared model Program
                function Main()
                    Console.WriteLine(Console.Read().Or("nothing"));
                    Console.WriteLine(Console.Read().Or("nothing"));
                end function
            end model
            """,
            "first", "second"),
        Is.EqualTo("first\nsecond\n"));

    [Test]
    public void ReadGivesNothingOnceTheInputHasRunOut() => Assert.That(
        RunReading(
            """
            shared model Program
                function Main()
                    Console.WriteLine(Console.Read().HasValue());
                    Console.WriteLine(Console.Read().HasValue());
                end function
            end model
            """,
            "only one"),
        Is.EqualTo("true\nfalse\n"));

    [Test]
    public void ReadGivesNothingWhenThereWasNeverAnything() => Assert.That(
        Run(
            """
            shared model Program
                function Main()
                    Console.WriteLine(Console.Read().HasValue());
                end function
            end model
            """),
        Is.EqualTo("false\n"));

    /// <summary>
    /// The whole point of the parsing members: what arrives is text, and turning it into
    /// anything else is a second question that may also have no answer.
    /// </summary>
    [Test]
    public void WhatWasTypedIsReadIntoANumber() => Assert.That(
        RunReading(
            """
            shared model Program
                function Main()
                    integer? first = Console.Read().Or("").ToInteger();
                    integer? second = Console.Read().Or("").ToInteger();

                    Console.WriteLine(first.Or(-1) + " and " + second.Or(-1));
                end function
            end model
            """,
            "21", "banana"),
        Is.EqualTo("21 and -1\n"));

    /// <summary>
    /// Compiles and runs a program that is expected to warn, returning what it wrote and the
    /// identifiers it reported. Errors still fail the test; a warning is a program the compiler
    /// accepted, so it must still run.
    /// </summary>
    private static (string Output, string[] Ids) RunAllowingWarnings(string source)
    {
        DiagnosticBag diagnostics = new();
        CompilationUnit unit = Parser.Parse(new SourceText(source, "<test>"), diagnostics);
        SemanticModel model = Resolver.Resolve(unit, diagnostics, requireEntryPoint: true);
        TypeChecker.Check(unit, model, diagnostics);
        DefiniteAssignment.Analyze(unit, model, diagnostics);

        Assert.That(
            diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                       .Select(d => $"{d.Id}: {d.Message}"),
            Is.Empty,
            "the program should have no errors");

        StringWriter output = new();
        ProfiC.Interpreter.Interpreter.Run(Lowering.Lower(unit, model), model, output);

        return (output.ToString().ReplaceLineEndings("\n"),
                [.. diagnostics.Sorted().Select(d => d.Id)]);
    }

    /// <summary>Runs statements inside <c>Main</c>, which is what most of these tests want.</summary>
    private static string RunBody(string body) => Run($$"""
        shared model Program
            function Main()
        {{body}}
            end function
        end model
        """);

    /// <summary>The lines a program printed, with the trailing blank one dropped.</summary>
    private static string[] Lines(string output) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Runs an expression and returns what printing it produced.</summary>
    private static string Print(string expression) =>
        RunBody($"        Console.WriteLine({expression});").TrimEnd('\n');

    // ---- Arithmetic -------------------------------------------------------------------------

    [TestCase("1 + 2", "3")]
    [TestCase("7 - 10", "-3")]
    [TestCase("6 * 7", "42")]
    [TestCase("7 / 2", "3")]
    [TestCase("-7 / 2", "-3")]
    [TestCase("7 % 3", "1")]
    [TestCase("2 - -1", "3")]
    [TestCase("1 + 2 * 3", "7")]
    [TestCase("(1 + 2) * 3", "9")]
    public void IntegerArithmeticIsExact(string expression, string expected) =>
        Assert.That(Print(expression), Is.EqualTo(expected));

    [TestCase("1.5 + 2.25", "3.75")]
    [TestCase("7.0 / 2.0", "3.5")]
    public void RealArithmeticIsFloatingPoint(string expression, string expected) =>
        Assert.That(Print(expression), Is.EqualTo(expected));

    [TestCase("2 ^ 10", "1024")]
    [TestCase("7 ^ 2", "49")]
    [TestCase("5 ^ 0", "1")]
    [TestCase("2 ^ 3 ^ 2", "512")]
    [TestCase("-2 ^ 2", "-4")]
    [TestCase("2 * 3 ^ 2", "18")]
    [TestCase("10 ^ 2 - 2 / 2 + 5", "104")]
    public void RaisingAnIntegerToAPowerStaysAnInteger(string expression, string expected) =>
        Assert.That(Print(expression), Is.EqualTo(expected));

    [TestCase("(1|2) ^ 3", "1|8")]
    [TestCase("(1|2) ^ -3", "8|1")]
    [TestCase("(2|3) ^ 2", "4|9")]
    [TestCase("(1|2) ^ 0", "1|1")]
    public void RaisingAFractionToAWholePowerStaysExact(string expression, string expected) =>
        Assert.That(Print(expression), Is.EqualTo(expected));

    /// <summary>
    /// A fraction exponent is a root, so it reads the way it is written on paper. The answer
    /// is a real, and is the same as spelling the exponent out in reals.
    /// </summary>
    [TestCase("9 ^ 1|2", "3")]
    [TestCase("8 ^ 1|3", "2")]
    [TestCase("16 ^ 3|4", "8")]
    [TestCase("32 ^ 1|5", "2")]
    [TestCase("(1|4) ^ 1|2", "0.5")]
    public void AFractionExponentTakesARoot(string expression, string expected) =>
        Assert.That(Print(expression), Is.EqualTo(expected));

    [Test]
    public void BothSpellingsOfAnExponentAgree() => Assert.That(
        Print("2 ^ 1|3"), Is.EqualTo(Print("2.0 ^ (1.0 / 3.0)")));

    [Test]
    public void AnIntegerPowerTooLargeToHoldFailsRatherThanWrapping() =>
        Assert.That(() => Print("2 ^ 100"), Throws.InstanceOf<OverflowException>());

    /// <summary>
    /// A variable exponent cannot be checked while compiling, so it throws — and it throws
    /// something a program can catch, as dividing by a variable zero does.
    /// </summary>
    [Test]
    public void ANegativeExponentFromAVariableThrowsAndIsCatchable() => Assert.That(
        RunBody("""
                integer e = -1;
                try
                    Console.WriteLine(2 ^ e);
                catch Exception problem
                    Console.WriteLine("caught");
                end try
        """),
        Is.EqualTo("caught\n"));

    /// <summary>
    /// A fraction literal is two numerals fixed when the program is written. This is how one
    /// is built from values that only exist while it runs, and what comes back is an ordinary
    /// fraction — reduced, with the sign on the numerator.
    /// </summary>
    [TestCase("Fraction.Create(1, 2)", "1|2")]
    [TestCase("Fraction.Create(6, 8)", "3|4")]
    [TestCase("Fraction.Create(-3, -9)", "1|3")]
    [TestCase("Fraction.Create(5, -10)", "-1|2")]
    [TestCase("Fraction.Create(4, 1)", "4|1")]
    [TestCase("Fraction.Create(0, 5)", "0|1")]
    public void AFractionCanBeBuiltFromValues(string expression, string expected) =>
        Assert.That(Print(expression), Is.EqualTo(expected));

    [Test]
    public void ABuiltFractionIsTheSameValueAsTheLiteral() => Assert.That(
        Print("Fraction.Create(5, -10) == -1|2"), Is.EqualTo("true"));

    [Test]
    public void ABuiltFractionTakesPartInArithmetic() => Assert.That(
        RunBody("""
                let half = Fraction.Create(1, 2);
                Console.WriteLine(half + 1|3);
                Console.WriteLine(half ^ 3);
        """),
        Is.EqualTo("5|6\n1|8\n"));

    /// <summary>
    /// A literal zero denominator is refused while compiling. Only one the compiler cannot
    /// see reaches here, and then it throws what dividing by zero throws.
    /// </summary>
    [Test]
    public void AZeroDenominatorFromAVariableThrows() => Assert.That(
        () => RunBody("""
                integer d = 0;
                Console.WriteLine(Fraction.Create(1, d));
        """),
        Throws.InstanceOf<DivideByZeroException>());

    [TestCase("Math.Min(3, 7)", "3")]
    [TestCase("Math.Max(3, 7)", "7")]
    [TestCase("Math.Min(-2, -9)", "-9")]
    [TestCase("Math.Sqrt(16.0)", "4")]
    [TestCase("Math.Abs(-3.5)", "3.5")]
    [TestCase("Math.Floor(3.7)", "3")]
    [TestCase("Math.Ceiling(3.2)", "4")]
    [TestCase("Math.Pow(2.0, 8.0)", "256")]
    public void MathAnswersRatherThanProducingNothing(string expression, string expected) =>
        Assert.That(Print(expression), Is.EqualTo(expected));

    [TestCase("1|3 + 1|6", "1|2")]
    [TestCase("1|2 * 2|3", "1|3")]
    [TestCase("1|2 - 1|2", "0|1")]
    [TestCase("2|4", "1|2")]
    [TestCase("1|3 / 1|3", "1|1")]
    public void FractionArithmeticStaysExact(string expression, string expected) =>
        Assert.That(Print(expression), Is.EqualTo(expected));

    [Test]
    public void AFractionIsNotSilentlyRounded() =>
        Assert.That(Print("1|3 + 1|3 + 1|3"), Is.EqualTo("1|1"));

    [TestCase("true and false", "false")]
    [TestCase("true or false", "true")]
    [TestCase("not true", "false")]
    [TestCase("1 < 2", "true")]
    [TestCase("2 <= 2", "true")]
    [TestCase("3 > 4", "false")]
    [TestCase("\"a\" == \"a\"", "true")]
    [TestCase("'a' != 'b'", "true")]
    public void ComparisonsAndLogicProduceBooleans(string expression, string expected) =>
        Assert.That(Print(expression), Is.EqualTo(expected));

    [Test]
    public void AndShortCircuits() => Assert.That(
        RunBody("""
                integer calls = 0;
                boolean delegate(integer) note = (n) yield n > 0;
                if false and note(1)
                    Console.WriteLine("unreachable");
                end if
                Console.WriteLine("survived");
        """),
        Is.EqualTo("survived\n"));

    // ---- Strings ----------------------------------------------------------------------------

    [TestCase("\"ab\" + \"cd\"", "abcd")]
    [TestCase("\"n is \" + 5", "n is 5")]
    [TestCase("\"exact: \" + 1|2", "exact: 1|2")]
    [TestCase("\"yes: \" + true", "yes: true")]
    public void ConcatenationConvertsTheOtherSide(string expression, string expected) =>
        Assert.That(Print(expression), Is.EqualTo(expected));

    [TestCase("\"hello\".Count()", "5")]
    [TestCase("\"hello\".Substring(1, 3)", "ell")]
    [TestCase("\"hello\".IndexOf(\"ll\")", "2")]
    [TestCase("\"hello\".Contains(\"ell\")", "true")]
    public void StringMembersWork(string expression, string expected) =>
        Assert.That(Print(expression), Is.EqualTo(expected));

    // ---- Sets -------------------------------------------------------------------------------

    [Test]
    public void ASetIsIndexedAndCounted() => Assert.That(
        RunBody("""
                integer[] xs = {10, 20, 30};
                Console.WriteLine(xs.Count());
                Console.WriteLine(xs[0]);
                Console.WriteLine(xs[2]);
        """),
        Is.EqualTo("3\n10\n30\n"));

    [Test]
    public void ASetGrowsAndShrinks() => Assert.That(
        RunBody("""
                integer[] xs = {1};
                xs.Insert(2);
                xs.Insert(3);
                Console.WriteLine(xs.Count());
                xs.RemoveAt(0);
                Console.WriteLine(xs[0]);
                Console.WriteLine(xs.Contains(3));
        """),
        Is.EqualTo("3\n2\ntrue\n"));

    [Test]
    public void IndexingPastTheEndFails() => Assert.That(
        () => RunBody("""
                integer[] xs = {1};
                integer i = 5;
                Console.WriteLine(xs[i]);
        """),
        Throws.InstanceOf<IndexOutOfRangeException>());

    // ---- Control flow -----------------------------------------------------------------------

    [Test]
    public void IfElseIfElseTakesExactlyOneBranch() => Assert.That(
        RunBody("""
                for n = 1 to 3
                    if n == 1
                        Console.WriteLine("one");
                    else if n == 2
                        Console.WriteLine("two");
                    else
                        Console.WriteLine("many");
                    end if
                end for
        """),
        Is.EqualTo("one\ntwo\nmany\n"));

    [Test]
    public void WhileRunsUntilItsConditionFails() => Assert.That(
        RunBody("""
                integer n = 3;
                while n > 0
                    Console.WriteLine(n);
                    n = n - 1;
                end while
        """),
        Is.EqualTo("3\n2\n1\n"));

    [Test]
    public void ForToIsInclusiveAndForUntilIsNot() => Assert.That(
        RunBody("""
                for i = 1 to 3
                    Console.Write(i);
                end for
                Console.WriteLine();
                for i = 1 until 3
                    Console.Write(i);
                end for
                Console.WriteLine();
        """),
        Is.EqualTo("123\n12\n"));

    [Test]
    public void ANegativeStepCountsDown() => Assert.That(
        RunBody("""
                for i = 3 until 0 stepby -1
                    Console.Write(i);
                end for
                Console.WriteLine();
        """),
        Is.EqualTo("321\n"));

    /// <summary>
    /// <para>The bound is read again at the top of every turn, so a loop counts as far as its
    /// header says now rather than as far as it said when the loop began.</para>
    /// <para>The bound here shrinks, which ends the loop early. A bound that grows never ends
    /// it at all, exactly as a C-style <c>for (int i = 0; i &lt; x; i++) x++;</c> never ends —
    /// which is the point: a header that reads as a condition behaves as one.</para>
    /// </summary>
    [Test]
    public void TheBoundIsReadAgainOnEveryTurn() => Assert.That(
        RunBody("""
                integer limit = 10;

                for i = 1 until limit
                    Console.Write(i);
                    limit = limit - 2;
                end for

                Console.WriteLine();
                Console.WriteLine("limit " + limit);
        """),
        Is.EqualTo("123\nlimit 4\n"));

    /// <summary>
    /// <para>And so is the step, at the same moment. One turn reads the header once: the step
    /// that decided whether this turn runs is the step that advances to the next.</para>
    /// <para>Written with a variable actually called <c>step</c>, which the keyword being
    /// <c>stepby</c> is what makes possible.</para>
    /// </summary>
    [Test]
    public void SoIsTheStep() => Assert.That(
        RunBody("""
                integer step = 2;

                for i = 0 until 10 stepby step
                    Console.Write(i);
                    Console.Write(" ");
                    step = step + 5;
                end for

                Console.WriteLine();
        """),
        Is.EqualTo("0 2 9 \n"));

    /// <summary>
    /// <para>A sequence is taken as it stands when the loop begins, so changing it during the
    /// walk is refused — through a second name as much as through the one the loop wrote.
    /// </para>
    /// <para><c>PC0243</c> catches the name the loop names, and stops at the first thing it
    /// cannot see through. This is what catches the rest: the set itself knows a walk is
    /// running, so no path to it matters.</para>
    /// </summary>
    [Test]
    public void ChangingAWalkedSequenceThroughAnotherNameIsRefusedAtRunTime() => Assert.That(
        Assert.Throws<SequenceChangedException>(() => RunBody("""
                integer[] items = {1, 2, 3};
                integer[] alias = items;

                for each item in items
                    alias.Insert(99);
                end for
        """))!.Message,
        Does.Contain("walking it"));

    // ---- Sets of sets -----------------------------------------------------------------------

    /// <summary>
    /// <para>A set of sets needs nothing added: <c>[]</c> applies to a type already ending in
    /// one, and indexing is that same operation twice.</para>
    /// <para>Pinned three deep as well as two, because nothing in the implementation counts
    /// the brackets and a rule that stopped at two would be a surprising place to stop.</para>
    /// </summary>
    [Test]
    public void SetsNestToAnyDepth() => Assert.That(
        RunBody("""
                integer[][] grid = {{1, 2}, {3, 4}};
                integer[][][] cube = {{{1, 2}, {3, 4}}, {{5, 6}, {7, 8}}};

                grid[0][1] = 9;

                Console.WriteLine(grid[0][1] + " " + grid[1][0]);
                Console.WriteLine(cube[1][0][1] + " " + cube.Count() + " " + cube[0].Count());
        """),
        Is.EqualTo("9 3\n6 2 2\n"));

    /// <summary>
    /// <para>Rows are sets in their own right, so a grid may be ragged and a row handed out is
    /// the row itself rather than a copy of it.</para>
    /// <para>This is the property the fixed-shape kind will exist to remove, so it is worth
    /// stating as behavior rather than leaving as something nobody happened to try. A change
    /// that quietly made rows uniform would break no other test here.</para>
    /// </summary>
    [Test]
    public void RowsOfAGridAreTheirOwnSets() => Assert.That(
        RunBody("""
                integer[][] ragged = {{1}, {2, 3}, {4, 5, 6}};

                for each row in ragged
                    Console.Write(row.Count() + " ");
                end for

                integer[] kept = ragged[1];
                kept.Insert(99);

                Console.WriteLine();
                Console.WriteLine("grown through the name it was given: " + ragged[1].Count());
        """),
        Is.EqualTo("1 2 3 \ngrown through the name it was given: 3\n"));

    // ---- Whose failure it is ----------------------------------------------------------------

    /// <summary>
    /// <para>A writer that fails on one line and carries the rest, so that a failure which is
    /// nobody's fault but ours can be produced on purpose.</para>
    /// <para>Selective rather than total: a writer that broke on everything would break the
    /// catch clause's own line too, and the test would pass whether or not the clause ran.</para>
    /// </summary>
    private sealed class FailingWriter : StringWriter
    {
        public override void Write(string? value)
        {
            if (value is not null && value.Contains("break", StringComparison.Ordinal))
            {
                throw new NotSupportedException("the world outside the program broke");
            }

            base.Write(value);
        }
    }

    /// <summary>
    /// <para>A fault that is not the program's cannot be caught by the program.</para>
    /// <para>Every .NET exception answers to <c>Exception</c>, so a clause naming it would take
    /// a bug in the interpreter as readily as a divide by zero — and having taken it, would
    /// report it as something the program did. The person who could fix it would never hear.
    /// </para>
    /// <para>Written with a broken writer because a real interpreter bug is by definition one
    /// nobody knows about; this stands in for one, and asks the question that matters, which is
    /// whether a catch clause can tell whose failure it has.</para>
    /// </summary>
    [Test]
    public void ACatchDoesNotTakeAFailureTheProgramDidNotCause()
    {
        DiagnosticBag diagnostics = new();

        CompilationUnit unit = Parser.Parse(
            new SourceText("""
                shared model Program
                    function Main()
                        try
                            Console.WriteLine("break the writer");
                        catch Exception e
                            Console.WriteLine("caught");
                        end try
                    end function
                end model
                """, "<test>"),
            diagnostics);

        SemanticModel model = Resolver.Resolve(unit, diagnostics, requireEntryPoint: true);
        TypeChecker.Check(unit, model, diagnostics);
        DefiniteAssignment.Analyze(unit, model, diagnostics);

        Assert.That(diagnostics, Is.Empty, "the program should check cleanly before it is run");

        Assert.Throws<NotSupportedException>(() => ProfiC.Interpreter.Interpreter.Run(
            Lowering.Lower(unit, model), model, new FailingWriter()));
    }

    /// <summary>
    /// The other side of the same rule: an exception the program threw itself is the program's,
    /// even where it is a bare <c>Exception</c> and so indistinguishable by type from a fault
    /// in the interpreter. What tells them apart is which of the two raised it.
    /// </summary>
    [Test]
    public void ACatchTakesABareExceptionTheProgramThrewItself() => Assert.That(
        RunBody("""
                try
                    throw new Exception("mine");
                catch Exception e
                    Console.WriteLine("caught " + e.Message());
                end try
        """),
        Is.EqualTo("caught mine\n"));

    /// <summary>
    /// <para>One the program threw and did not catch is described, not dumped.</para>
    /// <para>A declared exception has a carrier that says as much on the way up. A bare
    /// <c>Exception</c> has none, so nothing marked it as the program's and the top of the run
    /// treated it as a fault in the interpreter — which meant a .NET stack trace for a beginner
    /// whose only mistake was not catching what they threw.</para>
    /// </summary>
    [Test]
    public void AnUncaughtBareExceptionIsDescribedRatherThanDumped() => Assert.That(
        Assert.Throws<ProfiC.Interpreter.UncaughtProfiCException>(() => RunBody("""
                throw new Exception("mine to answer for");
        """))!.Message,
        Is.EqualTo("unhandled Exception: mine to answer for"));

    /// <summary>
    /// The mark is lifted however the walk ends, so a set left early by <c>break</c> can be
    /// changed straight afterwards.
    /// </summary>
    [Test]
    public void TheMarkIsLiftedWhenTheWalkEnds() => Assert.That(
        RunBody("""
                integer[] items = {1, 2, 3};

                for each item in items
                    Console.Write(item);
                    break;
                end for

                items.Insert(99);

                Console.WriteLine();
                Console.WriteLine("count " + items.Count());
        """),
        Is.EqualTo("1\ncount 4\n"));

    /// <summary>
    /// Two walks over one set are ordinary, and neither changes it. The mark is counted so the
    /// inner one ending does not leave the outer unguarded.
    /// </summary>
    [Test]
    public void ASetMayBeWalkedInsideItsOwnWalk() => Assert.That(
        RunBody("""
                integer[] items = {1, 2};

                for each left in items
                    for each right in items
                        Console.Write(left);
                        Console.Write(right);
                        Console.Write(" ");
                    end for
                end for

                Console.WriteLine();
        """),
        Is.EqualTo("11 12 21 22 \n"));

    [Test]
    public void ALoopWhoseBoundIsAlreadyPassedNeverRuns() => Assert.That(
        RunBody("""
                for i = 5 to 1
                    Console.WriteLine("unreachable");
                end for
                Console.WriteLine("done");
        """),
        Is.EqualTo("done\n"));

    [Test]
    public void BreakAndContinueApplyToTheNearestLoop() => Assert.That(
        RunBody("""
                for i = 1 to 10
                    if i == 2
                        continue;
                    end if
                    if i == 4
                        break;
                    end if
                    Console.Write(i);
                end for
                Console.WriteLine();
        """),
        Is.EqualTo("13\n"));

    [Test]
    public void SwitchHasNoFallthroughButCaseLabelsGroup() => Assert.That(
        RunBody("""
                for i = 1 to 4
                    switch i
                        case 1:
                            Console.WriteLine("one");
                        case 2:
                        case 3:
                            Console.WriteLine("two or three");
                        default:
                            Console.WriteLine("other");
                    end switch
                end for
        """),
        Is.EqualTo("one\ntwo or three\ntwo or three\nother\n"));

    [Test]
    public void TheConditionalExpressionYieldsOneSide() => Assert.That(
        RunBody("""
                for n = 1 to 2
                    Console.WriteLine(if n == 1 then "first" else "second");
                end for
        """),
        Is.EqualTo("first\nsecond\n"));

    // ---- for each, which only exists after lowering ------------------------------------------

    [Test]
    public void ForEachVisitsEveryElementInOrder() => Assert.That(
        RunBody("""
                integer[] xs = {10, 20, 30};
                integer total = 0;
                for each x in xs
                    Console.Write(x);
                    total = total + x;
                end for
                Console.WriteLine();
                Console.WriteLine(total);
        """),
        Is.EqualTo("102030\n60\n"));

    [Test]
    public void ForEachOverAnEmptySetRunsNothing() => Assert.That(
        RunBody("""
                integer[] xs = {};
                for each x in xs
                    Console.WriteLine("unreachable");
                end for
                Console.WriteLine("done");
        """),
        Is.EqualTo("done\n"));

    [Test]
    public void ForEachNestsWithoutTheTemporariesColliding() => Assert.That(
        RunBody("""
                integer[] xs = {1, 2};
                integer[] ys = {3, 4};
                for each x in xs
                    for each y in ys
                        Console.Write(x * y);
                    end for
                end for
                Console.WriteLine();
        """),
        Is.EqualTo("3468\n"));

    [Test]
    public void ForEachOverAStringVisitsCharacters() => Assert.That(
        RunBody("""
                character[] letters = "abc";
                for each c in letters
                    Console.Write(c);
                    Console.Write("-");
                end for
                Console.WriteLine();
        """),
        Is.EqualTo("a-b-c-\n"));

    /// <summary>
    /// The interpreter must run the <em>lowered</em> tree. Running the tree the resolver saw
    /// instead is silent rather than loud — <c>for each</c> simply does nothing — so this pins
    /// it directly.
    /// </summary>
    [Test]
    public void TheInterpreterRunsTheLoweredTreeInsideEveryFunction() => Assert.That(
        Run("""
            shared model Program
                function Main()
                    Console.WriteLine(Program.Sum());
                end function

                integer function Sum()
                    integer[] xs = {1, 2, 3};
                    integer total = 0;
                    for each x in xs
                        total = total + x;
                    end for
                    yield total;
                end function
            end model
            """),
        Is.EqualTo("6\n"),
        "a for each inside a non-entry function must run too");

    // ---- Functions --------------------------------------------------------------------------

    [Test]
    public void AFunctionYieldsAValueToItsCaller() => Assert.That(
        Run("""
            shared model Program
                function Main()
                    Console.WriteLine(Program.Double(21));
                end function

                integer function Double(integer n)
                    yield n * 2;
                end function
            end model
            """),
        Is.EqualTo("42\n"));

    [Test]
    public void RecursionTerminatesAtItsBaseCase() => Assert.That(
        Run("""
            shared model Program
                function Main()
                    Console.WriteLine(Program.Factorial(5));
                end function

                integer function Factorial(integer n)
                    if n <= 1
                        yield 1;
                    end if
                    yield n * Program.Factorial(n - 1);
                end function
            end model
            """),
        Is.EqualTo("120\n"));

    /// <summary>
    /// <para>Nesting too deep has a name, and says it is not the machine running out of room.
    /// </para>
    /// <para>Both halves matter. The name is what a reader is told, and it is not
    /// <c>StackOverflowException</c> because this is not one — the language counts calls and
    /// stops at its own number, long before the machine is anywhere near the end of its stack.
    /// </para>
    /// </summary>
    [Test]
    public void RunawayRecursionFailsWithAnExplanationRatherThanAStackOverflow() => Assert.That(
        () => Run("""
            shared model Program
                function Main()
                    Program.Forever(1);
                end function

                integer function Forever(integer n)
                    yield Program.Forever(n + 1);
                end function
            end model
            """),
        Throws.InstanceOf<RecursionTooDeepException>()
              .With.Message.Contains("not the machine running out of room"));

    /// <summary>
    /// <para>And no clause takes it, the root included.</para>
    /// <para>This is the one place the language breaks "a name it can raise is a name a program
    /// can catch", so it is the one place worth a test of its own: a change that quietly made
    /// this catchable would look like every other exception working.</para>
    /// </summary>
    [Test]
    public void NestingTooDeepIsNotCaughtByAnything() => Assert.That(
        () => Run("""
            shared model Program
                function Main()
                    try
                        Program.Forever(1);
                    catch Exception e
                        Console.WriteLine("caught");
                    end try
                end function

                integer function Forever(integer n)
                    yield Program.Forever(n + 1);
                end function
            end model
            """),
        Throws.InstanceOf<RecursionTooDeepException>());

    [Test]
    public void ALocalFunctionSeesTheEnclosingScope() => Assert.That(
        RunBody("""
                integer offset = 100;
                integer function Shift(integer n)
                    yield n + offset;
                end function
                Console.WriteLine(Shift(5));
        """),
        Is.EqualTo("105\n"));

    [Test]
    public void OverloadsAreSelectedByArgumentType() => Assert.That(
        Run("""
            shared model Program
                function Main()
                    Console.WriteLine(Program.Name(1));
                    Console.WriteLine(Program.Name("x"));
                end function

                string function Name(integer n)
                    yield "integer";
                end function

                string function Name(string s)
                    yield "string";
                end function
            end model
            """),
        Is.EqualTo("integer\nstring\n"));

    // ---- Lambdas and capture ------------------------------------------------------------------

    [Test]
    public void AnExpressionLambdaIsCalledLikeAFunction() => Assert.That(
        RunBody("""
                integer delegate(integer, integer) add = (a, b) yield a + b;
                Console.WriteLine(add(2, 3));
        """),
        Is.EqualTo("5\n"));

    /// <summary>
    /// A parameter left as a bare name runs as the type the surrounding code gave it, which is
    /// the point of settling it while checking rather than deciding anything at run time.
    /// </summary>
    [Test]
    public void ALambdaWithBareParameterNamesRuns() => Assert.That(
        RunBody("""
                integer delegate(integer, integer) add = (a, b) yield a + b;
                string delegate(string, integer) tag = function(text, count)
                    yield text + ":" + count;
                end function;

                Console.WriteLine(add(2, 3));
                Console.WriteLine(tag("n", add(1, 1)));
        """),
        Is.EqualTo("5\nn:2\n"));

    [Test]
    public void ABlockLambdaYieldsAValue() => Assert.That(
        RunBody("""
                integer delegate(integer) square = function(n)
                    yield n * n;
                end function;
                Console.WriteLine(square(7));
        """),
        Is.EqualTo("49\n"));

    [Test]
    public void CaptureIsByReferenceNotByValue() => Assert.That(
        RunBody("""
                integer captured = 10;
                integer delegate(integer) add = (n) yield n + captured;
                Console.WriteLine(add(5));
                captured = 100;
                Console.WriteLine(add(5));
        """),
        Is.EqualTo("15\n105\n"),
        "the lambda must see the variable, not a copy of its value");

    [Test]
    public void ALambdaCanBePassedToAFunction() => Assert.That(
        Run("""
            shared model Program
                function Main()
                    Console.WriteLine(Program.Apply((n) yield n * 3, 5));
                end function

                integer function Apply(integer delegate(integer) f, integer n)
                    yield f(n);
                end function
            end model
            """),
        Is.EqualTo("15\n"));

    // ---- Models -----------------------------------------------------------------------------

    [Test]
    public void AConstructorRunsAndFieldsPersist() => Assert.That(
        Run("""
            model Counter
                integer count;

                public function Counter(integer start)
                    this.count = start;
                end function

                public function Bump()
                    this.count = this.count + 1;
                end function

                public integer function Value()
                    yield this.count;
                end function
            end model

            shared model Program
                function Main()
                    Counter c = new Counter(5);
                    c.Bump();
                    c.Bump();
                    Console.WriteLine(c.Value());
                end function
            end model
            """),
        Is.EqualTo("7\n"));

    [Test]
    public void AFieldInitializerRunsBeforeTheConstructorBody() => Assert.That(
        Run("""
            model Box
                integer value = 41;

                public function Box()
                    this.value = this.value + 1;
                end function

                public integer function Value()
                    yield this.value;
                end function
            end model

            shared model Program
                function Main()
                    Console.WriteLine(new Box().Value());
                end function
            end model
            """),
        Is.EqualTo("42\n"));

    [Test]
    public void VirtualDispatchPicksTheOverride() => Assert.That(
        Run("""
            model Shape
                protected string name;

                public function Shape(string label)
                    this.name = label;
                end function

                public virtual string function Describe()
                    yield this.name;
                end function
            end model

            model Square extends Shape
                integer side;

                public function Square(integer length)
                    base("square");
                    this.side = length;
                end function

                public override string function Describe()
                    yield base.Describe() + " of side " + this.side;
                end function
            end model

            shared model Program
                function Main()
                    Shape s = new Square(4);
                    Console.WriteLine(s.Describe());
                end function
            end model
            """),
        Is.EqualTo("square of side 4\n"),
        "the runtime type decides, and base reaches the version that was overridden");

    [Test]
    public void AModelIsAReferenceAndIsSharedNotCopied() => Assert.That(
        Run("""
            model Holder
                public integer value;
            end model

            shared model Program
                function Main()
                    Holder a = new Holder();
                    Holder b = a;
                    b.value = 9;
                    Console.WriteLine(a.value);
                    Console.WriteLine(Reference.Equals(a, b));
                end function
            end model
            """),
        Is.EqualTo("9\ntrue\n"));

    [Test]
    public void ASharedModelHoldsStateAcrossCalls() => Assert.That(
        Run("""
            shared model Program
                shared integer seen = 0;

                function Main()
                    Program.Note();
                    Program.Note();
                    Console.WriteLine(Program.seen);
                end function

                function Note()
                    Program.seen = Program.seen + 1;
                end function
            end model
            """),
        Is.EqualTo("2\n"));

    // ---- Structures -------------------------------------------------------------------------

    [Test]
    public void AStructureIsCopiedOnAssignment() => Assert.That(
        Run("""
            structure Point
                public integer x;
                public integer y;
            end structure

            shared model Program
                function Main()
                    Point a = new Point();
                    a.x = 1;
                    Point b = a;
                    b.x = 99;
                    Console.WriteLine(a.x);
                    Console.WriteLine(b.x);
                end function
            end model
            """),
        Is.EqualTo("1\n99\n"),
        "assigning a structure copies it, which is the whole of value semantics");

    [Test]
    public void AStructureIsCopiedWhenPassedToAFunction() => Assert.That(
        Run("""
            structure Point
                public integer x;
            end structure

            shared model Program
                function Main()
                    Point p = new Point();
                    p.x = 1;
                    Program.Change(p);
                    Console.WriteLine(p.x);
                end function

                function Change(Point q)
                    q.x = 99;
                end function
            end model
            """),
        Is.EqualTo("1\n"));

    // ---- Enumerations -------------------------------------------------------------------------

    [Test]
    public void EnumerationMembersCompareAndPrintByName() => Assert.That(
        Run("""
            enumeration Color
                Red,
                Green,
                Blue
            end enumeration

            shared model Program
                function Main()
                    Color c = Color.Green;
                    Console.WriteLine(c);
                    Console.WriteLine(c == Color.Green);
                    Console.WriteLine(c == Color.Red);
                end function
            end model
            """),
        Is.EqualTo("Green\ntrue\nfalse\n"));

    // ---- Optionals --------------------------------------------------------------------------

    [Test]
    public void AnOptionalStartsEmptyAndOrSuppliesAFallback() => Assert.That(
        RunBody("""
                integer? missing;
                Console.WriteLine(missing.HasValue());
                Console.WriteLine(missing.Or(7));
        """),
        Is.EqualTo("false\n7\n"));

    [Test]
    public void AValueWidensIntoAnOptionalAndComesBackOut() => Assert.That(
        RunBody("""
                integer? present = 5;
                Console.WriteLine(present.HasValue());
                Console.WriteLine(present.Value());
        """),
        Is.EqualTo("true\n5\n"));

    [Test]
    public void ReadingAnEmptyOptionalFails() => Assert.That(
        () => RunBody("""
                integer? missing;
                Console.WriteLine(missing.Value());
        """),
        Throws.InstanceOf<EmptyOptionalException>());

    [Test]
    public void NarrowingLetsTheValueBeUsedDirectly() => Assert.That(
        RunBody("""
                integer? maybe = 12;
                if maybe.HasValue()
                    Console.WriteLine(maybe + 1);
                end if
        """),
        Is.EqualTo("13\n"));

    /// <summary>
    /// An optional holding a model <em>is</em> that model, so <c>Value</c> can mean either the
    /// optional's or the one the model declared. The declared one wins.
    /// </summary>
    [Test]
    public void ADeclaredMemberWinsOverTheOneTheLanguageProvides() => Assert.That(
        Run("""
            model Counter
                integer count;

                public function Counter(integer start)
                    this.count = start;
                end function

                public integer function Value()
                    yield this.count;
                end function
            end model

            shared model Program
                function Main()
                    Counter c = new Counter(7);
                    Console.WriteLine(c.Value());

                    Counter? maybe = c;
                    if maybe.HasValue()
                        Console.WriteLine(maybe.Value());
                    end if
                end function
            end model
            """),
        Is.EqualTo("7\n7\n"));

    [Test]
    public void CastingYieldsAnOptionalRatherThanFailing() => Assert.That(
        Run("""
            model Animal
            end model

            model Dog extends Animal
                public string function Speak()
                    yield "woof";
                end function
            end model

            model Cat extends Animal
            end model

            shared model Program
                function Main()
                    Animal a = new Dog();
                    Animal b = new Cat();
                    Console.WriteLine((a as Dog).HasValue());
                    Console.WriteLine((b as Dog).HasValue());
                    Console.WriteLine((a as Dog).Value().Speak());
                    Console.WriteLine(a is Dog);
                end function
            end model
            """),
        Is.EqualTo("true\nfalse\nwoof\ntrue\n"));

    // ---- Deep equality ----------------------------------------------------------------------

    [Test]
    public void EqualityOnSetsIsStructural() => Assert.That(
        RunBody("""
                integer[] a = {1, 2, 3};
                integer[] b = {1, 2, 3};
                integer[] c = {1, 2};
                Console.WriteLine(a == b);
                Console.WriteLine(a == c);
                Console.WriteLine(Reference.Equals(a, b));
        """),
        Is.EqualTo("true\nfalse\nfalse\n"));

    [Test]
    public void EqualityOnModelsComparesFields() => Assert.That(
        Run("""
            model Point
                public integer x;
                public integer y;
            end model

            shared model Program
                function Main()
                    Point a = new Point();
                    Point b = new Point();
                    a.x = 1;
                    b.x = 1;
                    Console.WriteLine(a == b);
                    b.y = 2;
                    Console.WriteLine(a == b);
                end function
            end model
            """),
        Is.EqualTo("true\nfalse\n"));

    [Test]
    public void EqualityTerminatesOnACycle() => Assert.That(
        Run("""
            model Node
                public Node? next;
            end model

            shared model Program
                function Main()
                    Node a = new Node();
                    Node b = new Node();
                    a.next = a;
                    b.next = b;
                    Console.WriteLine(a == b);
                end function
            end model
            """),
        Is.EqualTo("true\n"),
        "two identically shaped cycles are equal, and comparing them must not hang");

    // ---- Exceptions -------------------------------------------------------------------------

    [Test]
    public void CatchRunsForTheMatchingTypeAndFinallyAlwaysRuns() => Assert.That(
        RunBody("""
                integer zero = 0;
                try
                    Console.WriteLine(1 / zero);
                catch DivideByZeroException problem
                    Console.WriteLine("caught");
                finally
                    Console.WriteLine("finally");
                end try
        """),
        Is.EqualTo("caught\nfinally\n"));

    [Test]
    public void FinallyRunsEvenWhenNothingWentWrong() => Assert.That(
        RunBody("""
                try
                    Console.WriteLine("body");
                finally
                    Console.WriteLine("finally");
                end try
        """),
        Is.EqualTo("body\nfinally\n"));

    [Test]
    public void AThrownExceptionCarriesItsMessage() => Assert.That(
        RunBody("""
                try
                    throw new Exception("something specific");
                catch Exception problem
                    Console.WriteLine(problem.Message());
                end try
        """),
        Is.EqualTo("something specific\n"));

    [Test]
    public void AnUnmatchedCatchLetsTheExceptionPastIt() => Assert.That(
        () => RunBody("""
                integer zero = 0;
                try
                    Console.WriteLine(1 / zero);
                catch IndexOutOfRangeException problem
                    Console.WriteLine("wrong handler");
                end try
        """),
        Throws.InstanceOf<DivideByZeroException>());

    /// <summary>
    /// A declared exception is an ordinary model, not a .NET one, so it travels differently.
    /// Both kinds have to reach the same catch clause.
    /// </summary>
    [TestCase("NotFoundException", "specific")]
    [TestCase("Exception", "general")]
    public void ADeclaredExceptionIsThrownCaughtAndCarriesItsMessage(string caught, string label) =>
        Assert.That(
            Run($$"""
                model NotFoundException extends Exception
                    public function NotFoundException(string what)
                        base("could not find " + what);
                    end function
                end model

                shared model Program
                    function Main()
                        try
                            throw new NotFoundException("the key");
                        catch {{caught}} problem
                            Console.WriteLine("{{label}}: " + problem.Message());
                        end try
                    end function
                end model
                """),
            Is.EqualTo($"{label}: could not find the key\n"));

    [Test]
    public void ADeclaredExceptionThatNothingCatchesReachesTheTop() => Assert.That(
        () => Run("""
            model NotFoundException extends Exception
            end model

            shared model Program
                function Main()
                    try
                        throw new NotFoundException();
                    catch DivideByZeroException problem
                        Console.WriteLine("wrong handler");
                    end try
                end function
            end model
            """),
        Throws.Exception.With.Message.Contains("NotFoundException"));

    [Test]
    public void FinallyRunsWhileAnExceptionIsOnItsWayOut()
    {
        string output = string.Empty;

        Assert.That(
            () => output = RunBody("""
                    integer zero = 0;
                    try
                        try
                            Console.WriteLine(1 / zero);
                        finally
                            Console.WriteLine("inner finally");
                        end try
                    catch DivideByZeroException problem
                        Console.WriteLine("outer caught");
                    end try
            """),
            Throws.Nothing);

        Assert.That(output, Is.EqualTo("inner finally\nouter caught\n"));
    }

    // ---- Whole programs ------------------------------------------------------------------------

    [Test]
    public void HelloWorldRuns()
    {
        string path = Path.Combine(LexerTestBase.RepositoryRootForTests, "samples", "hello.pc");

        Assert.That(Run(File.ReadAllText(path)), Does.Contain("Hello, World!"));
    }

    [Test]
    public void ALongerProgramProducesTheWholeExpectedTranscript()
    {
        string[] lines = Lines(Run("""
            model Account
                string owner;
                integer balance;

                public function Account(string who, integer opening)
                    this.owner = who;
                    this.balance = opening;
                end function

                public function Deposit(integer amount)
                    if amount <= 0
                        throw new Exception("a deposit must be positive");
                    end if

                    this.balance = this.balance + amount;
                end function

                public virtual string function Describe()
                    yield this.owner + ": " + this.balance;
                end function
            end model

            model Savings extends Account
                public function Savings(string who, integer opening)
                    base(who, opening);
                end function

                public override string function Describe()
                    yield "savings " + base.Describe();
                end function
            end model

            shared model Program
                function Main()
                    Account[] accounts = {new Account("ada", 100), new Savings("alan", 50)};

                    for each account in accounts
                        account.Deposit(25);
                        Console.WriteLine(account.Describe());
                    end for

                    try
                        accounts[0].Deposit(-1);
                    catch Exception problem
                        Console.WriteLine("rejected: " + problem.Message());
                    end try

                    integer total = 0;
                    for each account in accounts
                        total = total + Program.BalanceOf(account);
                    end for

                    Console.WriteLine("total " + total);
                end function

                integer function BalanceOf(Account account)
                    let described = account.Describe();
                    yield described.Count();
                end function
            end model
            """));

        Assert.That(lines, Is.EqualTo(new[]
        {
            "ada: 125",
            "savings alan: 75",
            "rejected: a deposit must be positive",
            "total 24",
        }));
    }

    // ---- Testing and casting -----------------------------------------------------------------

    private const string Suits = """
        enumeration Suit
            Clubs,
            Diamonds,
            Hearts
        end enumeration
        """;

    /// <summary>
    /// <para>A value is of its own type — settled while compiling, not asked at run time.</para>
    /// <para>The types decide it, so the test is folded and warned about rather than being
    /// carried out. What is pinned here is that the settled answer reaches the running program
    /// intact, which it can only do by travelling on the node through lowering.</para>
    /// </summary>
    [Test]
    public void AValueIsOfItsOwnTypeAndTheAnswerIsSettledWhileCompiling()
    {
        (string output, string[] ids) = RunAllowingWarnings($$"""
            {{Suits}}

            structure Point
                public integer X;

                public function Point(integer x)
                    this.X = x;
                end function
            end structure

            shared model Program
                function Main()
                    Suit s = Suit.Hearts;
                    Point p = new Point(1);

                    Console.WriteLine(s is Suit);
                    Console.WriteLine((s as Suit).HasValue());
                    Console.WriteLine(p is Point);
                end function
            end model
            """);

        Assert.Multiple(() =>
        {
            Assert.That(Lines(output), Is.EqualTo(new[] { "true", "true", "true" }));
            Assert.That(ids, Is.EqualTo(new[] { "PC0334", "PC0334", "PC0334" }));
        });
    }

    /// <summary>
    /// <para>An exception the language provides answers <c>is</c> and <c>as</c> against what
    /// it actually is.</para>
    /// <para>One of these is a real .NET exception at run time rather than an instance of a
    /// declared model, so its inheritance is the runtime's. Before the runtime was asked, every
    /// such test came back false: an <c>ArgumentException</c> held in an <c>Exception</c>
    /// denied being an <c>ArgumentException</c>.</para>
    /// </summary>
    [Test]
    public void ABuiltInExceptionAnswersToWhatItIs() => Assert.That(
        Lines(Run("""
            shared model Program
                function Main()
                    Exception widened = new ArgumentException("bad");

                    Console.WriteLine(widened is ArgumentException);
                    Console.WriteLine(widened is OverflowException);
                    Console.WriteLine((widened as ArgumentException).HasValue());
                    Console.WriteLine((widened as OverflowException).HasValue());

                    try
                        throw new FormatException("nope");
                    catch Exception caught
                        Console.WriteLine(caught is FormatException);
                    end try
                end function
            end model
            """)),
        Is.EqualTo(new[] { "true", "false", "true", "false", "true" }));

    /// <summary>
    /// <para>An overloaded name reached through a type runs the version the checker chose.</para>
    /// <para>Finding one by name instead would pick whichever was written first in the
    /// catalog, so every <c>Math.Abs</c> would run the version taking integers and a real
    /// arriving at it would read as zero. The checker has already decided; this asks it.</para>
    /// </summary>
    [TestCase("Math.Abs(-3)", "3")]
    [TestCase("Math.Abs(-3.5)", "3.5")]
    [TestCase("Math.Abs(-3|4)", "3|4")]
    [TestCase("Math.Min(3, 7)", "3")]
    [TestCase("Math.Min(3.5, 7.5)", "3.5")]
    [TestCase("Math.Min(1|3, 1|2)", "1|3")]
    [TestCase("Math.Max(1|3, 1|2)", "1|2")]
    public void AnOverloadedBuiltInRunsTheVersionTheCheckerChose(string call, string expected) =>
        Assert.That(Print(call), Is.EqualTo(expected));

    /// <summary>
    /// Rounding lands on an integer, so the answer can be counted with. A real that happens to
    /// be whole prints the same either way, so these are asked where it is used as an index —
    /// which only compiles if it really is an integer.
    /// </summary>
    [Test]
    public void RoundingYieldsAnIntegerRatherThanAWholeReal() => Assert.That(
        Lines(RunBody("""
                string[] names = {"zero", "one", "two", "three", "four"};

                Console.WriteLine(names[Math.Floor(3.7)]);
                Console.WriteLine(names[Math.Ceiling(3.2)]);
                Console.WriteLine(names[Math.Round(2.5)]);
                Console.WriteLine(names[Math.Floor(9|2)]);
        """)),
        Is.EqualTo(new[] { "three", "four", "three", "four" }));

    /// <summary>
    /// A half goes away from zero and a floor goes down, which are the two answers a naive
    /// implementation gets wrong: .NET rounds a half to even, and integer division truncates.
    /// </summary>
    [TestCase("Math.Round(2.5)", "3")]
    [TestCase("Math.Round(3.5)", "4")]
    [TestCase("Math.Round(-2.5)", "-3")]
    [TestCase("Math.Floor(-7|2)", "-4")]
    [TestCase("Math.Ceiling(-7|2)", "-3")]
    public void RoundingFollowsTheRuleTaughtInSchool(string call, string expected) =>
        Assert.That(Print(call), Is.EqualTo(expected));

    /// <summary>
    /// <para>A root of an exact power is exact, on every machine.</para>
    /// <para>Roots are not required to be correctly rounded and the platforms disagree: the
    /// cube root of 27 is 3 from the Windows C runtime and 3.0000000000000004 from glibc, so
    /// a program printing one would print two different things depending on where it ran.</para>
    /// <para>The whole number is used wherever raising it by the degree gives the value back,
    /// so these are not merely tidier: they are the better answer, and the same one anywhere.
    /// </para>
    /// </summary>
    [TestCase("Math.Cbrt(27.0)", "3")]
    [TestCase("Math.Cbrt(8.0)", "2")]
    [TestCase("Math.Cbrt(-8.0)", "-2")]
    [TestCase("Math.Cbrt(1000000.0)", "100")]
    [TestCase("Math.Root(32.0, 5.0)", "2")]
    [TestCase("Math.Root(-32.0, 5.0)", "-2")]
    [TestCase("Math.Root(81.0, 4.0)", "3")]
    [TestCase("Math.Root(16.0, 2.0)", "4")]
    public void ARootOfAnExactPowerIsExact(string call, string expected) =>
        Assert.That(Print(call), Is.EqualTo(expected));

    /// <summary>
    /// And it corrects nothing it should not: where no whole root exists the answer is left
    /// as it was worked out, rather than being rounded to something near it.
    /// </summary>
    [TestCase("Math.Cbrt(28.0)")]
    [TestCase("Math.Cbrt(2.0)")]
    [TestCase("Math.Root(10.0, 2.0)")]
    public void ARootWithNoWholeAnswerIsLeftAlone(string call) =>
        Assert.That(Print(call), Does.Contain("."));

    /// <summary>
    /// <para>Converting an optional carries absence across rather than converting it.</para>
    /// <para>The empty case is the one worth running: turning nothing into characters would
    /// naturally produce an empty set, and an empty set is a different answer from no set at
    /// all. Nothing here should be able to tell the two apart afterwards.</para>
    /// </summary>
    [Test]
    public void ConvertingAnOptionalKeepsAbsenceAbsent() => Assert.That(
        Lines(Run("""
            shared model Program
                string? function Nothing()
                    string? none;
                    yield none;
                end function

                function Main()
                    string? present = "hi";
                    character[]? asLetters = present;
                    Console.WriteLine(asLetters.HasValue());
                    Console.WriteLine(asLetters.Value().Count());

                    string? absent = Program.Nothing();
                    character[]? stillAbsent = absent;
                    Console.WriteLine(stillAbsent.HasValue());

                    character[]? letters = {'a', 'b'};
                    string? asText = letters;
                    Console.WriteLine(asText.Or("?"));
                end function
            end model
            """)),
        Is.EqualTo(new[] { "true", "2", "false", "ab" }));

    /// <summary>A function of any shape is a Function, and answers to it while running.</summary>
    [Test]
    public void AFunctionOfAnyShapeIsHeldAsAFunction() => Assert.That(
        Lines(Run("""
            shared model Program
                integer function Twice(integer n)
                    yield n * 2;
                end function

                function Main()
                    Function[] all =
                    {
                        (integer n) yield n + 1,
                        Program.Twice,
                        (string s) yield Console.WriteLine(s)
                    };

                    Console.WriteLine(all.Count());

                    Model held = all[0];
                    Console.WriteLine(Reference.Equals(held, all[0]));
                end function
            end model
            """)),
        Is.EqualTo(new[] { "3", "true" }));

    /// <summary>
    /// <para>An ordinal names a member, or names none.</para>
    /// <para>This is the one cast that gives back a different value rather than the same one
    /// seen as another type, and the only reason a cast to an enumeration is optional: a
    /// number outside the range names nothing, and nothing is what it yields.</para>
    /// </summary>
    [Test]
    public void AnIntegerCastsToTheMemberWithThatOrdinal() => Assert.That(
        Lines(Run($$"""
            {{Suits}}

            shared model Program
                function Main()
                    for n = 0 to 3
                        Suit? found = n as Suit;

                        Console.WriteLine(
                            n + " " + if found.HasValue()
                                      then found.Value().ToString()
                                      else "nothing");
                    end for
                end function
            end model
            """)),
        Is.EqualTo(new[] { "0 Clubs", "1 Diamonds", "2 Hearts", "3 nothing" }));

    /// <summary>A member and its ordinal make the round trip.</summary>
    [Test]
    public void AMemberAndItsOrdinalRoundTrip() => Assert.That(
        Lines(Run($$"""
            {{Suits}}

            shared model Program
                function Main()
                    Suit start = Suit.Diamonds;
                    Suit? back = start.ToInteger() as Suit;

                    Console.WriteLine(back.HasValue() and back.Value() == start);
                end function
            end model
            """)),
        Is.EqualTo(new[] { "true" }));

    /// <summary>
    /// A cast down the family yields an optional, so a mismatch produces nothing rather than
    /// failing. There is no null for it to give back instead.
    /// </summary>
    [Test]
    public void ACastToADescendantYieldsAnOptional() => Assert.That(
        Lines(Run("""
            model Shape
                public function Shape()
                end function
            end model

            model Circle extends Shape
                public function Circle()
                end function
            end model

            model Square extends Shape
                public function Square()
                end function
            end model

            shared model Program
                function Main()
                    Shape[] shapes = {new Circle(), new Square()};

                    for each shape in shapes
                        Console.WriteLine((shape as Circle).HasValue());
                    end for
                end function
            end model
            """)),
        Is.EqualTo(new[] { "true", "false" }));

    // ---- A declared ToString ------------------------------------------------------------------

    /// <summary>
    /// <para>Every way of printing a value reaches a declared ToString.</para>
    /// <para>One list rather than one test each, because the point is that they agree: a value
    /// on its own, a value joined to a string, a value inside a set, and the call written out
    /// all end at the same function, and any of them going its own way is the bug.</para>
    /// </summary>
    [Test]
    public void ADeclaredToStringIsReachedHoweverAValueIsPrinted() => Assert.That(
        Lines(Run("""
            model Tag
                string label;

                public function Tag(string what)
                    this.label = what;
                end function

                public override string function ToString()
                    yield "<" + this.label + ">";
                end function
            end model

            shared model Program
                function Main()
                    Tag one = new Tag("a");

                    Console.WriteLine(one.ToString());
                    Console.WriteLine(one);
                    Console.WriteLine("joined: " + one);
                    Console.WriteLine({new Tag("x"), new Tag("y")});
                end function
            end model
            """)),
        Is.EqualTo(new[] { "<a>", "<a>", "joined: <a>", "{<x>, <y>}" }));

    /// <summary>A structure may override it as freely, and keeps field-by-field otherwise.</summary>
    [Test]
    public void AStructureMayDeclareToStringToo() => Assert.That(
        Lines(Run("""
            structure Point
                public integer X;
                public integer Y;

                public function Point(integer x, integer y)
                    this.X = x;
                    this.Y = y;
                end function

                public override string function ToString()
                    yield "(" + this.X + ", " + this.Y + ")";
                end function
            end structure

            structure Plain
                public integer N;

                public function Plain(integer n)
                    this.N = n;
                end function
            end structure

            shared model Program
                function Main()
                    Console.WriteLine(new Point(1, 2));
                    Console.WriteLine(new Plain(3));
                end function
            end model
            """)),
        Is.EqualTo(new[] { "(1, 2)", "Plain { 3 }" }));

    /// <summary>
    /// Dispatch is on the runtime type, so a value held as its base prints the version its own
    /// type declared. Anything less would make printing disagree with calling.
    /// </summary>
    [Test]
    public void ToStringDispatchesOnTheRuntimeType() => Assert.That(
        Lines(Run("""
            model Shape
                public override string function ToString()
                    yield "shape";
                end function
            end model

            model Square extends Shape
                public override string function ToString()
                    yield "square";
                end function
            end model

            shared model Program
                function Main()
                    Shape held = new Square();
                    Console.WriteLine(held);
                end function
            end model
            """)),
        Is.EqualTo(new[] { "square" }));

    /// <summary>A model declaring none prints its type name, which is the default it inherits.</summary>
    [Test]
    public void AModelDeclaringNoToStringPrintsItsTypeName() => Assert.That(
        Lines(Run("""
            model Plain
            end model

            shared model Program
                function Main()
                    Console.WriteLine(new Plain());
                end function
            end model
            """)),
        Is.EqualTo(new[] { "Plain" }));
}
