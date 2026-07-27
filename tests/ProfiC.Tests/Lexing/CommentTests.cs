using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Lexing;

namespace ProfiC.Tests.Lexing;

/// <summary>
/// Profi-C's comments are delimited by words rather than symbols, which is unusual enough
/// that the edges around the words themselves need pinning down.
/// </summary>
[TestFixture]
public sealed class CommentTests : LexerTestBase
{
    [Test]
    public void LineComment_RunsToEndOfLine()
    {
        List<Token> tokens = ScanWithoutEof("comment ignored entirely\nlet");

        Assert.That(tokens, Has.Count.EqualTo(1));
        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.Let));
    }

    [Test]
    public void LineComment_MayTrailCode()
    {
        List<Token> tokens = ScanWithoutEof("let x = 1;   comment trailing\nlet");

        Assert.That(tokens.Select(t => t.Type), Is.EqualTo(new[]
        {
            TokenType.Let, TokenType.Identifier, TokenType.Equal,
            TokenType.IntegerLiteral, TokenType.Semicolon, TokenType.Let,
        }));
    }

    [Test]
    public void LineComment_AtEndOfFileWithNoNewline_IsFine()
    {
        Assert.That(ScanWithoutEof("let comment trailing"), Has.Count.EqualTo(1));
    }

    [Test]
    public void BlockComment_SpansLines()
    {
        List<Token> tokens = ScanWithoutEof("comment begin\n  many\n  lines\nend comment\nlet");

        Assert.That(tokens, Has.Count.EqualTo(1));
        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.Let));
    }

    [Test]
    public void BlockComment_MayOpenAndCloseOnOneLine()
    {
        Assert.That(ScanWithoutEof("comment begin inline end comment let"), Has.Count.EqualTo(1));
    }

    [Test]
    public void BlockComment_ContainingTheWordEnd_IsNotClosedByIt()
    {
        List<Token> tokens = ScanWithoutEof("comment begin\n  the word end alone\nend comment\nlet");

        Assert.That(tokens, Has.Count.EqualTo(1));
        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.Let));
    }

    [Test]
    public void BlockComment_ContainingTheWordComment_IsNotClosedByIt()
    {
        List<Token> tokens = ScanWithoutEof("comment begin\n  the word comment alone\nend comment\nlet");

        Assert.That(tokens, Has.Count.EqualTo(1));
        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.Let));
    }

    [Test]
    public void BlockComment_ClosesOnTheTwoWordsEvenInsideQuotes()
    {
        // The scanner does not read quotes while skipping a comment, so the closing phrase
        // always closes the block, wherever it appears. A block comment therefore cannot
        // contain its own closer, not even quoted.
        //
        // The evidence is in which diagnostic appears. The block ends at the quoted closer,
        // leaving the trailing quote stranded, so the report is an unterminated string. Had
        // the quotes protected the closer, the block would have run to the end of input and
        // reported an unterminated block comment instead.
        (List<Token> tokens, DiagnosticBag diagnostics) =
            ScanRaw("comment begin \"end comment\"\nlet");

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.Select(d => d.Id), Is.EqualTo(new[] { "PFC0002" }));
            Assert.That(tokens.Select(t => t.Type), Is.EqualTo(new[]
            {
                TokenType.StringLiteral, TokenType.Let, TokenType.EndOfFile,
            }));
        });
    }

    [Test]
    public void BlockCloser_ToleratesWhitespaceBetweenItsWords()
    {
        Assert.That(ScanWithoutEof("comment begin body end\n\n  comment let"), Has.Count.EqualTo(1));
    }

    [Test]
    public void WordsBeginningWithComment_AreOrdinaryIdentifiers()
    {
        Token token = ScanSingle("commentary");

        Assert.That(token.Type, Is.EqualTo(TokenType.Identifier));
        Assert.That(token.Lexeme, Is.EqualTo("commentary"));
    }

    [Test]
    public void CommentAfterAnIdentifierCharacter_IsNotAComment()
    {
        // "xcomment" is one identifier; the comment word needs a boundary on both sides.
        Token token = ScanSingle("xcomment");
        Assert.That(token.Type, Is.EqualTo(TokenType.Identifier));
    }

    [Test]
    public void CommentBegin_RequiresBeginOnTheSameLine()
    {
        // "begin" on the next line makes this a line comment, so "begin" is then code.
        List<Token> tokens = ScanWithoutEof("comment\nbegin\nend");

        Assert.That(tokens.Select(t => t.Type),
                    Is.EqualTo(new[] { TokenType.Begin, TokenType.End }));
    }

    [Test]
    public void EmptySource_ProducesOnlyEndOfFile()
    {
        List<Token> tokens = Scan(string.Empty);

        Assert.That(tokens, Has.Count.EqualTo(1));
        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.EndOfFile));
    }

    [Test]
    public void SourceOfOnlyCommentsAndWhitespace_ProducesOnlyEndOfFile()
    {
        List<Token> tokens = Scan("  \n\t comment nothing here\n comment begin x end comment  \n");

        Assert.That(tokens, Has.Count.EqualTo(1));
        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.EndOfFile));
    }
}
