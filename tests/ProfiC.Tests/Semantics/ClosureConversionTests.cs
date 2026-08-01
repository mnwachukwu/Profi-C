using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Semantics;
using ProfiC.Compiler.Text;

namespace ProfiC.Tests.Semantics;

/// <summary>
/// <para>Closure conversion, checked by running the program both ways.</para>
/// <para>The pass exists for an emitter that does not exist yet, so there is nothing further
/// down the pipeline to catch a mistake in it. What there is instead is the interpreter, which
/// runs the tree before and after: a conversion that changes what a program prints is wrong,
/// whatever it did to the tree. That is the whole test, and it is why the pass was built to
/// produce ordinary Profi-C rather than a shape only an emitter would understand.</para>
/// </summary>
[TestFixture]
public sealed class ClosureConversionTests : LexerTestBase
{
    private sealed record Compiled(
        CompilationUnit Lowered,
        SemanticModel Model,
        DiagnosticBag Diagnostics);

    private static Compiled Compile(SourceText source)
    {
        DiagnosticBag diagnostics = new();
        CompilationUnit unit = Parser.Parse(source, diagnostics);
        SemanticModel model = Resolver.Resolve(unit, diagnostics, requireEntryPoint: true);
        TypeChecker.Check(unit, model, diagnostics);
        DefiniteAssignment.Analyze(unit, model, diagnostics);

        return new Compiled(Lowering.Lower(unit, model), model, diagnostics);
    }

    private static string RunOnce(CompilationUnit tree, SemanticModel model)
    {
        StringWriter output = new();
        ProfiC.Interpreter.Interpreter.Run(tree, model, output, TextReader.Null);
        return output.ToString().ReplaceLineEndings("\n");
    }

    /// <summary>
    /// <para>Runs a program lowered, then lowered and converted, and gives back both.</para>
    /// <para>Each run gets its own compilation. Conversion writes into the semantic model —
    /// new symbols for the frames — and sharing one model between the runs would let the
    /// second read what the first wrote.</para>
    /// </summary>
    private static (string Plain, string Converted) BothWays(SourceText source)
    {
        Compiled plain = Compile(source);

        Assert.That(
            plain.Diagnostics.Select(d => $"{d.Descriptor.Id}: {d.Message}"),
            Is.Empty,
            "the program should check cleanly before it is run");

        Compiled fresh = Compile(source);

        return (
            RunOnce(plain.Lowered, plain.Model),
            RunOnce(ClosureConversion.Convert(fresh.Lowered, fresh.Model), fresh.Model));
    }

    /// <summary>
    /// <para>Runs a body both ways and requires the same output.</para>
    /// <para><paramref name="frames"/> says how many frames the conversion should have made.
    /// Without it a case could pass by converting nothing at all — two runs of an untouched
    /// tree agree whatever the pass was supposed to do — so every case that expects to be
    /// converted says how much.</para>
    /// </summary>
    private static void AssertSameBothWays(string body, int frames)
    {
        SourceText source = new(
            $$"""
            shared model Program
                function Main()
            {{body}}
                end function
            end model
            """,
            "<test>");

        (string plain, string converted) = BothWays(source);

        Assert.That(converted, Is.EqualTo(plain), "conversion changed what the program printed");
        Assert.That(FramesIn(ConvertBody(body)), Is.EqualTo(frames), "frames made");
    }

    /// <summary>How many frames conversion made, which is how much of a program it moved.</summary>
    private static int FramesIn(CompilationUnit converted) =>
        converted.Declarations
                 .OfType<ModelDecl>()
                 .Count(m => m.Name.StartsWith("<frame$", StringComparison.Ordinal));

    /// <summary>How many function values are still lambdas rather than functions on a model.</summary>
    private static int LambdasIn(CompilationUnit converted) =>
        converted.Descendants().OfType<LambdaExpr>().Count();

    /// <summary>How many functions are still declared among statements rather than on a model.</summary>
    private static int LocalFunctionsIn(CompilationUnit converted) =>
        converted.Descendants()
                 .OfType<LocalDeclStmt>()
                 .Count(local => local.Declaration is FunctionDecl);

