using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Lexing;
using ProfiC.Compiler.Semantics;

namespace ProfiC.Tests.Lexing;

/// <summary>
/// <para>Block strings, and the length of the quotes that delimit one.</para>
/// <para>A run of three or more opens one and a run of the same length closes it, so any
/// shorter run inside is text. That is what lets a block hold quotes of its own, including the
/// three that would otherwise be the only string the language could not write.</para>
/// </summary>
[TestFixture]
public sealed class BlockStringTests : LexerTestBase
{
    /// <summary>Scans a block string and reads what it holds.</summary>
    private static string Decode(string source)
    {
        Token token = ScanSingle(source);
        Assert.That(token.Type, Is.EqualTo(TokenType.BlockStringLiteral));

        object? value = LiteralDecoder.Decode(
            new LiteralExpr(token.Span, LiteralKind.BlockString, token.Lexeme));

        return (string)value!;
    }

    // ---- The delimiter is however long it needs to be ----------------------------------------

    [TestCase("\"\"\"plain\"\"\"", ExpectedResult = "plain", TestName = "three")]
    [TestCase("\"\"\"\"plain\"\"\"\"", ExpectedResult = "plain", TestName = "four")]
    [TestCase("\"\"\"\"\"plain\"\"\"\"\"", ExpectedResult = "plain", TestName = "five")]
    public string AnyRunOfThreeOrMoreOpensOne(string source) => Decode(source);

    /// <summary>
    /// The point of the rule. A run shorter than the delimiter is text, so the delimiter is
    /// chosen to be one longer than the longest run the block has to hold.
    /// </summary>
    [TestCase("\"\"\"say \"hi\" now\"\"\"", ExpectedResult = "say \"hi\" now", TestName = "one quote")]
    [TestCase("\"\"\"a \"\"b\"\" c\"\"\"", ExpectedResult = "a \"\"b\"\" c", TestName = "two quotes")]
    [TestCase("\"\"\"\"holds \"\"\" here\"\"\"\"", ExpectedResult = "holds \"\"\" here",
              TestName = "three quotes, in a four-quote block")]
    [TestCase("\"\"\"\"\"holds \"\"\"\" here\"\"\"\"\"", ExpectedResult = "holds \"\"\"\" here",
              TestName = "four quotes, in a five-quote block")]
    public string AShorterRunInsideIsText(string source) => Decode(source);

    /// <summary>
    /// <para>A block that <em>ends</em> with a quote has no one-line form at any length: the
    /// last quote of the text sits against the closer, making a run one longer than the
    /// delimiter however long the delimiter is.</para>
    /// <para>Putting the closer on its own line breaks the run, which is the form to reach
    /// for.</para>
    /// </summary>
    [Test]
    public void TextEndingInAQuoteTakesTheMultiLineForm() => Assert.That(
        Decode("\"\"\"\n    say \"hi\"\n    \"\"\""),
        Is.EqualTo("say \"hi\""));

