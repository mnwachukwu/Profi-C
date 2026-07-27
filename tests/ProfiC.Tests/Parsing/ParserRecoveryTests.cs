using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;

namespace ProfiC.Tests.Parsing;

/// <summary>
/// <para>Error reporting and recovery.</para>
/// <para>Every case asserts the same three things the scanner's recovery tests do: the right
/// diagnostic identifier, that nothing was thrown, and that a usable tree still came back.
/// Several also assert that one mistake produces <em>one</em> diagnostic, which is the
/// property that separates a tolerable compiler from an infuriating one.</para>
/// </summary>
[TestFixture]
public sealed class ParserRecoveryTests : ParserTestBase
{
    private const string Shell = """
        global model Program
            function Main()
        {0}
            end function
        end model
        """;

    private static (CompilationUnit Unit, DiagnosticBag Diagnostics) ParseBody(string body) =>
        ParseRaw(Shell.Replace("{0}", body, StringComparison.Ordinal));

    [Test]
    public void ParsingNeverThrows()
    {
        string[] hostile =
        [
            "", "model", "model X", "end", "end model", "function", "function (",
            "global model P function M() end function", "if", "while", "for", "switch",
            "try", "let", "let x", "let x =", "yield", "{", "}", "(", ")", ";;;",
            "model X model Y model Z", "end end end end",
        ];

        foreach (string source in hostile)
        {
            Assert.DoesNotThrow(() => ParseRaw(source), $"parsing \"{source}\" threw");
        }
    }

    [Test]
    public void MalformedInputStillYieldsATree()
    {
        (CompilationUnit unit, DiagnosticBag diagnostics) = ParseRaw("model model model");

        Assert.Multiple(() =>
        {
            Assert.That(unit, Is.Not.Null);
            Assert.That(diagnostics.Count, Is.GreaterThan(0));
        });
    }

    // ---- The main prize: qualified end verification --------------------------------------

    [Test]
    public void MismatchedEndNamesBothTheCloserAndTheConstruct()
    {
        (_, DiagnosticBag diagnostics) = ParseBody(
            """
                    if x > 1
                        yield;
                    end while
            """);

        Assert.Multiple(() =>
        {
            Assert.That(IdsOf(diagnostics), Is.EqualTo(new[] { "PFC0104" }));

            string message = diagnostics.Single().Message;
            Assert.That(message, Does.Contain("end if"), "should name what was expected");
            Assert.That(message, Does.Contain("end while"), "should name what was written");
            Assert.That(message, Does.Contain("line 3"), "should point at the opener");
        });
    }

    [Test]
    public void AMismatchedEndStillClosesItsConstruct()
    {
        // Treating the qualifier as the typo, rather than the structure, is what keeps one
        // mistake from unterminating every enclosing construct.
        (_, DiagnosticBag diagnostics) = ParseBody(
            """
                    while x
                        yield;
                    end if
            """);

        Assert.That(IdsOf(diagnostics), Is.EqualTo(new[] { "PFC0104" }),
                    "one wrong qualifier should produce exactly one diagnostic");
    }

    [Test]
    public void UnterminatedConstructIsReportedAtItsOpener()
    {
        (_, DiagnosticBag diagnostics) = ParseRaw(
            """
            global model Program
                function Main()
                    if x
                        yield;
                end function
            end model
            """);

        Assert.That(IdsOf(diagnostics), Does.Contain("PFC0105"));
    }

    // ---- The statement boundary rule ------------------------------------------------------

    [TestCase("        (x as Dog).Value();", "(")]
    [TestCase("        -x.Compute();", "-")]
    public void StatementCannotBeginWithParenthesisOrMinus(string body, string offender)
    {
        (_, DiagnosticBag diagnostics) = ParseBody(body);

        Assert.Multiple(() =>
        {
            Assert.That(IdsOf(diagnostics), Is.EqualTo(new[] { "PFC0106" }));
            Assert.That(diagnostics.Single().Message, Does.Contain($"'{offender}'"));
            Assert.That(diagnostics.Single().Message, Does.Contain("let"),
                        "the message should name the rewrite");
        });
    }

