namespace ProfiC.Tests;

/// <summary>
/// <para>Every word a project file may say, held against what the corpus writes.</para>
/// <para><b>The compiler keeps no list of these to check anything against.</b>
/// <see cref="ProfiC.Cli.ProjectFile"/> reads them as cases in a switch, so a word added to the
/// format is added in one place and is silently absent from every other — the specification's
/// prose, the editor's grammar, the negative that lists the vocabulary, and the samples. Each of
/// those had fallen behind at least once by the time <c>output</c> was added, and none of it
/// failed a test.</para>
/// <para>What is asked here is the narrowest useful version of that: a word the format has and no
/// sample writes is a word nothing demonstrates and nothing exercises end to end.</para>
/// </summary>
[TestFixture]
public sealed class ProjectVocabularyTests : LexerTestBase
{
    /// <summary>
    /// <para>The whole vocabulary, as <see cref="ProfiC.Cli.ProjectFile"/> reads it.</para>
    /// <para>Written out because there is nowhere to read it from. That is the gap this fixture
    /// exists inside rather than one it can close: a list here can go stale the same way the
    /// others did, and what keeps it honest is that adding a word to the reader without adding
    /// it here leaves this passing while the new word goes unwritten anywhere.</para>
    /// </summary>
    private static readonly string[] Vocabulary =
        ["project", "source", "reference", "entry", "output", "ignore", "end project"];

    /// <summary>
    /// <para>Every line of every project file in the corpus, the negatives included.</para>
    /// <para>The negatives count, because several words are best shown going wrong: an
    /// <c>ignore</c> naming nothing and an <c>output</c> that disagrees with itself are both
    /// demonstrations of the word, and a reader looking one up finds them.</para>
    /// </summary>
    private static IEnumerable<string> EveryProjectLine =>
        Directory.EnumerateFiles(
                     Path.Combine(RepositoryRoot, "samples"),
                     "*" + ProfiC.Cli.SourceDiscovery.ProjectExtension,
                     SearchOption.AllDirectories)
                 .SelectMany(File.ReadAllLines)
                 .Select(line => line.Trim());

    /// <summary>
    /// <para>Which words are written on a line of their own, rather than merely mentioned.</para>
    /// <para>Mentions do not count. Every project file here has prose in it, and
    /// <c>ambiguous-entry.pcp</c> names the fix it does not apply — so counting the word anywhere
    /// in the text would report <c>entry</c> as demonstrated by the one file that exists to show
    /// what happens without it.</para>
    /// </summary>
    private static HashSet<string> Written()
    {
        HashSet<string> written = [];

        foreach (string line in EveryProjectLine)
        {
            foreach (string word in Vocabulary)
            {
                if (line == word
                    || line.StartsWith(word + " ", StringComparison.Ordinal))
                {
                    written.Add(word);
                }
            }
        }

        return written;
    }

    /// <summary>
    /// <para>Every word a project file may say is written in some project file.</para>
    /// <para>Held with no exclusions. <c>entry</c> was the one word nothing wrote, and it could
    /// not simply be added to an existing project: writing one where the sources declare a single
    /// <c>Program</c> is <c>PC0236</c>, an opinion, and a positive sample may report nothing.
    /// Demonstrating it took a project holding two programs in different namespaces, which is
    /// what <c>samples/observatory</c> is — the positive counterpart to
    /// <c>ambiguous-entry.pcp</c>, which is that project with the line left out.</para>
    /// </summary>
    [Test]
    public void EveryWordAProjectMaySayIsWrittenSomewhere()
    {
        HashSet<string> written = Written();

        Assert.That(
            Vocabulary.Where(word => !written.Contains(word)).Order(StringComparer.Ordinal),
            Is.Empty,
            "words a project file may say that no project file in the corpus writes");
    }
}
