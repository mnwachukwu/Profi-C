using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Lexing;
using ProfiC.Compiler.Text;

namespace ProfiC.Tests.Lexing;

/// <summary>
/// <para>Properties asserted over every sample program in the repository.</para>
/// <para>These carry more weight than the unit tests above, because they hold for all
/// inputs rather than for one hand-picked case. If any of them fails, something is wrong
/// with the scanner rather than with one construct.</para>
/// </summary>
[TestFixture]
public sealed class SampleCorpusTests : LexerTestBase
{
    private static (SourceText Source, List<Token> Tokens, DiagnosticBag Diagnostics) ScanSample(string name)
    {
        SourceText source = LoadSample(name);
        DiagnosticBag diagnostics = new();
        List<Token> tokens = new Lexer(source, diagnostics).Scan();
        return (source, tokens, diagnostics);
    }

    /// <summary>
    /// Every kind of token appearing anywhere in the samples, the multi-file ones included.
    /// The coverage tests below ask what the corpus contains rather than what any one file
    /// does, so they read all of it.
    /// </summary>
    private static HashSet<TokenType> EveryTokenKindInTheCorpus()
    {
        HashSet<TokenType> seen = [];

        foreach (string path in EverySampleFile)
        {
            DiagnosticBag diagnostics = new();
            List<Token> tokens = new Lexer(SourceText.FromFile(path), diagnostics).Scan();
            seen.UnionWith(tokens.Select(t => t.Type));
        }

        return seen;
    }

    [Test]
    public void TheCorpusIsNotEmpty()
    {
        Assert.That(SampleFiles.Count(), Is.GreaterThanOrEqualTo(5));
    }

    [TestCaseSource(nameof(SampleNames))]
    public void Sample_ScansWithoutAnyDiagnostic(string name)
    {
        (SourceText source, _, DiagnosticBag diagnostics) = ScanSample(name);

        Assert.That(
            diagnostics.Sorted().Select(d => $"({d.Span.Start.Line},{d.Span.Start.Column}) {d.Id}: {d.Message}"),
            Is.Empty,
            $"{source.FileName} should scan cleanly");
    }

    [TestCaseSource(nameof(SampleNames))]
    public void Sample_EveryLexemeIsTheExactSourceSlice(string name)
    {
        (SourceText source, List<Token> tokens, _) = ScanSample(name);

        foreach (Token token in tokens)
        {
            string slice = source.Text.Substring(token.Span.Start.Offset, token.Span.Length);
            Assert.That(slice, Is.EqualTo(token.Lexeme), $"at {token.Line}:{token.Column} in {name}");
        }
    }

    [TestCaseSource(nameof(SampleNames))]
    public void Sample_SpansAdvanceAndDoNotOverlap(string name)
    {
        (_, List<Token> tokens, _) = ScanSample(name);

        for (int i = 1; i < tokens.Count; i++)
        {
            Assert.That(
                tokens[i].Span.Start.Offset,
                Is.GreaterThanOrEqualTo(tokens[i - 1].Span.EndOffset),
                $"token {i} overlaps its predecessor in {name}");
        }
    }

    [TestCaseSource(nameof(SampleNames))]
    public void Sample_HasExactlyOneEndOfFileTokenAndItIsLast(string name)
    {
        (_, List<Token> tokens, _) = ScanSample(name);

        Assert.Multiple(() =>
        {
            Assert.That(tokens.Count(t => t.IsEndOfFile), Is.EqualTo(1));
            Assert.That(tokens[^1].IsEndOfFile, Is.True);
        });
    }

    [TestCaseSource(nameof(SampleNames))]
    public void Sample_EveryPositionIsOneBased(string name)
    {
        (_, List<Token> tokens, _) = ScanSample(name);

        foreach (Token token in tokens)
        {
            Assert.Multiple(() =>
            {
                Assert.That(token.Line, Is.GreaterThanOrEqualTo(1));
                Assert.That(token.Column, Is.GreaterThanOrEqualTo(1));
                Assert.That(token.Span.Start.Offset, Is.GreaterThanOrEqualTo(0));
            });
        }
    }