    private static CompilationUnit ConvertBody(string body)
    {
        Compiled compiled = Compile(new SourceText(
            $$"""
            shared model Program
                function Main()
            {{body}}
                end function
            end model
            """,
            "<test>"));

        Assert.That(compiled.Diagnostics.Errors, Is.Zero);

        return ClosureConversion.Convert(compiled.Lowered, compiled.Model);
    }

    // ---- The cell, which is the whole reason the pass is shaped this way ---------------------

    /// <summary>
    /// <para>A write outside is seen inside, and a write inside is seen outside.</para>
    /// <para>This is the case that rules out handing captured values over as arguments when the
    /// function value is made. Both sides have to name one field, so the code around the value
    /// is rewritten as well as the value's own body.</para>
    /// </summary>
    [Test]
    public void AWriteOnEitherSideIsSeenOnTheOther() => AssertSameBothWays(
        """
                integer total = 1;
                integer delegate() read = () yield total;

                total = 99;
                Console.WriteLine("written outside, read inside: " + read());

                delegate() bump = function()
                    total = total + 1;
                end function;

                bump();
                Console.WriteLine("written inside, read outside: " + total);
        """,
        frames: 1);

    // ---- A fresh frame per turn --------------------------------------------------------------

    /// <summary>
    /// <para>Three function values made in a loop answer 1, 2, 3.</para>
    /// <para>The frame is made where the names are declared, so a loop body makes one per turn
    /// and each value holds its own. One frame per call would give 3, 3, 3 — the trap
    /// <c>samples/looping.pc</c> exists to say Profi-C does not have.</para>
    /// </summary>
    [Test]
    public void EachTurnOfALoopGetsItsOwnFrame() => AssertSameBothWays(
        """
                integer delegate()[] made = {};

                loop for i = 1 to 3
                    made.Insert(() yield i);
                end loop

                loop each value in made
                    Console.Write(value() + " ");
                end loop

                Console.WriteLine();
        """,
        frames: 1);

    [Test]
    public void ALoopMakingFramesMakesOnePerTurn()
    {
        CompilationUnit converted = ConvertBody(
            """
                    integer delegate()[] made = {};

                    loop for i = 1 to 3
                        made.Insert(() yield i);
                    end loop
            """);

        Assert.That(
            FramesIn(converted),
            Is.EqualTo(1),
            "one frame model, made afresh on each turn rather than one model per turn");
    }

    // ---- Reaching into more than one run --------------------------------------------------------

    /// <summary>
    /// <para>A value naming both the loop counter and something declared before the loop.</para>
    /// <para>The two live in different frames — the counter's is made afresh each turn, the
    /// other once — so the value is written onto the inner one and follows its link outward for
    /// the rest. Reading both off one frame would mean either losing the per-turn counter or
    /// remaking the outer names every turn.</para>
    /// </summary>
    [Test]
    public void AValueMayReachIntoTheRunAroundTheOneItWasMadeIn() => AssertSameBothWays(
        """
                integer start = 100;
                integer delegate()[] made = {};

                loop for i = 1 to 3
                    made.Insert(() yield start + i);
                end loop

                loop each value in made
                    Console.Write(value() + " ");
                end loop

                Console.WriteLine();
        """,
        frames: 2);

    [Test]
    public void ReachingIntoTwoRunsMakesTwoFrames()
    {
        CompilationUnit converted = ConvertBody(
            """
                    integer start = 100;
                    integer delegate()[] made = {};

                    loop for i = 1 to 3
                        made.Insert(() yield start + i);
                    end loop
            """);

        Assert.That(
            FramesIn(converted),
            Is.EqualTo(2),
            "one frame for the body and one for the loop, linked");
    }

    /// <summary>Three runs deep, so the innermost follows two links to reach the outermost.</summary>
    [Test]
    public void AValueMayReachOutThroughSeveralRuns() => AssertSameBothWays(
        """
                integer start = 1000;
                integer delegate()[] made = {};

                loop for outer = 1 to 2
                    integer scaled = outer * 100;

                    loop for inner = 1 to 2
                        made.Insert(() yield start + scaled + inner);
                    end loop
                end loop

                loop each value in made
                    Console.Write(value() + " ");
                end loop

                Console.WriteLine();
        """,
        frames: 3);

