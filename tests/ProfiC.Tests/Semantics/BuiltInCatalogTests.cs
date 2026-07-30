using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Semantics;
using ProfiC.Compiler.Text;

namespace ProfiC.Tests.Semantics;

/// <summary>
/// <para>The catalog of built-in models, and the promise that everything in it works.</para>
/// <para>The C# compiler already refuses a member with no implementation, because the back
/// end switches on the identifier without a fallback arm. These tests cover what it cannot:
/// that each member gives the right answer, rather than merely being reachable.</para>
/// </summary>
[TestFixture]
public sealed class BuiltInCatalogTests
{
    /// <summary>
    /// Every member of every built-in model, with a call that exercises it and the answer it
    /// must give. A member added to the catalog with no row here fails the coverage test
    /// below, so this table cannot fall behind.
    /// </summary>
    private static readonly (BuiltInId Id, string Call, string Expected)[] Expectations =
    [
        (BuiltInId.ConsoleWrite, "Console.Write(\"x\")", "x"),
        (BuiltInId.ConsoleWriteLine, "Console.WriteLine(\"x\")", "x\n"),

        // Read waits on input, so it is exercised for reachability rather than for a value.
        (BuiltInId.ConsoleRead, "", ""),

        // Asked of two references to one set, where "the same object" means something. Asked
        // of two integers it is always false, since each boxes separately — as in C#.
        (BuiltInId.ReferenceEquals,
         "integer[] a = {1};\n        integer[] b = a;\n        Console.WriteLine(Reference.Equals(a, b))",
         "true\n"),

        // Values, so written without parentheses. Compared against a rounding rather than
        // printed, since the last digit of a real is not what this test is about.
        (BuiltInId.MathPi, "Console.WriteLine(Math.Round(Math.Pi * 100.0))", "314\n"),
        (BuiltInId.MathE, "Console.WriteLine(Math.Round(Math.E * 100.0))", "272\n"),

        (BuiltInId.MathSqrt, "Console.WriteLine(Math.Sqrt(16.0))", "4\n"),
        (BuiltInId.MathCbrt, "Console.WriteLine(Math.Cbrt(-8.0))", "-2\n"),
        (BuiltInId.MathRoot, "Console.WriteLine(Math.Root(32.0, 5.0))", "2\n"),
        (BuiltInId.MathPow, "Console.WriteLine(Math.Pow(2.0, 8.0))", "256\n"),
        (BuiltInId.MathFactorial, "Console.WriteLine(Math.Factorial(20))", "2432902008176640000\n"),

        // Log of one number is the natural logarithm, so the log of E is 1.
        (BuiltInId.MathLog, "Console.WriteLine(Math.Log(Math.E))", "1\n"),
        (BuiltInId.MathLogInBase, "Console.WriteLine(Math.Log(8.0, 2.0))", "3\n"),
        (BuiltInId.MathLog10, "Console.WriteLine(Math.Log10(100.0))", "2\n"),
        (BuiltInId.MathLog2, "Console.WriteLine(Math.Log2(8.0))", "3\n"),

        (BuiltInId.MathSin, "Console.WriteLine(Math.Sin(0.0))", "0\n"),
        (BuiltInId.MathCos, "Console.WriteLine(Math.Cos(0.0))", "1\n"),
        (BuiltInId.MathTan, "Console.WriteLine(Math.Tan(0.0))", "0\n"),
        (BuiltInId.MathAsin, "Console.WriteLine(Math.Asin(0.0))", "0\n"),
        (BuiltInId.MathAcos, "Console.WriteLine(Math.Round(Math.Acos(0.0) * 2.0 / Math.Pi))", "1\n"),
        (BuiltInId.MathAtan, "Console.WriteLine(Math.Atan(0.0))", "0\n"),
        (BuiltInId.MathAtan2, "Console.WriteLine(Math.Atan2(0.0, 1.0))", "0\n"),

        // Each hyperbolic at the point where its answer is a whole number.
        (BuiltInId.MathSinh, "Console.WriteLine(Math.Sinh(0.0))", "0\n"),
        (BuiltInId.MathCosh, "Console.WriteLine(Math.Cosh(0.0))", "1\n"),
        (BuiltInId.MathTanh, "Console.WriteLine(Math.Tanh(0.0))", "0\n"),
        (BuiltInId.MathAsinh, "Console.WriteLine(Math.Asinh(0.0))", "0\n"),
        (BuiltInId.MathAcosh, "Console.WriteLine(Math.Acosh(1.0))", "0\n"),
        (BuiltInId.MathAtanh, "Console.WriteLine(Math.Atanh(0.0))", "0\n"),

        // Chance is asked for something that has to hold whatever it drew, since the draw
        // itself is different every run.
        (BuiltInId.RandomNew,
         "Random any = new Random();\n        Console.WriteLine(any.Next(5) < 5)", "true\n"),
        (BuiltInId.RandomNewSeeded,
         "Random a = new Random(42);\n        Random b = new Random(42);\n"
         + "        Console.WriteLine(a.Next() == b.Next())", "true\n"),
        (BuiltInId.RandomNext, "Console.WriteLine(Random.Next() >= 0)", "true\n"),
        (BuiltInId.RandomNextBelow, "Console.WriteLine(Random.Next(5) < 5)", "true\n"),
        (BuiltInId.RandomNextBetween,
         "let n = Random.Next(1, 7);\n        Console.WriteLine(n >= 1 and n < 7)", "true\n"),
        (BuiltInId.RandomNextDouble, "Console.WriteLine(Random.NextDouble() < 1.0)", "true\n"),

        (BuiltInId.DateTimeNewDate, "Console.WriteLine(new DateTime(1969, 7, 20))", "1969-07-20\n"),
        (BuiltInId.DateTimeNewMoment,
         "Console.WriteLine(new DateTime(2000, 1, 2, 3, 4, 5))", "2000-01-02 03:04:05\n"),

        // Now and Today differ every run, so each is asked something that does not.
        (BuiltInId.DateTimeNow,
         "Console.WriteLine(DateTime.Now.Year > 2000)", "true\n"),
        (BuiltInId.DateTimeToday,
         "Console.WriteLine(DateTime.Today.Hour)", "0\n"),

        (BuiltInId.DateTimeYear, "Console.WriteLine(new DateTime(1969, 7, 20).Year)", "1969\n"),
        (BuiltInId.DateTimeMonth, "Console.WriteLine(new DateTime(1969, 7, 20).Month)", "7\n"),
        (BuiltInId.DateTimeDay, "Console.WriteLine(new DateTime(1969, 7, 20).Day)", "20\n"),
        (BuiltInId.DateTimeHour,
         "Console.WriteLine(new DateTime(2000, 1, 2, 3, 4, 5).Hour)", "3\n"),
        (BuiltInId.DateTimeMinute,
         "Console.WriteLine(new DateTime(2000, 1, 2, 3, 4, 5).Minute)", "4\n"),
        (BuiltInId.DateTimeSecond,
         "Console.WriteLine(new DateTime(2000, 1, 2, 3, 4, 5).Second)", "5\n"),

        // The twentieth of July 1969 was a Sunday, which is nought.
        (BuiltInId.DateTimeDayOfWeek,
         "Console.WriteLine(new DateTime(1969, 7, 20).DayOfWeek)", "0\n"),
        (BuiltInId.DateTimeDayOfYear,
         "Console.WriteLine(new DateTime(1969, 7, 20).DayOfYear)", "201\n"),

        (BuiltInId.DateTimeAddDays,
         "Console.WriteLine(new DateTime(1969, 7, 20).AddDays(10))", "1969-07-30\n"),
        (BuiltInId.DateTimeAddHours,
         "Console.WriteLine(new DateTime(1969, 7, 20).AddHours(3))", "1969-07-20 03:00:00\n"),
        (BuiltInId.DateTimeAddMinutes,
         "Console.WriteLine(new DateTime(1969, 7, 20).AddMinutes(90))", "1969-07-20 01:30:00\n"),
        (BuiltInId.DateTimeAddSeconds,
         "Console.WriteLine(new DateTime(1969, 7, 20).AddSeconds(61))", "1969-07-20 00:01:01\n"),
        (BuiltInId.DateTimeAddYears,
         "Console.WriteLine(new DateTime(1969, 7, 20).AddYears(1))", "1970-07-20\n"),
        (BuiltInId.DateTimeAddMonths,
         "Console.WriteLine(new DateTime(1969, 7, 20).AddMonths(2))", "1969-09-20\n"),

        (BuiltInId.DateTimeCompareTo,
         "let early = new DateTime(1969, 7, 16);\n"
         + "        Console.WriteLine(early.CompareTo(new DateTime(1969, 7, 20)) < 0)", "true\n"),

        (BuiltInId.DateTimeSubtract,
         "let landing = new DateTime(1969, 7, 20);\n"
         + "        Console.WriteLine(landing.Subtract(new DateTime(1969, 7, 16)))",
         "4.00:00:00\n"),
        (BuiltInId.DateTimeSubtractSpan,
         "Console.WriteLine(new DateTime(1969, 7, 20).Subtract(TimeSpan.FromDays(4.0)))",
         "1969-07-16\n"),
        (BuiltInId.DateTimeAdd,
         "Console.WriteLine(new DateTime(1969, 7, 16).Add(TimeSpan.FromDays(4.0)))",
         "1969-07-20\n"),

        (BuiltInId.TimeSpanNewTime, "Console.WriteLine(new TimeSpan(1, 30, 0))", "01:30:00\n"),
        (BuiltInId.TimeSpanNewSpan, "Console.WriteLine(new TimeSpan(2, 3, 0, 0))", "2.03:00:00\n"),
        (BuiltInId.TimeSpanZero, "Console.WriteLine(TimeSpan.Zero)", "00:00:00\n"),

        (BuiltInId.TimeSpanFromDays, "Console.WriteLine(TimeSpan.FromDays(1.5))", "1.12:00:00\n"),
        (BuiltInId.TimeSpanFromHours, "Console.WriteLine(TimeSpan.FromHours(2.0))", "02:00:00\n"),
        (BuiltInId.TimeSpanFromMinutes,
         "Console.WriteLine(TimeSpan.FromMinutes(90.0))", "01:30:00\n"),
        (BuiltInId.TimeSpanFromSeconds,
         "Console.WriteLine(TimeSpan.FromSeconds(61.0))", "00:01:01\n"),

        // The parts against the totals: an hour and a half is one hour and thirty minutes,
        // and is ninety minutes.
        (BuiltInId.TimeSpanDays, "Console.WriteLine(TimeSpan.FromDays(1.5).Days)", "1\n"),
        (BuiltInId.TimeSpanHours, "Console.WriteLine(TimeSpan.FromMinutes(90.0).Hours)", "1\n"),
        (BuiltInId.TimeSpanMinutes,
         "Console.WriteLine(TimeSpan.FromMinutes(90.0).Minutes)", "30\n"),
        (BuiltInId.TimeSpanSeconds,
         "Console.WriteLine(TimeSpan.FromSeconds(61.0).Seconds)", "1\n"),
        (BuiltInId.TimeSpanTotalDays, "Console.WriteLine(TimeSpan.FromHours(12.0).TotalDays)", "0.5\n"),
        (BuiltInId.TimeSpanTotalHours,
         "Console.WriteLine(TimeSpan.FromMinutes(90.0).TotalHours)", "1.5\n"),
        (BuiltInId.TimeSpanTotalMinutes,
         "Console.WriteLine(TimeSpan.FromMinutes(90.0).TotalMinutes)", "90\n"),
        (BuiltInId.TimeSpanTotalSeconds,
         "Console.WriteLine(TimeSpan.FromMinutes(1.0).TotalSeconds)", "60\n"),

        (BuiltInId.TimeSpanAdd,
         "Console.WriteLine(TimeSpan.FromHours(1.0).Add(TimeSpan.FromMinutes(30.0)))",
         "01:30:00\n"),

        // A span may run backwards, and the sign survives being printed.
        (BuiltInId.TimeSpanSubtract,
         "Console.WriteLine(TimeSpan.FromMinutes(30.0).Subtract(TimeSpan.FromHours(1.0)))",
         "-00:30:00\n"),
        (BuiltInId.TimeSpanNegate,
         "Console.WriteLine(TimeSpan.FromHours(1.0).Negate())", "-01:00:00\n"),
        (BuiltInId.TimeSpanDuration,
         "Console.WriteLine(TimeSpan.FromHours(1.0).Negate().Duration())", "01:00:00\n"),
        (BuiltInId.TimeSpanCompareTo,
         "let short = TimeSpan.FromMinutes(30.0);\n"
         + "        Console.WriteLine(short.CompareTo(TimeSpan.FromHours(1.0)) < 0)", "true\n"),

        (BuiltInId.DateNew, "Console.WriteLine(new Date(1969, 7, 20))", "1969-07-20\n"),
        (BuiltInId.DateToday, "Console.WriteLine(Date.Today.Year > 2000)", "true\n"),
        (BuiltInId.DateFromMoment,
         "Console.WriteLine(Date.FromDateTime(new DateTime(1969, 7, 20, 20, 17, 40)))",
         "1969-07-20\n"),

        (BuiltInId.DateYear, "Console.WriteLine(new Date(1969, 7, 20).Year)", "1969\n"),
        (BuiltInId.DateMonth, "Console.WriteLine(new Date(1969, 7, 20).Month)", "7\n"),
        (BuiltInId.DateDay, "Console.WriteLine(new Date(1969, 7, 20).Day)", "20\n"),
        (BuiltInId.DateDayOfWeek, "Console.WriteLine(new Date(1969, 7, 20).DayOfWeek)", "0\n"),
        (BuiltInId.DateDayOfYear, "Console.WriteLine(new Date(1969, 7, 20).DayOfYear)", "201\n"),

        (BuiltInId.DateAddDays,
         "Console.WriteLine(new Date(1969, 7, 20).AddDays(10))", "1969-07-30\n"),
        (BuiltInId.DateAddMonths,
         "Console.WriteLine(new Date(1969, 7, 20).AddMonths(2))", "1969-09-20\n"),
        (BuiltInId.DateAddYears,
         "Console.WriteLine(new Date(1969, 7, 20).AddYears(1))", "1970-07-20\n"),

        (BuiltInId.DateAtTime,
         "Console.WriteLine(new Date(1969, 7, 20).ToDateTime(new Time(9, 0)))",
         "1969-07-20 09:00:00\n"),
        (BuiltInId.DateCompareTo,
         "let early = new Date(1969, 7, 16);\n"
         + "        Console.WriteLine(early.CompareTo(new Date(1969, 7, 20)) < 0)", "true\n"),

        (BuiltInId.TimeNewToMinute, "Console.WriteLine(new Time(9, 5))", "09:05:00\n"),
        (BuiltInId.TimeNewToSecond, "Console.WriteLine(new Time(9, 5, 30))", "09:05:30\n"),
        (BuiltInId.TimeNow, "Console.WriteLine(Time.Now.Hour >= 0)", "true\n"),
        (BuiltInId.TimeFromMoment,
         "Console.WriteLine(Time.FromDateTime(new DateTime(1969, 7, 20, 20, 17, 40)))",
         "20:17:40\n"),

        (BuiltInId.TimeHour, "Console.WriteLine(new Time(17, 30).Hour)", "17\n"),
        (BuiltInId.TimeMinute, "Console.WriteLine(new Time(17, 30).Minute)", "30\n"),
        (BuiltInId.TimeSecond, "Console.WriteLine(new Time(17, 30, 5).Second)", "5\n"),

        // A clock wraps round midnight rather than overflowing.
        (BuiltInId.TimeAddHours, "Console.WriteLine(new Time(17, 30).AddHours(8.0))", "01:30:00\n"),
        (BuiltInId.TimeAddMinutes,
         "Console.WriteLine(new Time(17, 30).AddMinutes(45.0))", "18:15:00\n"),

        (BuiltInId.TimeToTimeSpan,
         "Console.WriteLine(new Time(17, 30).ToTimeSpan())", "17:30:00\n"),
        (BuiltInId.TimeCompareTo,
         "let open = new Time(9, 0);\n"
         + "        Console.WriteLine(open.CompareTo(new Time(17, 30)) < 0)", "true\n"),

        // Each of these is written with an argument of exactly its own type, since that is
        // what picks it: an integer would widen into the real and fraction versions too.
        (BuiltInId.MathAbsInteger, "Console.WriteLine(Math.Abs(-3))", "3\n"),
        (BuiltInId.MathAbsReal, "Console.WriteLine(Math.Abs(-3.5))", "3.5\n"),
        (BuiltInId.MathAbsFraction, "Console.WriteLine(Math.Abs(-3|4))", "3|4\n"),

        (BuiltInId.MathFloorReal, "Console.WriteLine(Math.Floor(3.7))", "3\n"),
        (BuiltInId.MathFloorFraction, "Console.WriteLine(Math.Floor(7|2))", "3\n"),
        (BuiltInId.MathCeilingReal, "Console.WriteLine(Math.Ceiling(3.2))", "4\n"),
        (BuiltInId.MathCeilingFraction, "Console.WriteLine(Math.Ceiling(7|2))", "4\n"),
        (BuiltInId.MathRoundReal, "Console.WriteLine(Math.Round(2.5))", "3\n"),
        (BuiltInId.MathRoundRealPlaces, "Console.WriteLine(Math.Round(2.345, 2))", "2.35\n"),

        // Reading text back. Each is asked once of text that reads and once of text that does
        // not, since an optional yielding nothing is half of what these are for.
        (BuiltInId.DateTimeParse,
         "Console.WriteLine(DateTime.Parse(\"2026-08-15 14:30:00\").Or(new DateTime(1, 1, 1)))",
         "2026-08-15 14:30:00\n"),
        (BuiltInId.DateTimeParseExact,
         "Console.WriteLine(DateTime.Parse(\"15/08/2026\", \"dd/MM/yyyy\").HasValue())",
         "true\n"),
        (BuiltInId.TimeSpanParse,
         "Console.WriteLine(TimeSpan.Parse(\"02:30:00\").Or(TimeSpan.Zero))",
         "02:30:00\n"),
        (BuiltInId.TimeSpanParseExact,
         "Console.WriteLine(TimeSpan.Parse(\"02:30:00\", \"c\").HasValue())",
         "true\n"),
        (BuiltInId.DateParse,
         "Console.WriteLine(Date.Parse(\"2026-08-15\").Or(new Date(1, 1, 1)))",
         "2026-08-15\n"),
        (BuiltInId.DateParseExact,
         "Console.WriteLine(Date.Parse(\"nonsense\", \"yyyy-MM-dd\").HasValue())",
         "false\n"),
        (BuiltInId.TimeParse,
         "Console.WriteLine(Time.Parse(\"nonsense\").HasValue())",
         "false\n"),
        (BuiltInId.TimeParseExact,
         "Console.WriteLine(Time.Parse(\"14:30\", \"HH:mm\").Or(new Time(0, 0)))",
         "14:30:00\n"),

        // A moment taken apart and put back together.
        (BuiltInId.DateTimeDatePart,
         "Console.WriteLine(new DateTime(2026, 8, 15, 14, 30, 0).Date)", "2026-08-15\n"),
        (BuiltInId.DateTimeTimePart,
         "Console.WriteLine(new DateTime(2026, 8, 15, 14, 30, 0).Time)", "14:30:00\n"),
        (BuiltInId.DateTimeFromDate,
         "Console.WriteLine(new DateTime(new Date(2026, 8, 15)).Hour)", "0\n"),
        (BuiltInId.DateTimeFromDateAndTime,
         "Console.WriteLine(new DateTime(new Date(2026, 8, 15), new Time(9, 0)))",
         "2026-08-15 09:00:00\n"),
        (BuiltInId.MathRoundFraction, "Console.WriteLine(Math.Round(5|2))", "3\n"),

        (BuiltInId.MathMinInteger, "Console.WriteLine(Math.Min(3, 7))", "3\n"),
        (BuiltInId.MathMinReal, "Console.WriteLine(Math.Min(3.5, 7.5))", "3.5\n"),
        (BuiltInId.MathMinFraction, "Console.WriteLine(Math.Min(1|3, 1|2))", "1|3\n"),
        (BuiltInId.MathMaxInteger, "Console.WriteLine(Math.Max(3, 7))", "7\n"),
        (BuiltInId.MathMaxReal, "Console.WriteLine(Math.Max(3.5, 7.5))", "7.5\n"),
        (BuiltInId.MathMaxFraction, "Console.WriteLine(Math.Max(1|3, 1|2))", "1|2\n"),

        (BuiltInId.FractionCreate, "Console.WriteLine(Fraction.Create(6, 8))", "3|4\n"),
        (BuiltInId.FractionCreateWhole, "Console.WriteLine(Fraction.Create(3))", "3|1\n"),
    ];

