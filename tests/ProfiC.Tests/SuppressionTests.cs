using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Semantics;
using ProfiC.Compiler.Text;

namespace ProfiC.Tests;

/// <summary>
/// <para>What <c>ignore</c> silences, what it refuses to silence, and what it is not.</para>
/// <para>The corpus covers the shapes a reader meets. These cover the edges: that an error
/// survives every form of it, that a comment beginning with the word stays a comment, and that
/// a directive reaches exactly as far as it says and no further.</para>
/// </summary>
[TestFixture]
public sealed class SuppressionTests : LexerTestBase
{
    /// <summary>Compiles a whole file and returns what anything reading the bag would see.</summary>
    private static string[] Report(string text)
    {
        DiagnosticBag diagnostics = new();

        CompilationUnit unit = Parser.Parse(new SourceText(text, "<test>"), diagnostics);
        SemanticModel model = Resolver.Resolve(unit, diagnostics, requireEntryPoint: true);
        TypeChecker.Check(unit, model, diagnostics);
        DefiniteAssignment.Analyze(unit, model, diagnostics);
        diagnostics.ReportUnusedSuppressions();

        return [.. diagnostics.Sorted().Select(d => d.Id)];
    }

    /// <summary>Wraps statements in the smallest program that runs them.</summary>
    private static string Program(string body) => $$"""
        global model Program
            function Main()
        {{body}}
            end function
        end model
        """;

    // ---- What it silences ---------------------------------------------------------------

    [Test]
    public void ASeverityCoversTheLineBelowIt() => Assert.That(
        Report(Program("""
                # ignore opinion
                Console.WriteLine("");
        """)),
        Is.Empty);

    [Test]
    public void AnIdentifierCoversTheLineBelowIt() => Assert.That(
        Report(Program("""
                # ignore PC0340
                Console.WriteLine("");
        """)),
        Is.Empty);

    /// <summary>
    /// Blank lines and further comments are passed over, so a directive may be written above
    /// the thing it is about with an explanation between them.
    /// </summary>
    [Test]
    public void ItReachesPastBlankLinesAndComments() => Assert.That(
        Report(Program("""
                # ignore opinion

                # because the blank line above the total is the point
                Console.WriteLine("");
        """)),
        Is.Empty);

    /// <summary>The line below, and only that one. A second is not covered by the first's.</summary>
    [Test]
    public void ItReachesOneLineAndNoFurther() => Assert.That(
        Report(Program("""
                # ignore opinion
                Console.WriteLine("");
                Console.WriteLine("");
        """)),
        Is.EqualTo(new[] { "PC0340" }));

    [Test]
    public void InFileReachesEveryLine() => Assert.That(
        Report("""
            # ignore opinion in file

            global model Program
                function Main()
                    Console.WriteLine("");
                    Console.WriteLine("");
                end function
            end model
            """),
        Is.Empty);

    /// <summary>Written anywhere in the file, since it is about the file and not the place.</summary>
    [Test]
    public void InFileNeedNotComeFirst() => Assert.That(
        Report(Program("""
                Console.WriteLine("");
                # ignore opinion in file
                Console.WriteLine("");
        """)),
        Is.Empty);

    /// <summary>
    /// A severity silences its own and leaves the other alone, which is the whole reason the
    /// two are separable: a writer tired of being told how to write turns off opinions and
    /// keeps warnings. Here `PC0340` is an opinion and survives a directive about warnings.
    /// </summary>
    [Test]
    public void ASeverityDoesNotReachAnother() => Assert.That(
        Report(Program("""
                # ignore warning in file
                Console.WriteLine("");
        """)),
        Is.EqualTo(new[] { "PC0340" }));

    // ---- What it refuses to silence -------------------------------------------------------

    /// <summary>
    /// The rule the whole feature rests on. An error survives an identifier naming it, a
    /// severity, and a whole-file directive alike — anything else would let a program be
    /// quietly wrong.
    /// </summary>
    [TestCase("# ignore PC0300")]
    [TestCase("# ignore warning")]
    [TestCase("# ignore opinion")]
    [TestCase("# ignore warning in file")]
    [TestCase("# ignore opinion in file")]
    public void NothingSilencesAnError(string directive) => Assert.That(
        Report(Program($"""
                {directive}
                integer wrong = "not a number";
                Console.WriteLine(wrong);
        """)),
        Does.Contain("PC0300"),
        "an error survives every form of ignore");

