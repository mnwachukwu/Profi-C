using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Lexing;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Text;

namespace ProfiC.Tests.Lexing;

/// <summary>
/// <para>Error handling. Every case asserts three things: the right diagnostic identifier,
/// that nothing was thrown, and that tokens still came back.</para>
/// <para>That last one is the point. Reporting an error is easy; continuing afterwards is
/// what an editor needs, since a file being typed into is malformed most of the time.</para>
/// </summary>
[TestFixture]
public sealed class RecoveryTests : LexerTestBase
{
    private static string[] IdsOf(DiagnosticBag bag) => [.. bag.Sorted().Select(d => d.Id)];

    [Test]
    public void UnrecognizedCharacter_IsReportedAndSkipped()
    {
        (List<Token> tokens, DiagnosticBag diagnostics) = ScanRaw("let # x");

        Assert.Multiple(() =>
        {
            Assert.That(IdsOf(diagnostics), Is.EqualTo(new[] { "PFC0001" }));
            Assert.That(tokens.Select(t => t.Type), Is.EqualTo(new[]
            {
                TokenType.Let, TokenType.Identifier, TokenType.EndOfFile,
            }));
        });
    }

    [Test]
    public void SeveralUnrecognizedCharacters_AreAllReportedInOnePass()
    {
        (List<Token> tokens, DiagnosticBag diagnostics) = ScanRaw("# @ $ ~");

        Assert.Multiple(() =>
        {
            Assert.That(IdsOf(diagnostics), Is.EqualTo(new[] { "PFC0001", "PFC0001", "PFC0001", "PFC0001" }));
            Assert.That(tokens, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void UnterminatedString_ReportsAtTheOpeningQuoteAndStopsAtTheNewline()
    {
        (List<Token> tokens, DiagnosticBag diagnostics) = ScanRaw("let s = \"unclosed\nlet");

        Assert.Multiple(() =>
        {
            Assert.That(IdsOf(diagnostics), Is.EqualTo(new[] { "PFC0002" }));

            // Reported at the opening quote, which is where the missing one belongs.
            Assert.That(diagnostics.Single().Span.Start.Line, Is.EqualTo(1));
            Assert.That(diagnostics.Single().Span.Start.Column, Is.EqualTo(9));

            // A partial token is still produced, and the next line still scans, so one
            // missing quote does not swallow the remainder of the file.
            Assert.That(tokens.Select(t => t.Type), Is.EqualTo(new[]
            {
                TokenType.Let, TokenType.Identifier, TokenType.Equal,
                TokenType.StringLiteral, TokenType.Let, TokenType.EndOfFile,
            }));
        });
    }

    [Test]
    public void UnterminatedCharacter_IsReportedAndRecovered()
    {
        (List<Token> tokens, DiagnosticBag diagnostics) = ScanRaw("let c = 'x\nlet");

        Assert.Multiple(() =>
        {
            Assert.That(IdsOf(diagnostics), Is.EqualTo(new[] { "PFC0003" }));
            Assert.That(tokens[^2].Type, Is.EqualTo(TokenType.Let));
        });
    }

    [TestCase("''")]
    [TestCase("'ab'")]
    [TestCase("'abc'")]
    public void CharacterLiteral_MustHoldExactlyOneCharacter(string source)
    {
        (List<Token> tokens, DiagnosticBag diagnostics) = ScanRaw(source);

        Assert.Multiple(() =>
        {
            Assert.That(IdsOf(diagnostics), Is.EqualTo(new[] { "PFC0004" }));
            Assert.That(tokens[0].Type, Is.EqualTo(TokenType.CharLiteral));
        });
    }

    [Test]
    public void UnterminatedBlockComment_IsReportedAtItsOpener()
    {
        (List<Token> tokens, DiagnosticBag diagnostics) = ScanRaw("let\ncomment begin\nnever closed");

        Assert.Multiple(() =>
        {
            Assert.That(IdsOf(diagnostics), Is.EqualTo(new[] { "PFC0005" }));
            Assert.That(diagnostics.Single().Span.Start.Line, Is.EqualTo(2));
            Assert.That(tokens.Select(t => t.Type),
                        Is.EqualTo(new[] { TokenType.Let, TokenType.EndOfFile }));
        });
    }

    [TestCase("&&", "and")]
    [TestCase("||", "or")]
    [TestCase("!", "not")]
    public void CSharpBooleanOperator_SuggestsTheProfiCSpelling(string source, string suggestion)
    {
        (_, DiagnosticBag diagnostics) = ScanRaw($"a {source} b");

        Assert.Multiple(() =>
        {
            Assert.That(IdsOf(diagnostics), Is.EqualTo(new[] { "PFC0006" }));
            Assert.That(diagnostics.Single().Message, Does.Contain($"'{suggestion}'"));
        });
    }

    /// <summary>
    /// Python's spelling for raising to a power. Named rather than scanned as two
    /// multiplications, and pointed at the one Profi-C uses.
    /// </summary>
    [Test]
    public void PythonExponentiationOperator_PointsAtCaret()
    {
        (_, DiagnosticBag diagnostics) = ScanRaw("a ** b");

        Assert.Multiple(() =>
        {
            Assert.That(IdsOf(diagnostics), Is.EqualTo(new[] { "PFC0006" }));
            Assert.That(diagnostics.Single().Message, Does.Contain("'^'"));
        });
    }

    /// <summary>
    /// The caret is an operator here, so it must scan cleanly rather than being reported as
    /// a character with no reading.
    /// </summary>
    [Test]
    public void TheCaretScansWithoutComplaint()
    {
        (List<Token> tokens, DiagnosticBag diagnostics) = ScanRaw("a ^ b");

        Assert.Multiple(() =>
        {
            Assert.That(IdsOf(diagnostics), Is.Empty);
            Assert.That(tokens.Select(t => t.Type), Is.EqualTo(new[]
            {
                TokenType.Identifier, TokenType.Caret, TokenType.Identifier, TokenType.EndOfFile,
            }));
        });
    }

    /// <summary>
    /// A compound assignment has no single token that means it, so <c>=</c> stands in. That
    /// keeps the statement's shape, which is what stops the parser reporting on the wreckage;
    /// the message carries the rewrite.
    /// </summary>
    [TestCase("+=")]
    [TestCase("-=")]
    [TestCase("*=")]
    [TestCase("/=")]
    [TestCase("%=")]
    public void CompoundAssignment_StandsInAsAPlainAssignment(string source)
    {
        (List<Token> tokens, DiagnosticBag diagnostics) = ScanRaw($"x {source} 1");

        Assert.Multiple(() =>
        {
            Assert.That(IdsOf(diagnostics), Is.EqualTo(new[] { "PFC0006" }));
            Assert.That(tokens.Select(t => t.Type), Is.EqualTo(new[]
            {
                TokenType.Identifier, TokenType.Equal, TokenType.IntegerLiteral,
                TokenType.EndOfFile,
            }));

            // The stand-in keeps the text that was actually written, so a token still slices
            // exactly out of the source and a printed stream does not lie about it.
            Assert.That(tokens[1].Lexeme, Is.EqualTo(source));
        });
    }

    /// <summary>
    /// Incrementing is the one with nothing to stand in for it — and it needs none, since
    /// dropping it leaves "x", already a well-formed expression statement.
    /// </summary>
    [Test]
    public void Increment_IsDroppedBecauseNothingStandsInForIt()
    {
        (List<Token> tokens, DiagnosticBag diagnostics) = ScanRaw("x++ 1");

        Assert.Multiple(() =>
        {
            Assert.That(IdsOf(diagnostics), Is.EqualTo(new[] { "PFC0006" }));
            Assert.That(tokens.Select(t => t.Type), Is.EqualTo(new[]
            {
                TokenType.Identifier, TokenType.IntegerLiteral, TokenType.EndOfFile,
            }));
        });
    }

    /// <summary>
    /// <para>The point of standing a token in: one mistake produces one message.</para>
    /// <para>Emitting no token would leave two operands adjacent, and the parser would report
    /// on every shape that follows — several diagnostics for one stray character, reading as
    /// though something is badly broken rather than mistyped.</para>
    /// </summary>
    [TestCase("Console.WriteLine(10 ** 2 - 2 / 2 + 5);")]
    [TestCase("integer x = 1;\n        x += 2;")]
    [TestCase("integer x = 1;\n        x++;")]
    [TestCase("if true && false\n            yield;\n        end if")]
    [TestCase("if true || false\n            yield;\n        end if")]
    [TestCase("if !true\n            yield;\n        end if")]
    public void OneMistakeProducesExactlyOneDiagnostic(string body)
    {
        DiagnosticBag diagnostics = new();
        CompilationUnit unit = Parser.Parse(
            new SourceText(
                $$"""
                global model Program
                    function Main()
                        {{body}}
                    end function
                end model
                """,
                "<test>"),
            diagnostics);

        _ = unit;
        Assert.That(IdsOf(diagnostics), Is.EqualTo(new[] { "PFC0006" }));
    }

    /// <summary>
    /// <para>Subtracting a negative number must keep working however it is spaced.</para>
    /// <para>This is why "--" is not treated as a decrement by the scanner: unary minus
    /// exists, so the sequence has a perfectly good reading, and choosing between the two
    /// needs to know whether an operand follows.</para>
    /// </summary>
    [TestCase("x - -1")]
    [TestCase("x - - 1")]
    [TestCase("x --1")]
    [TestCase("x-- 1")]
    [TestCase("x--1")]
    public void SubtractingANegativeNumber_ScansCleanlyHoweverItIsSpaced(string source)
    {
        (List<Token> tokens, DiagnosticBag diagnostics) = ScanRaw(source);

        Assert.Multiple(() =>
        {
            Assert.That(IdsOf(diagnostics), Is.Empty, $"\"{source}\" should scan cleanly");
            Assert.That(tokens.Select(t => t.Type), Is.EqualTo(new[]
            {
                TokenType.Identifier, TokenType.Minus, TokenType.Minus,
                TokenType.IntegerLiteral, TokenType.EndOfFile,
            }));
        });
    }

    [Test]
    public void SubtractingANegativeIdentifier_ScansCleanly()
    {
        (List<Token> tokens, DiagnosticBag diagnostics) = ScanRaw("x--y");

        Assert.Multiple(() =>
        {
            Assert.That(IdsOf(diagnostics), Is.Empty);
            Assert.That(tokens.Select(t => t.Type), Is.EqualTo(new[]
            {
                TokenType.Identifier, TokenType.Minus, TokenType.Minus,
                TokenType.Identifier, TokenType.EndOfFile,
            }));
        });
    }

    [Test]
    public void DoubleNegation_ScansCleanly()
    {
        (_, DiagnosticBag diagnostics) = ScanRaw("let n = --x;");
        Assert.That(IdsOf(diagnostics), Is.Empty);
    }

    [Test]
    public void PostfixDecrement_ScansAsTwoMinusSigns_AndIsLeftToTheParser()
    {
        // "i--;" is not rejected here. The scanner has no way to tell it from a
        // subtraction whose right operand happens to be missing, so the parser reports it.
        (List<Token> tokens, DiagnosticBag diagnostics) = ScanRaw("i--;");

        Assert.Multiple(() =>
        {
            Assert.That(IdsOf(diagnostics), Is.Empty);
            Assert.That(tokens.Select(t => t.Type), Is.EqualTo(new[]
            {
                TokenType.Identifier, TokenType.Minus, TokenType.Minus,
                TokenType.Semicolon, TokenType.EndOfFile,
            }));
        });
    }

    [Test]
    public void Increment_IsStillReported_BecauseThereIsNoUnaryPlus()
    {
        // The asymmetry with "--" is deliberate: "a++b" has no valid reading, because
        // Profi-C has no unary plus for the second "+" to be part of.
        (_, DiagnosticBag diagnostics) = ScanRaw("i++;");
        Assert.That(IdsOf(diagnostics), Is.EqualTo(new[] { "PFC0006" }));
    }

    [Test]
    public void NotEqual_IsStillScannedDespiteTheBareBangDiagnostic()
    {
        Token token = ScanSingle("!=");
        Assert.That(token.Type, Is.EqualTo(TokenType.NotEqual));
    }

    [TestCase("\"\\q\"")]
    [TestCase("'\\q'")]
    public void UnrecognizedEscape_IsReported(string source)
    {
        (List<Token> tokens, DiagnosticBag diagnostics) = ScanRaw(source);

        Assert.Multiple(() =>
        {
            Assert.That(IdsOf(diagnostics), Does.Contain("PFC0007"));
            Assert.That(tokens, Has.Count.EqualTo(2));
        });
    }

    [TestCase("\"\\u12\"")]
    [TestCase("\"\\uZZZZ\"")]
    public void MalformedUnicodeEscape_IsReported(string source)
    {
        (_, DiagnosticBag diagnostics) = ScanRaw(source);
        Assert.That(IdsOf(diagnostics), Does.Contain("PFC0008"));
    }

    [Test]
    public void SeveralIndependentErrors_AreAllReportedInOnePass()
    {
        (List<Token> tokens, DiagnosticBag diagnostics) = ScanRaw(
            """
            let a = #;
            let b = 'xy';
            let c = "unclosed
            let d = a && b;
            """);

        Assert.Multiple(() =>
        {
            Assert.That(IdsOf(diagnostics),
                        Is.EqualTo(new[] { "PFC0001", "PFC0004", "PFC0002", "PFC0006" }));
            Assert.That(tokens, Is.Not.Empty);
            Assert.That(diagnostics.HasErrors, Is.True);
        });
    }

    [Test]
    public void DiagnosticBag_StopsCollectingAtItsCap()
    {
        (_, DiagnosticBag diagnostics) = ScanRaw(new string('#', DiagnosticBag.MaximumDiagnostics + 50));

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.Count, Is.EqualTo(DiagnosticBag.MaximumDiagnostics));
            Assert.That(diagnostics.IsFull, Is.True);
        });
    }

    [Test]
    public void Scanning_NeverThrows()
    {
        string[] hostile =
        [
            "'", "\"", "\\", "comment begin", "'\\", "\"\\", "\\u", "'\\u12",
            "#$%^&", "\u0000", "let \"", "comment begin \"end",
        ];

        foreach (string source in hostile)
        {
            Assert.DoesNotThrow(() => ScanRaw(source), $"scanning \"{source}\" threw");
        }
    }
}