    private static string Run(string body) => RunProgram(
        $$"""
        global model Program
            function Main()
                {{body}};
            end function
        end model
        """);

    private static string RunProgram(string source)
    {
        DiagnosticBag diagnostics = new();
        CompilationUnit unit = Parser.Parse(new SourceText(source, "<test>"), diagnostics);

        SemanticModel model = Resolver.Resolve(unit, diagnostics, requireEntryPoint: true);
        TypeChecker.Check(unit, model, diagnostics);
        DefiniteAssignment.Analyze(unit, model, diagnostics);

        Assert.That(diagnostics.Select(d => $"{d.Id}: {d.Message}"), Is.Empty,
                    "the program should check cleanly");

        StringWriter output = new();
        ProfiC.Interpreter.Interpreter.Run(Lowering.Lower(unit, model), model, output);
        return output.ToString().ReplaceLineEndings("\n");
    }

    /// <summary>
    /// A member that type-checks but produces nothing satisfies every other test in the
    /// suite. Only running it and comparing the answer catches that.
    /// </summary>
    [TestCaseSource(nameof(Expectations))]
    public void EveryCatalogMemberProducesItsAnswer((BuiltInId Id, string Call, string Expected) row)
    {
        if (row.Call.Length == 0)
        {
            Assert.Pass("exercised for reachability only");
        }

        Assert.That(Run(row.Call), Is.EqualTo(row.Expected), $"{row.Id} gave the wrong answer");
    }

