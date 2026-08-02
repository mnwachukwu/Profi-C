using ProfiC.Compiler.Lexing;
using ProfiC.Compiler.Semantics;

namespace ProfiC.Tests;

/// <summary>
/// <para>The published vocabulary agrees with the compiler that publishes it.</para>
/// <para><c>docs/vocabulary.json</c> is how tooling outside this repository learns what the
/// language reserves — the editor grammar in <c>Profi-C.Editors</c> is checked against it, and a
/// language server will be. That makes it the one file here whose staleness is invisible from
/// here: a keyword added without regenerating leaves the grammar correct against a list that is
/// wrong, and nothing in either repository fails.</para>
/// <para>So this is the seam. Regenerate with <c>pc vocabulary &gt; docs/vocabulary.json</c>.
/// </para>
/// </summary>
[TestFixture]
public sealed class VocabularyTests : LexerTestBase
{
    private static string Path =>
        System.IO.Path.Combine(RepositoryRoot, "docs", "vocabulary.json");

    [Test]
    public void ThePublishedVocabularyIsWhatTheCompilerWouldPrint() => Assert.That(
        File.ReadAllText(Path).ReplaceLineEndings("\n").TrimEnd(),
        Is.EqualTo(Vocabulary.AsJson().ReplaceLineEndings("\n").TrimEnd()),
        "docs/vocabulary.json has drifted; regenerate with 'pc vocabulary > docs/vocabulary.json'");

    /// <summary>
    /// And it really carries both halves. A file that serialized cleanly but held an empty list
    /// would satisfy the comparison above, since the compiler would print the same empty list.
    /// </summary>
    [Test]
    public void ThePublishedVocabularyCarriesEveryWordAndEveryType()
    {
        string published = File.ReadAllText(Path);

        string[] absent = [.. ReservedWords.Keywords.Keys
                                           .Concat(BuiltIns.AllTypeNames)
                                           .Where(word => !published.Contains(
                                               $"\"{word}\"", StringComparison.Ordinal))
                                           .OrderBy(word => word, StringComparer.Ordinal)];

        Assert.That(absent, Is.Empty, "words the language owns that the published file omits");
    }
}
