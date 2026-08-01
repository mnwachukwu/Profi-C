using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Semantics;
using ProfiC.Compiler.Text;

namespace ProfiC.Tests.Semantics;

/// <summary>
/// <para>Types nothing can ever be, written where a value's type belongs.</para>
/// <para>A <c>shared model</c> has no instances, which is what the word means, and four of the
/// language's own are names to reach members through rather than things to hold. Each is
/// accepted by every rule taken singly, and together they let a program declare a variable
/// nothing can fill: nothing assigns to it, nothing reads it, and it runs.</para>
/// </summary>
[TestFixture]
public sealed class UninhabitableTypeTests
{
    private static string[] Check(string body) => Check(body, string.Empty);

    private static string[] Check(string body, string alongside)
    {
        DiagnosticBag diagnostics = new();

        CompilationUnit unit = Parser.Parse(
            new SourceText($$"""
                shared model Program
                    function Main()
                {{body}}
                    end function
                end model

                {{alongside}}
                """, "<test>"),
            diagnostics);

        SemanticModel model = Resolver.Resolve(unit, diagnostics);
        TypeChecker.Check(unit, model, diagnostics);

        return [.. diagnostics.Sorted().Select(d => d.Id)];
    }

    [TestCase("        Math m;", TestName = "Math")]
    [TestCase("        Console c;", TestName = "Console")]
    [TestCase("        Reference r;", TestName = "Reference")]
    [TestCase("        Fraction f;", TestName = "Fraction")]
    public void ACompanionCannotBeAVariablesType(string written) =>
        Assert.That(Check(written), Is.EqualTo(new[] { "PC0233" }));

    [Test]
    public void ASharedModelCannotBeAVariablesType() => Assert.That(
        Check("        Helpers h;", "shared model Helpers\nend model"),
        Is.EqualTo(new[] { "PC0233" }));

    /// <summary>
    /// <para>The declaration is wrong in one way, so it is reported once. A type nothing can be
    /// reads as the error type afterwards, which is what keeps the value's own diagnostic from
    /// following it.</para>
    /// <para>Without that, the mistake a capital letter away is told to write <c>fraction</c>
    /// and then told a fraction does not fit a <c>Fraction</c> — the second of which is only
    /// true because of the first.</para>
    /// </summary>
    [TestCase("        Fraction f = 1|2;", TestName = "an initializer")]
    [TestCase("        Fraction f;\n        f = 1|2;", TestName = "an assignment")]
    [TestCase("        Fraction f = 1|2;\n        Console.WriteLine(f);", TestName = "and a use")]
    public void OneMistakeIsReportedOnce(string written) =>
        Assert.That(Check(written), Is.EqualTo(new[] { "PC0233" }));

    /// <summary>
    /// The mistake this mostly catches is a capital letter, so the message names the type that
    /// was almost certainly meant rather than only refusing the one written.
    /// </summary>
    [Test]
    public void TheFractionSlipIsToldWhatToWriteInstead()
    {
        DiagnosticBag diagnostics = new();

        CompilationUnit unit = Parser.Parse(
            new SourceText("""
                shared model Program
                    function Main()
                        Fraction f;
                    end function
                end model
                """, "<test>"),
            diagnostics);

        Resolver.Resolve(unit, diagnostics);

        Assert.That(
            diagnostics.Sorted().Single(d => d.Id == "PC0233").Message,
            Does.Contain("Write 'fraction'"));
    }

    /// <summary>A parameter and a result are a value's type too, wherever they are written.</summary>
    [TestCase("    function Take(Math m)\n    end function", TestName = "a parameter")]
    [TestCase("    Math function Give()\n        yield 1;\n    end function", TestName = "a result")]
    [TestCase("        Math[] many;", TestName = "a set of them")]
    [TestCase("        Math? maybe;", TestName = "an optional one")]
    public void NorAnywhereElseAValueIsDescribed(string written)
    {
        DiagnosticBag diagnostics = new();

        CompilationUnit unit = Parser.Parse(
            new SourceText($$"""
                shared model Program
                    function Main()
                    end function

                {{written}}
                end model
                """, "<test>"),
            diagnostics);

        Resolver.Resolve(unit, diagnostics);

        Assert.That(diagnostics.Sorted().Select(d => d.Id), Does.Contain("PC0233"));
    }

    // ---- What must stay legal ----------------------------------------------------------------

    /// <summary>
    /// The four are still names to reach members through, which is the whole of what they are
    /// for. Refusing them as a type must not refuse them as a receiver.
    /// </summary>
    [Test]
    public void TheyAreStillNamesToReachMembersThrough() => Assert.That(
        Check("""
                    fraction half = Fraction.Create(1, 2);
                    Console.WriteLine(Math.Sqrt(4.0));
                    Console.WriteLine(Standard.Math.Pi);
                    Console.WriteLine(half);
            """),
        Is.Empty);

    /// <summary>
    /// Model and Function hold values despite having no constructors, so the rule cannot be
    /// read off that. Every model converts to one and every function to the other.
    /// </summary>
    [TestCase("        Model held = new Thing();", TestName = "Model")]
    [TestCase("        Function held = (integer n) yield n;", TestName = "Function")]
    public void TheRootsStillHoldValues(string written) => Assert.That(
        Check(written, "model Thing\nend model"),
        Is.Empty);

    [TestCase("        DateTime when = DateTime.Now;", TestName = "DateTime")]
    [TestCase("        Random chance = new Random();", TestName = "Random")]
    public void ATypeWithInstancesIsUntouched(string written) =>
        Assert.That(Check(written), Is.Empty);
}
