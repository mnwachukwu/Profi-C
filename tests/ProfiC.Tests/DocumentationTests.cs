using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Documentation;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Semantics;
using ProfiC.Compiler.Text;

namespace ProfiC.Tests;

/// <summary>
/// <para>What a documentation comment is, what it carries, and what it is held to.</para>
/// <para>Nothing else reads documentation yet — the language server that will is several
/// phases out — so without these the whole of it could rot unnoticed between now and then.
/// </para>
/// </summary>
[TestFixture]
public sealed class DocumentationTests : LexerTestBase
{
    private static CompilationUnit Compile(string text, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();

        CompilationUnit unit = Parser.Parse(new SourceText(text, "<test>"), diagnostics);
        SemanticModel model = Resolver.Resolve(unit, diagnostics, requireEntryPoint: true);
        TypeChecker.Check(unit, model, diagnostics);
        DefiniteAssignment.Analyze(unit, model, diagnostics);
        DocumentationChecker.Check(unit, diagnostics);

        return unit;
    }

    private static string[] Report(string text)
    {
        Compile(text, out DiagnosticBag diagnostics);
        return [.. diagnostics.Sorted().Select(d => d.Id)];
    }

    /// <summary>Wraps a member in the smallest program that holds one.</summary>
    private static string Program(string member) => $$"""
        global model Program
        {{member}}
            function Main()
            end function
        end model
        """;

    // ---- What is documentation, and what is a remark ---------------------------------------

    [Test]
    public void ACommentOpeningWithSummaryDocuments()
    {
        CompilationUnit unit = Compile(
            Program("""
                ##
                    @summary: Counts up to a limit.
                ##
                global integer function Total(integer n)
                    yield n;
                end function
            """),
            out _);

        Assert.That(unit.Documentation, Has.Count.EqualTo(1));
        Assert.That(unit.Documentation[0].Summary, Is.EqualTo("Counts up to a limit."));
    }

    /// <summary>
    /// Where a comment sits never makes it documentation. A block above a declaration is a
    /// remark like any other, which is what keeps prose from being published by accident.
    /// </summary>
    [Test]
    public void ARemarkAboveADeclarationIsStillARemark()
    {
        CompilationUnit unit = Compile(
            Program("""
                ##
                    Counts up to a limit. Written as prose, so it documents nothing.
                ##
                global integer function Total(integer n)
                    yield n;
                end function
            """),
            out _);

        Assert.That(unit.Documentation, Is.Empty);
    }

    /// <summary>One line is enough where one line is enough.</summary>
    [Test]
    public void ALineCommentDocumentsToo()
    {
        CompilationUnit unit = Compile(
            Program("""
                # @summary: How many terms to add.
                global constant integer Terms = 8;
            """),
            out _);

        Assert.That(unit.Documentation, Has.Count.EqualTo(1));
        Assert.That(
            unit.Documentation[0].Summary, Is.EqualTo("How many terms to add."));
    }

    // ---- What it carries ---------------------------------------------------------------

    /// <summary>
    /// A blank line inside a label is a paragraph break, not an ending. Without that a summary
    /// could only ever be one paragraph, or would need saying twice.
    /// </summary>
    [Test]
    public void ABlankLineKeepsALabelRunning()
    {
        CompilationUnit unit = Compile(
            Program("""
                ##
                    @summary: The first paragraph.

                    And the second, which is still the summary.
                ##
                global integer function Total(integer n)
                    yield n;
                end function
            """),
            out _);

        Assert.That(
            unit.Documentation[0].Summary,
            Is.EqualTo("The first paragraph.\n\nAnd the second, which is still the summary."));
    }

    /// <summary>A wrapped line joins the one above it with a space, as prose wraps.</summary>
    [Test]
    public void AWrappedLineJoinsTheOneAboveIt()
    {
        CompilationUnit unit = Compile(
            Program("""
                ##
                    @summary: One sentence
                    across two lines.
                ##
                global integer function Total(integer n)
                    yield n;
                end function
            """),
            out _);

        Assert.That(
            unit.Documentation[0].Summary, Is.EqualTo("One sentence across two lines."));
    }

    [Test]
    public void EveryLabelIsRead()
    {
        CompilationUnit unit = Compile(
            Program("""
                ##
                    @summary: Adds.
                    @remarks: At greater length.
                    @n: how many.
                    @yields: the total.
                    @throws: nothing.
                ##
                global integer function Total(integer n)
                    yield n;
                end function
            """),
            out _);

        DocComment doc = unit.Documentation[0];

        Assert.Multiple(() =>
        {
            Assert.That(doc.Summary, Is.EqualTo("Adds."));
            Assert.That(doc.Remark, Is.EqualTo("At greater length."));
            Assert.That(doc.Parameters.Select(p => p.Name), Is.EqualTo(new[] { "n" }));
        });
    }