    /// <summary>
    /// A write through the link is seen outside it. The frames are linked rather than copied,
    /// so a name reached two runs out is the same cell either way round.
    /// </summary>
    [Test]
    public void AWriteThroughTheLinkIsSeenOutsideIt() => AssertSameBothWays(
        """
                integer total = 0;
                delegate()[] bumps = {};

                loop for i = 1 to 3
                    bumps.Insert(function()
                        total = total + i;
                    end function);
                end loop

                loop each bump in bumps
                    bump();
                end loop

                Console.WriteLine("total: " + total);
        """,
        frames: 2);

    /// <summary>
    /// <para>A value written inside an inline value, capturing that one's parameter.</para>
    /// <para>An inline body is one expression rather than a run of statements, and converting it
    /// as an expression left its parameters with nowhere to live — so the value inside it found
    /// no frame and stayed a lambda. Making the expression into the statement it means and
    /// converting that gives the parameters a run to belong to, like any other body.</para>
    /// </summary>
    [Test]
    public void AValueInsideAnInlineValueCapturesItsParameter()
    {
        SourceText source = new(
            """
            shared model Program

                integer delegate(integer) delegate(integer) function SumOf(integer a)
                    yield (b) yield (c) yield a + b + c;
                end function

                function Main()
                    integer delegate(integer) delegate(integer) fromOne = Program.SumOf(1);
                    integer delegate(integer) andTwo = fromOne(2);

                    Console.WriteLine(andTwo(3));
                    Console.WriteLine(Program.SumOf(10)(20)(30));
                end function

            end model
            """,
            "<test>");

        Compiled compiled = Compile(source);
        Assert.That(compiled.Diagnostics.Errors, Is.Zero);

        CompilationUnit converted = ClosureConversion.Convert(compiled.Lowered, compiled.Model);

        Assert.That(LambdasIn(converted), Is.Zero, "every one of the three was lifted");

        (string plain, string ran) = BothWays(source);

        Assert.Multiple(() =>
        {
            Assert.That(plain, Is.EqualTo("6\n60\n"));
            Assert.That(ran, Is.EqualTo(plain));
        });
    }

    // ---- The ordinary cases -------------------------------------------------------------------

    [Test]
    public void ALambdaReadingALocalIsConverted()
    {
        CompilationUnit converted = ConvertBody(
            """
                    integer total = 7;
                    integer delegate() read = () yield total;
                    Console.WriteLine(read());
            """);

        Assert.That(FramesIn(converted), Is.EqualTo(1));
    }

    [Test]
    public void ALambdaReadingALocalPrintsTheSame() => AssertSameBothWays(
        """
                integer total = 7;
                integer delegate() read = () yield total;
                Console.WriteLine(read());
        """,
        frames: 1);

    /// <summary>
    /// <para>A value that captures nothing gets no frame, but stops being a lambda all the same.
    /// </para>
    /// <para>There is nothing for a frame to hold, so the body becomes a shared function that
    /// nothing is bound to. The emitter should meet functions rather than lambdas whether or not
    /// capture was involved, and allocating an object per value to say "this holds nothing"
    /// would be paying for a difference that does not exist.</para>
    /// </summary>
    [Test]
    public void ALambdaCapturingNothingIsLiftedWithoutAFrame()
    {
        CompilationUnit converted = ConvertBody(
            """
                    integer delegate(integer) twice = (n) yield n * 2;
                    Console.WriteLine(twice(21));
            """);

        Assert.Multiple(() =>
        {
            Assert.That(FramesIn(converted), Is.Zero, "nothing was captured");
            Assert.That(LambdasIn(converted), Is.Zero, "but it is a function now");
            Assert.That(
                converted.Declarations.OfType<ModelDecl>()
                         .Count(m => m.Name.StartsWith("<loose$", StringComparison.Ordinal)),
                Is.EqualTo(1),
                "one shared model per file holds the bodies that captured nothing");
        });
    }

    [Test]
    public void ALambdaCapturingNothingStillRuns() => AssertSameBothWays(
        """
                integer delegate(integer) twice = (n) yield n * 2;
                Console.WriteLine(twice(21));
        """,
        frames: 0);

    [Test]
    public void ABlockBodiedLambdaKeepsItsStatements() => AssertSameBothWays(
        """
                integer floor = 10;

                integer delegate(integer, integer) larger = function(a, b)
                    if a > b
                        yield a + floor;
                    end if

                    yield b + floor;
                end function;

                Console.WriteLine(larger(1, 2));
                Console.WriteLine(larger(5, 3));
        """,
        frames: 1);