    [Test]
    public void TheSameExpressionIsFineWhereItIsNotStartingAStatement()
    {
        // The rule is narrow on purpose: only a bare expression statement is affected.
        Assert.DoesNotThrow(() => ParseUnit(Shell.Replace("{0}",
            """
                    let d = (x as Dog).Value();
                    this.pet = (x as Dog).Value();
                    Register((x as Dog).Value());
                    yield (a + b).Describe();
            """, StringComparison.Ordinal)));
    }

    [Test]
    public void AConditionIsNotAStatementSoParenthesesAreFineThere()
    {
        Assert.DoesNotThrow(() => ParseUnit(Shell.Replace("{0}",
            """
                    if (x as Dog).Value().IsReady()
                        yield;
                    end if
            """, StringComparison.Ordinal)));
    }

    // ---- The decrement diagnostic, deferred here from the scanner ------------------------

    [Test]
    public void PostfixDecrementIsReportedWithItsRewrite()
    {
        (_, DiagnosticBag diagnostics) = ParseBody("        i--;");

        Assert.Multiple(() =>
        {
            Assert.That(IdsOf(diagnostics), Is.EqualTo(new[] { "PFC0006" }));
            Assert.That(diagnostics.Single().Message, Does.Contain("no decrement operator"));
            Assert.That(diagnostics.Single().Message, Does.Contain("x = x - 1"));
        });
    }

    [Test]
    public void SubtractingANegativeIsStillFine()
    {
        // The whole reason the scanner does not make this call.
        Assert.DoesNotThrow(() => ParseUnit(Shell.Replace("{0}",
            """
                    let a = x--1;
                    let b = x - -1;
                    let c = --x;
            """, StringComparison.Ordinal)));
    }

    [Test]
    public void SpacedMinusSignsGetThePlainMessageInstead()
    {
        // "x - - ;" is equally wrong but is not an attempted decrement, so claiming it was
        // would be a worse message than the truthful one.
        (_, DiagnosticBag diagnostics) = ParseBody("        let a = x - - ;");

        Assert.That(IdsOf(diagnostics), Does.Contain("PFC0101"));
        Assert.That(IdsOf(diagnostics), Does.Not.Contain("PFC0006"));
    }

    // ---- Missing nodes --------------------------------------------------------------------

    [Test]
    public void AMissingExpressionLeavesAMissingNodeWithAnEmptySpan()
    {
        (CompilationUnit unit, DiagnosticBag diagnostics) = ParseBody("        let x = ;");

        MissingExpr missing = unit.Descendants().OfType<MissingExpr>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(IdsOf(diagnostics), Is.EqualTo(new[] { "PFC0101" }));
            Assert.That(missing.Span.Length, Is.Zero, "a missing node occupies no source");
            Assert.That(unit.ContainsMissing(), Is.True);
        });
    }

    [Test]
    public void ACleanTreeContainsNoMissingNodes()
    {
        Assert.That(ParseUnit(Shell.Replace("{0}", "        let x = 1;", StringComparison.Ordinal))
                        .ContainsMissing(),
                    Is.False);
    }

    // ---- Assignability --------------------------------------------------------------------

    [TestCase("        x = 1;")]
    [TestCase("        a[0] = 1;")]
    [TestCase("        this.field = 1;")]
    public void ValidAssignmentTargetsAreAccepted(string body)
    {
        (_, DiagnosticBag diagnostics) = ParseBody(body);
        Assert.That(IdsOf(diagnostics), Is.Empty);
    }

    [Test]
    public void AssigningToSomethingThatIsNotATargetIsReported()
    {
        (_, DiagnosticBag diagnostics) = ParseBody("        f() = 1;");
        Assert.That(IdsOf(diagnostics), Is.EqualTo(new[] { "PFC0109" }));
    }

    // ---- Progress -------------------------------------------------------------------------

    [Test]
    public void ParsingTerminatesOnPathologicalInput()
    {
        // The guard against a recovery step that consumes nothing. Without it this hangs
        // rather than fails, which is the worst way for a parser to be wrong.
        string source = string.Join(' ', Enumerable.Repeat("end", 500));

        Task<bool> parse = Task.Run(() =>
        {
            ParseRaw(source);
            return true;
        });

        Assert.That(parse.Wait(TimeSpan.FromSeconds(10)), Is.True, "parsing did not terminate");
    }
}
