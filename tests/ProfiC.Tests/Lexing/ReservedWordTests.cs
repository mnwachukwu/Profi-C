using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Lexing;

namespace ProfiC.Tests.Lexing;

/// <summary>
/// Checks the keyword table against the token enum. The failure this exists to catch is
/// adding a <see cref="TokenType"/> member and forgetting the dictionary entry, or the
/// reverse, which nothing else would notice until a program mysteriously failed to parse.
/// </summary>
[TestFixture]
public sealed class ReservedWordTests : LexerTestBase
{
    /// <summary>The 55 reserved words, written out rather than derived from the table.</summary>
    private static readonly string[] Expected =
    [
        "abstract", "and", "as", "base", "begin", "boolean", "break", "case", "catch",
        "character", "constant", "continue", "default", "each", "else", "end", "enumeration",
        "extends", "false", "finally", "for", "fraction", "function", "global", "if", "import", "in",
        "integer", "is", "let", "model", "namespace", "new", "not", "or", "override",
        "protected", "public", "real", "sealed", "step", "string", "structure", "switch",
        "then", "this", "throw", "to", "true", "try", "until", "using", "virtual", "while",
        "yield",
    ];

    /// <summary>Words a C# author might expect to be reserved, which deliberately are not.</summary>
    private static readonly string[] NotReserved =
    [
        "bool", "write", "read", "private", "static", "null", "void", "return", "class",
        "interface", "enum", "struct", "var", "do", "foreach", "select", "when", "const",
        "int", "func", "public2", "Model", "Program", "Console",

        // Dropped along with capture on nested models: a nested model holds no reference to
        // the model it sits inside, so there was nothing left for the word to name.
        "outer",
    ];

    public static IEnumerable<string> ExpectedWords => Expected;

    public static IEnumerable<string> NonReservedWords => NotReserved;

    // ---- Taking a reserved word back as a name ---------------------------------------------

    /// <summary>
    /// <para>A reserved word may be used as a name by writing '@' in front of it.</para>
    /// <para>Twelve of the reserved words are ordinary things to call a variable — 'end',
    /// 'base', 'to', 'each', 'step' among them — and no amount of renaming keywords frees them
    /// all. The mark takes one back deliberately, and it is the only place a name may begin
    /// with something other than a letter.</para>
    /// </summary>
    [TestCase("@end")]
    [TestCase("@base")]
    [TestCase("@to")]
    [TestCase("@each")]
    [TestCase("@step")]
    [TestCase("@this")]
    public void AnEscapedReservedWordScansAsAName(string written)
    {
        Token token = ScanSingle(written);

        Assert.Multiple(() =>
        {
            Assert.That(token.Type, Is.EqualTo(TokenType.Identifier));
            Assert.That(token.Lexeme, Is.EqualTo(written), "a lexeme is the exact source slice");
            Assert.That(token.Name, Is.EqualTo(written[1..]), "the mark is no part of the name");
        });
    }

    /// <summary>The mark says "this word is otherwise taken", so one that is not misleads.</summary>
    [Test]
    public void AnEscapeOnAWordThatNeedsNoneIsReported()
    {
        (List<Token> tokens, DiagnosticBag diagnostics) = ScanRaw("@total");

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.Select(d => d.Id), Is.EqualTo(new[] { "PC0009" }));
            Assert.That(diagnostics.Single().Severity, Is.EqualTo(DiagnosticSeverity.Warning));
            Assert.That(tokens[0].Name, Is.EqualTo("total"), "and it still means that name");
        });
    }

    [Test]
    public void AnEscapeWithNoNameAfterItIsReported() =>
        Assert.That(ScanRaw("@ ").Diagnostics.Select(d => d.Id), Is.EqualTo(new[] { "PC0010" }));

    [Test]
    public void KeywordTable_ContainsExactlyFiftyFiveWords()
    {
        Assert.That(ReservedWords.Count, Is.EqualTo(55));
    }

    [Test]
    public void KeywordTable_MatchesTheExpectedWordList()
    {
        Assert.That(ReservedWords.Keywords.Keys.OrderBy(k => k, StringComparer.Ordinal),
                    Is.EqualTo(Expected.OrderBy(k => k, StringComparer.Ordinal)));
    }

    [Test]
    public void KeywordTable_MapsEachWordToADistinctTokenType()
    {
        Assert.That(ReservedWords.Keywords.Values.Distinct().Count(), Is.EqualTo(55));
    }

    [Test]
    public void EveryKeywordTokenType_IsClassifiedAsAKeyword()
    {
        foreach (TokenType type in ReservedWords.Keywords.Values)
        {
            Assert.That(type.IsKeyword(), Is.True, $"{type} should classify as a keyword");
        }
    }

    [Test]
    public void EveryKeywordTokenType_RoundTripsThroughItsCanonicalText()
    {
        foreach ((string word, TokenType type) in ReservedWords.Keywords)
        {
            Assert.That(type.Text(), Is.EqualTo(word));
        }
    }

    [TestCaseSource(nameof(ExpectedWords))]
    public void ReservedWord_ScansAsItsKeywordToken(string word)
    {
        Token token = ScanSingle(word);

        Assert.Multiple(() =>
        {
            Assert.That(token.Type, Is.EqualTo(ReservedWords.Keywords[word]));
            Assert.That(token.Lexeme, Is.EqualTo(word));
            Assert.That(token.Type.IsKeyword(), Is.True);
        });
    }

    [TestCaseSource(nameof(NonReservedWords))]
    public void NonReservedWord_ScansAsAnIdentifier(string word)
    {
        Token token = ScanSingle(word);

        Assert.Multiple(() =>
        {
            Assert.That(token.Type, Is.EqualTo(TokenType.Identifier));
            Assert.That(token.Lexeme, Is.EqualTo(word));
        });
    }

    [Test]
    public void EveryReservedWordIsInTheKeywordTable()
    {
        // A comment is marked rather than named, so it reserves nothing: the table is the
        // whole list, and a word absent from it is a word a program may use.
        Assert.Multiple(() =>
        {
            Assert.That(ReservedWords.IsReserved("comment"), Is.False);
            Assert.That(ReservedWords.IsReserved("commentary"), Is.False);
            Assert.That(ReservedWords.IsReserved("model"), Is.True);
            Assert.That(ReservedWords.IsReserved("begin"), Is.True, "begin still opens a block");
        });
    }

    [Test]
    public void KeywordMatching_IsCaseSensitive()
    {
        Assert.That(ScanSingle("Model").Type, Is.EqualTo(TokenType.Identifier));
        Assert.That(ScanSingle("model").Type, Is.EqualTo(TokenType.Model));
    }
}
