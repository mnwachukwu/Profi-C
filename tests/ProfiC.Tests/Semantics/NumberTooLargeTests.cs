using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Semantics;
using ProfiC.Compiler.Text;

namespace ProfiC.Tests.Semantics;

/// <summary>
/// <para>Numbers written down that no number type can hold.</para>
/// <para>The scanner reads a number's shape and cannot see its size, so every one of these
/// scans without complaint and only fails when the digits are turned into a value. Before this
/// was reported, that failure was silent three different ways: the checker said the program was
/// fine, the interpreter printed <c>empty</c> where a number belonged, and the emitter threw a
/// .NET exception out of the compiler.</para>
/// <para>What is asserted is the identifier and where the caret lands. The wording is pinned by
/// <c>samples/negatives/compile/numbers.pc</c> instead, where a reader can see it in a
/// program.</para>
/// </summary>
[TestFixture]
public sealed class NumberTooLargeTests
{
    private static IReadOnlyList<Diagnostic> Check(string body)
    {
        DiagnosticBag diagnostics = new();

        CompilationUnit unit = Parser.Parse(
            new SourceText($$"""
                shared model Program
                    function Main()
                {{body}}
                    end function
                end model
                """, "<test>"),
            diagnostics);

        SemanticModel model = Resolver.Resolve(unit, diagnostics);
        TypeChecker.Check(unit, model, diagnostics);

        return diagnostics.Sorted();
    }

    private static string[] IdsIn(string body) => [.. Check(body).Select(d => d.Id)];

    [TestCase("        integer n = 9223372036854775808;", TestName = "one past the largest")]
    [TestCase("        integer n = 0xFFFFFFFFFFFFFFFFF;", TestName = "seventeen hex digits")]
    [TestCase(
        "        integer n = 0b11111111111111111111111111111111111111111111111111111111111111111;",
        TestName = "sixty-five bits")]
    [TestCase("        real r = 99999999999999999999999999999999999.0;", TestName = "past a real")]
    [TestCase("        fraction f = 99999999999999999999|7;", TestName = "a half past an integer")]
    public void ANumberTooLargeIsReported(string written) =>
        Assert.That(IdsIn(written), Is.EqualTo(new[] { "PC0026" }));

    /// <summary>
    /// <para>A float saturates rather than being refused, and that is the whole of the
    /// exception.</para>
    /// <para>It is the one number type with a value for a number too large. Its own arithmetic
    /// already produces that value — <c>1.0f / 0.0f</c> is <c>Float.Infinity</c>, which
    /// <c>PC0324</c> also leaves alone — so refusing the literal would have the language
    /// disagree with itself about the same number.</para>
    /// </summary>
    [Test]
    public void AFloatSaturatesInsteadOfBeingReported() =>
        Assert.That(IdsIn("        float f = 1e400f;"), Is.Empty);

    /// <summary>
    /// <para>Where the context wants a real, the literal is still an integer literal.</para>
    /// <para>Reading it as whatever the left-hand side asked for would make a literal's meaning
    /// depend on where it sits, and would leave <c>let</c> — which has no type to read it into —
    /// as the one place the same digits are refused.</para>
    /// </summary>
    [Test]
    public void AnIntegerLiteralIsNotWidenedToFitTheTypeWanted() =>
        Assert.That(IdsIn("        real r = 99999999999999999999;"), Is.EqualTo(new[] { "PC0026" }));

    /// <summary>
    /// <para>The most negative integer, written out, is reported against the minus sign.</para>
    /// <para>The minus is a separate operator, so what the compiler reads is one past the
    /// largest. Pointing at the digits alone would tell a reader their most negative integer is
    /// too large — true of the digits, and useless as an explanation — so the caret covers the
    /// sign and the message talks about it.</para>
    /// </summary>
    [Test]
    public void TheMostNegativeIntegerIsReportedAgainstItsMinusSign()
    {
        IReadOnlyList<Diagnostic> reported = Check("        integer n = -9223372036854775808;");

        Assert.Multiple(() =>
        {
            Assert.That(reported.Select(d => d.Id), Is.EqualTo(new[] { "PC0026" }));
            Assert.That(reported[0].Message, Does.Contain("Integer.MinValue"));

            // Column 21 is the minus, and 22 the first digit.
            Assert.That(reported[0].Span.Start.Column, Is.EqualTo(21));
        });
    }

    /// <summary>A fraction over zero is its own fault, and says so rather than saying "too large".</summary>
    [Test]
    public void AFractionOverZeroIsReportedAsDivisionRatherThanSize() =>
        Assert.That(IdsIn("        fraction f = 1|0;"), Is.EqualTo(new[] { "PC0027" }));

