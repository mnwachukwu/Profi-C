using ProfiC.Compiler.Lexing;

namespace ProfiC.Tests.Lexing;

/// <summary>
/// Operators and punctuation. The longest-match cases matter most: they are what regresses
/// when someone reorders the scanner's symbol handling.
/// </summary>
[TestFixture]
public sealed class OperatorTests : LexerTestBase
{
    [TestCase("+", TokenType.Plus)]
    [TestCase("-", TokenType.Minus)]
    [TestCase("*", TokenType.Star)]
    [TestCase("/", TokenType.Slash)]
    [TestCase("%", TokenType.Percent)]
    [TestCase("^", TokenType.Caret)]
    [TestCase("|", TokenType.Pipe)]
    [TestCase("?", TokenType.Question)]
    [TestCase(":", TokenType.Colon)]
    [TestCase("(", TokenType.LeftParen)]
    [TestCase(")", TokenType.RightParen)]
    [TestCase("{", TokenType.LeftBrace)]
    [TestCase("}", TokenType.RightBrace)]
    [TestCase("[", TokenType.LeftBracket)]
    [TestCase("]", TokenType.RightBracket)]
    [TestCase(",", TokenType.Comma)]
    [TestCase(";", TokenType.Semicolon)]
    [TestCase(".", TokenType.Dot)]
    public void SingleCharacterSymbol_Scans(string source, TokenType expected)
    {
        Token token = ScanSingle(source);
        Assert.That(token.Type, Is.EqualTo(expected));
        Assert.That(token.Lexeme, Is.EqualTo(source));
    }

    [TestCase("==", TokenType.EqualEqual)]
    [TestCase("!=", TokenType.NotEqual)]
    [TestCase("<=", TokenType.LessThanOrEqual)]
    [TestCase(">=", TokenType.GreaterThanOrEqual)]
    [TestCase("=>", TokenType.Arrow)]
    public void TwoCharacterOperator_Scans(string source, TokenType expected)
    {
        Token token = ScanSingle(source);
        Assert.That(token.Type, Is.EqualTo(expected));
        Assert.That(token.Lexeme, Is.EqualTo(source));
    }

    /// <summary>
    /// Each longer operator written next to the shorter one it could swallow. A scanner
    /// that checked the single-character forms first would fail every one of these.
    /// </summary>
    [TestCase("a<=b", TokenType.LessThanOrEqual)]
    [TestCase("a<b", TokenType.LessThan)]
    [TestCase("a>=b", TokenType.GreaterThanOrEqual)]
    [TestCase("a>b", TokenType.GreaterThan)]
    [TestCase("a==b", TokenType.EqualEqual)]
    [TestCase("a=b", TokenType.Equal)]
    [TestCase("a=>b", TokenType.Arrow)]
    [TestCase("a!=b", TokenType.NotEqual)]
    public void LongestMatch_PicksTheRightOperatorBetweenOperands(string source, TokenType expected)
    {
        List<Token> tokens = ScanWithoutEof(source);

        Assert.Multiple(() =>
        {
            Assert.That(tokens, Has.Count.EqualTo(3));
            Assert.That(tokens[0].Type, Is.EqualTo(TokenType.Identifier));
            Assert.That(tokens[1].Type, Is.EqualTo(expected));
            Assert.That(tokens[2].Type, Is.EqualTo(TokenType.Identifier));
        });
    }

    [Test]
    public void Arrow_AndEqualEqual_DoNotInterfere()
    {
        List<Token> tokens = ScanWithoutEof("= == => =");

        Assert.That(tokens.Select(t => t.Type), Is.EqualTo(new[]
        {
            TokenType.Equal, TokenType.EqualEqual, TokenType.Arrow, TokenType.Equal,
        }));
    }

    [Test]
    public void QuestionMark_ScansAsATypeSuffix()
    {
        List<Token> tokens = ScanWithoutEof("string? nickname");

        Assert.That(tokens.Select(t => t.Type), Is.EqualTo(new[]
        {
            TokenType.String, TokenType.Question, TokenType.Identifier,
        }));
    }

    [Test]
    public void TypeSuffixes_Nest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ScanWithoutEof("Node?[]").Select(t => t.Type), Is.EqualTo(new[]
            {
                TokenType.Identifier, TokenType.Question,
                TokenType.LeftBracket, TokenType.RightBracket,
            }));

            Assert.That(ScanWithoutEof("Node[]?").Select(t => t.Type), Is.EqualTo(new[]
            {
                TokenType.Identifier, TokenType.LeftBracket,
                TokenType.RightBracket, TokenType.Question,
            }));
        });
    }

    [Test]
    public void Colon_ScansInACaseLabel()
    {
        List<Token> tokens = ScanWithoutEof("case 1:");

        Assert.That(tokens.Select(t => t.Type), Is.EqualTo(new[]
        {
            TokenType.Case, TokenType.IntegerLiteral, TokenType.Colon,
        }));
    }

    [Test]
    public void CanonicalText_IsAvailableForFixedTextTokens()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TokenType.Arrow.Text(), Is.EqualTo("=>"));
            Assert.That(TokenType.Question.Text(), Is.EqualTo("?"));
            Assert.That(TokenType.End.Text(), Is.EqualTo("end"));
            Assert.That(TokenType.Identifier.Text(), Is.Null);
            Assert.That(TokenType.IntegerLiteral.Text(), Is.Null);
            Assert.That(TokenType.EndOfFile.Text(), Is.Null);
        });
    }
}