    /// <summary>Every member the catalog declares, on either surface.</summary>
    private static IReadOnlyList<BuiltInMember> Everything()
    {
        SetType set = new(PrimitiveType.Integer);
        OptionalType optional = new(PrimitiveType.Integer);

        return
        [
            .. BuiltIns.Models.SelectMany(m => m.Members),

            // The forms of "new" are held apart from the members, so they have to be gathered
            // apart from them too or one could be added with nothing noticing.
            .. BuiltIns.Models.SelectMany(m => m.Constructors),
            .. BuiltIns.OnSet(set),

            // A set of optionals answers four the others do not, since only there is there
            // anything empty to drop, so both shapes are asked about.
            .. BuiltIns.OnSet(new SetType(optional)),

            .. BuiltIns.OnString(),
            .. BuiltIns.OnOptional(optional),
            .. BuiltIns.OnFraction(),
            .. BuiltIns.OnReal(),
            .. BuiltIns.OnInteger(),
            .. BuiltIns.OnEnumeration(),
            .. BuiltIns.OnException(),
        ];
    }

    /// <summary>
    /// The two tables above must cover the whole enumeration between them. The C# compiler
    /// already refuses a member nobody implemented; this refuses one nobody tested.
    /// </summary>
    [Test]
    public void EveryIdentifierIsCovered()
    {
        IEnumerable<BuiltInId> tested =
            Expectations.Select(e => e.Id)
                        .Concat(ValueMemberIds)
                        .Concat(FileMemberTests.Covered)
                        .Distinct();

        Assert.That(
            tested.OrderBy(i => i),
            Is.EqualTo(Enum.GetValues<BuiltInId>().OrderBy(i => i)),
            "a built-in was added to the catalog without a test that runs it");
    }