    /// <summary>
    /// <para>A float may divide by a zero written down, and nothing else may.</para>
    /// <para>Kept here beside the literal that saturates, because the two are one decision: a
    /// float is the type with values for the answers, so it is the type that gets to ask. Every
    /// other refuses, which is the half worth pinning — an exemption written a little too wide
    /// would take the integer case with it and nothing else would notice.</para>
    /// </summary>
    [TestCase("        Console.WriteLine(1.0f / 0.0f);", TestName = "a float divides")]
    [TestCase("        Console.WriteLine(0.0f / 0.0f);", TestName = "a float over itself")]
    [TestCase("        Console.WriteLine(1.0f % 0.0f);", TestName = "a float remainder")]
    public void AFloatMayDivideByAZeroWrittenDown(string written) =>
        Assert.That(IdsIn(written), Is.Empty);

    [TestCase("        Console.WriteLine(1 / 0);", TestName = "an integer")]
    [TestCase("        Console.WriteLine(1 % 0);", TestName = "an integer remainder")]
    [TestCase("        Console.WriteLine(1.0 / 0.0);", TestName = "a real")]
    [TestCase("        Console.WriteLine(1|2 / 0|1);", TestName = "a fraction")]
    [TestCase("        Console.WriteLine(Fraction.Create(1, 0));", TestName = "a built fraction")]
    public void EverythingElseStillRefusesOne(string written) =>
        Assert.That(IdsIn(written), Is.EqualTo(new[] { "PC0324" }));

    /// <summary>
    /// <para>An enumeration's ordinal is checked, though nothing types it.</para>
    /// <para>The resolver reads these and the type checker never visits them, which is why the
    /// pass walks the tree itself rather than hanging off the checker's walk. Left out, this
    /// ordinal quietly became zero and the member after it one.</para>
    /// </summary>
    [Test]
    public void AnEnumerationOrdinalIsChecked()
    {
        DiagnosticBag diagnostics = new();

        CompilationUnit unit = Parser.Parse(
            new SourceText("""
                enumeration Big
                    Huge = 9223372036854775808,
                    Next
                end enumeration
                """, "<test>"),
            diagnostics);

        SemanticModel model = Resolver.Resolve(unit, diagnostics);
        TypeChecker.Check(unit, model, diagnostics);

        Assert.That(diagnostics.Sorted().Select(d => d.Id), Does.Contain("PC0026"));
    }

    /// <summary>
    /// <para>A constant built from one is told the real reason, and only that.</para>
    /// <para>It does not fold, so the constant check would otherwise add <c>PC0321</c> — "can
    /// only be built from literals and other constants" — of an initializer that is a literal.
    /// Two errors for one mistake is bad enough; one of them describing a mistake that was not
    /// made sends a reader looking for a second problem.</para>
    /// </summary>
    [Test]
    public void AConstantBuiltFromOneIsNotAlsoToldItIsNotAConstant()
    {
        DiagnosticBag diagnostics = new();

        CompilationUnit unit = Parser.Parse(
            new SourceText("""
                shared model Program
                    constant integer Huge = 9223372036854775808;

                    function Main()
                        Console.WriteLine(Program.Huge);
                    end function
                end model
                """, "<test>"),
            diagnostics);

        SemanticModel model = Resolver.Resolve(unit, diagnostics);
        TypeChecker.Check(unit, model, diagnostics);

        Assert.That(diagnostics.Sorted().Select(d => d.Id), Is.EqualTo(new[] { "PC0026" }));
    }

    /// <summary>
    /// Every number a program can hold still reads. The bounds themselves are the interesting
    /// row: each is the largest thing of its type, and each has to survive being written down.
    /// </summary>
    [TestCase("        integer n = 9223372036854775807;", TestName = "the largest integer")]
    [TestCase("        integer n = -9223372036854775807;", TestName = "one above the smallest")]
    [TestCase("        integer n = 0xFFFFFFFFFFFFFFF;", TestName = "fifteen hex digits")]
    [TestCase("        integer n = 0b111111111111111;", TestName = "fifteen bits")]
    [TestCase("        real r = 1.5;", TestName = "an ordinary real")]
    [TestCase("        fraction f = 22|7;", TestName = "an ordinary fraction")]
    [TestCase("        integer n = 1_000_000;", TestName = "separators")]
    public void ANumberThatFitsIsLeftAlone(string written) =>
        Assert.That(IdsIn(written), Is.Empty);
}