    /// <summary>
    /// A value that yields nothing keeps its expression as a statement. Written as a yield it
    /// would be a function that yields something, which is a different function.
    /// </summary>
    [Test]
    public void ALambdaYieldingNothingStillDoesWhatItDid() => AssertSameBothWays(
        """
                string mark = "* ";
                delegate(string) announce = (what) yield Console.WriteLine(mark + what);

                announce("one");
                announce("two");
        """,
        frames: 1);

    [Test]
    public void ALocalFunctionCapturingALocalPrintsTheSame() => AssertSameBothWays(
        """
                integer total = 7;

                integer function Doubled()
                    yield total * 2;
                end function

                Console.WriteLine(Doubled());
        """,
        frames: 1);

    /// <summary>
    /// A value made in one call and used after it has returned, which is the case that makes
    /// the pass necessary rather than merely tidy.
    /// </summary>
    [Test]
    public void AValueOutlivingTheCallThatMadeItStillReads()
    {
        SourceText source = new(
            """
            shared model Program

                integer delegate(integer) function AdderOf(integer by)
                    yield (n) yield n + by;
                end function

                function Main()
                    integer delegate(integer) addTen = Program.AdderOf(10);
                    integer delegate(integer) addOne = Program.AdderOf(1);

                    Console.WriteLine(addTen(5));
                    Console.WriteLine(addOne(5));
                end function

            end model
            """,
            "<test>");

        (string plain, string converted) = BothWays(source);

        Assert.Multiple(() =>
        {
            Assert.That(plain, Is.EqualTo("15\n6\n"));
            Assert.That(converted, Is.EqualTo(plain));
        });
    }

    // ---- The receiver --------------------------------------------------------------------------

    /// <summary>
    /// <para>A value naming <c>this</c> is moved, and the instance travels with it.</para>
    /// <para>Inside the frame's function <c>this</c> is the frame, so the body cannot keep
    /// meaning it: what it meant is a field the frame carries. The value outlives the call that
    /// made it, which is what makes carrying the instance necessary rather than tidy.</para>
    /// </summary>
    [Test]
    public void AValueNamingThisCarriesTheInstance()
    {
        SourceText source = new(
            """
            model Counter
                integer count;

                public function Counter(integer start)
                    this.count = start;
                end function

                public integer delegate() function Reader()
                    yield () yield this.count;
                end function
            end model

            shared model Program
                function Main()
                    Counter four = new Counter(4);
                    Counter nine = new Counter(9);

                    integer delegate() readFour = four.Reader();
                    integer delegate() readNine = nine.Reader();

                    Console.WriteLine(readFour());
                    Console.WriteLine(readNine());
                end function
            end model
            """,
            "<test>");

        Compiled compiled = Compile(source);
        Assert.That(compiled.Diagnostics.Errors, Is.Zero);

        CompilationUnit converted = ClosureConversion.Convert(compiled.Lowered, compiled.Model);

        Assert.That(FramesIn(converted), Is.EqualTo(1), "the value naming 'this' was moved");

        (string plain, string ran) = BothWays(source);

        Assert.Multiple(() =>
        {
            Assert.That(plain, Is.EqualTo("4\n9\n"));
            Assert.That(ran, Is.EqualTo(plain), "each value kept its own instance");
        });
    }

    /// <summary>
    /// A write through the captured instance is seen outside, because what travelled is the
    /// instance and not a copy of what it held.
    /// </summary>
    [Test]
    public void AWriteThroughTheCapturedInstanceIsSeenOutside()
    {
        SourceText source = new(
            """
            model Counter
                integer count;

                public function Counter()
                    this.count = 0;
                end function

                public integer function Count()
                    yield this.count;
                end function

                public delegate() function Bumper()
                    yield function()
                        this.count = this.count + 1;
                    end function;
                end function
            end model

            shared model Program
                function Main()
                    Counter c = new Counter();
                    delegate() bump = c.Bumper();

                    bump();
                    bump();

                    Console.WriteLine(c.Count());
                end function
            end model
            """,
            "<test>");

        (string plain, string converted) = BothWays(source);

        Assert.Multiple(() =>
        {
            Assert.That(plain, Is.EqualTo("2\n"));
            Assert.That(converted, Is.EqualTo(plain));
        });
    }

