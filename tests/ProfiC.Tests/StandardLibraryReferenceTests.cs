using System.Text.RegularExpressions;
using ProfiC.Compiler.Semantics;

namespace ProfiC.Tests;

/// <summary>
/// <para>Holds <c>docs/standard-library/</c> to the catalog the compiler actually has.</para>
/// <para>A reference is the one document a reader trusts to be complete, which is exactly what
/// makes an incomplete one worse than none: a member missing from it is a member nobody learns
/// exists, and nothing anywhere fails. The same argument as
/// <see cref="DiagnosticsAppendixTests"/> and <see cref="ReadmeSampleTests"/>, applied to the
/// library.</para>
/// <para>The index on <c>README.md</c> is what is held, rather than the folder read as one body
/// of text. A name that appears only in a sentence somewhere is a name nobody looking it up will
/// find, so the claim worth making is that every member has a row.</para>
/// <para>Both directions are checked. A member the catalog has and the index does not is a gap; a
/// member the index has and the catalog does not is worse, since a reader would write it and be
/// told it does not exist.</para>
/// </summary>
[TestFixture]
public sealed class StandardLibraryReferenceTests : LexerTestBase
{
    private static string Folder => Path.Combine(RepositoryRoot, "docs", "standard-library");

    private static string Index() => File.ReadAllText(Path.Combine(Folder, "README.md"));

    /// <summary>
    /// <para>A member's name as the pages spell it, reduced to the part that names it.</para>
    /// <para>The same member is written several ways across a page — <c>Math.Sqrt(real x)</c> in a
    /// table, <c>Sqrt</c> in the section list, <c>someDateTime.Date</c> where a receiver is being
    /// shown. Everything before the last dot is a qualifier rather than the name, so it goes, and
    /// what is left is what two lists can be compared on.</para>
    /// </summary>
    private static string MemberName(string written)
    {
        string bare = written.Trim();

        if (bare.StartsWith("new ", StringComparison.Ordinal))
        {
            bare = bare[4..];
        }

        int opened = bare.IndexOf('(', StringComparison.Ordinal);
        if (opened >= 0)
        {
            bare = bare[..opened];
        }

        int dot = bare.LastIndexOf('.');

        return (dot >= 0 ? bare[(dot + 1)..] : bare).Trim();
    }

    /// <summary>Every backticked span in a line, unwrapped.</summary>
    private static IEnumerable<string> Backticked(string line) =>
        Regex.Matches(line, @"`([^`]+)`").Select(found => found.Groups[1].Value);

    /// <summary>
    /// <para>Every member a page documents, taken from the column its tables call
    /// <c>Member</c>.</para>
    /// <para>Only that column, and only tables that have one. A page carries several tables that
    /// are not about members at all — the four number types, the conversion chart, what each
    /// exception is raised for — and reading a name out of those would be reading the wrong
    /// thing.</para>
    /// </summary>
    private static HashSet<string> Documented(string page)
    {
        HashSet<string> found = new(StringComparer.Ordinal);
        List<int> columns = [];

        foreach (string line in page.ReplaceLineEndings("\n").Split('\n'))
        {
            if (!line.TrimStart().StartsWith('|'))
            {
                columns.Clear();
                continue;
            }

            string[] cells = [.. line.Trim().Trim('|').Split('|')];

            // A header row names the columns; the divider under it is skipped, and every row
            // after it is read against whatever the header said.
            if (cells.Any(cell => cell.Trim() is "Member" or "Members"))
            {
                columns =
                    [.. cells.Index()
                             .Where(cell => cell.Item.Trim() is "Member" or "Members")
                             .Select(cell => cell.Index)];
                continue;
            }

            foreach (int column in columns.Where(column => column < cells.Length))
            {
                foreach (string written in Backticked(cells[column]))
                {
                    found.Add(MemberName(written));
                }
            }
        }

        return found;
    }

