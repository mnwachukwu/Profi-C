using ProfiC.Compiler.Lexing;

namespace ProfiC.Tests.Lexing;

/// <summary>The five literal forms, their escapes, and the edges between them.</summary>
[TestFixture]
public sealed class LiteralTests : LexerTestBase
{
    [TestCase("0")]
    [TestCase("7")]
    [TestCase("007")]
    [TestCase("2147483647")]
    public void Integer_Scans(string source)
    {
        Token token = ScanSingle(source);
        Assert.That(token.Type, Is.EqualTo(TokenType.IntegerLiteral));
        Assert.That(token.Lexeme, Is.EqualTo(source));
    }

    [TestCase("3.14")]
    [TestCase("0.5")]
    [TestCase("1.0")]
    public void Real_Scans(string source)
    {
        Token token = ScanSingle(source);
        Assert.That(token.Type, Is.EqualTo(TokenType.RealLiteral));
        Assert.That(token.Lexeme, Is.EqualTo(source));
    }

    [TestCase("1|3")]
    [TestCase("22|7")]
    [TestCase("3|4")]
    public void Fraction_Scans(string source)
    {
        Token token = ScanSingle(source);
        Assert.That(token.Type, Is.EqualTo(TokenType.FractionLiteral));
        Assert.That(token.Lexeme, Is.EqualTo(source));
    }

    [Test]
    public void Real_RequiresDigitsAfterThePoint()
    {
        // "3." must stay an integer followed by a dot, or member access on a number
        // could never be written.
        List<Token> tokens = ScanWithoutEof("3.");

        Assert.That(tokens.Select(t => t.Type),
                    Is.EqualTo(new[] { TokenType.IntegerLiteral, TokenType.Dot }));
    }

    [Test]
    public void Fraction_RequiresDigitsOnBothSides()
    {
        List<Token> tokens = ScanWithoutEof("a|b");

        Assert.That(tokens.Select(t => t.Type),
                    Is.EqualTo(new[] { TokenType.Identifier, TokenType.Pipe, TokenType.Identifier }));
    }

    [Test]
    public void Number_FollowedByLetters_SplitsIntoTwoTokens()
    {
        List<Token> tokens = ScanWithoutEof("40var");

        Assert.Multiple(() =>
        {
            Assert.That(tokens.Select(t => t.Type),
                        Is.EqualTo(new[] { TokenType.IntegerLiteral, TokenType.Identifier }));
            Assert.That(tokens[0].Lexeme, Is.EqualTo("40"));
            Assert.That(tokens[1].Lexeme, Is.EqualTo("var"));
        });
    }

    [Test]
    public void Identifier_MayContainDigitsAfterTheFirstCharacter()
    {
        Token token = ScanSingle("var10");
        Assert.That(token.Type, Is.EqualTo(TokenType.Identifier));
        Assert.That(token.Lexeme, Is.EqualTo("var10"));
    }

    [TestCase("_")]
    [TestCase("_count")]
    [TestCase("_1")]
    [TestCase("max_score")]
    [TestCase("trailing_")]
    [TestCase("__double")]
    public void Identifier_MayUseUnderscores(string source)
    {
        Token token = ScanSingle(source);
        Assert.That(token.Type, Is.EqualTo(TokenType.Identifier));
        Assert.That(token.Lexeme, Is.EqualTo(source));
    }

    [TestCase("café")]
    [TestCase("naïve")]
    [TestCase("变量")]
    public void Identifier_MayUseUnicodeLetters(string source)
    {
        Token token = ScanSingle(source);
        Assert.That(token.Type, Is.EqualTo(TokenType.Identifier));
        Assert.That(token.Lexeme, Is.EqualTo(source));
    }

    [TestCase("_comment")]
    [TestCase("comment_text")]
    [TestCase("_end")]
    public void UnderscoreAdjacentToAReservedWord_KeepsItAnIdentifier(string source)
    {
        // Underscore counts as an identifier character, so it forms a word boundary the
        // same way a letter does. Without that, "comment_text" would open a comment.
        Token token = ScanSingle(source);
        Assert.That(token.Type, Is.EqualTo(TokenType.Identifier));
        Assert.That(token.Lexeme, Is.EqualTo(source));
    }

    [Test]
    public void UnderscoreDoesNotMakeAReservedWordAnIdentifier()
    {
        // "model" is still a keyword; only adjacency to an underscore changes anything.
        Assert.That(ScanSingle("model").Type, Is.EqualTo(TokenType.Model));
        Assert.That(ScanSingle("model_").Type, Is.EqualTo(TokenType.Identifier));
    }

    [TestCase("'A'")]
    [TestCase("'7'")]
    [TestCase("' '")]
    [TestCase("'\\n'")]
    [TestCase("'\\t'")]
    [TestCase("'\\r'")]
    [TestCase("'\\0'")]
    [TestCase("'\\\\'")]
    [TestCase("'\\''")]
    [TestCase("'\\\"'")]
    [TestCase("'\\u0041'")]
    public void Character_Scans(string source)
    {
        Token token = ScanSingle(source);
        Assert.That(token.Type, Is.EqualTo(TokenType.CharLiteral));
        Assert.That(token.Lexeme, Is.EqualTo(source));
    }

    [TestCase("\"\"")]
    [TestCase("\"Profi-C\"")]
    [TestCase("\"with several spaces\"")]
    [TestCase("\"braces {} brackets [] semicolons;\"")]
    [TestCase("\"a newline \\n a tab \\t\"")]
    [TestCase("\"an escaped quote \\\" inside\"")]
    [TestCase("\"a backslash \\\\ inside\"")]
    [TestCase("\"\\u00e9\"")]
    public void String_Scans(string source)
    {
        Token token = ScanSingle(source);
        Assert.That(token.Type, Is.EqualTo(TokenType.StringLiteral));
        Assert.That(token.Lexeme, Is.EqualTo(source));
    }

    [Test]
    public void String_KeepsItsQuotesInTheLexeme()
    {
        // The lexeme is the exact source slice, never a reconstruction. Decoding escapes
        // is the parser's job.
        Assert.That(ScanSingle("\"text\"").Lexeme, Is.EqualTo("\"text\""));
    }

    [Test]
    public void EscapedQuote_DoesNotTerminateTheString()
    {
        Token token = ScanSingle("\"before \\\" after\"");
        Assert.That(token.Lexeme, Is.EqualTo("\"before \\\" after\""));
    }

    [TestCase("true", TokenType.True)]
    [TestCase("false", TokenType.False)]
    public void BooleanLiteral_ScansAsAKeyword(string source, TokenType expected)
    {
        Assert.That(ScanSingle(source).Type, Is.EqualTo(expected));
    }

    [Test]
    public void LiteralClassification_CoversTheFiveValueForms()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TokenType.IntegerLiteral.IsLiteral(), Is.True);
            Assert.That(TokenType.RealLiteral.IsLiteral(), Is.True);
            Assert.That(TokenType.CharLiteral.IsLiteral(), Is.True);
            Assert.That(TokenType.StringLiteral.IsLiteral(), Is.True);
            Assert.That(TokenType.FractionLiteral.IsLiteral(), Is.True);
            Assert.That(TokenType.Identifier.IsLiteral(), Is.False);
            Assert.That(TokenType.True.IsLiteral(), Is.False);
        });
    }
}
