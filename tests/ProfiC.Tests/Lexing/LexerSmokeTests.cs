using ProfiC.Compiler.Lexing;

namespace ProfiC.Tests.Lexing;

/// <summary>
/// <para>Baseline checks that the lexer is reachable and behaves as expected.</para>
/// <para>These are superseded once the full suite lands: golden token streams, structural
/// properties over the sample corpus, the 55-keyword table, and recovery fixtures.</para>
/// </summary>
[TestFixture]
public sealed class LexerSmokeTests
{
    private static List<Token> Scan(string source) => new Lexer(source).Scan();

    [Test]
    public void Scan_EmptySource_ProducesNoTokens()
    {
        Assert.That(Scan(string.Empty), Is.Empty);
    }

    [TestCase("integer", TokenType.Integer)]
    [TestCase("function", TokenType.Function)]
    [TestCase("yield", TokenType.Yield)]
    [TestCase("begin", TokenType.Begin)]
    [TestCase("end", TokenType.End)]
    public void Scan_Keyword_ProducesOneTokenOfThatType(string source, TokenType expected)
    {
        List<Token> tokens = Scan(source);

        Assert.That(tokens, Has.Count.EqualTo(1));
        Assert.That(tokens[0].Type, Is.EqualTo(expected));
        Assert.That(tokens[0].Lexeme, Is.EqualTo(source));
    }

    [TestCase("42", TokenType.IntegerLiteral)]
    [TestCase("3.14", TokenType.RealLiteral)]
    [TestCase("22|7", TokenType.FractionLiteral)]
    [TestCase("\"text\"", TokenType.StringLiteral)]
    [TestCase("'x'", TokenType.CharLiteral)]
    public void Scan_Literal_ProducesOneTokenOfThatType(string source, TokenType expected)
    {
        List<Token> tokens = Scan(source);

        Assert.That(tokens, Has.Count.EqualTo(1));
        Assert.That(tokens[0].Type, Is.EqualTo(expected));
    }

    [TestCase("<=", TokenType.LessThanOrEqual)]
    [TestCase("<", TokenType.LessThan)]
    [TestCase("==", TokenType.EqualEqual)]
    [TestCase("=", TokenType.Equal)]
    [TestCase("!=", TokenType.NotEqual)]
    [TestCase(">=", TokenType.GreaterThanOrEqual)]
    [TestCase(">", TokenType.GreaterThan)]
    public void Scan_Operator_AppliesLongestMatch(string source, TokenType expected)
    {
        List<Token> tokens = Scan(source);

        Assert.That(tokens, Has.Count.EqualTo(1));
        Assert.That(tokens[0].Type, Is.EqualTo(expected));
    }

    [Test]
    public void Scan_LineComment_IsSkipped()
    {
        Assert.That(Scan("comment this is ignored\nlet"), Has.Count.EqualTo(1));
    }

    [Test]
    public void Scan_BlockComment_IsSkipped()
    {
        Assert.That(Scan("comment begin\n  ignored\nend comment\nlet"), Has.Count.EqualTo(1));
    }

    [Test]
    public void Scan_UnclosedBlockComment_Throws()
    {
        // The scanner will later report this as a diagnostic and recover, rather than throw.
        Assert.That(() => Scan("comment begin\n  never closed"), Throws.TypeOf<FormatException>());
    }

    [Test]
    public void Scan_QuestionMark_ThrowsUntilPhase1()
    {
        // Documents a known gap: "?" is the optional type suffix, but the scanner does not
        // yet recognize it. This becomes a positive assertion once it does.
        Assert.That(() => Scan("string? nickname"), Throws.TypeOf<FormatException>());
    }
}