    /// <summary>The instance and a local together, which share one frame.</summary>
    [Test]
    public void AValueNamingThisAndALocalCarriesBoth()
    {
        SourceText source = new(
            """
            model Greeter
                string name;

                public function Greeter(string called)
                    this.name = called;
                end function

                public string delegate() function GreetingWith(string mark)
                    yield () yield mark + this.name + mark;
                end function
            end model

            shared model Program
                function Main()
                    Greeter g = new Greeter("ada");
                    string delegate() greet = g.GreetingWith("*");
                    Console.WriteLine(greet());
                end function
            end model
            """,
            "<test>");

        (string plain, string converted) = BothWays(source);

        Assert.Multiple(() =>
        {
            Assert.That(plain, Is.EqualTo("*ada*\n"));
            Assert.That(converted, Is.EqualTo(plain));
        });
    }

    // ---- Reaching a parent ----------------------------------------------------------------------

    /// <summary>
    /// <para>A value naming <c>base</c> is moved, and still reaches the parent's version.</para>
    /// <para>This is the case a frame cannot answer on its own: it extends nothing, so
    /// <c>base</c> means nothing there, and reading the instance is not enough because
    /// <c>&lt;self&gt;.Speak()</c> dispatches to the override. The answer would be "woof" where
    /// the program said "..." — right shape, wrong version, and nothing would say so. The type
    /// that does have a parent holds the call instead.</para>
    /// </summary>
    [Test]
    public void AValueNamingBaseReachesTheParentsVersion()
    {
        SourceText source = new(
            """
            model Animal
                public virtual string function Speak()
                    yield "...";
                end function
            end model

            model Dog extends Animal
                public override string function Speak()
                    yield "woof";
                end function

                public string delegate() function Quieter()
                    yield () yield base.Speak();
                end function
            end model

            shared model Program
                function Main()
                    Dog d = new Dog();
                    string delegate() quiet = d.Quieter();
                    Console.WriteLine(quiet());
                    Console.WriteLine(d.Speak());
                end function
            end model
            """,
            "<test>");

        Compiled compiled = Compile(source);
        Assert.That(compiled.Diagnostics.Errors, Is.Zero);

        CompilationUnit converted = ClosureConversion.Convert(compiled.Lowered, compiled.Model);

        Assert.Multiple(() =>
        {
            Assert.That(FramesIn(converted), Is.EqualTo(1), "it was moved onto a frame");
            Assert.That(LambdasIn(converted), Is.Zero, "and stopped being a lambda");
        });

        (string plain, string ran) = BothWays(source);

        Assert.Multiple(() =>
        {
            Assert.That(plain, Is.EqualTo("...\nwoof\n"));
            Assert.That(
                ran,
                Is.EqualTo(plain),
                "the parent's version, not the override the instance would dispatch to");
        });
    }

    // ---- The corpus, run both ways ---------------------------------------------------------------

    /// <summary>
    /// <para>Every runnable sample prints the same thing converted as it does not.</para>
    /// <para>This is the oracle the phase was planned around. The samples reach shapes no
    /// written-out case does — a lambda in a collection literal, one inside a switch, one
    /// handed straight to a call — and running the corpus twice is what turns "the pass looks
    /// right" into "the pass did not change any program's answer".</para>
    /// </summary>
    [TestCaseSource(nameof(SampleNames))]
    public void ASamplePrintsTheSameConverted(string name)
    {
        SourceText source = LoadSample(name);
        Compiled plain = Compile(source);

        if (plain.Diagnostics.Errors > 0 || plain.Model.EntryPoint is null)
        {
            Assert.Ignore($"{name} is not a runnable program on its own");
        }

        (string before, string after) = BothWays(source);

        Assert.That(after, Is.EqualTo(before), $"conversion changed what {name} printed");
    }