    [TestCaseSource(nameof(SampleNames))]
    public void Sample_ScanningIsIdempotent(string name)
    {
        (_, List<Token> first, _) = ScanSample(name);
        (_, List<Token> second, _) = ScanSample(name);

        // Token is a record, so this compares every field of every token.
        Assert.That(second, Is.EqualTo(first));
    }

    [TestCaseSource(nameof(SampleNames))]
    public void Sample_ProducesNoUnknownIdentifierWhereAKeywordWasIntended(string name)
    {
        // Catches the specific failure of dropping a word from the keyword table: the
        // scanner would silently emit an identifier and nothing else would notice.
        (_, List<Token> tokens, _) = ScanSample(name);

        foreach (Token token in tokens.Where(t => t.Type == TokenType.Identifier))
        {
            Assert.That(
                ReservedWords.Keywords.ContainsKey(token.Lexeme),
                Is.False,
                $"'{token.Lexeme}' at {token.Line}:{token.Column} in {name} scanned as an identifier "
                + "but is a reserved word");
        }
    }

    /// <summary>
    /// The tour is meant to exercise the whole grammar, so every reserved word should
    /// appear somewhere across the corpus. This is the check that catches a keyword being
    /// added to the table but never actually written down anywhere.
    /// </summary>
    [Test]
    public void Corpus_UsesEveryReservedWord()
    {
        HashSet<TokenType> seen = EveryTokenKindInTheCorpus();

        List<string> missing = [.. ReservedWords.Keywords
            .Where(entry => !seen.Contains(entry.Value))
            .Select(entry => entry.Key)
            .OrderBy(word => word, StringComparer.Ordinal)];

        Assert.That(missing, Is.Empty, "these reserved words appear in no sample");
    }

    [Test]
    public void Corpus_UsesEveryOperatorAndPunctuationMark()
    {
        HashSet<TokenType> seen = EveryTokenKindInTheCorpus();

        // Pipe is deliberately absent. A "|" only ever occurs inside a fraction literal,
        // and the scanner consumes "1|3" whole as one FractionLiteral, so no valid program
        // produces a standalone Pipe token. The token type still exists because the scanner
        // must emit something for a stray "|", and because a future fraction whose operands
        // are expressions rather than digits would need it.
        TokenType[] symbols =
        [
            TokenType.Plus, TokenType.Minus, TokenType.Star, TokenType.Slash, TokenType.Percent,
            TokenType.EqualEqual, TokenType.NotEqual, TokenType.LessThan, TokenType.GreaterThan,
            TokenType.LessThanOrEqual, TokenType.GreaterThanOrEqual, TokenType.Equal,
            TokenType.Question, TokenType.Colon, TokenType.Arrow,
            TokenType.LeftParen, TokenType.RightParen, TokenType.LeftBrace, TokenType.RightBrace,
            TokenType.LeftBracket, TokenType.RightBracket, TokenType.Comma, TokenType.Semicolon,
            TokenType.Dot,
        ];

        Assert.That(symbols.Where(t => !seen.Contains(t)), Is.Empty,
                    "these symbols appear in no sample");
    }

    [Test]
    public void Pipe_IsUnreachableFromValidSourceButStillScans()
    {
        // Documents the gap above rather than leaving it implicit.
        Assert.That(ScanSingle("|").Type, Is.EqualTo(TokenType.Pipe));
        Assert.That(ScanSingle("1|3").Type, Is.EqualTo(TokenType.FractionLiteral));
    }

    [Test]
    public void Corpus_UsesEveryLiteralForm()
    {
        HashSet<TokenType> seen = EveryTokenKindInTheCorpus();

        Assert.That(
            new[]
            {
                TokenType.IntegerLiteral, TokenType.RealLiteral, TokenType.CharLiteral,
                TokenType.StringLiteral, TokenType.FractionLiteral,
            }.Where(t => !seen.Contains(t)),
            Is.Empty);
    }
}
