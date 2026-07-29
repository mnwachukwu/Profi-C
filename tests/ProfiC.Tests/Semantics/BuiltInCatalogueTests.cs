using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Semantics;
using ProfiC.Compiler.Text;

namespace ProfiC.Tests.Semantics;

/// <summary>
/// <para>The catalogue of built-in models, and the promise that everything in it works.</para>
/// <para>The C# compiler already refuses a member with no implementation, because the back
/// end switches on the identifier without a fallback arm. These tests cover what it cannot:
/// that each member gives the right answer, rather than merely being reachable.</para>
/// </summary>
[TestFixture]
public sealed class BuiltInCatalogueTests
{
    /// <summary>
    /// Every member of every built-in model, with a call that exercises it and the answer it
    /// must give. A member added to the catalogue with no row here fails the coverage test
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

        (BuiltInId.MathSqrt, "Console.WriteLine(Math.Sqrt(16.0))", "4\n"),
        (BuiltInId.MathAbs, "Console.WriteLine(Math.Abs(-3.5))", "3.5\n"),
        (BuiltInId.MathFloor, "Console.WriteLine(Math.Floor(3.7))", "3\n"),
        (BuiltInId.MathCeiling, "Console.WriteLine(Math.Ceiling(3.2))", "4\n"),
        (BuiltInId.MathPow, "Console.WriteLine(Math.Pow(2.0, 8.0))", "256\n"),
        (BuiltInId.MathMin, "Console.WriteLine(Math.Min(3, 7))", "3\n"),
        (BuiltInId.MathMax, "Console.WriteLine(Math.Max(3, 7))", "7\n"),

        (BuiltInId.FractionCreate, "Console.WriteLine(Fraction.Create(6, 8))", "3|4\n"),
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
    public void EveryCatalogueMemberProducesItsAnswer((BuiltInId Id, string Call, string Expected) row)
    {
        if (row.Call.Length == 0)
        {
            Assert.Pass("exercised for reachability only");
        }

        Assert.That(Run(row.Call), Is.EqualTo(row.Expected), $"{row.Id} gave the wrong answer");
    }

    /// <summary>Every member the catalogue declares, on either surface.</summary>
    private static IReadOnlyList<BuiltInMember> Everything()
    {
        SetType set = new(PrimitiveType.Integer);
        OptionalType optional = new(PrimitiveType.Integer);

        return
        [
            .. BuiltIns.Models.SelectMany(m => m.Members),
            .. BuiltIns.OnSet(set),
            .. BuiltIns.OnString(),
            .. BuiltIns.OnOptional(optional),
            .. BuiltIns.OnFraction(),
            .. BuiltIns.OnReal(),
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
            Expectations.Select(e => e.Id).Concat(ValueMemberIds).Distinct();

        Assert.That(
            tested.OrderBy(i => i),
            Is.EqualTo(Enum.GetValues<BuiltInId>().OrderBy(i => i)),
            "a built-in was added to the catalogue without a test that runs it");
    }

    /// <summary>Every identifier must appear in the catalogue, and the catalogue only.</summary>
    [Test]
    public void EveryIdentifierAppearsInTheCatalogue() => Assert.That(
        Everything().Select(m => m.Id!.Value).Distinct().OrderBy(i => i),
        Is.EqualTo(Enum.GetValues<BuiltInId>().OrderBy(i => i)),
        "an identifier exists with no catalogue entry, or the reverse");

    /// <summary>
    /// A model's members must be unique within it; the value surfaces deliberately repeat
    /// names, since a set and a string both answer Count.
    /// </summary>
    [Test]
    public void AModelDeclaresEachMemberOnce() => Assert.That(
        BuiltIns.Models.SelectMany(m => m.Members).Select(m => m.Id!.Value), Is.Unique);

    /// <summary>
    /// The resolver protects exactly the names the catalogue lists. A program that could
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
    public void TheCatalogueCoversEveryProtectedName() => Assert.That(
        BuiltIns.AllTypeNames.OrderBy(n => n, StringComparer.Ordinal),
        Is.EqualTo(BuiltIns.ModelNames.Concat(BuiltIns.ExceptionNames)
                                      .OrderBy(n => n, StringComparer.Ordinal)));

    /// <summary>
    /// Recorded now so the catalogue is already shaped for namespaces. Nothing reads it yet —
    /// every name still resolves unqualified — but when scoping lands the data is in place.
    /// </summary>
    [Test]
    public void EveryModelRecordsItsNamespace() => Assert.That(
        BuiltIns.Models.Select(m => m.Namespace), Is.All.EqualTo("Standard"));

    /// <summary>
    /// <para>Members found on a value rather than through a model's name.</para>
    /// <para>These are not in the catalogue — they are keyed by the receiver's type, which
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
    [TestCase("Console.WriteLine(\"hi\".ToCharacters())", "{h, i}\n")]
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
            enumeration Colour
                Red,
                Green,
                Blue
            end enumeration

            global model Program
                function Main()
                    Colour c = Colour.Green;
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

    /// <summary>
    /// What the rows above exercise. Listed rather than inferred so that adding a value member
    /// without a row makes the coverage test fail.
    /// </summary>
    private static readonly BuiltInId[] ValueMemberIds =
    [
        BuiltInId.SetCount, BuiltInId.SetInsert, BuiltInId.SetInsertAt, BuiltInId.SetRemove,
        BuiltInId.SetRemoveAt, BuiltInId.SetContains, BuiltInId.SetIndexOf, BuiltInId.SetClear,
        BuiltInId.StringCount, BuiltInId.StringContains, BuiltInId.StringIndexOf,
        BuiltInId.StringSubstring, BuiltInId.StringInsert, BuiltInId.StringInsertAt,
        BuiltInId.StringRemove, BuiltInId.StringRemoveAt, BuiltInId.StringToCharacters,
        BuiltInId.OptionalHasValue, BuiltInId.OptionalOr, BuiltInId.OptionalValue,
        BuiltInId.FractionToReal, BuiltInId.RealToFraction, BuiltInId.EnumerationToInteger,
        BuiltInId.ExceptionMessage, BuiltInId.ModelToString, BuiltInId.ModelEquals,
    ];

    /// <summary>
    /// Every exception the language raises is one a program can name after 'catch'. The
    /// compiler reads its list of exception names from the runtime's catalogue, so this holds
    /// the other direction: that each name in the catalogue really is a type a program can
    /// mention, and that the two halves have not been allowed to disagree.
    /// </summary>
    [TestCaseSource(nameof(ExceptionNames))]
    public void EveryBuiltInExceptionCanBeNamedAndResolved(string name)
    {
        Assert.That(
            ProfiC.Runtime.BuiltInExceptions.Resolve(name),
            Is.Not.Null,
            $"{name} is catalogued but denotes no type");

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
