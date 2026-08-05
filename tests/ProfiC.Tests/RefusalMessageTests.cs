using System.Text.RegularExpressions;
using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Semantics;
using ProfiC.Compiler.Text;

namespace ProfiC.Tests;

/// <summary>
/// <para>What a program is told when it fails while running, and that it is told at all.</para>
/// <para>Every refusal in the interpreter and the runtime carries a written message, and those
/// messages are held to the same standard as a diagnostic: a reader meets them at the moment
/// they most need them to be right. Nothing else checks them. A refusal that stopped throwing,
/// began throwing the wrong type, or had its wording decay would fail no test, because a
/// program that never reaches it passes just the same.</para>
/// <para>The completeness test does not carry a list of refusals. It reads the interpreter and
/// the runtime, finds every <c>throw</c>, and requires each to be either exercised by a row
/// here or named below with the reason it cannot be.</para>
/// </summary>
[TestFixture]
public sealed class RefusalMessageTests : LexerTestBase
{
    /// <summary>A body to run, the exception it must raise, and what it must say.</summary>
    private static readonly (string Body, Type Failure, string Message)[] Refusals =
    [
        // ---- Reaching outside a string or a set ---------------------------------------------
        ("Console.WriteLine(\"abc\".Substring(1, 99));", typeof(IndexOutOfRangeException),
         "Cannot take 99 characters from position 1 of a string of 3."),

        ("Console.WriteLine(\"abc\".Subset(0, 99));", typeof(IndexOutOfRangeException),
         "Cannot take the run from 0 to 99 of a string of 3."),

        ("integer[] xs = {1, 2};\n        Console.WriteLine(xs.Subset(0, 99));",
         typeof(IndexOutOfRangeException),
         "Cannot take the run from 0 to 99 of a set of 2 elements."),

        ("Console.WriteLine(\"abc\"[99]);", typeof(IndexOutOfRangeException),
         "Index 99 is outside a string of 3 characters."),

        // Taking a character out names a position rather than a run, so it says so its own way.
        ("Console.WriteLine(\"abc\".RemoveAt(99));", typeof(IndexOutOfRangeException),
         "There is no position 99 in a string of 3."),

        ("integer[] xs = {1};\n        xs.InsertAt(9, 5);", typeof(IndexOutOfRangeException),
         "Cannot insert at 9; the set holds 1 elements."),

        ("integer[] xs = {1};\n        Console.WriteLine(xs[9]);",
         typeof(IndexOutOfRangeException), "Index 9 is outside a set of 1 elements."),

        // ---- Moments that are not moments ----------------------------------------------------
        ("Console.WriteLine(new DateTime(2026, 2, 31, 0, 0, 0));", typeof(ArgumentException),
         "There is no such moment as 2026-2-31."),

        ("Console.WriteLine(new Date(2026, 2, 31));", typeof(ArgumentException),
         "There is no such date as 2026-2-31."),

        ("Console.WriteLine(new Time(25, 0));", typeof(ArgumentException),
         "There is no such time of day as 25:0:0."),

        ("Console.WriteLine(new TimeSpan(400000000, 0, 0, 0));", typeof(OverflowException),
         "A span of 400000000 days, 0 hours, 0 minutes and 0 seconds is too long to hold."),

        // ---- Arithmetic that has no answer ---------------------------------------------------
        ("Console.WriteLine(Math.Factorial(-1));", typeof(ArgumentException),
         "A factorial counts arrangements, so it needs a whole number that is not negative."),

        ("integer n = -1;\n        Console.WriteLine(2 ^ n);", typeof(ArgumentException),
         "An integer raised to the power -1 is not a whole number. Raise a fraction instead, "
         + "or use Math.Pow for a real result."),

        ("Console.WriteLine(Math.Root(8.0, 0));", typeof(ArgumentException),
         "A root of degree zero is not a number."),

        // The amount arrived in a variable, so nothing could judge it while compiling. Written
        // down, it is PC0343 instead.
        ("integer places = 64;\n        Console.WriteLine(1 shiftleft places);",
         typeof(ArgumentException),
         "A shift of 64 places is outside an integer, which holds 64 bits. An amount from 0 to "
         + "63 is what there is to move."),

        ("integer low = 5;\n        Console.WriteLine(Random.Next(low, 1));",
         typeof(ArgumentException),
         "A random number needs a low bound no greater than the high one, but 5 is greater "
         + "than 1."),

        // ---- Dividing by nothing --------------------------------------------------------------
        // Worded here rather than left as the platform's, which says the same thing for both
        // operators and says nothing about why the compiler let it through. Both facts are what
        // a reader wants at this moment: a literal divisor is refused before the program runs,
        // so meeting this at all means the zero arrived in a variable.
        ("integer zero = 0;\n        Console.WriteLine(1 / zero);", typeof(DivideByZeroException),
         "Cannot divide by zero. A divisor written down is refused while compiling as PC0324, "
         + "so a zero reaching here arrived in a variable and could only be found now."),

        ("integer zero = 0;\n        Console.WriteLine(1 % zero);", typeof(DivideByZeroException),
         "Cannot take the remainder of a division by zero. A remainder is what a division "
         + "leaves behind, so it needs a division that can happen."),

        // ---- Numbers that outgrow what holds them ---------------------------------------------
        ("integer big = 4000000000;\n        Console.WriteLine(big * big);",
         typeof(OverflowException),
         "This result is too large to hold. An integer counts up to about 9.2 quintillion, and "
         + "arithmetic is checked rather than wrapped round, so it stops here instead of "
         + "carrying on with a number that would look plausible and be wrong."),

        // Denominators multiply on every unlike addition, so a chain of them overflows long
        // before any one fraction looks large. This is that chain.
        ("fraction total = 0|1;\n        loop for n = 1 to 60\n"
         + "            total = total + Fraction.Create(1, n);\n        end loop\n"
         + "        Console.WriteLine(total);",
         typeof(OverflowException),
         "The parts of this fraction have grown too large to hold. Denominators multiply every "
         + "time two unlike fractions are added, so a long chain of them can outgrow an integer "
         + "even where no single fraction looks large."),

        ("integer zero = 0;\n        Console.WriteLine(Fraction.Create(1, zero));",
         typeof(DivideByZeroException), "A fraction cannot have a denominator of zero."),

        ("fraction zero = Fraction.Create(0, 1);\n        Console.WriteLine(1|2 / zero);",
         typeof(DivideByZeroException), "Cannot divide a fraction by zero."),

        ("fraction zero = Fraction.Create(0, 1);\n        Console.WriteLine(1|2 % zero);",
         typeof(DivideByZeroException), "Cannot take the remainder of a fraction by zero."),

        ("fraction zero = Fraction.Create(0, 1);\n"
         + "        Console.WriteLine(zero.Reciprocal());",
         typeof(DivideByZeroException),
         "Zero has no reciprocal: nothing multiplied by zero gives one."),

        ("fraction zero = Fraction.Create(0, 1);\n        integer power = -2;\n"
         + "        Console.WriteLine(zero ^ power);",
         typeof(DivideByZeroException), "Cannot raise zero to a negative power."),

        // ---- Reading an optional that holds nothing --------------------------------------------
        ("Date? none = Date.Parse(\"the fifteenth\");\n        Console.WriteLine(none.Value());",
         typeof(ProfiC.Runtime.EmptyOptionalException),
         "Cannot read the value of an empty optional."),

        // ---- Calling without ever stopping ------------------------------------------------------
        // The one refusal no catch clause takes. It carries a name all the same, because a
        // reader still has to be told what stopped their program.
        ("Console.WriteLine(Program.Forever(1));", typeof(ProfiC.Runtime.RecursionTooDeepException),
         "Calls nested more than 512 deep, so the program stopped. This nearly always means a "
         + "function calls itself without ever reaching the case that stops it. This is not the "
         + "machine running out of room — the language counts the calls and stops early, while "
         + "it can still say what happened."),

        // ---- Changing a set that is being walked ------------------------------------------------
        ("integer[] xs = {1, 2};\n        integer[] alias = xs;\n"
         + "        loop each x in xs\n            alias.Insert(9);\n        end loop",
         typeof(ProfiC.Runtime.SequenceChangedException), null!),

        // ---- Where a real runs out, and where a float does not -------------------------------
        //
        // A real stops at its bounds the way an integer does. A float would answer both of these
        // with a value of its own — an infinity, or something that is not a number — and having
        // no such value is most of what tells the two types apart.
        ("Console.WriteLine(Math.Pow(10.0, 300.0));", typeof(OverflowException),
         "This result is too large to hold. A real counts in tens up to about 79 followed by 27 "
         + "zeros, and it stops at the end rather than carrying on into an infinity — which is "
         + "what a float does, and what makes the two worth telling apart."),

        ("Console.WriteLine(Math.Sqrt(-1.0));", typeof(ArgumentException),
         "That has no answer among the reals. A float would give back 'NaN' here and carry on; "
         + "a real has no such value, so the calculation stops instead."),

        // Every float is exactly some ratio, and a large one needs parts too big to hold.
        ("Console.WriteLine(Float.MaxValue.ToFraction());", typeof(OverflowException),
         "This float is too large to write as a fraction. Every float is exactly some ratio, but "
         + "the parts of a fraction are whole numbers, and this one needs a numerator larger than "
         + "an integer holds."),

        // Coming back from a float is the direction that can fail, and the two values only a
        // float has are the reason. A real has no form for either.
        ("Console.WriteLine(Float.NotANumber.ToReal());", typeof(ArgumentException),
         "A value that is not a number has no real to become. Only a float has one at all, so "
         + "there is nothing here for a real to hold."),

        ("Console.WriteLine(Float.Infinity.ToReal());", typeof(ArgumentException),
         "An infinity has no real to become. A float carries on past its bounds and a real stops "
         + "at them, so there is no number here for it to hold."),

        // And the third: a float larger than a real can carry.
        ("Console.WriteLine(Float.MaxValue.ToReal());", typeof(OverflowException),
         "This result is too large to hold. A real counts in tens up to about 79 followed by 27 "
         + "zeros, and it stops at the end rather than carrying on into an infinity — which is "
         + "what a float does, and what makes the two worth telling apart."),

        // ---- Reals that will not fit a fraction ---------------------------------------------
        //
        // Written down, this is PC0346 and never runs. Here it arrives in a variable, which is
        // the same division PC0324 draws around dividing by zero.
        ("real tiny = 0.0000000000000000001;\n        fraction held = tiny;"
         + "\n        Console.WriteLine(held);",
         typeof(OverflowException),
         "0.0000000000000000001 has no fraction to become. Every real is exactly a fraction, "
         + "but the parts of one are whole numbers — and this needs a numerator or a denominator "
         + "larger than an integer holds. Up to eighteen places after the point will convert."),
    ];

