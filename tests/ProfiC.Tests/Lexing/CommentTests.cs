using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Lexing;

namespace ProfiC.Tests.Lexing;

/// <summary>
/// <para>A comment is marked rather than named: <c>#</c> runs to the end of the line and
/// <c>##</c> opens a block closed by the next pair.</para>
/// <para>The block's closer takes the rest of its own line with it, which is what settles two
/// questions at once — there is no depth to count, so nesting is not an idea that can go
/// wrong; and nothing can follow a closer and still be code, so a comment is a line of its own
/// or the end of a line and never sits in the middle of one.</para>
/// </summary>
[TestFixture]
public sealed class CommentTests : LexerTestBase
{
    [Test]
    public void LineComment_RunsToEndOfLine()
    {
        List<Token> tokens = ScanWithoutEof("# ignored entirely\nlet");

        Assert.That(tokens, Has.Count.EqualTo(1));
        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.Let));
    }

    [Test]
    public void LineComment_MayTrailCode()
    {
        List<Token> tokens = ScanWithoutEof("let x = 1;   # trailing\nlet");

        Assert.That(tokens.Select(t => t.Type), Is.EqualTo(new[]
        {
            TokenType.Let, TokenType.Identifier, TokenType.Equal,
            TokenType.IntegerLiteral, TokenType.Semicolon, TokenType.Let,
        }));
    }

    [Test]
    public void LineComment_AtEndOfFileWithNoNewline_IsFine()
    {
        Assert.That(ScanWithoutEof("let # trailing"), Has.Count.EqualTo(1));
    }

    [Test]
    public void BlockComment_SpansLines()
    {
        List<Token> tokens = ScanWithoutEof("##\n  many\n  lines\n##\nlet");

        Assert.That(tokens, Has.Count.EqualTo(1));
        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.Let));
    }

    /// <summary>
    /// The closer takes the rest of its line, so the "let" beside it is part of the comment
    /// rather than code. This is the rule that keeps a comment out of the middle of a line.
    /// </summary>
    [Test]
    public void BlockCloser_TakesTheRestOfItsLine()
    {
        List<Token> tokens = ScanWithoutEof("## inline ## let\nlet");

        Assert.That(tokens, Has.Count.EqualTo(1), "only the 'let' on the next line survives");
        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.Let));
    }

    /// <summary>
    /// <para>Nesting is not a thing that can go wrong, because it is not a thing.</para>
    /// <para>The first pair after the opener closes the block, whatever was written between,
    /// so a comment discussing comment syntax cannot half-close itself and spill its remainder
    /// into the program as code.</para>
    /// </summary>
    [Test]
    public void BlockComment_IsClosedByTheFirstPair_WhateverIsInside()
    {
        List<Token> tokens = ScanWithoutEof("##\n  a ## inside\n let");

        Assert.That(tokens, Has.Count.EqualTo(1), "the inner pair closed it, taking its line");
        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.Let));
    }

    /// <summary>A run of marks is a heading, since the extra ones are simply comment text.</summary>
    [Test]
    public void ARunOfMarks_IsAHeadingRatherThanAnError()
    {
        List<Token> tokens = ScanWithoutEof("#########\n#  title  #\n#########\nlet");

        Assert.That(tokens, Has.Count.EqualTo(1));
        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.Let));
    }

    /// <summary>A single mark cannot close a block; only a pair does.</summary>
    [Test]
    public void ASingleMark_DoesNotCloseABlock()
    {
        (List<Token> tokens, DiagnosticBag diagnostics) = ScanRaw("##\n # not a closer\nlet");

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.Select(d => d.Id), Is.EqualTo(new[] { "PC0005" }));
            Assert.That(tokens.Select(t => t.Type), Is.EqualTo(new[] { TokenType.EndOfFile }));
        });
    }

    /// <summary>
    /// A mark inside a string is text. The scanner reaches a comment only where a token could
    /// begin, and a string literal is consumed whole.
    /// </summary>
    [Test]
    public void AMarkInsideAString_IsText()
    {
        List<Token> tokens = ScanWithoutEof("let s = \"# not a comment\";");

        Assert.That(tokens.Select(t => t.Type), Is.EqualTo(new[]
        {
            TokenType.Let, TokenType.Identifier, TokenType.Equal,
            TokenType.StringLiteral, TokenType.Semicolon,
        }));
    }

    /// <summary>
    /// Marking a comment rather than naming one means comment syntax speaks for no word at
    /// all. "begin" is reserved because it opens a block, and for no other reason.
    /// </summary>
    [TestCase("comment")]
    [TestCase("commentary")]
    [TestCase("begin")]
    public void CommentSyntaxSpeaksForNoWord(string word)
    {
        Token token = ScanSingle(word);

        Assert.That(
            token.Type,
            Is.EqualTo(word == "begin" ? TokenType.Begin : TokenType.Identifier),
            $"'{word}' is not spoken for by comment syntax");
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
        List<Token> tokens = Scan("  \n\t # nothing here\n ## x ##  \n");

        Assert.That(tokens, Has.Count.EqualTo(1));
        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.EndOfFile));
    }
}
