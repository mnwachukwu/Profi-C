using System.Text.RegularExpressions;

namespace ProfiC.Tests;

/// <summary>
/// <para>Keeps the specification's cross-references and its contents pointing at sections that
/// are really there.</para>
/// <para>A document that refers to itself by number rots the moment a section is renumbered or
/// renamed, and nothing about editing prose says so. <c>§4.7</c> pointed at nothing for as long
/// as it took somebody to follow it — which nobody did, because following a reference in a
/// document this size is work.</para>
/// </summary>
[TestFixture]
public sealed class SpecificationLinkTests : LexerTestBase
{
    private static string Path =>
        System.IO.Path.Combine(RepositoryRoot, "docs", "language-spec.md");

    private static string[] Lines => File.ReadAllLines(Path);

    /// <summary>
    /// How GitHub names a heading: lowercased, everything but letters, digits, spaces and
    /// hyphens dropped, then spaces to hyphens. A link is only clickable if it agrees.
    /// </summary>
    private static string AnchorOf(string heading) =>
        Regex.Replace(heading.ToLowerInvariant(), @"[^\p{L}\p{N} \-]", string.Empty)
             .Trim()
             .Replace(' ', '-');

    /// <summary>Every heading outside a fenced block, with the anchor it will answer to.</summary>
    private static Dictionary<string, string> Headings()
    {
        Dictionary<string, string> anchors = new(StringComparer.Ordinal);
        bool fenced = false;

        foreach (string line in Lines)
        {
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                fenced = !fenced;
                continue;
            }

            if (fenced || Regex.Match(line, @"^(#{2,4}) (.+)$") is not { Success: true } heading)
            {
                continue;
            }

            anchors[heading.Groups[2].Value.Trim()] = AnchorOf(heading.Groups[2].Value.Trim());
        }

        return anchors;
    }

    /// <summary>
    /// Every link the document makes to itself lands on a heading. Covers the contents and the
    /// cross-references together, since both are written the same way.
    /// </summary>
    [Test]
    public void EveryLinkIntoTheDocumentLandsOnAHeading()
    {
        HashSet<string> anchors = new(Headings().Values, StringComparer.Ordinal);
        List<string> broken = [];
        bool fenced = false;
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

        Assert.That(broken, Is.Empty, "links pointing at no heading in this document");
    }

    /// <summary>
    /// <para>No section reference is left as bare text.</para>
    /// <para>A reader following one by scrolling is doing by hand what a link does, so a bare
    /// one is a link somebody forgot rather than a style.</para>
    /// </summary>
    [Test]
    public void EverySectionReferenceIsALink()
    {
        List<string> bare = [];
        bool fenced = false;
        int number = 0;

        foreach (string line in Lines)
        {
            number++;

            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                fenced = !fenced;
                continue;
            }

            // A heading carries its own number and does not link to itself.
            if (fenced || line.StartsWith('#'))
            {
                continue;
            }

            // The links are taken out first rather than excluded by a lookahead. A pattern
            // that tries to skip them backtracks into one instead: offered "[§1.5](#…)" it
            // gives up on the whole reference and matches the "§1" inside it, then reports
            // that as bare. Removing them leaves nothing to backtrack into.
            string outsideLinks = Regex.Replace(line, @"\[§[\d.a-z]+\]\(#[^)]*\)", string.Empty);

            foreach (Match reference in Regex.Matches(outsideLinks, @"§\d+(?:\.\d+[a-z]?)?"))
            {
                bare.Add($"line {number}: {reference.Value}");
            }
        }

        Assert.That(bare, Is.Empty, "section references written as text rather than as links");
    }

    /// <summary>
    /// The contents list every section, in the order they appear. Written by hand it would
    /// drift the first time one was added; asserted, it cannot.
    /// </summary>
    [Test]
    public void TheContentsListEverySection()
    {
        string[] lines = Lines;

        int start = Array.FindIndex(lines, l => l.Trim() == "## Contents");
        Assert.That(start, Is.GreaterThanOrEqualTo(0), "the document has no contents");

        int end = start + 1;
        while (end < lines.Length && !lines[end].StartsWith("## ", StringComparison.Ordinal))
        {
            end++;
        }

        List<string> listed =
            [.. lines[start..end]
                .Select(l => Regex.Match(l, @"^\s*- \[(.+?)\]\(#"))
                .Where(m => m.Success)
                .Select(m => m.Groups[1].Value)];

        // Everything from the first top-level section on; what sits above it is front matter
        // about the document rather than part of it.
        List<string> expected = [];
        bool started = false;
        bool fenced = false;

        foreach (string line in lines)
        {
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                fenced = !fenced;
                continue;
            }

            if (fenced || Regex.Match(line, @"^(#{2,3}) (.+)$") is not { Success: true } heading)
            {
                continue;
            }

            string title = heading.Groups[2].Value.Trim();

            if (title == "Contents")
            {
                continue;
            }

            if (heading.Groups[1].Value.Length == 2)
            {
                started = true;
            }

            if (started)
            {
                expected.Add(title);
            }
        }

        Assert.That(listed, Is.EqualTo(expected));
    }
}
