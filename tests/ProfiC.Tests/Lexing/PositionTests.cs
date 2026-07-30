using ProfiC.Compiler.Lexing;

namespace ProfiC.Tests.Lexing;

/// <summary>
/// Source positions. These matter more than they look: every AST node will derive its
/// position from a token, so an error here would surface much later as diagnostics that
/// point at the wrong place.
/// </summary>
[TestFixture]
public sealed class PositionTests : LexerTestBase
{
    [Test]
    public void FirstToken_IsAtLineOneColumnOne()
    {
        Token token = ScanWithoutEof("model")[0];

        Assert.Multiple(() =>
        {
            Assert.That(token.Line, Is.EqualTo(1));
            Assert.That(token.Column, Is.EqualTo(1));
            Assert.That(token.Span.Start.Offset, Is.EqualTo(0));
        });
    }

    [Test]
    public void EachLine_StartsAtColumnOne()
    {
        List<Token> tokens = ScanWithoutEof("let\nlet\nlet");

        Assert.Multiple(() =>
        {
            Assert.That(tokens[0].Line, Is.EqualTo(1));
            Assert.That(tokens[1].Line, Is.EqualTo(2));
            Assert.That(tokens[2].Line, Is.EqualTo(3));
            Assert.That(tokens.Select(t => t.Column), Is.All.EqualTo(1));
        });
    }

    [Test]
    public void CarriageReturnLineFeed_CountsAsOneLineBreak()
    {
        List<Token> tokens = ScanWithoutEof("let\r\nlet");

        Assert.Multiple(() =>
        {
            Assert.That(tokens[1].Line, Is.EqualTo(2));
            Assert.That(tokens[1].Column, Is.EqualTo(1));
        });
    }

    [Test]
    public void ColumnsAdvanceAcrossALine()
    {
        //           1234567890
        List<Token> tokens = ScanWithoutEof("let x = 1;");

        Assert.That(tokens.Select(t => t.Column), Is.EqualTo(new[] { 1, 5, 7, 9, 10 }));
    }

    [Test]
    public void PositionAfterAMultiLineBlockComment_IsCorrect()
    {
        List<Token> tokens = ScanWithoutEof("##\n\n\n##\nlet");

        Assert.Multiple(() =>
        {
            Assert.That(tokens[0].Line, Is.EqualTo(5));
            Assert.That(tokens[0].Column, Is.EqualTo(1));
        });
    }

    [Test]
    public void PositionAfterAStringContainingEscapes_IsCorrect()
    {
        //                                   123456789...
        List<Token> tokens = ScanWithoutEof("\"a\\nb\" let");

        Assert.Multiple(() =>
        {
            Assert.That(tokens[0].Column, Is.EqualTo(1));
            Assert.That(tokens[1].Column, Is.EqualTo(8));
        });
    }

    [Test]
    public void EndOfFileToken_SitsPastTheLastCharacter()
    {
        List<Token> tokens = Scan("let");
        Token eof = tokens[^1];

        Assert.Multiple(() =>
        {
            Assert.That(eof.Type, Is.EqualTo(TokenType.EndOfFile));
            Assert.That(eof.Lexeme, Is.Empty);
            Assert.That(eof.Span.Length, Is.EqualTo(0));
            Assert.That(eof.Span.Start.Offset, Is.EqualTo(3));
            Assert.That(eof.Line, Is.EqualTo(1));
            Assert.That(eof.Column, Is.EqualTo(4));
        });
    }

    [Test]
    public void EndOfFileToken_OnAnEmptySource_IsAtLineOneColumnOne()
    {
        Token eof = Scan(string.Empty)[0];

        Assert.Multiple(() =>
        {
            Assert.That(eof.Line, Is.EqualTo(1));
            Assert.That(eof.Column, Is.EqualTo(1));
        });
    }

    [Test]
    public void SpanLength_MatchesTheLexeme()
    {
        foreach (Token token in ScanWithoutEof("model Program let x = 22|7; \"text\" 'c'"))
        {
            Assert.That(token.Span.Length, Is.EqualTo(token.Lexeme.Length), $"for {token}");
        }
    }

    [Test]
    public void TabCountsAsOneColumn()
    {
        List<Token> tokens = ScanWithoutEof("\tlet");
        Assert.That(tokens[0].Column, Is.EqualTo(2));
    }
}
