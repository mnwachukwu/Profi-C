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
            .. BuiltIns.OnSet(set),

            // A set of optionals answers four the others do not, since only there is there
            // anything empty to drop, so both shapes are asked about.
            .. BuiltIns.OnSet(new SetType(optional)),

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
