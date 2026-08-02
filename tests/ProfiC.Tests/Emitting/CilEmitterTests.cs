using System.Diagnostics;
using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Emit;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Semantics;
using ProfiC.Compiler.Text;

namespace ProfiC.Tests.Emitting;

/// <summary>
/// <para>What the emitter produces, checked by running it.</para>
/// <para><b>The interpreter is the oracle.</b> Every program here is run both ways and the two
/// outputs must match, which is a far stronger claim than any assertion about instructions: it
/// says the emitted code means what the language means. Asserting on opcodes would pin the
/// emitter to today's choices and would not notice a correct-looking sequence that computes the
/// wrong answer.</para>
/// <para>The emitted assembly is run in a process of its own rather than loaded here. Loading it
/// would leave it locked and the test would be unable to clean up after itself, and a program
/// that fails to start is a thing worth seeing as a failure to start rather than as an exception
/// inside the test host.</para>
/// </summary>
[TestFixture]
public sealed class CilEmitterTests
{
    /// <summary>Everything the front end produces for one program.</summary>
    private sealed record Compiled(
        IReadOnlyList<CompilationUnit> Emitting,
        SemanticModel Model,
        DiagnosticBag Diagnostics);

    private static Compiled Compile(string source)
    {
        DiagnosticBag diagnostics = new();
        CompilationUnit unit = Parser.Parse(new SourceText(source, "<test>"), diagnostics);
        SemanticModel model = Resolver.Resolve(unit, diagnostics, requireEntryPoint: true);
        TypeChecker.Check(unit, model, diagnostics);
        DefiniteAssignment.Analyze(unit, model, diagnostics);

        Assert.That(
            diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error),
            Is.Empty,
            "the program should compile before it is emitted");