    /// <summary>
    /// <para>A run longer than the delimiter is the one place the rule is not obvious, so it is
    /// said out loud. The last quotes of the run close the block and the rest are held, which
    /// is what the author almost certainly meant.</para>
    /// <para>A warning, because that reading is settled rather than guessed at.</para>
    /// </summary>
    [Test]
    public void ARunLongerThanTheDelimiterIsWarnedAboutAndHeld()
    {
        (List<Token> tokens, DiagnosticBag diagnostics) = ScanRaw("\"\"\"He said \"hi\"\"\"\"");

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.Select(d => d.Id), Is.EqualTo(new[] { "PC0015" }));
            Assert.That(diagnostics.Single().Severity, Is.EqualTo(DiagnosticSeverity.Warning));
            Assert.That(diagnostics.Single().Message, Does.Contain("\"\"\"\"\""));
            Assert.That(tokens[0].Type, Is.EqualTo(TokenType.BlockStringLiteral));
        });
    }

    /// <summary>The reading the warning describes is the one the block actually has.</summary>
    [Test]
    public void TheQuotesBeyondTheDelimiterAreHeld() =>
        Assert.That(
            (string)LiteralDecoder.Decode(
                new LiteralExpr(
                    ScanRaw("\"\"\"He said \"hi\"\"\"\"").Tokens[0].Span,
                    LiteralKind.BlockString,
                    ScanRaw("\"\"\"He said \"hi\"\"\"\"").Tokens[0].Lexeme))!,
            Is.EqualTo("He said \"hi\""));

    /// <summary>
    /// A quote in the run does not open a second string on the way out. Recovery that leaves
    /// the rest of the file readable is the whole reason the run closes the block.
    /// </summary>
    [Test]
    public void TheCodeAfterTooLongARunStillScans()
    {
        (List<Token> tokens, DiagnosticBag diagnostics) =
            ScanRaw("string s = \"\"\"a\"\"\"\";\nCatalog c;");

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.Select(d => d.Id), Is.EqualTo(new[] { "PC0015" }));
            Assert.That(
                tokens.Select(t => t.Type),
                Does.Contain(TokenType.Semicolon).And.Contain(TokenType.EndOfFile));
        });
    }

    // ---- Unterminated -------------------------------------------------------------------------

    /// <summary>The message names the run that would close it, which is not always three.</summary>
    [TestCase("\"\"\"never closed", "\"\"\"", TestName = "three")]
    [TestCase("\"\"\"\"never closed", "\"\"\"\"", TestName = "four")]
    public void AnUnterminatedBlockNamesItsOwnCloser(string source, string expected)
    {
        (_, DiagnosticBag diagnostics) = ScanRaw(source);

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.Select(d => d.Id), Is.EqualTo(new[] { "PC0013" }));
            Assert.That(diagnostics.Single().Message, Does.Contain($"expected '{expected}'"));
        });
    }

    /// <summary>
    /// <para>A closer written a quote short is text by the rule, so the block runs on and takes
    /// the rest of the file with it. Saying "unterminated" at the opener buries the one edit
    /// that fixes it, so the run that all but closed the block is what gets named.</para>
    /// <para>An error, not a warning: everything after the run is inside a string that was
    /// never meant to hold it.</para>
    /// </summary>
    [Test]
    public void AShorterRunIsNamedAsTheCloserThatWasMeant()
    {
        (_, DiagnosticBag diagnostics) = ScanRaw(
            "string s = \"\"\"\"\n    held\n    \"\"\";\nCatalog c;");

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.Select(d => d.Id), Is.EqualTo(new[] { "PC0016" }));
            Assert.That(diagnostics.Single().Severity, Is.EqualTo(DiagnosticSeverity.Error));
            Assert.That(diagnostics.Single().Message,
                        Does.Contain("Open it with '\"\"\"'").Or.Contain("close it with '\"\"\"\"'"));
            Assert.That(diagnostics.Single().Span.Start.Line, Is.EqualTo(3));
        });
    }

    /// <summary>
    /// The near miss has to be a run that could have been a delimiter. One or two quotes are
    /// ordinary text inside a block, and calling them a mistaken closer would fire on most
    /// blocks that hold any quotes at all.
    /// </summary>
    [Test]
    public void AQuoteOrTwoIsNotANearMiss()
    {
        (_, DiagnosticBag diagnostics) = ScanRaw("\"\"\"\"holds \"\" and \" but never closes");

        Assert.That(diagnostics.Select(d => d.Id), Is.EqualTo(new[] { "PC0013" }));
    }

    // ---- What the rest of the scanner still sees ---------------------------------------------

    /// <summary>
    /// Two quotes are an empty string and three open a block, so the boundary between the two
    /// rules has to fall exactly there.
    /// </summary>
    [TestCase("\"\"", TokenType.StringLiteral, TestName = "two quotes are an empty string")]
    [TestCase("\"\"\"\"\"\"", TokenType.BlockStringLiteral, TestName = "six open one, not two")]
    public void TheBoundaryWithAnOrdinaryStringHolds(string source, TokenType expected)
    {
        (List<Token> tokens, _) = ScanRaw(source);
        Assert.That(tokens[0].Type, Is.EqualTo(expected));
    }

    /// <summary>
    /// Nothing inside one is read, whatever the delimiter's length. A hole, an escape and a
    /// quote all survive, which is what makes a block the verbatim form.
    /// </summary>
    [Test]
    public void NothingInsideOneIsRead() => Assert.That(
        Decode("\"\"\"\"{{n}} \\t \"\"\" and \\u0041\"\"\"\""),
        Is.EqualTo("{{n}} \\t \"\"\" and \\u0041"));

    /// <summary>
    /// The margin is read off the closing run's indentation whatever its length, so a longer
    /// delimiter does not cost the block its alignment with the code around it.
    /// </summary>
    [Test]
    public void TheMarginIsStillReadFromALongerCloser() => Assert.That(
        Decode("\"\"\"\"\n    holds \"\"\" here\n    and this\n    \"\"\"\""),
        Is.EqualTo("holds \"\"\" here\nand this"));
}
