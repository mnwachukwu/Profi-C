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

    /// <summary>
    /// <para>The directive rule sets apart exactly the comments the compiler heeds.</para>
    /// <para>Two ways to be wrong, and the second is the one that matters. Missing a real
    /// directive leaves it looking like prose, which is untidy. Colouring a comment that is
    /// <i>not</i> one tells a reader the compiler is acting on a sentence it is ignoring, and
    /// they would have no way to find out otherwise. So the rows below are the scanner's own
    /// edges: the word must come first, the target must be whole, and a near miss is prose.
    /// </para>
    /// </summary>
    [TestCase("# ignore warning", true)]
    [TestCase("# ignore opinion", true)]
    [TestCase("# ignore PC0340", true)]
    [TestCase("# ignore pc0340", true, TestName = "an identifier is read whatever its case")]
    [TestCase("# ignore opinion in file", true)]
    [TestCase("# ignore opinion because the blank line is the point", true)]
    [TestCase("#ignore opinion", true, TestName = "no space after the mark")]
    [TestCase("Console.WriteLine(x); # ignore opinion", true, TestName = "after code on the line")]
    [TestCase("# ignore opinions", false, TestName = "a near miss is prose")]
    [TestCase("# ignore PC03400", false, TestName = "an identifier is four digits")]
    [TestCase("# ignore the sign for now", false)]
    [TestCase("# ignore", false, TestName = "the target is never absent")]
    [TestCase("# please ignore opinion", false, TestName = "the word comes first or not at all")]
    [TestCase("# a remark", false)]
    public void TheDirectiveRuleMatchesWhatTheScannerReads(string line, bool directive)
    {
        string pattern = Grammar().RootElement
                                  .GetProperty("repository")
                                  .GetProperty("comment-directive")
                                  .GetProperty("match")
                                  .GetString()!;

        Assert.That(new Regex(pattern).IsMatch(line), Is.EqualTo(directive));
    }

    /// <summary>
    /// The directive rule is offered before the ordinary line rule, which would otherwise take
    /// every comment first and leave the directive rule matching nothing at all.
    /// </summary>
    [Test]
    public void TheDirectiveRuleIsTriedBeforeTheLineRule()
    {
        string[] order = [.. Grammar().RootElement
                                      .GetProperty("patterns")
                                      .EnumerateArray()
                                      .Select(p => p.GetProperty("include").GetString()!)];

        Assert.That(
            Array.IndexOf(order, "#comment-directive"),
            Is.LessThan(Array.IndexOf(order, "#comment-line")));
    }

    /// <summary>
    /// <para>The colour is the extension's, since a grammar names meaning and a theme chooses
    /// what it looks like.</para>
    /// <para>A directive is addressed to the compiler rather than to a reader, so it is set
    /// apart from the prose around it rather than sharing its colour. Shipped as a default, so
    /// anyone who disagrees overrides it the ordinary way.</para>
    /// </summary>
    [Test]
    public void TheExtensionGivesTheDirectiveItsOwnColour()
    {
        using JsonDocument manifest = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(
                RepositoryRoot, "editors", "vscode", "package.json")));

        string[] scopes = [.. manifest.RootElement
            .GetProperty("contributes")
            .GetProperty("configurationDefaults")
            .GetProperty("editor.tokenColorCustomizations")
            .GetProperty("textMateRules")
            .EnumerateArray()
            .Select(rule => rule.GetProperty("scope").GetString()!)];

        Assert.That(scopes, Does.Contain("comment.line.number-sign.directive.profi-c"));
    }

    /// <summary>
    /// <para>The label rule picks out what the scanner reads as a label, and nothing else.</para>
    /// <para>The mark is the whole of it. Colouring a wrapped line beginning with a word and a
    /// colon would tell a reader the compiler is acting on prose it is passing over, and they
    /// would have no way to find out otherwise.</para>
    /// </summary>
    [TestCase("@summary: A thing.", true)]
    [TestCase("@remarks: At greater length.", true)]
    [TestCase("@n: how many terms.", true)]
    [TestCase("@yields: the total.", true)]
    [TestCase("optional: nothing more to read is an answer.", false, TestName = "wrapped prose")]
    [TestCase("summary: without the mark", false, TestName = "the mark is required")]
    [TestCase("@ n: a space after the mark", false)]
    [TestCase("@1st: a name cannot begin with a digit", false)]
    public void TheLabelRuleMatchesWhatTheScannerReads(string line, bool label)
    {
        string pattern = Grammar().RootElement
                                  .GetProperty("repository")
                                  .GetProperty("documentation-label")
                                  .GetProperty("match")
                                  .GetString()!;

        Assert.That(new Regex(pattern).IsMatch(line), Is.EqualTo(label));
    }

    /// <summary>
    /// Both comment forms carry labels, since one line is enough to document something and a
    /// block is what a longer one needs. A rule reaching only into blocks would leave the line
    /// form looking like prose while the compiler read it as documentation.
    /// </summary>
    [TestCase("comment-block")]
    [TestCase("comment-line")]
    public void BothCommentFormsReachTheLabelRule(string rule)
    {
        JsonElement comment = Grammar().RootElement
                                       .GetProperty("repository")
                                       .GetProperty(rule);

        Assert.That(
            comment.GetRawText(),
            Does.Contain("#documentation-label"),
            $"{rule} should pick out documentation labels");
    }
}
