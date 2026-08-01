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
    /// <summary>The 61 reserved words, written out rather than derived from the table.</summary>
    private static readonly string[] Expected =
    [
        "abstract", "and", "as", "base", "begin", "bitwise", "boolean", "break", "case", "catch",
        "character", "constant", "continue", "default", "delegate", "each", "else", "end",
        "enumeration",
        "extends", "false", "finally", "for", "fraction", "function", "if", "import", "in",
        "integer", "internal", "is", "let", "model", "namespace", "new", "not", "or",
        "override",
        "protected", "public", "real", "sealed", "shared", "shiftleft", "shiftright", "step",
        "string",
        "structure", "switch",
        "then", "this", "throw", "to", "true", "try", "until", "using", "virtual", "while",
        "xor", "yield",
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

        // 'shared' is the word for a member there is one of. 'global' is an ordinary name, and
        // is worth pinning as one: it reads like a modifier, so a program that uses it as a
        // variable must still lex as a variable.
        "global",
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
            Assert.That(diagnostics.Single().Severity, Is.EqualTo(DiagnosticSeverity.Opinion));
            Assert.That(tokens[0].Name, Is.EqualTo("total"), "and it still means that name");
        });
    }

    [Test]
    public void AnEscapeWithNoNameAfterItIsReported() =>
        Assert.That(ScanRaw("@ ").Diagnostics.Select(d => d.Id), Is.EqualTo(new[] { "PC0010" }));

    [Test]
    public void KeywordTable_ContainsExactlySixtyOneWords()
    {
        Assert.That(ReservedWords.Count, Is.EqualTo(61));
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
        Assert.That(ReservedWords.Keywords.Values.Distinct().Count(), Is.EqualTo(61));
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

    /// <summary>
    /// The table is the whole list. Nothing else reserves a word, so a word it does not hold
    /// is one a program may use, and <c>IsReserved</c> can only be asking the table.
    /// </summary>
    [Test]
    public void IsReservedAgreesWithTheKeywordTable()
    {
        Assert.Multiple(() =>
        {
            foreach (string word in Expected)
            {
                Assert.That(ReservedWords.IsReserved(word), Is.True, word);
            }

            foreach (string word in NotReserved)
            {
                Assert.That(ReservedWords.IsReserved(word), Is.False, word);
            }
        });
    }

    [Test]
    public void KeywordMatching_IsCaseSensitive()
    {
        Assert.That(ScanSingle("Model").Type, Is.EqualTo(TokenType.Identifier));
        Assert.That(ScanSingle("model").Type, Is.EqualTo(TokenType.Model));
    }

    // ---- What the summary tells a reader ---------------------------------------------

    /// <summary>
    /// <para>The list printed in the language summary is the list the compiler has.</para>
    /// <para>It is written out by hand, in a grid, so a word added to the language reaches it
    /// only if somebody remembers — and the grid has to be reflowed to stay square, which is
    /// the sort of edit that drops one.</para>
    /// </summary>
    [Test]
    public void TheSummaryListsEveryReservedWord()
    {
        string[] listed = WordsInTheSummary();

        Assert.Multiple(() =>
        {
            Assert.That(
                listed.Except(ReservedWords.Keywords.Keys),
                Is.Empty,
                "the summary lists words the language does not reserve");

            Assert.That(
                ReservedWords.Keywords.Keys.Except(listed),
                Is.Empty,
                "the language reserves words the summary does not list");
        });
    }

    /// <summary>And the heading counts them, since a reader takes the number on trust.</summary>
    [Test]
    public void TheSummarySaysHowManyThereAre() => Assert.That(
        File.ReadAllText(SummaryPath),
        Does.Contain($"### 1.1 The {ReservedWords.Count} reserved words"));

    /// <summary>
    /// <para>The specification prints the same list, and it is the one nothing was checking —
    /// which is how it came to be missing <c>delegate</c> for as long as that word existed.
    /// </para>
    /// <para>Two hand-written copies of one table drift independently, so both are held to it.
    /// </para>
    /// </summary>
    [Test]
    public void TheSpecificationListsEveryReservedWord()
    {
        string[] listed = WordsInTheFenceAfter(
            SpecificationPath, "These are every reserved word");

        Assert.Multiple(() =>
        {
            Assert.That(
                listed.Except(ReservedWords.Keywords.Keys),
                Is.Empty,
                "the specification lists words the language does not reserve");

            Assert.That(
                ReservedWords.Keywords.Keys.Except(listed),
                Is.Empty,
                "the language reserves words the specification does not list");
        });
    }

    /// <summary>
    /// And counts them, as the summary's heading does. The list and the number beside it are
    /// written separately, so a word added to one leaves the other saying something false —
    /// and a reader who counts a fence of sixty-one words is not the reader the sentence is
    /// there for.
    /// </summary>
    [Test]
    public void TheSpecificationSaysHowManyThereAre() => Assert.That(
        File.ReadAllText(SpecificationPath),
        Does.Contain($"Profi-C has **{ReservedWords.Count}** reserved words"));

    private static string SummaryPath =>
        Path.Combine(RepositoryRootForTests, "docs", "language-summary.md");

    private static string SpecificationPath =>
        Path.Combine(RepositoryRootForTests, "docs", "language-spec.md");

    /// <summary>
    /// The words in the fenced block that ends just before a given sentence. The specification
    /// names its list by what follows rather than by a heading, so that is what locates it.
    /// </summary>
    private static string[] WordsInTheFenceAfter(string path, string sentence)
    {
        string[] lines = File.ReadAllLines(path);

        int after = Array.FindIndex(
            lines, l => l.StartsWith(sentence, StringComparison.Ordinal));

        Assert.That(after, Is.GreaterThanOrEqualTo(0), $"{path} has no reserved-word list");

        int close = Array.FindLastIndex(
            lines, after, l => l.StartsWith("```", StringComparison.Ordinal));

        int open = Array.FindLastIndex(
            lines, close - 1, l => l.StartsWith("```", StringComparison.Ordinal));

        return [.. lines[(open + 1)..close]
            .SelectMany(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries))];
    }

    /// <summary>The words in the summary's fenced block, whatever shape the grid is in.</summary>
    private static string[] WordsInTheSummary()
    {
        string[] lines = File.ReadAllLines(SummaryPath);

        int heading = Array.FindIndex(
            lines, l => l.StartsWith("### 1.1 ", StringComparison.Ordinal));

        Assert.That(heading, Is.GreaterThanOrEqualTo(0), "the summary has no reserved-word list");

        int open = Array.FindIndex(lines, heading, l => l.StartsWith("```", StringComparison.Ordinal));
        int close = Array.FindIndex(lines, open + 1, l => l.StartsWith("```", StringComparison.Ordinal));

        return [.. lines[(open + 1)..close]
            .SelectMany(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries))];
    }
}
