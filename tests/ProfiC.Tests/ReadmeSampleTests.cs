namespace ProfiC.Tests;

/// <summary>
/// <para>Holds the README's sample tables to the samples that are actually there.</para>
/// <para>Each table is a second, hand-written list of a folder, and adding a file does nothing
/// to it. The drift is quiet: nothing fails, the sample is still run and still asserted on, and
/// the only sign is a reader who never learns it exists. A catalog that is silently incomplete
/// is worse than none, because it reads as though it were complete.</para>
/// <para>The same argument as <see cref="DiagnosticsAppendixTests"/> and
/// <see cref="EditorGrammarTests"/>, applied to the front page.</para>
/// </summary>
[TestFixture]
public sealed class ReadmeSampleTests : LexerTestBase
{
    /// <summary>
    /// Only table rows count. A sample named in passing prose is mentioned, not cataloged, and
    /// a reader scanning the tables for what to read next will not find it.
    /// </summary>
    private static string TableRows() =>
        string.Join(
            "\n",
            File.ReadAllLines(Path.Combine(RepositoryRoot, "README.md"))
                .Where(line => line.TrimStart().StartsWith('|')));

    /// <summary>
    /// The files a folder holds, without descending. A sample lives at the top of its folder;
    /// anything deeper is support for one, which the tables deliberately do not list.
    /// </summary>
    private static IEnumerable<string> SamplesIn(string folder, string pattern)
    {
        string[] parts = [RepositoryRoot, "samples", .. folder.Split('/')];

        return Directory.EnumerateFiles(Path.Combine(parts), pattern)
                        .Select(Path.GetFileName)
                        .OrderBy(name => name, StringComparer.Ordinal)!;
    }

    public static IEnumerable<TestCaseData> Folders =>
    [
        new TestCaseData("", "*.pc").SetName("the programs"),
        new TestCaseData("reference", "*.pc").SetName("the reference corpus"),
        new TestCaseData("negatives/compile", "*.pc").SetName("programs that do not compile"),
        new TestCaseData("negatives/runtime", "*.pc").SetName("programs that fail while running"),
        new TestCaseData("negatives/project", "*.pcp").SetName("projects that do not build"),
    ];

    /// <summary>
    /// <para>Every sample is named in the README, and every sample the README names is there.
    /// </para>
    /// <para>The reference corpus is described in a sentence rather than a table, since four
    /// files that are not programs do not want a column saying what each is for. It is checked
    /// against the whole file for that reason.</para>
    /// </summary>
    [TestCaseSource(nameof(Folders))]
    public void EverySampleIsNamedInTheReadme(string folder, string pattern)
    {
        string readme = folder == "reference"
            ? File.ReadAllText(Path.Combine(RepositoryRoot, "README.md"))
            : TableRows();

        string prefix = folder.Length == 0 ? "samples/" : $"samples/{folder}/";

        Assert.That(
            SamplesIn(folder, pattern).Where(name => !readme.Contains($"{prefix}{name}",
                                                                      StringComparison.Ordinal)),
            Is.Empty,
            $"samples under {prefix} that the README does not name");
    }

    /// <summary>
    /// The other direction. A sample renamed or removed leaves a row pointing at nothing, which
    /// reads as a broken link rather than as an omission and is just as quiet.
    /// </summary>
    [Test]
    public void EveryReadmeSampleLinkPointsAtAFile()
    {
        string readme = File.ReadAllText(Path.Combine(RepositoryRoot, "README.md"));

        IEnumerable<string> broken = System.Text.RegularExpressions.Regex
            .Matches(readme, @"\((samples/[^)#]+\.(?:pc|pcp))\)")
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .Where(relative => !File.Exists(
                Path.Combine(RepositoryRoot, relative.Replace('/', Path.DirectorySeparatorChar))))
            .Order(StringComparer.Ordinal);

        Assert.That(broken, Is.Empty, "README links naming a sample that is not there");
    }
}