    /// <summary>
    /// <para>Two files that both capture end up with two frames, not one name used twice.</para>
    /// <para>A frame is told from every other frame by the number in its name, and the files of
    /// one program share a namespace. Numbering per file would mint two called
    /// <c>&lt;frame$0&gt;</c>, and the second would take the first's place — so one file's
    /// captured names would be read out of the other file's frame.</para>
    /// </summary>
    [Test]
    public void FramesFromDifferentFilesDoNotShareAName()
    {
        DiagnosticBag diagnostics = new();

        CompilationUnit[] units =
        [
            Parser.Parse(new SourceText(
                """
                model Left
                    public shared integer function Of(integer seed)
                        integer held = seed * 10;
                        integer delegate() read = () yield held;
                        yield read();
                    end function
                end model
                """,
                "left.pc"), diagnostics),

            Parser.Parse(new SourceText(
                """
                model Right
                    public shared integer function Of(integer seed)
                        integer held = seed * 100;
                        integer delegate() read = () yield held;
                        yield read();
                    end function
                end model

                shared model Program
                    function Main()
                        Console.WriteLine(Left.Of(1));
                        Console.WriteLine(Right.Of(1));
                    end function
                end model
                """,
                "right.pc"), diagnostics),
        ];

        SemanticModel model = Resolver.Resolve(units, diagnostics, requireEntryPoint: true);
        TypeChecker.Check(units, model, diagnostics);
        DefiniteAssignment.Analyze(units, model, diagnostics);

        Assert.That(diagnostics.Errors, Is.Zero, "the program should check cleanly");

        IReadOnlyList<CompilationUnit> lowered = Lowering.Lower(units, model);
        IReadOnlyList<CompilationUnit> converted = ClosureConversion.Convert(lowered, model);

        string[] names =
        [
            .. converted.SelectMany(unit => unit.Declarations)
                        .OfType<ModelDecl>()
                        .Select(m => m.Name)
                        .Where(n => n.StartsWith("<frame$", StringComparison.Ordinal)),
        ];

        Assert.That(names, Has.Length.EqualTo(2), "each file's member made one");
        Assert.That(names, Is.Unique);

        StringWriter output = new();
        ProfiC.Interpreter.Interpreter.Run(converted, model, output, TextReader.Null);

        Assert.That(
            output.ToString().ReplaceLineEndings("\n"),
            Is.EqualTo("10\n100\n"),
            "each file read its own frame");
    }

    /// <summary>
    /// <para>No sample still holds a lambda once it has been converted.</para>
    /// <para>This is the invariant the emitter is owed: every function value is a function on a
    /// model, so emitting one is emitting a method and capture never has to be reasoned about.
    /// Only a value naming <c>base</c> is allowed to survive as a lambda, and no sample writes
    /// one — which is what makes this assertable as zero rather than as a list of exceptions.
    /// </para>
    /// </summary>
    [TestCaseSource(nameof(SampleNames))]
    public void ASampleKeepsNoLambdaAfterConversion(string name)
    {
        Compiled compiled = Compile(LoadSample(name));

        if (compiled.Diagnostics.Errors > 0)
        {
            Assert.Ignore($"{name} does not check on its own");
        }

        CompilationUnit converted = ClosureConversion.Convert(compiled.Lowered, compiled.Model);

        Assert.Multiple(() =>
        {
            Assert.That(
                LambdasIn(converted),
                Is.Zero,
                $"{name} still holds a lambda, so the emitter would meet one");

            Assert.That(
                LocalFunctionsIn(converted),
                Is.Zero,
                $"{name} still declares a function among statements");
        });
    }

    // ---- Functions declared among statements ------------------------------------------------

    /// <summary>
    /// <para>A local function becomes a member and its declaration goes.</para>
    /// <para>It captures exactly as a lambda does, so it is moved the same way — the difference
    /// is only that it is reached by name, so every name that led to it has to lead to the
    /// member instead.</para>
    /// </summary>
    [Test]
    public void ALocalFunctionBecomesAMember()
    {
        CompilationUnit converted = ConvertBody(
            """
                    integer total = 7;

                    integer function Doubled()
                        yield total * 2;
                    end function

                    Console.WriteLine(Doubled());
            """);

        Assert.Multiple(() =>
        {
            Assert.That(FramesIn(converted), Is.EqualTo(1), "it captured, so it went on a frame");
            Assert.That(LocalFunctionsIn(converted), Is.Zero, "and stopped being a local one");
        });
    }