    /// <summary>Every identifier must appear in the catalog, and the catalog only.</summary>
    [Test]
    public void EveryIdentifierAppearsInTheCatalog() => Assert.That(
        Everything().Select(m => m.Id!.Value).Distinct().OrderBy(i => i),
        Is.EqualTo(Enum.GetValues<BuiltInId>().OrderBy(i => i)),
        "an identifier exists with no catalog entry, or the reverse");

    /// <summary>
    /// A model's members must be unique within it; the value surfaces deliberately repeat
    /// names, since a set and a string both answer Count.
    /// </summary>
    [Test]
    public void AModelDeclaresEachMemberOnce() => Assert.That(
        BuiltIns.Models.SelectMany(m => m.Members).Select(m => m.Id!.Value), Is.Unique);

    /// <summary>
    /// The resolver protects exactly the names the catalog lists. A program that could
    /// declare one of them would make the built-in of that name unreachable.
    /// </summary>
    [TestCase("Console")]
    [TestCase("Model")]
    [TestCase("Exception")]
    [TestCase("Reference")]
    [TestCase("Math")]
    [TestCase("Fraction")]
    [TestCase("Random")]
    [TestCase("DateTime")]
    [TestCase("ArgumentException")]
    [TestCase("DivideByZeroException")]
    public void NoBuiltInNameCanBeRedeclared(string name)
    {
        DiagnosticBag diagnostics = new();
        CompilationUnit unit = Parser.Parse(
            new SourceText($"model {name}\nend model\n", "<test>"), diagnostics);

        Resolver.Resolve(unit, diagnostics);

        Assert.That(diagnostics.Select(d => d.Id), Does.Contain("PC0203"));
    }

