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
/// <para>Both directions are checked. A member the catalog has and the pages do not is a gap; a
/// member the pages have and the catalog does not is worse, since a reader would write it and be
/// told it does not exist.</para>
/// </summary>
[TestFixture]
public sealed class StandardLibraryReferenceTests : LexerTestBase
{
    private static string Folder => Path.Combine(RepositoryRoot, "docs", "standard-library");

    /// <summary>Every page, read as one body of text. Which page a member is on is not the claim.</summary>
    private static string Reference() =>
        string.Join(
            "\n",
            Directory.EnumerateFiles(Folder, "*.md")
                     .OrderBy(path => path, StringComparer.Ordinal)
                     .Select(File.ReadAllText));

    /// <summary>
    /// Every member the catalog has, by name. Types are left out on purpose: the pages write a
    /// parameter's name beside its type for reading, so matching whole signatures would be
    /// matching the prose rather than the fact.
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
            .. BuiltIns.OnEnumeration(),
            .. BuiltIns.OnException(),
            .. BuiltIns.Models.SelectMany(model => model.Members),
        ];

        return members.Select(member => member.Name).Distinct(StringComparer.Ordinal);
    }

    /// <summary>
    /// <para>Every member the language provides is written down.</para>
    /// <para>Searched for as a whole word, so that <c>Add</c> is not counted as found because
    /// <c>AddDays</c> is on the page — which is the failure this would otherwise have.</para>
    /// </summary>
    [Test]
    public void EveryMemberTheCatalogHasIsInTheReference()
    {
        string reference = Reference();

        Assert.That(
            Cataloged().Where(name => !Regex.IsMatch(reference, $@"\b{Regex.Escape(name)}\b")),
            Is.Empty,
            "members the compiler provides that docs/standard-library/ never names");
    }

    /// <summary>
    /// <para>Every page is reachable from the index.</para>
    /// <para>A page nothing links to is a page nobody opens, which is the same silence as a
    /// member nothing documents.</para>
    /// </summary>
    [Test]
    public void EveryPageIsLinkedFromTheIndex()
    {
        string index = File.ReadAllText(Path.Combine(Folder, "README.md"));

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
    /// <para>Every link between the pages points at a file that is there.</para>
    /// <para>Only links to other pages in this folder are checked. A link out of it is somebody
    /// else's file to move, and the specification's own link tests cover those.</para>
    /// </summary>
    [Test]
    public void EveryLinkBetweenThePagesLands()
    {
        Assert.Multiple(() =>
        {
            foreach (string path in Directory.EnumerateFiles(Folder, "*.md"))
            {
                foreach (Match link in Regex.Matches(File.ReadAllText(path), @"\]\((?!\.\./)([a-z-]+\.md)"))
                {
                    Assert.That(
                        File.Exists(Path.Combine(Folder, link.Groups[1].Value)),
                        Is.True,
                        $"{Path.GetFileName(path)} links to {link.Groups[1].Value}, which is not there");
                }
            }
        });
    }

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