        return new Compiled(
            ClosureConversion.Convert(Lowering.Lower([unit], model), model),
            model,
            diagnostics);
    }

    /// <summary>Runs a program on the interpreter and gives back what it printed.</summary>
    private static string Interpreted(string source)
    {
        Compiled compiled = Compile(source);
        StringWriter output = new();

        ProfiC.Interpreter.Interpreter.Run(compiled.Emitting, compiled.Model, output);

        return output.ToString().ReplaceLineEndings("\n");
    }

    /// <summary>
    /// Emits a program, runs the assembly, and gives back what it printed. The folder goes with
    /// it, so a run leaves nothing behind whether it passed or not.
    /// </summary>
    private static string Emitted(string source)
    {
        Compiled compiled = Compile(source);

        string folder = Path.Combine(Path.GetTempPath(), $"profi-c-emit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);

        try
        {
            string assembly = Path.Combine(folder, "Emitted.dll");
            DiagnosticBag diagnostics = new();

            Assert.That(
                CilEmitter.Emit(compiled.Emitting, compiled.Model, "Emitted", assembly, diagnostics),
                Is.True,
                string.Join("\n", diagnostics.Select(d => d.Message)));

            return Run(assembly);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static string Run(string assembly)
    {
        ProcessStartInfo start = new()
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        start.ArgumentList.Add(assembly);

        using Process running = Process.Start(start)!;

        string output = running.StandardOutput.ReadToEnd();
        string failed = running.StandardError.ReadToEnd();

        running.WaitForExit();

        Assert.That(running.ExitCode, Is.Zero, failed);

        return output.ReplaceLineEndings("\n");
    }

    /// <summary>
    /// <para>The claim every one of these makes: emitted and interpreted agree.</para>
    /// <para>Compared against the interpreter rather than against a written-out expectation,
    /// because the interpreter is the definition of what the language does and a literal in a
    /// test is only my idea of it. Where they differ, this says so without either of them having
    /// to be right in advance.</para>
    /// </summary>
    private static void Agrees(string body)
    {
        string source = $$"""
            shared model Program
                function Main()
            {{body}}
                end function

                integer function Doubled(integer n)
                    yield n * 2;
                end function

                integer function Sum(integer a, integer b)
                    yield a + b;
                end function
            end model
            """;

        AgreesWholly(source);
    }

    /// <summary>The same claim, for a program that declares more than <c>Program</c>.</summary>
    private static void AgreesWholly(string source)
    {
        string interpreted = Interpreted(source);

        Assert.That(Emitted(source), Is.EqualTo(interpreted), $"interpreted:\n{interpreted}");
    }

    [Test]
    public void APrintedStringComesOut() =>
        Agrees("""
                    Console.WriteLine("plain");
            """);

    [Test]
    public void ArithmeticIsExact() =>
        Agrees("""
                    Console.WriteLine(2 + 3 * 4);
                    Console.WriteLine((2 + 3) * 4);
                    Console.WriteLine(7 / 2);
                    Console.WriteLine(-7 / 2);
                    Console.WriteLine(7 % 3);
                    Console.WriteLine(-5);
            """);

    [Test]
    public void LocalsHoldWhatTheyWereGiven() =>
        Agrees("""
                    integer a = 4;
                    integer b = a * 2;
                    a = a + b;
                    Console.WriteLine(a);
                    Console.WriteLine(b);
            """);

    [Test]
    public void ComparisonsProduceBooleans() =>
        Agrees("""
                    Console.WriteLine(1 < 2);
                    Console.WriteLine(2 <= 2);
                    Console.WriteLine(3 > 4);
                    Console.WriteLine(3 >= 3);
                    Console.WriteLine(1 == 1);
                    Console.WriteLine(1 != 1);
            """);

    /// <summary>
    /// Equality on a string compares what it says, not where it lives — which is why it is a
    /// call rather than the instruction every other type uses.
    /// </summary>
    [Test]
    public void StringsCompareByWhatTheySay() =>
        Agrees("""
                    string one = "ab";
                    string two = "a" + "b";
                    Console.WriteLine(one == two);
                    Console.WriteLine(one != two);
            """);

    /// <summary>
    /// <para>Short-circuiting, checked by what the skipped side would have done.</para>
    /// <para>The right side divides by a local that is zero — a local rather than the literal,
    /// since dividing by a written zero is caught while checking (<c>PC0324</c>) and the program
    /// would never reach the emitter. Emitted without the branch, this raises instead of
    /// printing, which is a loud way to be wrong rather than a quiet one.</para>
    /// </summary>
    [Test]
    public void LogicShortCircuits() =>
        Agrees("""
                    integer zero = 0;

                    Console.WriteLine(true and false);
                    Console.WriteLine(true or false);
                    Console.WriteLine(not true);
                    Console.WriteLine(false and 1 / zero == 0);
                    Console.WriteLine(true or 1 / zero == 0);
            """);

    [Test]
    public void ConcatenationConvertsTheOtherSide() =>
        Agrees("""
                    Console.WriteLine("n is " + 5);
                    Console.WriteLine("yes: " + true);
                    Console.WriteLine("real: " + 1.5);
            """);

    [Test]
    public void AnIfChoosesOneArm() =>
        Agrees("""
                    integer n = 7;

                    if n < 5
                        Console.WriteLine("small");
                    else if n < 10
                        Console.WriteLine("middling");
                    else
                        Console.WriteLine("big");
                    end if
            """);

    [Test]
    public void ACountedLoopCounts() =>
        Agrees("""
                    integer total = 0;

                    loop for i = 1 to 5
                        total = total + i;
                    end loop

                    Console.WriteLine(total);

                    loop for i = 0 until 3
                        Console.WriteLine(i);
                    end loop

                    loop for i = 10 to 1 stepby -3
                        Console.WriteLine(i);
                    end loop
            """);

    [Test]
    public void AWhileRunsWhileItHolds() =>
        Agrees("""
                    integer n = 5;

                    loop while n > 0
                        Console.WriteLine(n);
                        n = n - 2;
                    end loop
            """);

    /// <summary>The bottom-tested loop, whose body always runs at least once.</summary>
    [Test]
    public void AnUntilLoopAlwaysRunsOnce() =>
        Agrees("""
                    integer n = 100;

                    loop
                        Console.WriteLine(n);
                        n = n + 1;
                    until n > 100
            """);

    [Test]
    public void BreakAndContinueLeaveTheRightLoop() =>
        Agrees("""
                    loop for i = 1 to 10
                        if i == 3
                            continue;
                        end if

                        if i == 6
                            break;
                        end if

                        Console.WriteLine(i);
                    end loop
            """);

    [Test]
    public void ABareLoopIsLeftByBreak() =>
        Agrees("""
                    integer n = 0;

                    loop
                        n = n + 1;

                        if n == 4
                            break;
                        end if
                    end loop

                    Console.WriteLine(n);
            """);

    /// <summary>
    /// A call, its arguments, and its result. Calls between functions are what the two passes
    /// exist for — the callee's signature is defined before any body is written, so a call may
    /// name a method that has not been emitted yet.
    /// </summary>
    [Test]
    public void FunctionsCallEachOther() =>
        Agrees("""
                    Console.WriteLine(Program.Doubled(21));
                    Console.WriteLine(Program.Sum(3, 4));
                    Console.WriteLine(Program.Sum(Program.Doubled(5), 1));
            """);

    [Test]
    public void WriteDoesNotEndTheLine() =>
        Agrees("""
                    Console.Write("a");
                    Console.Write("b");
                    Console.WriteLine();
                    Console.WriteLine("c");
            """);

    [Test]
    public void RealsAreFloatingPoint() =>
        Agrees("""
                    real x = 1.5;
                    real y = x * 2.0;
                    Console.WriteLine(y);
                    Console.WriteLine(7.0 / 2.0);
            """);

    /// <summary>An integer used where a real belongs is widened, which is a recorded conversion.</summary>
    [Test]
    public void AnIntegerWidensToAReal() =>
        Agrees("""
                    real half = 1 / 2.0;
                    Console.WriteLine(half);
            """);

    // ---- Models with state ------------------------------------------------------------------

    /// <summary>
    /// A model with fields, a constructor, and methods reached through an instance — the whole
    /// shape at once, since none of it is much use without the rest.
    /// </summary>
    [Test]
    public void AModelHoldsStateAndAnswersAboutIt() =>
        AgreesWholly("""
            model Counter

                integer count;
                string name;

                public function Counter(string name)
                    this.name = name;
                    this.count = 0;
                end function

                public function Bump(integer by)
                    this.count = this.count + by;
                end function

                public string function Described()
                    yield this.name + " is at " + this.count;
                end function

            end model

            shared model Program
                function Main()
                    Counter clicks = new Counter("clicks");
                    clicks.Bump(3);
                    clicks.Bump(4);
                    Console.WriteLine(clicks.Described());
                end function
            end model
            """);

    // ---- Inheritance ------------------------------------------------------------------------

    /// <summary>
    /// <para>A child reaches what its parent declared, and builds it on the way in.</para>
    /// <para>The smallest inheritance there is: one field, set by a parent's constructor the
    /// child chains to, read back through a method the child never wrote. An emitter that puts
    /// the base call in the wrong place, or hangs the field on the wrong type, fails here rather
    /// than somewhere further along where the cause is harder to see.</para>
    /// </summary>
    [Test]
    public void AChildBuildsItsParentAndReachesWhatItDeclared() =>
        AgreesWholly("""
            model Animal

                protected string name;

                public function Animal(string given)
                    this.name = given;
                end function

                public string function Name()
                    yield this.name;
                end function

            end model

            model Dog extends Animal

                public function Dog()
                    base("rex");
                end function

            end model

            shared model Program
                function Main()
                    Dog pet = new Dog();
                    Console.WriteLine(pet.Name());
                end function
            end model
            """);

    /// <summary>
    /// <para>A call through a parent-typed name reaches the child's version.</para>
    /// <para>The whole of what virtual dispatch is, and the one an emitter gets wrong in a way
    /// nothing else notices: mark the override as taking a slot of its own rather than reusing
    /// its parent's and the assembly still builds, still verifies, and quietly runs the parent's
    /// version every time it is reached through a parent.</para>
    /// </summary>
    [Test]
    public void ACallThroughTheParentReachesTheChildsVersion() =>
        AgreesWholly("""
            model Greeter

                public virtual string function Greeting()
                    yield "hello";
                end function

            end model

            model LoudGreeter extends Greeter

                public override string function Greeting()
                    yield "HELLO";
                end function

            end model

            shared model Program
                function Main()
                    Greeter plain = new Greeter();
                    Greeter loud = new LoudGreeter();

                    Console.WriteLine(plain.Greeting());
                    Console.WriteLine(loud.Greeting());
                end function
            end model
            """);

    /// <summary>
    /// <para>An abstract model is never made, and the function it left open dispatches.</para>
    /// <para>Two claims in one program because they only make sense together: the parent declares
    /// what every child must answer without answering it, and a name of the parent's type reaches
    /// whichever child it turned out to be.</para>
    /// </summary>
    [Test]
    public void AnAbstractParentDeclaresWhatItsChildrenAnswer() =>
        AgreesWholly("""
            abstract model Shape

                public abstract integer function Sides();

                public string function Described()
                    yield "a shape with " + this.Sides() + " sides";
                end function

            end model

            model Triangle extends Shape

                public override integer function Sides()
                    yield 3;
                end function

            end model

            model Square extends Shape

                public override integer function Sides()
                    yield 4;
                end function

            end model

            shared model Program
                function Main()
                    Shape one = new Triangle();
                    Shape two = new Square();

                    Console.WriteLine(one.Described());
                    Console.WriteLine(two.Described());
                end function
            end model
            """);

    /// <summary>
    /// <para><c>base.Member()</c> reaches past the override that is running.</para>
    /// <para>The one call in the language that must not dispatch. Emitted as an ordinary virtual
    /// call it finds the override it was written inside, and the program does not print the wrong
    /// answer — it never prints anything, because it recurses until the stack is gone.</para>
    /// </summary>
    [Test]
    public void BaseReachesPastTheOverrideItIsWrittenIn() =>
        AgreesWholly("""
            model Label

                public virtual string function Text()
                    yield "plain";
                end function

            end model

            model Fancy extends Label

                public override string function Text()
                    yield "very " + base.Text();
                end function

            end model

            shared model Program
                function Main()
                    Console.WriteLine(new Fancy().Text());
                end function
            end model
            """);

    /// <summary>
    /// <para>Starting values run down the whole chain, in the order the language says.</para>
    /// <para>Nearest type first, all of them before any constructor body — so what this prints is
    /// the order itself rather than only the values. C#'s order, and the reason the emitter runs
    /// a model's own initializers and leaves its parent's to the parent's constructor.</para>
    /// </summary>
    [Test]
    public void StartingValuesRunDownTheChainBeforeAnyConstructor() =>
        AgreesWholly("""
            model Parent

                integer first = Parent.Announce("parent field");

                public function Parent()
                    Console.WriteLine("parent constructor");
                end function

                public shared integer function Announce(string what)
                    Console.WriteLine(what);
                    yield 0;
                end function

            end model

            model Child extends Parent

                integer second = Parent.Announce("child field");

                public function Child()
                    base();
                    Console.WriteLine("child constructor");
                end function

            end model

            shared model Program
                function Main()
                    Child made = new Child();
                end function
            end model
            """);

    /// <summary>
    /// A parent that declares no constructor is still built, through the one made for it — the
    /// case where a child chains to something nobody wrote.
    /// </summary>
    [Test]
    public void AChildBuildsAParentThatDeclaredNoConstructor() =>
        AgreesWholly("""
            model Base

                integer held = 7;

                public integer function Held()
                    yield this.held;
                end function

            end model

            model Derived extends Base

                public integer function Twice()
                    yield this.Held() * 2;
                end function

            end model

            shared model Program
                function Main()
                    Console.WriteLine(new Derived().Twice());
                end function
            end model
            """);

    /// <summary>
    /// <para>A parent's constructor runs whether or not the child said to run it.</para>
    /// <para>The two engines disagreed about this once, and in the direction that hides it: the
    /// emitter had to find a parent constructor because the CLR will not verify one that reaches
    /// none, while the interpreter simply skipped it. The program ran either way, printing a
    /// value the parent's constructor would have settled and did not.</para>
    /// </summary>
    [Test]
    public void AParentIsBuiltWithoutTheChildAskingItTo() =>
        AgreesWholly("""
            model Root

                public string trail;

                public function Root()
                    this.trail = "root";
                end function

                public function Root(string given)
                    this.trail = given;
                end function

            end model

            model Middle extends Root
            end model

            model Leaf extends Middle

                public function Leaf()
                    this.trail = this.trail + " leaf";
                end function

            end model

            shared model Program
                function Main()
                    Console.WriteLine(new Leaf().trail);
                    Console.WriteLine(new Middle().trail);
                end function
            end model
            """);

    /// <summary>
    /// <para>Three deep, with the middle one overriding and the last reaching past it.</para>
    /// <para>A chain rather than a pair, because everything about slots and base calls can be
    /// wrong in a way two types cannot show: a child that reuses the wrong slot, or a base call
    /// that reaches the root instead of the parent, both look correct until there is a middle.
    /// </para>
    /// </summary>
    [Test]
    public void AChainOfThreeDispatchesAndChainsCorrectly() =>
        AgreesWholly("""
            model One

                public virtual string function Say()
                    yield "one";
                end function

            end model

            model Two extends One

                public override string function Say()
                    yield "two on " + base.Say();
                end function

            end model

            model Three extends Two

                public override string function Say()
                    yield "three on " + base.Say();
                end function

            end model

            shared model Program
                function Main()
                    One held = new Three();
                    Console.WriteLine(held.Say());
                end function
            end model
            """);

    /// <summary>
    /// <para>Two instances hold their own state.</para>
    /// <para>The claim that separates a field from a shared one, and the one an emitter gets
    /// wrong by storing to the type instead of the instance — which looks right until there are
    /// two of them.</para>
    /// </summary>
    [Test]
    public void TwoInstancesDoNotShareTheirFields() =>
        AgreesWholly("""
            model Counter

                integer count;

                public function Bump()
                    this.count = this.count + 1;
                end function

                public integer function Total()
                    yield this.count;
                end function

            end model

            shared model Program
                function Main()
                    Counter one = new Counter();
                    Counter two = new Counter();

                    one.Bump();
                    one.Bump();
                    two.Bump();

                    Console.WriteLine(one.Total());
                    Console.WriteLine(two.Total());
                end function
            end model
            """);

    /// <summary>
    /// <para>A field's initializer runs before the constructor body, and the constructor may
    /// overwrite it.</para>
    /// <para>Both halves matter and they pull in opposite directions: run the initializers after
    /// the body and the constructor's work is thrown away; leave them out and a field the
    /// constructor does not mention holds nothing.</para>
    /// </summary>
    [Test]
    public void AFieldStartsAtItsInitializerAndTheConstructorMayReplaceIt() =>
        AgreesWholly("""
            model Settings

                string mode = "quiet";
                integer volume = 3;
                boolean ready = true;

                public function Settings(string mode)
                    this.mode = mode;
                end function

                public string function Described()
                    yield this.mode + " " + this.volume + " " + this.ready;
                end function

            end model

            shared model Program
                function Main()
                    Console.WriteLine(new Settings("loud").Described());
                end function
            end model
            """);

    /// <summary>A field written with no value starts at the empty value for its type.</summary>
    [Test]
    public void AFieldWithNoInitializerStartsEmpty() =>
        AgreesWholly("""
            model Blank

                integer number;
                boolean flag;

                public string function Described()
                    yield this.number + " " + this.flag;
                end function

            end model

            shared model Program
                function Main()
                    Console.WriteLine(new Blank().Described());
                end function
            end model
            """);

    /// <summary>
    /// A model that declares no constructor can still be made. The CLR supplies a default only
    /// where a compiler asked for one, and this emitter defines every member itself.
    /// </summary>
    [Test]
    public void AModelWithNoConstructorCanStillBeMade() =>
        AgreesWholly("""
            model Simple

                integer value = 42;

                public integer function Value()
                    yield this.value;
                end function

            end model

            shared model Program
                function Main()
                    Console.WriteLine(new Simple().Value());
                end function
            end model
            """);

    /// <summary>
    /// A shared field is one of the field, held on the type — so both instances see the same
    /// one, which is the whole distinction from an ordinary field.
    /// </summary>
    [Test]
    public void ASharedFieldIsOneOfIt() =>
        AgreesWholly("""
            model Tally

                public shared integer made = 0;

                public function Tally()
                    Tally.made = Tally.made + 1;
                end function

            end model

            shared model Program
                function Main()
                    Tally first = new Tally();
                    Tally second = new Tally();
                    Console.WriteLine(Tally.made);
                end function
            end model
            """);

    /// <summary>An instance passed to a function is the same instance, not a copy of one.</summary>
    [Test]
    public void AModelIsPassedByReference() =>
        AgreesWholly("""
            model Box

                integer held;

                public function Put(integer value)
                    this.held = value;
                end function

                public integer function Held()
                    yield this.held;
                end function

            end model

            shared model Program

                function Main()
                    Box box = new Box();
                    box.Put(1);
                    Program.Fill(box);
                    Console.WriteLine(box.Held());
                end function

                function Fill(Box box)
                    box.Put(9);
                end function

            end model
            """);

    // ---- What it will not emit yet ----------------------------------------------------------

    /// <summary>
    /// Tries to emit a program and gives back what was refused, along with whatever the attempt
    /// left in the folder — read before it is cleaned up, since that is the thing worth knowing.
    /// </summary>
    private static (string[] Refusals, string[] Written) Refused(string source)
    {
        Compiled compiled = Compile(source);

        string folder = Path.Combine(Path.GetTempPath(), $"profi-c-refuse-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);

        try
        {
            DiagnosticBag diagnostics = new();

            bool emitted = CilEmitter.Emit(
                compiled.Emitting,
                compiled.Model,
                "Refused",
                Path.Combine(folder, "Refused.dll"),
                diagnostics);

            Assert.That(emitted, Is.False, "this should not have been emitted");

            return (
                [.. diagnostics.Select(d => d.Id)],
                [.. Directory.GetFiles(folder).Select(Path.GetFileName)!]);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static string[] RefusalsFor(string source) => Refused(source).Refusals;

    [TestCase("structure", """
        structure Point
            integer x;
        end structure
        """)]
    [TestCase("enumeration", """
        enumeration Color
            Red,
            Green
        end enumeration
        """)]
    public void ADeclarationItCannotEmitIsRefused(string what, string declaration) =>
        Assert.That(
            RefusalsFor($$"""
                {{declaration}}

                shared model Program
                    function Main()
                        Console.WriteLine("hello");
                    end function
                end model
                """),
            Does.Contain("PC0501"),
            what);

    [TestCase("a set", "integer[] xs = {1, 2};")]
    [TestCase("an optional", "integer? maybe = 4;")]
    [TestCase("a fraction", "fraction half = 1|2;")]
    [TestCase("a lambda", "integer delegate(integer) f = (n) yield n + 1;")]
    [TestCase("a switch", """
                switch 1
                    case 1:
                        Console.WriteLine("one");
                end switch
        """)]
    [TestCase("an interpolated string", """"
                integer n = 1;
                Console.WriteLine("n is {{n}}");
        """")]
    public void AStatementItCannotEmitIsRefused(string what, string body) =>
        Assert.That(
            RefusalsFor($$"""
                shared model Program
                    function Main()
                {{body}}
                    end function
                end model
                """),
            Does.Contain("PC0501"),
            what);

    /// <summary>
    /// <para>A refused build writes no file at all.</para>
    /// <para>The reason the survey runs ahead of the emitter rather than inside it. An assembly
    /// missing one method still loads and still verifies, and fails only when a run reaches the
    /// gap — which is a worse answer than not building, and much harder to trace back to here.
    /// </para>
    /// </summary>
    [Test]
    public void ARefusedBuildLeavesNothingBehind()
    {
        (string[] refusals, string[] written) = Refused("""
            structure Point
                integer x;
            end structure

            shared model Program
                function Main()
                    Console.WriteLine("hello");
                end function
            end model
            """);

        Assert.Multiple(() =>
        {
            Assert.That(refusals, Does.Contain("PC0501"));

            Assert.That(written, Is.Empty,
                        "a refused build should write no assembly, no config, and no runtime");
        });
    }
}