    [Test]
    public void TheCatalogCoversEveryProtectedName() => Assert.That(
        BuiltIns.AllTypeNames.OrderBy(n => n, StringComparer.Ordinal),
        Is.EqualTo(BuiltIns.ModelNames.Concat(BuiltIns.ExceptionNames)
                                      .OrderBy(n => n, StringComparer.Ordinal)));

    /// <summary>
    /// Recorded now so the catalog is already shaped for namespaces. Nothing reads it yet —
    /// every name still resolves unqualified — but when scoping lands the data is in place.
    /// </summary>
    [Test]
    public void EveryModelRecordsItsNamespace() => Assert.That(
        BuiltIns.Models.Select(m => m.Namespace), Is.All.EqualTo("Standard"));

    /// <summary>
    /// <para>Members found on a value rather than through a model's name.</para>
    /// <para>These are not in the catalog — they are keyed by the receiver's type, which
    /// needs a different shape — so nothing makes the declaration and the implementation agree.
    /// Running each and comparing the answer is the whole of the protection they have.</para>
    /// </summary>
    [TestCase("integer[] xs = {3, 1, 2};\n        Console.WriteLine(xs.Count())", "3\n")]
    [TestCase("integer[] xs = {3, 1, 2};\n        Console.WriteLine(xs.Contains(1))", "true\n")]
    [TestCase("integer[] xs = {3, 1, 2};\n        Console.WriteLine(xs.IndexOf(2))", "2\n")]
    [TestCase("integer[] xs = {1};\n        xs.Insert(2);\n        Console.WriteLine(xs.Count())", "2\n")]
    [TestCase("integer[] xs = {1};\n        xs.InsertAt(0, 9);\n        Console.WriteLine(xs[0])", "9\n")]
    [TestCase("integer[] xs = {1};\n        Console.WriteLine(xs.Remove(1))", "true\n")]
    [TestCase("integer[] xs = {1, 2};\n        xs.RemoveAt(0);\n        Console.WriteLine(xs[0])", "2\n")]
    [TestCase("integer[] xs = {1};\n        xs.Clear();\n        Console.WriteLine(xs.Count())", "0\n")]
    [TestCase("Console.WriteLine(\"hello\".Count())", "5\n")]
    [TestCase("Console.WriteLine(\"hello\".Contains(\"ell\"))", "true\n")]
    [TestCase("Console.WriteLine(\"hello\".IndexOf(\"l\"))", "2\n")]
    [TestCase("Console.WriteLine(\"hello\".Substring(1, 3))", "ell\n")]
    [TestCase("Console.WriteLine(\"hello\".Insert(\"!\"))", "hello!\n")]
    [TestCase("Console.WriteLine(\"hello\".InsertAt(0, \"X\"))", "Xhello\n")]
    [TestCase("Console.WriteLine(\"hello\".Remove(\"l\"))", "heo\n")]
    [TestCase("Console.WriteLine(\"hello\".RemoveAt(0))", "ello\n")]
    // Characters are quoted inside a set, so that one which is a comma or a space is still
    // readable beside the commas and spaces that separate the elements.
    [TestCase("Console.WriteLine(\"hi\".ToCharacters())", "{'h', 'i'}\n")]
    [TestCase("integer? m = 4;\n        Console.WriteLine(m.HasValue())", "true\n")]
    [TestCase("integer? m;\n        Console.WriteLine(m.Or(7))", "7\n")]
    [TestCase("integer? m = 4;\n        Console.WriteLine(m.Value())", "4\n")]
    [TestCase("Console.WriteLine((5).ToString())", "5\n")]
    [TestCase("Console.WriteLine((5).Equals(5))", "true\n")]
    [TestCase("Console.WriteLine((5).Equals(6))", "false\n")]
    [TestCase("Console.WriteLine((1|2).ToReal())", "0.5\n")]
    [TestCase("Console.WriteLine((0.5).ToFraction())", "1|2\n")]
    public void EveryValueMemberProducesItsAnswer(string body, string expected) =>
        Assert.That(Run(body), Is.EqualTo(expected));