    /// <summary>
    /// <para>The mark is what tells a label from prose, and this is the case that decides it.
    /// </para>
    /// <para>Wrapped text often puts a word and a colon at the start of a line — every such
    /// line in this repository's samples turned out to be exactly that. Read as a label, it
    /// would report a documented parameter nobody wrote.</para>
    /// </summary>
    [Test]
    public void AWrappedLineBeginningWithAWordAndAColonIsProse()
    {
        CompilationUnit unit = Compile(
            Program("""
                ##
                    @summary: That is why it yields an
                    optional: nothing more to read is an answer, not a fault.
                ##
                global integer function Total(integer n)
                    yield n;
                end function
            """),
            out DiagnosticBag diagnostics);

        Assert.Multiple(() =>
        {
            Assert.That(unit.Documentation[0].Parameters, Is.Empty);
            Assert.That(diagnostics.Select(d => d.Id), Is.Empty);
        });
    }

    // ---- What it is held to ---------------------------------------------------------------

    [Test]
    public void ADocumentedParameterMustBeOneTheFunctionTakes() => Assert.That(
        Report(Program("""
            ##
                @summary: Adds.
                @count: how many.
            ##
            global integer function Total(integer n)
                yield n;
            end function
        """)),
        Is.EqualTo(new[] { "PC0245" }));

    [Test]
    public void YieldsNeedsAValueToDescribe() => Assert.That(
        Report(Program("""
            ##
                @summary: Says something.
                @yields: a value that is not there.
            ##
            global function Speak()
                Console.WriteLine("hi");
            end function
        """)),
        Is.EqualTo(new[] { "PC0246" }));

    [Test]
    public void ALabelWrittenTwiceIsReported() => Assert.That(
        Report(Program("""
            ##
                @summary: Adds.
                @n: how many.
                @n: and again.
            ##
            global integer function Total(integer n)
                yield n;
            end function
        """)),
        Is.EqualTo(new[] { "PC0247" }));

    /// <summary>A statement cannot carry documentation, so a comment above one reaches nothing.</summary>
    [Test]
    public void DocumentationAboveAStatementIsReported() => Assert.That(
        Report("""
            global model Program
                function Main()
                    ##
                        @summary: this documents nothing.
                    ##
                    Console.WriteLine("hi");
                end function
            end model
            """),
        Is.EqualTo(new[] { "PC0244" }));

    /// <summary>
    /// Documentation nobody wrote is never reported. Demanding it everywhere is how it stops
    /// being a help and becomes a form to fill in, and the language has no interest in that.
    /// </summary>
    [Test]
    public void NothingIsReportedForDocumentationThatWasNeverWritten() => Assert.That(
        Report(Program("""
            global integer function Total(integer n)
                yield n;
            end function
        """)),
        Is.Empty);

    /// <summary>
    /// A parameter left undocumented while others are named is still not reported. Part of a
    /// thing described is better than none, and the language does not punish stopping there.
    /// </summary>
    [Test]
    public void APartlyDocumentedFunctionIsNotReported() => Assert.That(
        Report(Program("""
            ##
                @summary: Adds two numbers.
                @a: the first.
            ##
            global integer function Add(integer a, integer b)
                yield a + b;
            end function
        """)),
        Is.Empty);

    // ---- What can carry it -----------------------------------------------------------------

    [TestCase("model Thing\nend model", TestName = "a model")]
    [TestCase("structure Point\n    integer x;\nend structure", TestName = "a structure")]
    [TestCase("enumeration Suit\n    Hearts,\n    Spades\nend enumeration", TestName = "an enumeration")]
    public void ATypeCanBeDocumented(string declaration) => Assert.That(
        Report($"""
            ##
                @summary: A thing worth naming.
            ##
            {declaration}

            global model Program
                function Main()
                end function
            end model
            """),
        Is.Empty);

    [TestCase("integer count;", TestName = "a field")]
    [TestCase("global integer Total = 0;", TestName = "a global field")]
    [TestCase("public function Speak()\n    end function", TestName = "a function")]
    public void AMemberCanBeDocumented(string member) => Assert.That(
        Report($"""
            model Thing
                ##
                    @summary: A member worth naming.
                ##
                {member}
            end model

            global model Program
                function Main()
                end function
            end model
            """),
        Is.Empty);

    /// <summary>An enumeration's members carry documentation, since they are what is read.</summary>
    [Test]
    public void AnEnumerationMemberCanBeDocumented() => Assert.That(
        Report("""
            enumeration Suit
                ##
                    @summary: The red one drawn on cards as a heart.
                ##
                Hearts,
                Spades
            end enumeration

            global model Program
                function Main()
                end function
            end model
            """),
        Is.Empty);
}