    public static IEnumerable<TestCaseData> Cases =>
        Refusals.Select(r => new TestCaseData(r.Body, r.Failure, r.Message)
            .SetName(r.Failure.Name + ": " + (r.Message ?? "any").Split('.')[0][..Math.Min(
                40, (r.Message ?? "any").Split('.')[0].Length)]));

    [TestCaseSource(nameof(Cases))]
    public void TheRefusalHappensAndSaysWhatItSays(string body, Type failure, string? message)
    {
        Exception raised = Assert.Catch(() => Run(body), $"'{body}' should have failed")!;

        Assert.That(raised, Is.TypeOf(failure));

        if (message is not null)
        {
            Assert.That(raised.Message, Is.EqualTo(message));
        }
    }

    private static void Run(string body)
    {
        DiagnosticBag diagnostics = new();

        CompilationUnit unit = Parser.Parse(
            new SourceText($$"""
                shared model Program
                    function Main()
                {{"        " + body}}
                    end function

                    # For the one refusal that is about calling rather than about a value.
                    integer function Forever(integer n)
                        yield Program.Forever(n + 1);
                    end function
                end model
                """, "<test>"),
            diagnostics);

        SemanticModel model = Resolver.Resolve(unit, diagnostics, requireEntryPoint: true);
        TypeChecker.Check(unit, model, diagnostics);
        DefiniteAssignment.Analyze(unit, model, diagnostics);

        Assert.That(
            diagnostics.Select(d => $"{d.Id}: {d.Message}"),
            Is.Empty,
            "a refusal is about running, so the program has to compile first");

        ProfiC.Interpreter.Interpreter.Run(
            Lowering.Lower(unit, model), model, new StringWriter());
    }