    /// <summary>
    /// <para>A local function that calls itself, which after moving means naming a member from
    /// inside that member's own body.</para>
    /// <para>The place on the model is taken before the body is written, which is what this
    /// needs: writing the body first would meet the call and have nowhere to send it. Nothing
    /// more than that is needed, because a local function is in scope only below its
    /// declaration — a call above one is refused rather than reaching forward.</para>
    /// </summary>
    [Test]
    public void ARecursiveLocalFunctionStillReachesItself() => AssertSameBothWays(
        """
                integer by = 1;

                integer function CountDown(integer from)
                    if from <= 0
                        yield 0;
                    end if

                    yield from + CountDown(from - by);
                end function

                Console.WriteLine("sum: " + CountDown(4));
        """,
        frames: 1);

    /// <summary>
    /// <para>Two local functions calling each other, and a call above both of them.</para>
    /// <para>A place on the model is taken for every function a run declares before any of the
    /// run is rewritten, which is what lets a call written above a declaration be sent
    /// somewhere. Rewriting statement by statement would meet the call with nowhere to send it.
    /// </para>
    /// </summary>
    [Test]
    public void LocalFunctionsMayCallEachOtherAndBeCalledFromAbove() => AssertSameBothWays(
        """
                integer by = 1;

                Console.WriteLine("4 even? " + Even(4));

                boolean function Even(integer n)
                    if n == 0
                        yield true;
                    end if

                    yield Odd(n - by);
                end function

                boolean function Odd(integer n)
                    if n == 0
                        yield false;
                    end if

                    yield Even(n - by);
                end function
        """,
        frames: 1);

    /// <summary>A local function named rather than called, which is a value like any other.</summary>
    [Test]
    public void ALocalFunctionMayBeNamedAsAValue() => AssertSameBothWays(
        """
                integer by = 10;

                integer function Raised(integer n)
                    yield n + by;
                end function

                integer delegate(integer) raise = Raised;

                Console.WriteLine("through the name: " + raise(5));
        """,
        frames: 1);

    /// <summary>
    /// A local function capturing nothing goes on the shared model, and one beside it that does
    /// capture goes on a frame — both still reachable from where they are called.
    /// </summary>
    [Test]
    public void LocalFunctionsThatCaptureAndOnesThatDoNotBothWork() => AssertSameBothWays(
        """
                integer by = 4;

                integer function Twice(integer n)
                    yield n * 2;
                end function

                integer function Raised(integer n)
                    yield Twice(n) + by;
                end function

                Console.WriteLine("both: " + Raised(3));
        """,
        frames: 1);

    /// <summary>
    /// <para>The corpus actually reaches the pass.</para>
    /// <para>Running every sample both ways proves nothing if the pass moved nothing in any of
    /// them — two identical runs of an untouched tree agree trivially. This names the samples
    /// that do get frames, so the coverage behind the oracle is a number somebody chose rather
    /// than one nobody looked at.</para>
    /// </summary>
    [Test]
    public void TheCorpusExercisesTheConversion()
    {
        List<string> framed = [];
        List<string> hadLocals = [];
        int lambdas = 0;

        foreach (string name in SampleNames)
        {
            Compiled compiled = Compile(LoadSample(name));

            if (compiled.Diagnostics.Errors > 0)
            {
                continue;
            }

            int locals = LocalFunctionsIn(compiled.Lowered);
            lambdas += LambdasIn(compiled.Lowered);

            int frames = FramesIn(ClosureConversion.Convert(compiled.Lowered, compiled.Model));

            if (frames > 0)
            {
                framed.Add($"{name}: {frames}");
            }

            if (locals > 0)
            {
                hadLocals.Add($"{name}: {locals}");
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(
                framed,
                Is.Not.Empty,
                "no sample made a frame, so running the corpus both ways proves nothing");

            Assert.That(
                lambdas,
                Is.GreaterThan(0),
                "no sample held a lambda, so the no-lambda invariant proves nothing");

            Assert.That(
                hadLocals,
                Is.Not.Empty,
                "no sample declared a local function, so lifting them proves nothing");
        });

        TestContext.Out.WriteLine($"frames:          {string.Join(", ", framed)}");
        TestContext.Out.WriteLine($"local functions: {string.Join(", ", hadLocals)}");
        TestContext.Out.WriteLine($"lambdas before:  {lambdas}");
    }
}
