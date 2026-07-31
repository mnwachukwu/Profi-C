using ProfiC.Compiler.Diagnostics;
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

    // ---- Bases other than ten -----------------------------------------------------------

    /// <summary>
    /// A prefix says which digits follow, and the result is a whole number like any other —
    /// the same token, so nothing past the scanner knows how it was written.
    /// </summary>
    [TestCase("0xFF")]
    [TestCase("0XFF")]
    [TestCase("0x0")]
    [TestCase("0xdeadBEEF")]
    [TestCase("0b1010")]
    [TestCase("0B1010")]
    [TestCase("0b0")]
    public void NumberInAnotherBase_ScansAsAWholeNumber(string source)
    {
        Token token = ScanSingle(source);

        Assert.Multiple(() =>
        {
            Assert.That(token.Type, Is.EqualTo(TokenType.IntegerLiteral));
            Assert.That(token.Lexeme, Is.EqualTo(source));
        });
    }

    /// <summary>
    /// Everything after the prefix is taken before any of it is judged, so a stray digit is one
    /// mistake with one message rather than a number that stops early and a name that starts
    /// where it stopped.
    /// </summary>
    [TestCase("0x", "PC0017")]
    [TestCase("0b", "PC0017")]
    [TestCase("0xG", "PC0018")]
    [TestCase("0b12", "PC0018")]
    [TestCase("0xFFZZ", "PC0018")]
    public void AMalformedBase_IsOneMistake(string source, string expected)
    {
        (List<Token> tokens, DiagnosticBag diagnostics) = ScanRaw(source);

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.Select(d => d.Id), Is.EqualTo(new[] { expected }));
            Assert.That(tokens[0].Type, Is.EqualTo(TokenType.IntegerLiteral),
                        "a token is still produced, so the parser can carry on");
        });
    }

    /// <summary>Only a whole number may name a base; the prefix is not a real's business.</summary>
    [Test]
    public void ABaseIsNotFollowedByAPoint()
    {
        List<Token> tokens = ScanWithoutEof("0x1.5");

        Assert.That(
            tokens.Select(t => t.Type),
            Is.EqualTo(new[] { TokenType.IntegerLiteral, TokenType.Dot, TokenType.IntegerLiteral }));
    }

    // ---- Exponents -----------------------------------------------------------------------

    /// <summary>
    /// An exponent names a scale rather than a count, so what it produces is a real whether or
    /// not a point was written.
    /// </summary>
    [TestCase("1e3")]
    [TestCase("1E3")]
    [TestCase("1e+3")]
    [TestCase("1e-3")]
    [TestCase("1.5e3")]
    [TestCase("2.5e-30")]
    [TestCase("0e0")]
    public void AnExponent_ScansAsAReal(string source)
    {
        Token token = ScanSingle(source);

        Assert.Multiple(() =>
        {
            Assert.That(token.Type, Is.EqualTo(TokenType.RealLiteral));
            Assert.That(token.Lexeme, Is.EqualTo(source));
        });
    }

    // ---- Separators -----------------------------------------------------------------------

    /// <summary>
    /// <para>An underscore groups digits for a reader and means nothing to the value, so it is
    /// allowed in every run of digits rather than only in a whole number.</para>
    /// <para>A rule with a hole in it — grouping allowed before a point and not after — would
    /// be one more thing to learn and buy nothing.</para>
    /// </summary>
    [TestCase("1_000", TokenType.IntegerLiteral)]
    [TestCase("1_000_000", TokenType.IntegerLiteral)]
    [TestCase("0xFF_FF", TokenType.IntegerLiteral)]
    [TestCase("0b1010_1010", TokenType.IntegerLiteral)]
    [TestCase("1_000.5", TokenType.RealLiteral)]
    [TestCase("1.000_5", TokenType.RealLiteral)]
    [TestCase("1e1_0", TokenType.RealLiteral)]
    [TestCase("1_500|1_000", TokenType.FractionLiteral)]
    public void ASeparator_GroupsDigitsWithoutChangingTheForm(string source, TokenType expected)
    {
        Token token = ScanSingle(source);

        Assert.Multiple(() =>
        {
            Assert.That(token.Type, Is.EqualTo(expected));
            Assert.That(token.Lexeme, Is.EqualTo(source), "the lexeme is the exact source slice");
        });
    }

    /// <summary>
    /// A separator sits between digits, so one at either end has nothing to group. Reported
    /// in every base, since a rule that holds in ten and not in sixteen is not a rule.
    /// </summary>
    [TestCase("1_")]
    [TestCase("1_000_")]
    [TestCase("0xFF_")]
    [TestCase("0x_FF")]
    [TestCase("1.5_")]
    public void ASeparatorWithNothingToGroup_IsReported(string source) => Assert.That(
        ScanRaw(source).Diagnostics.Select(d => d.Id), Is.EqualTo(new[] { "PC0020" }));

    /// <summary>
    /// A name may still begin with one, which is what keeps the rule about numbers from
    /// reaching into identifiers.
    /// </summary>
    [TestCase("_")]
    [TestCase("_1")]
    [TestCase("_count")]
    public void ANameMayStillBeginWithASeparator(string source) =>
        Assert.That(ScanSingle(source).Type, Is.EqualTo(TokenType.Identifier));

    /// <summary>
    /// A number cannot sit against a name, so an <c>e</c> with nothing usable after it was
    /// being written as an exponent whatever else it looks like. Saying so beats leaving the
    /// parser to report a name where a semicolon was wanted.
    /// </summary>
    [TestCase("1e")]
    [TestCase("1e;")]
    [TestCase("40e")]
    [TestCase("1e+")]
    [TestCase("1.5e-")]
    public void AnExponentWithNoDigits_IsReported(string source) => Assert.That(
        ScanRaw(source).Diagnostics.Select(d => d.Id), Is.EqualTo(new[] { "PC0019" }));

    /// <summary>
    /// <para>A name cannot begin with a digit, and a number cannot sit against one, so a word
    /// written straight after a number is one mistake rather than two things.</para>
    /// <para>The word is taken into the number's lexeme so the parser sees one token and adds
    /// nothing: <c>1each</c> used to draw three complaints about a statement that could not
    /// start, none of which named what was written.</para>
    /// </summary>
    [TestCase("1each")]
    [TestCase("1extra")]
    [TestCase("40var")]
    [TestCase("1.5abc")]
    [TestCase("1|2fifths")]
    public void ANameWrittenAgainstANumber_IsOneMistake(string source)
    {
        (List<Token> tokens, DiagnosticBag diagnostics) = ScanRaw(source);

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.Select(d => d.Id), Is.EqualTo(new[] { "PC0021" }));
            Assert.That(tokens, Has.Count.EqualTo(2), "one number, then the end of the file");
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