    // ---- Completeness -------------------------------------------------------------------------

    /// <summary>
    /// Throw sites no row can reach, each with the reason. A site listed here is a claim that
    /// no Profi-C program gets to it, which is a claim worth writing down rather than leaving
    /// as a blank in the coverage.
    /// </summary>
    private static readonly Dictionary<string, string> Unreachable = new(StringComparer.Ordinal)
    {
        ["This is not something that can be called."] =
            "the checker refuses a call on anything that is not callable",
        ["'*' cannot be called on this value."] =
            "the checker settles which member a call reaches",
        ["This program has no entry point to run."] =
            "the resolver requires one before anything runs",
        ["The entry point has no body."] =
            "Main cannot be abstract, and every other function has a body",
        ["an abstract function was reached with nothing written for it"] =
            "PC0241 refuses a model that can be constructed and leaves one open",
        ["Fraction is too large to normalize."] =
            "reachable only from a fraction whose parts already overflowed",
        ["Real is too precise to write as a fraction."] =
            "a real precise enough to reach it cannot be written as a literal",
        ["Only a finite real can be written as a fraction."] =
            "dividing a real by zero is refused before a non-finite one exists",
    };

    /// <summary>Every throw in the interpreter and the runtime, by the message it carries.</summary>
    private static IEnumerable<string> Sites()
    {
        string[] roots =
        [
            Path.Combine(RepositoryRoot, "src", "ProfiC.Interpreter"),
            Path.Combine(RepositoryRoot, "src", "ProfiC.Runtime"),
        ];

        foreach (string file in roots.SelectMany(
                     root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
                                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                                                 && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
        {
            string text = File.ReadAllText(file);

            // Two shapes carry a message. A throw writes one where it is raised; a factory
            // returns one worded in a single place because more than one back end will raise
            // it. Reading only the first would let anything moved into a factory quietly leave
            // the coverage, which is the opposite of what centralizing it was for.
            //
            // Both are anchored to the start of a line so that a "throw new" written inside a
            // doc comment is not read as a site. One was, and its message was a worked example.
            foreach (Match site in Regex.Matches(
                         text,
                         @"^\s*throw new (\w+)\(([\s\S]*?)\);"
                         + @"|^\s*public static \w*Exception \w+\(\) => (new)\(([\s\S]*?)\);",
                         RegexOptions.Multiline))
            {
                string written = site.Groups[2].Value + site.Groups[4].Value;

                string literals = string.Join(
                    " ",
                    Regex.Matches(written, @"""((?:[^""\\]|\\.)*)""")
                         .Select(m => m.Groups[1].Value));

                literals = Regex.Replace(literals, @"\{[^}]*\}", "*");
                literals = Regex.Replace(literals, @"\s+", " ").Trim();

                // A message built elsewhere and handed in carries nothing to match on. Those
                // are the re-throws, whose wording belongs to the site that first raised it.
                if (literals.Length > 0)
                {
                    yield return literals;
                }
            }
        }
    }

    /// <summary>
    /// Every refusal is either exercised or explained. A new one added to the interpreter with
    /// neither fails here, which is the only moment anyone would notice.
    /// </summary>
    [Test]
    public void EveryRefusalIsExercisedOrExplained()
    {
        HashSet<string> covered = [.. Refusals.Select(r => r.Message).Where(m => m is not null)];

        List<string> unaccounted = [];

        foreach (string site in Sites().Distinct(StringComparer.Ordinal))
        {
            if (Unreachable.ContainsKey(site))
            {
                continue;
            }

            // The site's literals are a skeleton with '*' where a value was written in.
            string pattern = "^" + string.Join(
                ".*", site.Split('*').Select(Regex.Escape)) + "$";

            if (!covered.Any(message => Regex.IsMatch(message, pattern)))
            {
                unaccounted.Add(site);
            }
        }

        Assert.That(
            unaccounted.Order(StringComparer.Ordinal),
            Is.Empty,
            "refusals with neither a row that reaches them nor a reason they cannot be reached");
    }
}
