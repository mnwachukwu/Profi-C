using System.Text.RegularExpressions;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Lexing;
using ProfiC.Compiler.Text;

namespace ProfiC.Tests;

/// <summary>
/// <para>The prose documents' own links, and the Profi-C written in them.</para>
/// <para><c>side-by-side.md</c> writes every construct out twice, which makes it the part of the
/// documentation most exposed to a change in the language — and the least likely to be noticed,
/// since nothing runs it. A stale row survived exactly that way: the loop comparison still read
/// <c>for i = 0 until n</c> after every loop gained its <c>loop</c>.</para>
/// <para>The specification has checks of its own in <c>SpecificationLinkTests</c>; these two are
/// held to the same standard.</para>
/// </summary>
[TestFixture]
public sealed class SummaryLinkTests : LexerTestBase
{
    /// <summary>The prose documents, each of which carries Profi-C and links to itself.</summary>
    public static IEnumerable<string> Documents => ["language-summary.md", "side-by-side.md"];

    private static string[] LinesOf(string document) =>
        File.ReadAllLines(System.IO.Path.Combine(RepositoryRoot, "docs", document));

    /// <summary>
    /// How GitHub names a heading: lowercased, everything but letters, digits, spaces and
    /// hyphens dropped, then spaces to hyphens.
    /// </summary>
    private static string AnchorOf(string heading) =>
        Regex.Replace(heading.ToLowerInvariant(), @"[^\p{L}\p{N} \-]", string.Empty)
             .Trim()
             .Replace(' ', '-');

    /// <summary>
    /// Every link a document makes to itself lands on a heading, so the contents and the
    /// cross-references are both clickable rather than merely present.
    /// </summary>
    [TestCaseSource(nameof(Documents))]
    public void EveryLinkIntoTheDocumentLandsOnAHeading(string document)
    {
        string[] Lines = LinesOf(document);
        HashSet<string> anchors = new(StringComparer.Ordinal);
        List<string> broken = [];
        bool fenced = false;

        foreach (string line in Lines)
        {
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                fenced = !fenced;
            }
            else if (!fenced && Regex.Match(line, @"^#{2,4} (.+)$") is { Success: true } heading)
            {
                anchors.Add(AnchorOf(heading.Groups[1].Value));
            }
        }

        fenced = false;
        int number = 0;

        foreach (string line in Lines)
        {
            number++;

            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                fenced = !fenced;
                continue;
            }

            if (fenced)
            {
                continue;
            }

            foreach (Match link in Regex.Matches(line, @"\]\(#([^)]+)\)"))
            {
                if (!anchors.Contains(link.Groups[1].Value))
                {
                    broken.Add($"line {number}: #{link.Groups[1].Value}");
                }
            }
        }

        Assert.That(broken, Is.Empty, $"links in {document} pointing at no heading in it");
    }

    /// <summary>
    /// <para>Every Profi-C fence scans without a lexical diagnostic.</para>
    /// <para>The snippets are fragments and most would not parse as programs, which is what
    /// they are for — but a fragment made of tokens the language does not have is wrong however
    /// little context it carries. Lexing is the strongest check that survives the fragments
    /// being fragments, and it has already caught a comment example whose own prose closed it
    /// early.</para>
    /// </summary>
    [TestCaseSource(nameof(Documents))]
    public void EveryProfiCSnippetScansCleanly(string document)
    {
        List<string> complaints = [];

        foreach ((string snippet, int at) in ProfiCFences(document))
        {
            DiagnosticBag diagnostics = new();
            _ = new Lexer(new SourceText(snippet, document), diagnostics).Scan();

            foreach (Diagnostic diagnostic in diagnostics)
            {
                complaints.Add($"line {at}: {diagnostic.Id}: {diagnostic.Message}");
            }
        }

        Assert.That(complaints, Is.Empty, $"Profi-C in {document} that does not scan");
    }

    /// <summary>
    /// <para>No snippet uses a spelling the language has dropped.</para>
    /// <para>Scanning cannot catch this: <c>for i = 1 to 10</c> is made entirely of words the
    /// language still has, and reads as a loop to anyone who does not know the opener is now
    /// required. This is the check that would have caught the stale comparison row.</para>
    /// </summary>
    [TestCaseSource(nameof(Documents))]
    public void NoSnippetUsesASpellingTheLanguageDropped(string document)
    {
        (string Pattern, string Says)[] gone =
        [
            (@"\bend for\b", "'end for' — every loop closes with 'end loop'"),
            (@"\bend while\b", "'end while' — every loop closes with 'end loop'"),
            (@"(?m)^\s*for\s+[A-Za-z@]", "a 'for' with no 'loop' in front of it"),
            (@"(?m)^\s*while\s+[A-Za-z@]", "a 'while' with no 'loop' in front of it"),
            (@"(?m)^\s*for each\b", "'for each' — the walking loop is 'loop each'"),
            (@"\bstep\s+-?[0-9]", "'step' — the range loop's third clause is 'stepby'"),
            (@"\bglobal\s+(model|function|integer|string)\b", "'global' — the word is 'shared'"),
        ];

        List<string> complaints = [];

        foreach ((string snippet, int at) in ProfiCFences(document))
        {
            foreach ((string pattern, string says) in gone)
            {
                if (Regex.IsMatch(snippet, pattern))
                {
                    complaints.Add($"line {at}: {says}");
                }
            }
        }

        Assert.That(complaints, Is.Empty, $"{document} writes syntax the language dropped");
    }

    /// <summary>
    /// <para>Every fenced block that is Profi-C rather than C#, with the line it opens on.</para>
    /// <para>Told apart by the fence's language tag: the C# blocks carry <c>csharp</c> and the
    /// Profi-C ones carry nothing, since no highlighter knows the language yet.</para>
    /// </summary>
    private static IEnumerable<(string Snippet, int At)> ProfiCFences(string document)
    {
        List<string> current = [];
        bool inside = false;
        bool profiC = false;
        int opened = 0;
        int number = 0;

        foreach (string line in LinesOf(document))
        {
            number++;

            if (!line.StartsWith("```", StringComparison.Ordinal))
            {
                if (inside && profiC)
                {
                    current.Add(line);
                }

                continue;
            }

            if (!inside)
            {
                inside = true;
                profiC = line.Trim() == "```";
                opened = number;
                current.Clear();
                continue;
            }

            inside = false;

            if (profiC && current.Count > 0)
            {
                yield return (string.Join("\n", current), opened);
            }
        }
    }
}