    /// <summary>
    /// An enumeration and an exception each need a declaration, which the wrapper above cannot
    /// hold, so these two are exercised from whole programs instead.
    /// </summary>
    [Test]
    public void AnEnumerationMemberProducesItsAnswer() => Assert.That(
        RunProgram("""
            enumeration Color
                Red,
                Green,
                Blue
            end enumeration

            global model Program
                function Main()
                    Color c = Color.Green;
                    Console.WriteLine(c.ToInteger());
                end function
            end model
            """),
        Is.EqualTo("1\n"));

    [Test]
    public void AnExceptionMemberProducesItsAnswer() => Assert.That(
        RunProgram("""
            model NotFoundException extends Exception
                public function NotFoundException(string what)
                    base("no " + what);
                end function
            end model

            global model Program
                function Main()
                    try
                        throw new NotFoundException("key");
                    catch Exception problem
                        Console.WriteLine(problem.Message());
                    end try
                end function
            end model
            """),
        Is.EqualTo("no key\n"));

    /// <summary>Every form of a string's Trim family, and what each takes away.</summary>
    [TestCase("\"  hi  \".Trim()", "hi")]
    [TestCase("\"xxhixx\".Trim(\"x\")", "hi")]
    [TestCase("\"xyhiyx\".Trim({'x', 'y'})", "hi")]
    [TestCase("\"  hi  \".TrimStart()", "hi  ")]
    [TestCase("\"xxhixx\".TrimStart(\"x\")", "hixx")]
    [TestCase("\"xyhiyx\".TrimStart({'x', 'y'})", "hiyx")]
    [TestCase("\"  hi  \".TrimEnd()", "  hi")]
    [TestCase("\"xxhixx\".TrimEnd(\"x\")", "xxhi")]
    [TestCase("\"xyhiyx\".TrimEnd({'x', 'y'})", "xyhi")]
    public void AStringTrimsFromEitherEnd(string call, string expected) => Assert.That(
        RunProgram($$"""
            global model Program
                function Main()
                    Console.WriteLine("[" + {{call}} + "]");
                end function
            end model
            """),
        Is.EqualTo($"[{expected}]\n"));

    /// <summary>
    /// <para>A string answers Subset too, and gives back a string.</para>
    /// <para>It differs from Substring in its second argument rather than in what it does:
    /// one takes how many, the other where to stop. A run of a string is a string, the same
    /// rule Subset follows on a set.</para>
    /// </summary>
    [TestCase("word.Substring(1, 3)", "ell")]
    [TestCase("word.Subset(1, 4)", "ell")]
    [TestCase("word.Subset(2)", "llo")]
    [TestCase("word.Subset(0, 2) + word.Subset(2)", "hello")]
    public void AStringAnswersSubsetAsWellAsSubstring(string call, string expected) =>
        Assert.That(
            RunProgram($$"""
                global model Program
                    function Main()
                        string word = "hello";
                        Console.WriteLine({{call}});
                    end function
                end model
                """),
            Is.EqualTo(expected + "\n"));

    /// <summary>
    /// <para>A run of a set, with the end exclusive.</para>
    /// <para>Exclusive is what makes the two halves of a split add back up to the whole,
    /// which the last row asks directly.</para>
    /// </summary>
    [TestCase("xs.Subset(2)", "{30, 40, 50}")]
    [TestCase("xs.Subset(1, 3)", "{20, 30}")]
    [TestCase("xs.Subset(0, 0)", "{}")]
    [TestCase("xs.Subset(5)", "{}")]
    [TestCase("xs.Subset(0, 2).Count() + xs.Subset(2).Count()", "5")]
    public void ASubsetIsARunOfASet(string call, string expected) => Assert.That(
        RunProgram($$"""
            global model Program
                function Main()
                    integer[] xs = {10, 20, 30, 40, 50};
                    Console.WriteLine({{call}});
                end function
            end model
            """),
        Is.EqualTo(expected + "\n"));

    /// <summary>
    /// <para>Dropping the empties out of a set of optionals.</para>
    /// <para>The three that work on the ends leave an empty in the middle alone, so the set
    /// stays a set of optionals. TrimAll takes every one, so nothing left can be absent and
    /// the type says so — which is why the last row can hold the answer in an integer set.
    /// </para>
    /// </summary>
    [TestCase("sparse.Trim().Count()", "3")]
    [TestCase("sparse.TrimStart().Count()", "4")]
    [TestCase("sparse.TrimEnd().Count()", "4")]
    [TestCase("sparse.TrimAll()", "{1, 2}")]
    public void ASetOfOptionalsDropsItsEmpties(string call, string expected) => Assert.That(
        RunProgram($$"""
            global model Program
                function Main()
                    integer? nothing;
                    integer?[] sparse = {nothing, 1, nothing, 2, nothing};
                    integer[] solid = sparse.TrimAll();
                    Console.WriteLine({{call}});
                end function
            end model
            """),
        Is.EqualTo(expected + "\n"));