    /// <summary>
    /// <para>Each page's own list of sections names every member that page documents.</para>
    /// <para>That list is the first thing on a page and the only summary of it, so a member left
    /// out of it is one a reader scanning for it decides is not there. Four of the six ways to
    /// read a value out of text were listed and <c>ToFloat</c> and <c>ToCharacter</c> were not,
    /// for as long as it took to read the section under it.</para>
    /// <para>The index on <c>README.md</c> is a different list and is checked separately. A member
    /// can be in that one and missing here, which is exactly what happened.</para>
    /// </summary>
    [Test]
    public void EachPageListsTheMembersItDocuments()
    {
        List<string> missing = [];

        foreach (string path in Directory.EnumerateFiles(Folder, "*.md")
                                         .Where(p => !p.EndsWith("README.md", StringComparison.Ordinal))
                                         .OrderBy(p => p, StringComparer.Ordinal))
        {
            string page = File.ReadAllText(path);

            // The list of sections is the table whose columns are Section and Members. Read
            // whole, since which section a member is filed under is not what this is about.
            HashSet<string> listed = new(StringComparer.Ordinal);

            foreach (string line in page.ReplaceLineEndings("\n").Split('\n'))
            {
                if (Regex.IsMatch(line, @"^\|\s*\[[^\]]+\]\(#[^)]+\)\s*\|"))
                {
                    foreach (string written in Backticked(line[(line.IndexOf('|', 1) + 1)..]))
                    {
                        listed.Add(MemberName(written));
                    }
                }
            }

            if (listed.Count == 0)
            {
                continue;
            }

            missing.AddRange(
                Documented(page).Except(listed, StringComparer.Ordinal)
                                .Order(StringComparer.Ordinal)
                                .Select(name => $"{Path.GetFileName(path)}: {name}"));
        }

        Assert.That(missing, Is.Empty, "members a page documents and leaves out of its own list");
    }

    /// <summary>
    /// <para>Every member the catalog has, by name. Constructors included: <c>new Random(42)</c>
    /// is as much a thing a reader looks up as <c>Random.Next()</c> is.</para>
    /// <para>Types are left out on purpose: the pages write a parameter's name beside its type
    /// for reading, so matching whole signatures would be matching the prose rather than the
    /// fact.</para>
    /// </summary>
    private static IEnumerable<string> Cataloged()
    {
        SetType numbers = new(PrimitiveType.Integer);
        SetType maybes = new(new OptionalType(PrimitiveType.Integer));

        IEnumerable<BuiltInMember> members =
        [
            .. BuiltIns.OnEveryType(),
            .. BuiltIns.OnSet(numbers),
            .. BuiltIns.OnSet(maybes),
            .. BuiltIns.OnString(),
            .. BuiltIns.OnOptional(new OptionalType(PrimitiveType.Integer)),
            .. BuiltIns.OnInteger(),
            .. BuiltIns.OnFraction(),
            .. BuiltIns.OnReal(),
            .. BuiltIns.OnFloat(),
            .. BuiltIns.OnEnumeration(),
            .. BuiltIns.OnException(),
            .. BuiltIns.Models.SelectMany(model => model.Members),
            .. BuiltIns.Models.SelectMany(model => model.Constructors),
        ];

        return members.Select(member => member.Name).Distinct(StringComparer.Ordinal);
    }

    /// <summary>
    /// <para>Every name the A to Z index claims, read out of the first column.</para>
    /// <para>Only that column: the others name the types a member sits on, which are not members
    /// and would let a row claim anything. A cell holds one or more backticked forms —
    /// <c>`Log(x)` · `Log(x, base)`</c> — and each contributes the name it starts with, past a
    /// <c>new</c> where the form is a constructor.</para>
    /// </summary>
    private static IReadOnlyCollection<string> Indexed()
    {
        string index = Index();
        int start = index.IndexOf("## Every member", StringComparison.Ordinal);

        Assert.That(start, Is.GreaterThanOrEqualTo(0), "README.md has no A to Z index");

        int end = index.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        string table = end < 0 ? index[start..] : index[start..end];

        HashSet<string> named = new(StringComparer.Ordinal);

        foreach (string line in table.Split('\n').Where(line => line.StartsWith("| ", StringComparison.Ordinal)))
        {
            string first = line.Split('|')[1];

            foreach (Match form in Regex.Matches(first, @"`(?:new\s+)?([A-Za-z][A-Za-z0-9]*)"))
            {
                named.Add(form.Groups[1].Value);
            }
        }

        return named;
    }

    /// <summary>Every member the language provides has a row a reader can find it by.</summary>
    [Test]
    public void EveryMemberTheCatalogHasIsInTheIndex()
    {
        IReadOnlyCollection<string> indexed = Indexed();

        Assert.That(
            Cataloged().Where(name => !indexed.Contains(name)),
            Is.Empty,
            "members the compiler provides that the index on README.md never lists");
    }

    /// <summary>
    /// Nothing is listed that the language does not have. The worse direction of the two: a
    /// reader would write it and be told it does not exist.
    /// </summary>
    [Test]
    public void EveryRowInTheIndexNamesAMemberTheLanguageHas()
    {
        HashSet<string> cataloged = new(Cataloged(), StringComparer.Ordinal);

        Assert.That(
            Indexed().Where(name => !cataloged.Contains(name)),
            Is.Empty,
            "names the index lists that the compiler does not provide");
    }