    // ---- What is not a directive ----------------------------------------------------------

    /// <summary>
    /// A comment is prose first. Turning a sentence that opens with the word into a diagnostic
    /// would be a worse failure than passing over a near miss.
    /// </summary>
    [TestCase("# ignore the sign for now")]
    [TestCase("# ignore opinions")]
    [TestCase("# ignore this")]
    [TestCase("# ignore")]
    public void ProseBeginningWithTheWordStaysProse(string comment) => Assert.That(
        Report(Program($"""
                {comment}
                Console.WriteLine("");
        """)),
        Is.EqualTo(new[] { "PC0340" }),
        "nothing was silenced and nothing was reported about the comment");

    /// <summary>A block is prose by construction, so a directive inside one is not one.</summary>
    [Test]
    public void ABlockCommentCarriesNoDirective() => Assert.That(
        Report(Program("""
                ## ignore opinion ##
                Console.WriteLine("");
        """)),
        Is.EqualTo(new[] { "PC0340" }));

    // ---- When it cannot work ---------------------------------------------------------------

    [Test]
    public void AnIdentifierNothingCarriesIsReported() => Assert.That(
        Report(Program("""
                # ignore PC9999
                Console.WriteLine("ok");
        """)),
        Is.EqualTo(new[] { "PC0022" }));

    [Test]
    public void AnIdentifierThatStopsCompilationIsReported() => Assert.That(
        Report(Program("""
                # ignore PC0300
                Console.WriteLine("ok");
        """)),
        Is.EqualTo(new[] { "PC0023" }));

    [Test]
    public void AnIdentifierThatSilencedNothingIsReported() => Assert.That(
        Report(Program("""
                # ignore PC0403
                Console.WriteLine("ok");
        """)),
        Is.EqualTo(new[] { "PC0024" }));

    /// <summary>
    /// Naming a severity claims nothing is there, so it draws nothing where nothing is. This
    /// is what makes the directive free to write defensively.
    /// </summary>
    [Test]
    public void ASeverityThatSilencedNothingIsNotReported() => Assert.That(
        Report(Program("""
                # ignore opinion
                Console.WriteLine("ok");
        """)),
        Is.Empty);

    /// <summary>
    /// Two that overlap are both working. Charging one of them with silencing nothing would be
    /// reporting on a line that is doing its job.
    /// </summary>
    [Test]
    public void TwoThatOverlapAreBothUsed() => Assert.That(
        Report(Program("""
                # ignore PC0340 in file
                # ignore opinion in file
                Console.WriteLine("");
        """)),
        Is.Empty);

    // ---- What it does to the compilation ----------------------------------------------------

    /// <summary>
    /// A directive covering an error's line does not make the compilation succeed, whatever
    /// else it silences on the way past.
    /// </summary>
    [Test]
    public void ASilencedCompilationStillFails()
    {
        DiagnosticBag diagnostics = new();

        CompilationUnit unit = Parser.Parse(
            new SourceText(
                Program("""
                        # ignore opinion in file
                        # ignore warning in file
                        integer wrong = "not a number";
                        Console.WriteLine(wrong);
                """),
                "<test>"),
            diagnostics);

        SemanticModel model = Resolver.Resolve(unit, diagnostics, requireEntryPoint: true);
        TypeChecker.Check(unit, model, diagnostics);
        DefiniteAssignment.Analyze(unit, model, diagnostics);
        diagnostics.ReportUnusedSuppressions();

        Assert.That(diagnostics.HasErrors, Is.True);
    }

    /// <summary>
    /// <para>Reporting the dead ones is a pass, and a compilation that stopped early has not
    /// run it.</para>
    /// <para>Scanning alone leaves every directive naming something a later pass reports
    /// looking dead. An editor scanning on each keystroke would otherwise report on a line
    /// that works, over and over, which is the failure this ordering exists to prevent.</para>
    /// </summary>
    [Test]
    public void ScanningAloneReportsNoDeadDirective()
    {
        (_, DiagnosticBag diagnostics) = ScanRaw(Program("""
                # ignore PC0340
                Console.WriteLine("");
        """));

        Assert.That(diagnostics.Select(d => d.Id), Is.Empty);
    }
}
