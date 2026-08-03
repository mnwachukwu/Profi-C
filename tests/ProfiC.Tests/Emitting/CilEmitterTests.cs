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

    /// <summary>
    /// <para>A real counts in tens, so the arithmetic comes out as written.</para>
    /// <para><b>Emitted as binary floating point, the third line prints 0.30000000000000004.</b>
    /// That is the whole reason a real is not a double, and it is the shape that would otherwise
    /// pass unnoticed: the first two lines agree either way.</para>
    /// </summary>
    [Test]
    public void RealsCountInTens() =>
        Agrees("""
                    real x = 1.5;
                    real y = x * 2.0;
                    Console.WriteLine(y);
                    Console.WriteLine(7.0 / 2.0);
                    Console.WriteLine(0.1 + 0.2);
                    Console.WriteLine((0.1 + 0.2) == 0.3);
                    Console.WriteLine(-2.5);
            """);

    /// <summary>
    /// <para>A float is binary floating point, and keeps every part of that.</para>
    /// <para>Written beside the test above, these two are the difference between the types: the
    /// same sum, one exact and one not, and the values only a float has.</para>
    /// </summary>
    [Test]
    public void FloatsAreBinaryFloatingPoint() =>
        Agrees("""
                    float x = 1.5f;
                    Console.WriteLine(x * 2.0f);
                    Console.WriteLine(0.1f + 0.2f);
                    Console.WriteLine((0.1f + 0.2f) == 0.3f);

                    # A division by zero produces an infinity rather than stopping, and the
                    # value that is not a number is not equal to itself.
                    float zero = 0.0f;
                    Console.WriteLine(1.0f / zero);
                    Console.WriteLine(zero / zero);
                    Console.WriteLine((zero / zero) == (zero / zero));
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

    // ---- Optionals --------------------------------------------------------------------------

    /// <summary>
    /// <para>The three members an optional has, on one that holds something and one that does
    /// not.</para>
    /// <para><c>Console.Read</c> with no input is the only way a program can come by an empty
    /// optional without one being handed to it, which is why it appears in a test about
    /// something else.</para>
    /// </summary>
    [Test]
    public void AnOptionalAnswersAboutWhatItHolds() =>
        Agrees("""
                    integer? here = 5;
                    string? gone = Console.Read();

                    Console.WriteLine(here.HasValue());
                    Console.WriteLine(gone.HasValue());
                    Console.WriteLine(here);
                    Console.WriteLine(gone);
                    Console.WriteLine(here.Or(-1));
                    Console.WriteLine(gone.Or("a fallback"));
            """);

    /// <summary>
    /// <para>A guard is what makes reading one legal, and the reading is what it held.</para>
    /// <para>Narrowing leaves no mark on the lowered tree, so this is where the emitter has to
    /// notice it for itself: inside the guard the checker calls the name definite while the local
    /// still holds an optional, and every read has to take the value out.</para>
    /// </summary>
    [Test]
    public void AGuardMakesTheValueReadable() =>
        Agrees("""
                    integer? here = 5;

                    if here.HasValue()
                        Console.WriteLine(here.Value());

                        # Narrowed, so this is arithmetic on the value rather than on the optional.
                        Console.WriteLine(here + 1);
                    end if
            """);

    /// <summary>
    /// <para>The fallback does not run unless it is needed.</para>
    /// <para>The whole of what <c>Or</c> promises, and the reason it is emitted as a branch
    /// rather than as a call — a call would have evaluated the fallback before entering. Nothing
    /// about the printed value would show that; the line the fallback prints is what shows it.
    /// </para>
    /// </summary>
    [Test]
    public void TheFallbackRunsOnlyWhenItIsNeeded() =>
        AgreesWholly("""
            shared model Program

                function Main()
                    integer? here = 5;
                    Console.WriteLine(here.Or(Program.Noisy()));

                    string? gone = Console.Read();
                    Console.WriteLine(gone.Or(Program.AlsoNoisy()));
                end function

                integer function Noisy()
                    Console.WriteLine("  (this should not run)");
                    yield -1;
                end function

                string function AlsoNoisy()
                    Console.WriteLine("  (this one should)");
                    yield "fell back";
                end function

            end model
            """);

    /// <summary>
    /// <para>Chaining: an optional fallback keeps the chain going, a definite one ends it.</para>
    /// <para>The two forms differ in what they give back rather than in what they do, and both
    /// are one sequence — so this is what says the branch yields the right thing on each arm.
    /// </para>
    /// </summary>
    [Test]
    public void OrChainsUntilSomethingIsDefinite() =>
        Agrees("""
                    string? first = Console.Read();
                    string? second = Console.Read();
                    string? third = "here";

                    Console.WriteLine(first.Or(second).Or("all empty"));
                    Console.WriteLine(first.Or(third).Or("all empty"));
            """);

    /// <summary>
    /// A set of optionals holds them, counts them, and prints each as what it holds — which is
    /// where an emitter that forgot to box the struct produces a program the runtime refuses.
    /// </summary>
    [Test]
    public void ASetHoldsOptionals() =>
        Agrees("""
                    integer? here = 5;
                    integer? also = 7;

                    integer?[] held = {here, also};

                    Console.WriteLine(held.Count);
                    Console.WriteLine(held);
                    Console.WriteLine(held[1]);
            """);

    // ---- Sets -------------------------------------------------------------------------------

    /// <summary>
    /// <para>A set is built, read, written and counted.</para>
    /// <para>The whole shape at once, since a set that can be made and not read is no use. What
    /// it proves beyond the values is the boundary: Profi-C counts in 64 bits and the list
    /// underneath addresses in 32, so every index narrows and every count widens, and getting
    /// either backwards is a program that runs and reads the wrong element.</para>
    /// </summary>
    [Test]
    public void ASetIsBuiltReadAndWritten() =>
        Agrees("""
                    integer[] scores = {70, 85, 90};

                    Console.WriteLine(scores.Count);
                    Console.WriteLine(scores[0]);
                    Console.WriteLine(scores[2]);

                    scores[1] = 99;
                    Console.WriteLine(scores[1]);
            """);

    /// <summary>Every member of a set the emitter has a sequence for, and what each answers.</summary>
    [Test]
    public void TheMembersOfASetAnswerAsTheyDo() =>
        Agrees("""
                    integer[] xs = {1, 2, 3};

                    xs.Insert(4);
                    xs.InsertAt(0, 0);

                    Console.WriteLine(xs.Count);
                    Console.WriteLine(xs.Contains(4));
                    Console.WriteLine(xs.Contains(9));
                    Console.WriteLine(xs.IndexOf(3));
                    Console.WriteLine(xs.Remove(2));
                    Console.WriteLine(xs.Remove(2));

                    xs.RemoveAt(0);
                    Console.WriteLine(xs.Count);

                    xs.Clear();
                    Console.WriteLine(xs.Count);
            """);

    /// <summary>
    /// <para>An empty set is a set, and grows from nothing.</para>
    /// <para>Worth its own claim because a literal with no elements is where the element type
    /// comes from the declaration rather than from anything written between the braces — so an
    /// emitter that read the type off the first element has nothing to read.</para>
    /// </summary>
    [Test]
    public void AnEmptySetGrows() =>
        Agrees("""
                    string[] names = {};

                    Console.WriteLine(names.Count);

                    names.Insert("Ada");
                    names.Insert("Grace");

                    Console.WriteLine(names.Count);
                    Console.WriteLine(names[1]);
            """);

    /// <summary>
    /// <para>A <c>loop each</c> walks what it was given.</para>
    /// <para>By the time the emitter sees it this is an index loop with a mark around it, so what
    /// is really being checked is that the mark is balanced and the loop reads every element
    /// once — including the awkward ones, which are none and one.</para>
    /// </summary>
    [Test]
    public void ALoopEachWalksEveryElement() =>
        Agrees("""
                    integer[] several = {1, 2, 3};
                    integer[] one = {9};
                    integer[] none = {};

                    loop each n in several
                        Console.Write(n + " ");
                    end loop
                    Console.WriteLine();

                    loop each n in one
                        Console.Write(n + " ");
                    end loop
                    Console.WriteLine();

                    loop each n in none
                        Console.Write("never");
                    end loop
                    Console.WriteLine("done");
            """);

    /// <summary>
    /// <para>Yielding out of the middle of a walk leaves the set walkable.</para>
    /// <para>The mark that refuses a change mid-walk is paired in a <c>finally</c>, and this is
    /// what says so. <b>A <c>break</c> does not test it</b>: break jumps to the loop's own exit,
    /// which is still inside the sequence the walk emits, so the unmark runs either way. Only
    /// leaving the whole function does — and then the set is left marked forever, so the failure
    /// surfaces at the next change with no walk anywhere in sight.</para>
    /// </summary>
    [Test]
    public void YieldingOutOfAWalkLeavesTheSetUsable() =>
        AgreesWholly("""
            shared model Program

                shared integer[] xs = {1, 2, 3, 4};

                function Main()
                    Console.WriteLine(Program.FirstOver(2));

                    # Only legal if the walk that was abandoned unmarked the set on its way out.
                    Program.xs.Insert(5);
                    Console.WriteLine(Program.xs.Count);
                end function

                integer function FirstOver(integer limit)
                    loop each n in Program.xs
                        if n > limit
                            yield n;
                        end if
                    end loop

                    yield -1;
                end function

            end model
            """);

    /// <summary>A set of sets needs nothing of its own: the element type is simply another set.</summary>
    [Test]
    public void ASetOfSetsIsASetLikeAnyOther() =>
        Agrees("""
                    integer[][] grid = {{1, 2}, {3, 4, 5}};

                    Console.WriteLine(grid.Count);
                    Console.WriteLine(grid[1].Count);
                    Console.WriteLine(grid[1][2]);

                    loop each row in grid
                        Console.Write(row.Count + " ");
                    end loop
                    Console.WriteLine();
            """);

    /// <summary>
    /// <para>Reading one set into another: the six that give back a new set and change nothing.
    /// </para>
    /// <para>These moved into the runtime to be emitted at all, so what this really holds is that
    /// the move worked — both engines now reach the same method, and the answers below are the
    /// ones neither of them decides alone. The values are chosen to catch the readings that are
    /// easy to get wrong: a repeated element, so <c>Union</c> appending rather than merging shows;
    /// and <c>Subset</c> split at a point, so an inclusive end would not add back up.</para>
    /// </summary>
    [Test]
    public void SetsAreReadIntoNewSets() =>
        Agrees("""
                    integer[] mine = {1, 2, 3, 3};
                    integer[] yours = {3, 4};

                    Console.WriteLine(mine.Union(yours).Join(","));
                    Console.WriteLine(mine.Intersect(yours).Join(","));
                    Console.WriteLine(mine.Except(yours).Join(","));
                    Console.WriteLine(mine.Distinct().Join(","));
                    Console.WriteLine(mine.Subset(1).Join(","));
                    Console.WriteLine(mine.Subset(1, 3).Join(","));

                    # The two halves put back together, which only holds where the end is exclusive.
                    Console.WriteLine(mine.Subset(0, 2).Union(mine.Subset(2, 4)).Join(","));

                    # Reading one leaves it as it was.
                    Console.WriteLine(mine.Join(","));
            """);

    /// <summary>
    /// <para>Any set joins, not only a set of strings.</para>
    /// <para>Each element is written the way it would be written on its own, which is what makes
    /// this worth having at all — and is the part an emitter gets wrong by reaching for the
    /// framework's own joining, where a boolean reads <c>True</c>.</para>
    /// </summary>
    [Test]
    public void AnySetJoins() =>
        Agrees("""
                    Console.WriteLine("{1, 2}: " + "");
                    integer[] numbers = {1, 2, 3};
                    string[] words = {"a", "b"};
                    boolean[] answers = {true, false};

                    Console.WriteLine(numbers.Join(" | "));
                    Console.WriteLine(words.Join(" and "));
                    Console.WriteLine(answers.Join(", "));
            """);

    /// <summary>
    /// <para>A set of a model this build is still writing.</para>
    /// <para>The case that needs its own machinery. Every other set closes over a type the CLR
    /// already has, so ordinary reflection names its members; a set of a declared model closes
    /// over a builder for a type that does not exist yet, where nothing can be looked up and the
    /// member has to be reached another way.</para>
    /// </summary>
    [Test]
    public void ASetOfADeclaredModelIsEmitted() =>
        AgreesWholly("""
            model Book

                public string title;

                public function Book(string named)
                    this.title = named;
                end function

            end model

            shared model Program
                function Main()
                    Book[] shelf = {};

                    shelf.Insert(new Book("Dune"));
                    shelf.Insert(new Book("Emma"));

                    Console.WriteLine(shelf.Count);

                    loop each book in shelf
                        Console.WriteLine(book.title);
                    end loop

                    Console.WriteLine(shelf[0].title);
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
    /// <para><c>ToString</c>, which every value answers, <c>Model</c> being the root of them
    /// all.</para>
    /// <para>It goes to the runtime rather than the framework, and the difference shows: a
    /// boolean reads as <c>true</c> and not <c>True</c>, and a set reads with its braces. A
    /// model that declares its own is asked that one, which is the part that needs the virtual
    /// dispatch underneath to be right.</para>
    /// <para>An interpolated string is lowered to these same calls, so it rides along.</para>
    /// </summary>
    [Test]
    public void EveryValueAnswersToString() =>
        AgreesWholly("""
            model Point
                public integer x;
                public integer y;

                public function Point(integer x, integer y)
                    this.x = x;
                    this.y = y;
                end function
            end model

            model Named
                public string what;

                public function Named(string what)
                    this.what = what;
                end function

                public override string function ToString()
                    yield "the " + this.what;
                end function
            end model

            shared model Program
                function Main()
                    Console.WriteLine((5).ToString());
                    Console.WriteLine(true.ToString());
                    Console.WriteLine("plain".ToString());
                    Console.WriteLine(new Named("thing").ToString());

                    integer[] xs = {1, 2};
                    Console.WriteLine(xs.ToString());

                    # An interpolated string is this same call, written shorter.
                    integer n = 7;
                    Console.WriteLine("n is {{n}}, and a point reads as {{new Point(1, 2)}}");
                end function
            end model
            """);

    /// <summary>
    /// <para>Equality is deep and structural, and an emitted program says so too.</para>
    /// <para>The instruction a stack machine reaches for here compares references, which finds
    /// two equal values different — so this is a call into the runtime's walk, and a model is
    /// made to answer that walk about its own fields. What is checked is the whole of it: a
    /// field inherited from a parent counts, two types are never equal however well their
    /// fields line up, a set and an optional held inside are walked in turn, and a graph that
    /// points back at itself is compared without looping forever.</para>
    /// </summary>
    [Test]
    public void EqualityComparesWhatAValueHoldsRatherThanWhereItLives() =>
        AgreesWholly("""
            model Animal
                public string name;

                public function Animal(string name)
                    this.name = name;
                end function
            end model

            model Dog extends Animal
                public integer legs;

                public function Dog(string name, integer legs)
                    base(name);
                    this.legs = legs;
                end function
            end model

            model Cat extends Animal
                public integer lives;

                public function Cat(string name, integer lives)
                    base(name);
                    this.lives = lives;
                end function
            end model

            model Holder
                public integer[] numbers;
                public integer? maybe;

                public function Holder(integer[] numbers, integer? maybe)
                    this.numbers = numbers;
                    this.maybe = maybe;
                end function
            end model

            model Node
                public integer value;
                public Node? next;

                public function Node(integer value)
                    this.value = value;
                end function
            end model

            shared model Program
                function Main()
                    # A field the parent declared counts as much as one this model did.
                    Console.WriteLine(new Dog("rex", 4) == new Dog("rex", 4));
                    Console.WriteLine(new Dog("rex", 4) == new Dog("rex", 3));
                    Console.WriteLine(new Dog("rex", 4) == new Dog("bo", 4));

                    # Two types are never equal, however well the fields line up.
                    Animal one = new Dog("x", 4);
                    Animal two = new Cat("x", 9);
                    Console.WriteLine(one == two);

                    # A set and an optional held inside are walked in their turn.
                    Console.WriteLine(new Holder({1, 2}, 5) == new Holder({1, 2}, 5));
                    Console.WriteLine(new Holder({1, 2}, 5) == new Holder({1, 3}, 5));

                    # Two sets on their own, which is the other reference type.
                    integer[] left = {1, 2};
                    integer[] right = {1, 2};
                    Console.WriteLine(left == right);
                    Console.WriteLine(left != right);

                    # A graph pointing back at itself ends rather than running forever.
                    Node first = new Node(1);
                    Node second = new Node(1);
                    first.next = first;
                    second.next = second;
                    Console.WriteLine(first == second);

                    # And the member says what the operator says.
                    Console.WriteLine(new Dog("rex", 4).Equals(new Dog("rex", 4)));
                    Console.WriteLine(new Dog("rex", 4).Equals(new Dog("bo", 4)));
                end function
            end model
            """);

    /// <summary>
    /// <para>The four ways to drop the empties out of a set of optionals.</para>
    /// <para><c>TrimAll</c> is the one worth the trouble: it answers with a different kind of set
    /// than it was asked of, so it is a call to a method of its own that unwraps as it filters,
    /// while the other three are ordinary members of the set. The interpreter needs no unwrapping
    /// at all, holding an empty as a null — which is exactly the sort of gap that lets two
    /// engines drift, so both are held to one answer here.</para>
    /// </summary>
    [Test]
    public void TheTrimFamilyDropsTheEmptiesInBothEngines() =>
        AgreesWholly("""
            shared model Program
                function Main()
                    integer?[] sparse = {1, 2, 3, 4, 5};
                    sparse[0] = Program.Nothing();
                    sparse[2] = Program.Nothing();
                    sparse[4] = Program.Nothing();

                    Console.WriteLine(sparse.Trim().Count);
                    Console.WriteLine(sparse.TrimStart().Count);
                    Console.WriteLine(sparse.TrimEnd().Count);
                    Console.WriteLine(sparse.TrimAll());

                    string?[] words = {"x", "y"};
                    words[0] = Program.NoWord();
                    Console.WriteLine(words.TrimAll());

                    integer?[] nothingAbsent = {1, 2};
                    Console.WriteLine(nothingAbsent.Trim().Count);
                end function

                integer? function Nothing()
                    integer? empty;
                    yield empty;
                end function

                string? function NoWord()
                    string? none;
                    yield none;
                end function
            end model
            """);

    /// <summary>
    /// The same over a set of a model this build has not finished writing, which is the case that
    /// makes <c>TrimAll</c> awkward: the method it calls has to be made for a type that does not
    /// exist yet.
    /// </summary>
    [Test]
    public void TrimAllWorksOverASetOfAModelBeingBuilt() =>
        AgreesWholly("""
            model Tag
                public string name;

                public function Tag(string name)
                    this.name = name;
                end function

                public override string function ToString()
                    yield this.name;
                end function
            end model

            shared model Program
                function Main()
                    Tag?[] tags = {new Tag("a"), new Tag("b"), new Tag("c")};
                    tags[0] = Program.NoTag();
                    tags[2] = Program.NoTag();

                    Console.WriteLine(tags.Trim().Count);
                    Console.WriteLine(tags.TrimAll());
                end function

                Tag? function NoTag()
                    Tag? none;
                    yield none;
                end function
            end model
            """);

    /// <summary>
    /// <para>What the checker narrowed, the emitter unwraps — including where the narrowing came
    /// from an arm leaving rather than from a guard around the code.</para>
    /// <para>Nothing in the lowered tree marks a read as narrowed, so the emitter works it out
    /// again from the type. Any place the checker starts narrowing and the emitter does not is a
    /// program that compiles and then reads an optional as though it were a value, which is not
    /// something either engine reports — it is only visible by running both.</para>
    /// </summary>
    [Test]
    public void AnEarlyExitNarrowsForTheEmitterToo() =>
        AgreesWholly("""
            shared model Program
                function Main()
                    Console.WriteLine(Program.PlusOne(5));
                    Console.WriteLine(Program.PlusOne(Program.Nothing()));
                    Console.WriteLine(Program.Either(true));
                    Console.WriteLine(Program.Either(false));
                end function

                integer? function Nothing()
                    integer? empty;
                    yield empty;
                end function

                integer function PlusOne(integer? found)
                    if not found.HasValue()
                        yield 0;
                    end if

                    yield found + 1;
                end function

                integer function Either(boolean take)
                    integer? n;

                    if take
                        n = 7;
                    else
                        yield -1;
                    end if

                    yield n * 2;
                end function
            end model
            """);

    /// <summary>
    /// <para>Every member of a string, run both ways.</para>
    /// <para>The corpus already reaches most of these, but a member is easy to route to the wrong
    /// overload and be right for the argument that happened to be written — <c>Subset</c> takes
    /// where to stop while <c>Substring</c> takes how many, and either reading is plausible for
    /// the pair (2, 4). So each is asked once here with an argument that tells the two apart.
    /// </para>
    /// </summary>
    [Test]
    public void EveryMemberOfAStringMeansTheSameBothWays() =>
        AgreesWholly("""
            shared model Program
                function Main()
                    string word = "  Hello, World  ";

                    Console.WriteLine(word.Count);
                    Console.WriteLine(word.Trim().Count);
                    Console.WriteLine(word.Contains("World"));
                    Console.WriteLine(word.IndexOf("World"));

                    string plain = "abcdef";

                    # The pair that is easy to confuse: how many, against where to stop.
                    Console.WriteLine(plain.Substring(2, 3));
                    Console.WriteLine(plain.Subset(2, 3));
                    Console.WriteLine(plain.Subset(2));

                    Console.WriteLine(plain.Insert("gh"));
                    Console.WriteLine(plain.InsertAt(0, "z"));
                    Console.WriteLine(plain.InsertAt(6, "z"));
                    Console.WriteLine(plain.Remove("cd"));
                    Console.WriteLine(plain.RemoveAt(0));
                    Console.WriteLine(plain.Replace("cd", "--"));

                    Console.WriteLine(plain.ToCharacters());
                    Console.WriteLine(plain.ToCharacters().Count);
                    Console.WriteLine("a,b,,c".Split(","));
                    Console.WriteLine("a,b,,c".Split(",").Count);

                    Console.WriteLine("xyABCyx".Trim("xy"));
                    Console.WriteLine("xyABCyx".TrimStart("xy"));
                    Console.WriteLine("xyABCyx".TrimEnd("xy"));

                    character[] edges = {'x', 'y'};
                    Console.WriteLine("xyABCyx".Trim(edges));
                    Console.WriteLine("xyABCyx".TrimStart(edges));
                    Console.WriteLine("xyABCyx".TrimEnd(edges));

                    Console.WriteLine("mcDonald".ToUpper());
                    Console.WriteLine("mcDonald".ToLower());
                    Console.WriteLine("mcDonald".Capitalize());

                    Console.WriteLine("42".ToInteger().Or(-1));
                    Console.WriteLine("four".ToInteger().Or(-1));
                    Console.WriteLine("3.5".ToReal().Or(0.0));
                    Console.WriteLine("true".ToBoolean().Or(false));
                    Console.WriteLine("yes".ToBoolean().Or(false));

                    Console.WriteLine((1234).Format("N0"));
                    Console.WriteLine((3.14159).Format("F2"));
                end function
            end model
            """);

    /// <summary>
    /// The one rule that separates a Profi-C string from a .NET one: an empty argument matches
    /// trivially and takes nothing away. .NET raises for two of these, so an emitter that called
    /// its methods directly would stop a program the interpreter runs to the end.
    /// </summary>
    [Test]
    public void AnEmptyArgumentChangesNothingInEitherEngine() =>
        AgreesWholly("""
            shared model Program
                function Main()
                    string word = "abc";

                    Console.WriteLine(word.Replace("", "-"));
                    Console.WriteLine(word.Remove(""));
                    Console.WriteLine(word.Trim(""));
                    Console.WriteLine(word.Contains(""));
                    Console.WriteLine(word.IndexOf(""));
                    Console.WriteLine(word.Split(""));
                    Console.WriteLine(word.Split("").Count);
                end function
            end model
            """);

    /// <summary>
    /// <para><c>Math</c>, including the members whose answer is not the framework's.</para>
    /// <para>A half away from zero, a rounding that lands on an integer, and a cube root corrected
    /// to the whole number it is — each written here with the value that tells the language's
    /// answer from .NET's, since for most inputs the two agree and prove nothing.</para>
    /// </summary>
    [Test]
    public void MathAnswersTheLanguagesWayInBothEngines() =>
        AgreesWholly("""
            shared model Program
                function Main()
                    Console.WriteLine(Math.Pi);
                    Console.WriteLine(Math.E);

                    # A half goes away from zero, so these are 3 and -3 rather than 2 and -2.
                    Console.WriteLine(Math.Round(2.5));
                    Console.WriteLine(Math.Round(-2.5));
                    Console.WriteLine(Math.Round(3.14159, 2));

                    Console.WriteLine(Math.Floor(2.7));
                    Console.WriteLine(Math.Ceiling(2.1));
                    Console.WriteLine(Math.Floor(-2.7));

                    # Exactly 3, on every machine.
                    Console.WriteLine(Math.Cbrt(27.0));
                    Console.WriteLine(Math.Root(32.0, 5.0));
                    Console.WriteLine(Math.Root(-8.0, 3.0));

                    Console.WriteLine(Math.Sqrt(16.0));
                    Console.WriteLine(Math.Pow(2.0, 10.0));
                    Console.WriteLine(Math.Factorial(20));

                    Console.WriteLine(Math.Log(Math.E));
                    Console.WriteLine(Math.Log(8.0, 2.0));
                    Console.WriteLine(Math.Log10(1000.0));
                    Console.WriteLine(Math.Log2(8.0));

                    Console.WriteLine(Math.Sin(0.0));
                    Console.WriteLine(Math.Cos(0.0));
                    Console.WriteLine(Math.Tan(0.0));
                    Console.WriteLine(Math.Asin(0.0));
                    Console.WriteLine(Math.Acos(1.0));
                    Console.WriteLine(Math.Atan(0.0));
                    Console.WriteLine(Math.Atan2(1.0, 1.0));
                    Console.WriteLine(Math.Sinh(0.0));
                    Console.WriteLine(Math.Cosh(0.0));
                    Console.WriteLine(Math.Tanh(0.0));
                    Console.WriteLine(Math.Asinh(0.0));
                    Console.WriteLine(Math.Acosh(1.0));
                    Console.WriteLine(Math.Atanh(0.0));

                    Console.WriteLine(Math.Abs(-7));
                    Console.WriteLine(Math.Abs(-7.5));
                    Console.WriteLine(Math.Min(3, 9));
                    Console.WriteLine(Math.Max(3, 9));
                    Console.WriteLine(Math.Min(3.5, 9.5));
                    Console.WriteLine(Math.Max(3.5, 9.5));
                end function
            end model
            """);

    /// <summary>
    /// <para><c>if</c> written where a value belongs, which is what this language has instead of a
    /// ternary.</para>
    /// <para>The arm not taken must not run, and that is more than an economy: it may be the arm
    /// that would have failed. <c>Chosen</c> prints as it goes, so an emitter that evaluated both
    /// would print twice and disagree.</para>
    /// </summary>
    [Test]
    public void AnIfExpressionRunsOnlyTheArmItTakes() =>
        AgreesWholly("""
            shared model Program
                function Main()
                    Console.WriteLine(if true then "yes" else "no");
                    Console.WriteLine(if 3 > 4 then 1 else 2);

                    Console.WriteLine(Program.Chosen(true));
                    Console.WriteLine(Program.Chosen(false));

                    # Nested, and narrowing inside an arm the way the statement does.
                    integer? here = 7;
                    Console.WriteLine(if here.HasValue() then here.Value() else 0);

                    integer n = 5;
                    Console.WriteLine(if n < 0 then "under" else if n > 3 then "over" else "in");
                end function

                integer function Chosen(boolean take)
                    yield if take then Program.Say("left") else Program.Say("right");
                end function

                integer function Say(string which)
                    Console.WriteLine("  ran " + which);
                    yield which.Count;
                end function
            end model
            """);

    /// <summary>
    /// <para><c>is</c> and <c>as</c>, including the answers the checker settled while compiling.
    /// </para>
    /// <para>A settled test is the interesting half: the emitter must read what was recorded
    /// rather than ask the value, and it must still run whatever was written on the left — so
    /// <c>Made()</c> prints, and a program that skipped the operand would print less.</para>
    /// </summary>
    [Test]
    public void IsAndAsAgreeIncludingTheTestsSettledWhileCompiling() =>
        AgreesWholly("""
            model Animal
                public override string function ToString()
                    yield "an animal";
                end function
            end model

            model Dog extends Animal
                public override string function ToString()
                    yield "a dog";
                end function
            end model

            model Cat extends Animal
                public override string function ToString()
                    yield "a cat";
                end function
            end model

            shared model Program
                function Main()
                    Animal[] all = {new Dog(), new Cat(), new Animal()};

                    loop each one in all
                        Console.WriteLine(one + ": dog? " + (one is Dog)
                                          + " cat? " + (one is Cat)
                                          + " animal? " + (one is Animal));

                        Console.WriteLine("  as a dog -> " + (one as Dog).HasValue());
                    end loop

                    # Settled while compiling: a Dog is always an Animal, and never a Cat.
                    Dog rex = new Dog();
                    Console.WriteLine(rex is Animal);
                    Console.WriteLine(rex is Cat);
                    Console.WriteLine((rex as Animal).HasValue());
                    Console.WriteLine((rex as Cat).HasValue());

                    # The operand still runs, settled or not.
                    Console.WriteLine(Program.Made() is Animal);
                    Console.WriteLine((Program.Made() as Cat).HasValue());
                    Console.WriteLine((Program.Made() as Dog).HasValue());
                end function

                Dog function Made()
                    Console.WriteLine("  made one");
                    yield new Dog();
                end function
            end model
            """);

    /// <summary>
    /// <para><c>try</c>, <c>catch</c> and <c>finally</c>, and the ways out of one.</para>
    /// <para>The two engines reach this differently — the interpreter matches a thrown value
    /// against each clause by hand, and emitted code hands the question to the CLR — so which
    /// clause takes what is exactly the kind of thing they could quietly differ about. The first
    /// matching clause wins in both, a parent's clause takes a child, and a <c>finally</c> runs
    /// whichever way the block turned out.</para>
    /// </summary>
    [Test]
    public void TryCatchAndFinallyAgree() =>
        AgreesWholly("""
            model Trouble extends Exception
                public function Trouble(string what)
                    base("trouble: " + what);
                end function
            end model

            model WorseTrouble extends Trouble
                public function WorseTrouble(string what)
                    base("worse " + what);
                end function
            end model

            shared model Program
                function Main()
                    Program.Catches(0);
                    Program.Catches(1);
                    Program.Catches(2);
                    Program.Catches(3);

                    Console.WriteLine(Program.Guarded(true));
                    Console.WriteLine(Program.Guarded(false));
                end function

                function Catches(integer which)
                    Console.WriteLine("-- " + which);

                    try
                        if which == 1
                            throw new Trouble("mild");
                        else if which == 2
                            throw new WorseTrouble("indeed");
                        else if which == 3
                            throw new Exception("plain");
                        end if

                        Console.WriteLine("  nothing went wrong");
                    catch Trouble problem
                        # A clause for the parent takes the child too, and it is written first,
                        # so a WorseTrouble arrives here rather than at the one below.
                        Console.WriteLine("  caught trouble: " + problem.Message());
                    catch Exception any
                        Console.WriteLine("  caught something: " + any.Message());
                    finally
                        Console.WriteLine("  tidied up");
                    end try
                end function

                # A yield out of a try still runs the finally, and a 'ret' inside a protected
                # region is not something the CLR will run at all — so this is the shape that
                # says whether the way out was written correctly.
                integer function Guarded(boolean early)
                    try
                        if early
                            yield 1;
                        end if

                        yield 2;
                    finally
                        Console.WriteLine("  left the guard");
                    end try
                end function
            end model
            """);

    /// <summary>
    /// <para>Leaving a loop from under a <c>try</c>, which is not the branch it looks like.</para>
    /// <para>The CLR refuses an ordinary jump out of a protected region, because leaving one has
    /// to run its <c>finally</c> — so a <c>break</c> for a loop written outside the <c>try</c>
    /// has to be a <c>leave</c>. Emitted as a plain branch the assembly does not verify, and
    /// nothing about the program says so.</para>
    /// </summary>
    [Test]
    public void BreakingOutOfATryStillLeavesTheLoop() =>
        AgreesWholly("""
            shared model Program
                function Main()
                    loop for n = 1 to 5
                        try
                            if n == 3
                                Console.WriteLine("stopping at " + n);
                                break;
                            end if

                            if n == 2
                                Console.WriteLine("skipping " + n);
                                continue;
                            end if

                            Console.WriteLine("saw " + n);
                        finally
                            Console.WriteLine("  finished with " + n);
                        end try
                    end loop

                    # The same, in a walk — which is already wrapped in a try of its own.
                    integer[] counts = {1, 2, 3};

                    loop each count in counts
                        try
                            if count == 2
                                break;
                            end if

                            Console.WriteLine("walked " + count);
                        finally
                            Console.WriteLine("  done with " + count);
                        end try
                    end loop
                end function
            end model
            """);

    /// <summary>
    /// The exceptions the language raises itself are the same .NET exceptions in both engines, so
    /// a clause naming one takes what the runtime threw rather than what a program did.
    /// </summary>
    [Test]
    public void TheLanguagesOwnFailuresAreCaughtByName() =>
        AgreesWholly("""
            shared model Program
                function Main()
                    integer zero = 0;

                    try
                        Console.WriteLine(10 / zero);
                    catch DivideByZeroException problem
                        Console.WriteLine("divided by zero");
                    end try

                    integer[] few = {1, 2};

                    try
                        Console.WriteLine(few[9]);
                    catch IndexOutOfRangeException problem
                        Console.WriteLine("no such position");
                    end try

                    # Caught by the root, which every one of them descends from.
                    try
                        Console.WriteLine(few[9]);
                    catch Exception any
                        Console.WriteLine("caught by the root");
                    end try
                end function
            end model
            """);

    /// <summary>
    /// <para>Integer arithmetic stops where the language says it stops.</para>
    /// <para><b>Emitted as the instruction of the same name, none of these does what the language
    /// promises.</b> A sum past the end of an integer comes back negative, a shift of 64 quietly
    /// means a shift of none, and a division by zero carries the framework's wording rather than
    /// the one written to explain <c>PC0324</c>. The first is the worst by a distance: it is not a
    /// stop in the wrong words, it is a wrong answer printed as though it were right — which the
    /// language's own overflow message promises will never happen.</para>
    /// <para>Every case here is caught rather than left to reach the top, so that the message is
    /// compared as ordinary output.</para>
    /// </summary>
    [Test]
    public void IntegerArithmeticStopsRatherThanWrappingRound() =>
        AgreesWholly("""
            shared model Program
                function Main()
                    integer big = 9223372036854775807;

                    # Worked out rather than written: the smallest integer has no literal, since
                    # the digits of it are one past the largest and the minus is a separate word.
                    integer small = -9223372036854775807 - 1;

                    integer zero = 0;
                    integer minusOne = -1;
                    integer wide = 64;

                    # Wrapped round this would print a negative number and carry on.
                    try
                        Console.WriteLine(big + 1);
                    catch OverflowException problem
                        Console.WriteLine("add: " + problem.Message());
                    end try

                    try
                        Console.WriteLine(small - 1);
                    catch OverflowException problem
                        Console.WriteLine("subtract: " + problem.Message());
                    end try

                    try
                        Console.WriteLine(big * 2);
                    catch OverflowException problem
                        Console.WriteLine("multiply: " + problem.Message());
                    end try

                    # The one division that overflows: the smallest integer has no positive twin.
                    try
                        Console.WriteLine(small / minusOne);
                    catch OverflowException problem
                        Console.WriteLine("divide: " + problem.Message());
                    end try

                    try
                        Console.WriteLine(10 / zero);
                    catch DivideByZeroException problem
                        Console.WriteLine("by zero: " + problem.Message());
                    end try

                    try
                        Console.WriteLine(10 % zero);
                    catch DivideByZeroException problem
                        Console.WriteLine("remainder: " + problem.Message());
                    end try

                    # Folded into range this would quietly be a shift of none.
                    try
                        Console.WriteLine(1 shiftleft wide);
                    catch ArgumentException problem
                        Console.WriteLine("shift: " + problem.Message());
                    end try

                    # What still means exactly what the instruction means.
                    Console.WriteLine(small % minusOne);
                    Console.WriteLine(7 / 2);
                    Console.WriteLine(7 % 2);
                    Console.WriteLine(1 shiftleft 10);
                    Console.WriteLine(1024 shiftright 3);
                    Console.WriteLine(12 bitwise and 10);
                    Console.WriteLine(12 bitwise or 10);
                    Console.WriteLine(12 xor 10);
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

    [TestCase("a set of something unemittable", "DateTime[] days = {DateTime.Now};")]
    [TestCase("an optional of something unemittable", "DateTime? day = DateTime.Now;")]
    [TestCase("a moment", "DateTime day = DateTime.Now;")]
    [TestCase("a lambda", "integer delegate(integer) f = (n) yield n + 1;")]
    [TestCase("a switch", """
                switch 1
                    case 1:
                        Console.WriteLine("one");
                end switch
        """)]
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
