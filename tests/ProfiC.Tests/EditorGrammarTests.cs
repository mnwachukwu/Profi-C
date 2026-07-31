using System.Text.Json;
using System.Text.RegularExpressions;
using ProfiC.Compiler.Lexing;
using ProfiC.Compiler.Semantics;

namespace ProfiC.Tests;

/// <summary>
/// <para>Holds the editor grammar to what the compiler actually reads.</para>
/// <para>A TextMate grammar is a second, hand-written description of the same language, and
/// nothing about adding a keyword makes it change too. The word lists have drifted the moment
/// one is added and the other is not, and the only sign is a word that stops being coloured —
/// which nobody notices, because a missing colour looks like an ordinary name.</para>
/// </summary>
[TestFixture]
public sealed class EditorGrammarTests : LexerTestBase
{
    private static string GrammarPath =>
        Path.Combine(RepositoryRootForTests, "editors", "vscode", "syntaxes", "profi-c.tmLanguage.json");

    private static JsonDocument Grammar() =>
        JsonDocument.Parse(File.ReadAllText(GrammarPath));

    /// <summary>
    /// Every word in every <c>match</c> the grammar holds, taken from the alternations they are
    /// written as. Reading the file rather than a list kept beside it, since a list beside it
    /// would be a third thing to drift.
    /// </summary>
    private static HashSet<string> WordsInGrammar()
    {
        HashSet<string> words = new(StringComparer.Ordinal);
        Collect(Grammar().RootElement, words);
        return words;

        static void Collect(JsonElement element, HashSet<string> into)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (JsonProperty property in element.EnumerateObject())
                    {
                        if (property.NameEquals("match") || property.NameEquals("begin"))
                        {
                            foreach (Match found in Regex.Matches(
                                         property.Value.GetString() ?? string.Empty,
                                         @"[A-Za-z][A-Za-z]*"))
                            {
                                into.Add(found.Value);
                            }
                        }

                        Collect(property.Value, into);
                    }

                    break;

                case JsonValueKind.Array:
                    foreach (JsonElement item in element.EnumerateArray())
                    {
                        Collect(item, into);
                    }

                    break;
            }
        }
    }

    [Test]
    public void TheGrammarFileIsThere() =>
        Assert.That(File.Exists(GrammarPath), Is.True, GrammarPath);

    [Test]
    public void TheGrammarIsValidJson() =>
        Assert.DoesNotThrow(() => Grammar().Dispose());

    /// <summary>
    /// <para>Every pattern in the file is a working regular expression. A broken one does not
    /// stop the editor loading the grammar; it silently colours nothing.</para>
    /// <para>An <c>end</c> pattern is compiled behind its own <c>begin</c>, because that is the
    /// only context it ever runs in: the editor substitutes what <c>begin</c> captured before
    /// matching it, so a back-reference there is checked against the groups <c>begin</c>
    /// defines rather than against nothing.</para>
    /// </summary>
    [Test]
    public void EveryPatternCompiles()
    {
        List<string> broken = [];
        Walk(Grammar().RootElement);

        Assert.That(broken, Is.Empty);

        void Walk(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    string opener = element.TryGetProperty("begin", out JsonElement begin)
                                    && begin.ValueKind == JsonValueKind.String
                        ? begin.GetString()!
                        : string.Empty;

                    foreach (JsonProperty property in element.EnumerateObject())
                    {
                        if (property.Value.ValueKind == JsonValueKind.String
                            && property.Name is "match" or "begin" or "end")
                        {
                            string pattern = property.Name == "end"
                                ? opener + property.Value.GetString()
                                : property.Value.GetString()!;

                            try
                            {
                                _ = new Regex(pattern);
                            }
                            catch (ArgumentException problem)
                            {
                                broken.Add($"{property.Value.GetString()}: {problem.Message}");
                            }
                        }

                        Walk(property.Value);
                    }

                    break;

                case JsonValueKind.Array:
                    foreach (JsonElement item in element.EnumerateArray())
                    {
                        Walk(item);
                    }

                    break;
            }
        }
    }

    /// <summary>
    /// <para>The block comment rule reads a block the way the scanner does.</para>
    /// <para>The closer takes the rest of its line, which is the rule the whole comment design
    /// rests on, and an editor that ends the block at the marks instead would colour the tail
    /// of that line as code. Checked against the same awkward lines the scanner is.</para>
    /// </summary>
    [TestCase("##", false, TestName = "an opener alone stays open")]
    [TestCase("## text ##", true, TestName = "a block on one line closes")]
    [TestCase("####", true, TestName = "four marks are a whole comment")]
    [TestCase("#########", true, TestName = "a heading run is a whole comment")]
    [TestCase("## and then some", false, TestName = "an opener with text stays open")]
    public void TheBlockRuleClosesWhereTheScannerDoes(string line, bool closes)
    {
        JsonElement block = Grammar().RootElement
                                     .GetProperty("repository")
                                     .GetProperty("comment-block");

        Match opened = new Regex(block.GetProperty("begin").GetString()!).Match(line);
        Assert.That(opened.Success, Is.True, "the line opens a block");

        string rest = line[(opened.Index + opened.Length)..];

        Assert.That(
            new Regex(block.GetProperty("end").GetString()!).IsMatch(rest),
            Is.EqualTo(closes));
    }

    /// <summary>
    /// The block rule is offered before the line rule, or <c>##</c> scans as two line comments
    /// and a block spanning lines is never recognized at all.
    /// </summary>
    [Test]
    public void TheBlockRuleIsTriedBeforeTheLineRule()
    {
        string[] order = [.. Grammar().RootElement
                                      .GetProperty("patterns")
                                      .EnumerateArray()
                                      .Select(p => p.GetProperty("include").GetString()!)];

        Assert.That(
            Array.IndexOf(order, "#comment-block"),
            Is.LessThan(Array.IndexOf(order, "#comment-line")));
    }

    /// <summary>
    /// <para>Every reserved word appears somewhere in the grammar.</para>
    /// <para>This is the assertion that catches the drift: a word added to the language and
    /// not to the grammar fails here, at the moment it is added, rather than months later when
    /// somebody notices it is the wrong colour.</para>
    /// </summary>
    [Test]
    public void EveryReservedWordIsInTheGrammar()
    {
        HashSet<string> words = WordsInGrammar();

        string[] missing = [.. ReservedWords.Keywords.Keys
                                            .Where(word => !words.Contains(word))
                                            .OrderBy(word => word, StringComparer.Ordinal)];

        Assert.That(missing, Is.Empty, "reserved words the editor grammar does not colour");
    }

    /// <summary>
    /// And every type the language provides, which the scanner reads as an ordinary name and
    /// so cannot be caught by the word list above.
    /// </summary>
    [Test]
    public void EveryTypeTheLanguageProvidesIsInTheGrammar()
    {
        HashSet<string> words = WordsInGrammar();

        string[] missing = [.. BuiltIns.AllTypeNames
                                       .Where(name => !words.Contains(name))
                                       .OrderBy(name => name, StringComparer.Ordinal)];

        Assert.That(missing, Is.Empty, "types the editor grammar does not colour");
    }

    /// <summary>
    /// Nothing in the grammar's word lists is a word the language dropped. Catches the other
    /// direction of the same drift, where a keyword is removed and its colour outlives it.
    /// </summary>
    [Test]
    public void TheGrammarColoursNoWordTheLanguageDropped()
    {
        // Read from the keyword patterns alone: the rest of the file is scope names and
        // regular-expression machinery, and neither is a claim about the language.
        JsonElement repository = Grammar().RootElement.GetProperty("repository");
        HashSet<string> claimed = new(StringComparer.Ordinal);

        foreach (string section in new[] { "keyword", "type", "constant" })
        {
            foreach (Match found in Regex.Matches(
                         repository.GetProperty(section).GetRawText(),
                         @"\\\\b\(([a-z|]+)\)\\\\b"))
            {
                foreach (string word in found.Groups[1].Value.Split('|'))
                {
                    claimed.Add(word);
                }
            }
        }

        string[] unknown = [.. claimed
                                .Where(word => !ReservedWords.IsReserved(word))
                                .OrderBy(word => word, StringComparer.Ordinal)];

        Assert.That(unknown, Is.Empty, "words the grammar colours that the language does not reserve");
    }
}
