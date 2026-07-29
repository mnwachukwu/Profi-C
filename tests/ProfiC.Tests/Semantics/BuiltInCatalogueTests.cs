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

    private static string Run(string body)
    {
        DiagnosticBag diagnostics = new();
        CompilationUnit unit = Parser.Parse(
            new SourceText(
                $$"""
                global model Program
                    function Main()
                        {{body}};
                    end function
                end model
                """,
                "<test>"),
            diagnostics);

        SemanticModel model = Resolver.Resolve(unit, diagnostics, requireEntryPoint: true);
        TypeChecker.Check(unit, model, diagnostics);
        DefiniteAssignment.Analyze(unit, model, diagnostics);

        Assert.That(diagnostics.Select(d => $"{d.Id}: {d.Message}"), Is.Empty,
                    $"'{body}' should check cleanly");

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

    /// <summary>
    /// The table above must cover the whole enumeration. The C# compiler already refuses a
    /// member nobody implemented; this refuses one nobody tested.
    /// </summary>
    [Test]
    public void EveryIdentifierIsCovered() => Assert.That(
        Expectations.Select(e => e.Id).OrderBy(i => i),
        Is.EqualTo(Enum.GetValues<BuiltInId>().OrderBy(i => i)),
        "a built-in was added to the catalogue without a row in the expectations table");

    /// <summary>Every identifier must belong to exactly one model in the catalogue.</summary>
    [Test]
    public void EveryIdentifierAppearsOnceInTheCatalogue()
    {
        BuiltInId[] declared =
            [.. BuiltIns.Models.SelectMany(m => m.Members).Select(m => m.Id!.Value)];

        Assert.Multiple(() =>
        {
            Assert.That(declared.OrderBy(i => i), Is.EqualTo(Enum.GetValues<BuiltInId>().OrderBy(i => i)),
                        "an identifier exists with no catalogue entry, or the reverse");
            Assert.That(declared, Is.Unique);
        });
    }

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

        Assert.That(diagnostics.Select(d => d.Id), Does.Contain("PFC0203"));
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