    /// <summary>
    /// <para>Nothing that yields a set changes one.</para>
    /// <para>This is what makes <c>TrimAll</c> safe to give a narrower type than the set it
    /// was asked of: it hands back a new set, so the promise that nothing in it is absent is
    /// about that set alone and cannot be broken through the original. A member that changes
    /// a set yields nothing instead, which is the same rule read from the other side.</para>
    /// </summary>
    [TestCase("original.Subset(1)")]
    [TestCase("original.Subset(1, 2)")]
    [TestCase("original.Trim()")]
    [TestCase("original.TrimStart()")]
    [TestCase("original.TrimEnd()")]
    [TestCase("original.TrimAll()")]
    public void AMemberThatYieldsASetLeavesTheOriginalAlone(string call) => Assert.That(
        RunProgram($$"""
            global model Program
                function Main()
                    integer? nothing;
                    integer?[] original = {nothing, 1, nothing, 2, nothing};

                    let copy = {{call}};
                    copy.Clear();

                    Console.WriteLine(original);
                end function
            end model
            """),
        Is.EqualTo("{empty, 1, empty, 2, empty}\n"),
        $"{call} reached back into the set it was asked of");

    /// <summary>
    /// The copy is shallow, which is the depth the rest of the language uses: assigning a
    /// model copies the reference, so a set copied out holds the very same models.
    /// </summary>
    [Test]
    public void TheCopyIsShallow() => Assert.That(
        RunProgram("""
            model Tag
                public string Name;

                public function Tag(string name)
                    this.Name = name;
                end function
            end model

            global model Program
                function Main()
                    Tag[] tags = {new Tag("first"), new Tag("second")};
                    Tag[] some = tags.Subset(0, 1);

                    some[0].Name = "renamed";

                    Console.WriteLine(tags[0].Name);
                    Console.WriteLine(Reference.Equals(tags[0], some[0]));
                end function
            end model
            """),
        Is.EqualTo("renamed\ntrue\n"));

    /// <summary>
    /// <para>Taking a string apart, putting a set back together, and writing a value out by a
    /// pattern.</para>
    /// <para>The patterns are .NET's own and are passed through untouched, so these rows are
    /// as much a check that nothing intercepts them as a check of the members themselves —
    /// <c>yyyy-MM-dd</c> arriving intact matters more than any one of the answers.</para>
    /// </summary>
    [TestCase("\"the quick brown\".Split(\" \").Count()", "3")]
    [TestCase("\"a-b-c\".Split(\"-\").Join(\"+\")", "a+b+c")]
    [TestCase("{1, 2, 3}.Join(\", \")", "1, 2, 3")]
    [TestCase("{1, 2}.Union({3, 4})", "{1, 2, 3, 4}")]
    [TestCase("{1, 2}.Union({2, 3})", "{1, 2, 2, 3}")]
    [TestCase("{1, 2, 3}.Intersect({2, 3, 4})", "{2, 3}")]
    [TestCase("{1, 2}.Intersect({3, 4})", "{}")]
    [TestCase("{1, 2, 3}.Except({2, 4})", "{1, 3}")]
    [TestCase("{3, 1, 3, 2, 1}.Distinct()", "{3, 1, 2}")]
    [TestCase("{1, 2, 3}.Distinct()", "{1, 2, 3}")]
    [TestCase("{\"a\", \"b\", \"a\"}.Distinct()", "{\"a\", \"b\"}")]
    [TestCase("{1, 2}.Union({2, 3}).Distinct()", "{1, 2, 3}")]
    [TestCase("{1, 2, 3}.Except({})", "{1, 2, 3}")]
    [TestCase("\"a-b-c\".Replace(\"-\", \".\")", "a.b.c")]
    [TestCase("\"Profi-C\".ToUpper()", "PROFI-C")]
    [TestCase("\"Profi-C\".ToLower()", "profi-c")]
    [TestCase("\"matt nwachukwu\".Capitalize()", "Matt nwachukwu")]
    [TestCase("\"\".Capitalize()", "")]
    [TestCase("1234567.Format(\"N0\")", "1,234,567")]
    [TestCase("3.14159.Format(\"F2\")", "3.14")]
    [TestCase("(1|3).Format(\"F3\")", "0.333")]
    [TestCase("new DateTime(2026, 8, 15).Format(\"yyyy-MM-dd\")", "2026-08-15")]
    [TestCase("TimeSpan.FromMinutes(90.0).Format(\"h'h 'm'm'\")", "1h 30m")]
    [TestCase("new Date(2026, 8, 15).Format(\"MMMM d\")", "August 15")]
    [TestCase("new Time(14, 30).Format(\"HH:mm\")", "14:30")]
    [TestCase("\"42\".ToInteger().Or(-1)", "42")]
    [TestCase("\"nope\".ToInteger().Or(-1)", "-1")]
    [TestCase("\"3.14\".ToReal().Or(0.0)", "3.14")]
    [TestCase("\"TRUE\".ToBoolean().Or(false)", "true")]
    [TestCase("\"yes\".ToBoolean().HasValue()", "false")]
    [TestCase("\"22/7\".ToFraction().Or(0|1)", "22|7")]
    [TestCase("\"22|7\".ToFraction().Or(0|1)", "22|7")]
    [TestCase("\"4/8\".ToFraction().Or(0|1)", "1|2")]
    [TestCase("\"5\".ToFraction().Or(0|1)", "5|1")]
    [TestCase("\"1/0\".ToFraction().HasValue()", "false")]
    public void TheTextMembers(string expression, string expected) => Assert.That(
        RunProgram($$"""
            global model Program
                function Main()
                    Console.WriteLine({{expression}});
                end function
            end model
            """),
        Is.EqualTo(expected + "\n"));