    /// <summary>
    /// <para>Every name the language owns is on the map.</para>
    /// <para>The members have an index of their own; this is the other half, and the half a
    /// reader usually arrives with — they met a name in a diagnostic or in somebody else's
    /// program and want to know what it is. A name the compiler protects and the map never shows
    /// is a name nothing explains.</para>
    /// <para>Matched as a code span rather than as a bare word, so that a name is counted found
    /// only where it is written as the language writes it.</para>
    /// </summary>
    [Test]
    public void EveryTypeTheLanguageOwnsIsOnTheMap()
    {
        string index = Index();

        Assert.That(
            BuiltIns.AllTypeNames.Where(
                name => !Regex.IsMatch(index, $"`{Regex.Escape(name)}[`.]")),
            Is.Empty,
            "type names the language owns that README.md never shows");
    }

    /// <summary>
    /// <para>Every page is reachable from the index.</para>
    /// <para>A page nothing links to is a page nobody opens, which is the same silence as a
    /// member nothing documents.</para>
    /// </summary>
    [Test]
    public void EveryPageIsLinkedFromTheIndex()
    {
        string index = Index();

        IEnumerable<string> pages = Directory.EnumerateFiles(Folder, "*.md")
                                             .Select(Path.GetFileName)
                                             .Where(name => name != "README.md")!;

        Assert.Multiple(() =>
        {
            foreach (string page in pages)
            {
                Assert.That(index, Does.Contain($"]({page})"), $"{page} is linked from nowhere");
            }
        });
    }

    /// <summary>
    /// <para>Every link between the pages lands — on a file that is there, and where it names
    /// one, on a heading that is there too.</para>
    /// <para>The second half is what an index of a hundred and thirty members is worth. A row
    /// pointing at a section that has been renamed drops the reader at the top of a page to hunt,
    /// which is the state this whole reference exists to end, and nothing about it looks broken
    /// from the row.</para>
    /// <para>Only links inside this folder are checked. A link out of it is somebody else's file
    /// to move, and the specification's own link tests cover those.</para>
    /// </summary>
    [Test]
    public void EveryLinkBetweenThePagesLands()
    {
        Assert.Multiple(() =>
        {
            foreach (string path in Directory.EnumerateFiles(Folder, "*.md"))
            {
                string from = Path.GetFileName(path);

                foreach (Match link in Regex.Matches(File.ReadAllText(path), @"\]\(([^)]+)\)"))
                {
                    string target = link.Groups[1].Value;

                    if (target.StartsWith("../", StringComparison.Ordinal)
                        || target.StartsWith("http", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string[] halves = target.Split('#');
                    string page = halves[0].Length == 0 ? from : halves[0];

                    if (!File.Exists(Path.Combine(Folder, page)))
                    {
                        Assert.Fail($"{from} links to {page}, which is not there");
                        continue;
                    }

                    if (halves.Length > 1)
                    {
                        Assert.That(
                            AnchorsIn(page),
                            Does.Contain(halves[1]),
                            $"{from} links to {target}, and {page} has no such heading");
                    }
                }
            }
        });
    }

    /// <summary>
    /// <para>Everywhere a link can land in a page: a heading, or an anchor written by hand where
    /// a heading's own name would have read badly.</para>
    /// <para>Headings are slugged the way GitHub slugs them — lowered, punctuation dropped,
    /// spaces hyphenated — so that what passes here is what resolves in a browser.</para>
    /// </summary>
    private static IReadOnlyCollection<string> AnchorsIn(string page)
    {
        string text = File.ReadAllText(Path.Combine(Folder, page));

        IEnumerable<string> headings = Regex.Matches(text, @"^#{1,6} (.+)$", RegexOptions.Multiline)
                                            .Select(heading => Slug(heading.Groups[1].Value));

        IEnumerable<string> written = Regex.Matches(text, @"<a id=""([^""]+)""")
                                           .Select(anchor => anchor.Groups[1].Value);

        return [.. headings, .. written];
    }

    private static string Slug(string heading) =>
        string.Concat(
            heading.Trim()
                   .ToLowerInvariant()
                   .Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_'))
              .Replace(' ', '-');

    /// <summary>
    /// <para>The specification points at the reference rather than repeating it.</para>
    /// <para>Section 11 summarizes and links; the pages carry the members. Two copies of a member
    /// list is two answers about it, and the second is always the one that goes stale.</para>
    /// </summary>
    [Test]
    public void TheSpecificationPointsAtTheReference() =>
        Assert.That(
            File.ReadAllText(Path.Combine(RepositoryRoot, "docs", "language-spec.md")),
            Does.Contain("standard-library/README.md"));
}