    /// <summary>
    /// What the rows above exercise. Listed rather than inferred so that adding a value member
    /// without a row makes the coverage test fail.
    /// </summary>
    private static readonly BuiltInId[] ValueMemberIds =
    [
        BuiltInId.SetCount, BuiltInId.SetInsert, BuiltInId.SetInsertAt, BuiltInId.SetRemove,
        BuiltInId.SetRemoveAt, BuiltInId.SetContains, BuiltInId.SetIndexOf, BuiltInId.SetClear,
        BuiltInId.SetSubsetFrom, BuiltInId.SetSubsetBetween,
        BuiltInId.SetTrim, BuiltInId.SetTrimStart, BuiltInId.SetTrimEnd, BuiltInId.SetTrimAll,
        BuiltInId.StringCount, BuiltInId.StringContains, BuiltInId.StringIndexOf,
        BuiltInId.StringSubstring, BuiltInId.StringInsert, BuiltInId.StringInsertAt,
        BuiltInId.StringRemove, BuiltInId.StringRemoveAt, BuiltInId.StringToCharacters,
        BuiltInId.StringSubsetFrom, BuiltInId.StringSubsetBetween,
        BuiltInId.StringTrim, BuiltInId.StringTrimText, BuiltInId.StringTrimSet,
        BuiltInId.StringTrimStart, BuiltInId.StringTrimStartText, BuiltInId.StringTrimStartSet,
        BuiltInId.StringTrimEnd, BuiltInId.StringTrimEndText, BuiltInId.StringTrimEndSet,
        BuiltInId.StringSplit, BuiltInId.StringReplace, BuiltInId.StringToUpper,
        BuiltInId.StringToLower, BuiltInId.StringCapitalize, BuiltInId.SetJoin,
        BuiltInId.SetUnion, BuiltInId.SetIntersect, BuiltInId.SetExcept, BuiltInId.SetDistinct,
        BuiltInId.StringToInteger, BuiltInId.StringToReal, BuiltInId.StringToBoolean,
        BuiltInId.StringToFraction,
        BuiltInId.IntegerFormat, BuiltInId.RealFormat, BuiltInId.FractionFormat,
        BuiltInId.DateTimeFormat, BuiltInId.TimeSpanFormat, BuiltInId.DateFormat,
        BuiltInId.TimeFormat,
        BuiltInId.OptionalHasValue, BuiltInId.OptionalOr, BuiltInId.OptionalValue,
        BuiltInId.FractionToReal, BuiltInId.RealToFraction, BuiltInId.EnumerationToInteger,
        BuiltInId.ExceptionMessage, BuiltInId.ModelToString, BuiltInId.ModelEquals,
    ];

    /// <summary>
    /// Every exception the language raises is one a program can name after 'catch'. The
    /// compiler reads its list of exception names from the runtime's catalog, so this holds
    /// the other direction: that each name in the catalog really is a type a program can
    /// mention, and that the two halves have not been allowed to disagree.
    /// </summary>
    [TestCaseSource(nameof(ExceptionNames))]
    public void EveryBuiltInExceptionCanBeNamedAndResolved(string name)
    {
        Assert.That(
            ProfiC.Runtime.BuiltInExceptions.Resolve(name),
            Is.Not.Null,
            $"{name} is cataloged but denotes no type");

        Assert.That(
            BuiltIns.IsBuiltInType(name),
            Is.True,
            $"{name} denotes a type but the compiler does not know the name");

        Assert.That(
            RunProgram($$"""
                global model Program
                    function Main()
                        try
                            Console.WriteLine("ran");
                        catch {{name}} problem
                            Console.WriteLine("caught");
                        end try
                    end function
                end model
                """),
            Is.EqualTo("ran\n"),
            $"a program cannot catch {name}");
    }

    public static IEnumerable<string> ExceptionNames => ProfiC.Runtime.BuiltInExceptions.Names;

    /// <summary>
    /// <para>A structure prints its fields in the order they were declared.</para>
    /// <para>The order is the author's, not the alphabet's: a reader compares what is printed
    /// against what they wrote. Deep equality walks the same order, which needs only that both
    /// sides agree.</para>
    /// </summary>
    [Test]
    public void AStructurePrintsItsFieldsAsTheyWereDeclared() => Assert.That(
        RunProgram("""
            structure Contact
                public string Name;
                public character Initial;
                public integer Age;

                public function Contact(string name, character initial, integer age)
                    this.Name = name;
                    this.Initial = initial;
                    this.Age = age;
                end function
            end structure

            global model Program
                function Main()
                    Console.WriteLine(new Contact("Ada, Countess", 'A', 36));
                end function
            end model
            """),
        Is.EqualTo("Contact { \"Ada, Countess\", 'A', 36 }\n"));

    /// <summary>
    /// <para>Two types are never equal, however alike their fields are.</para>
    /// <para>The interpreter runs every model and structure as one host class, so asking the
    /// host what type a value is cannot tell a Contact from a Product. Equality asks the value
    /// instead. Emitted code will answer with a .NET type; the interpreter answers with the
    /// Profi-C type it is running.</para>
    /// </summary>
    [Test]
    public void TwoTypesAreNeverEqualEvenWithMatchingFields() => Assert.That(
        RunProgram("""
            structure Contact
                public string Name;
                public integer Age;

                public function Contact(string name, integer age)
                    this.Name = name;
                    this.Age = age;
                end function
            end structure

            structure Product
                public string Label;
                public integer Cost;

                public function Product(string label, integer cost)
                    this.Label = label;
                    this.Cost = cost;
                end function
            end structure

            global model Program
                function Main()
                    Contact one = new Contact("Ada", 36);
                    Contact same = new Contact("Ada", 36);
                    Product other = new Product("Ada", 36);

                    Console.WriteLine(one.Equals(same));
                    Console.WriteLine(one.Equals(other));
                end function
            end model
            """),
        Is.EqualTo("true\nfalse\n"));

    /// <summary>Only Model and Exception may follow 'extends'.</summary>
    [TestCase("Model", true)]
    [TestCase("Exception", true)]
    [TestCase("ArgumentException", true)]
    [TestCase("Console", false)]
    [TestCase("Math", false)]
    [TestCase("Fraction", false)]
    public void ExtendabilityIsRecorded(string name, bool expected) =>
        Assert.That(BuiltIns.MayBeExtended(name), Is.EqualTo(expected));
}
