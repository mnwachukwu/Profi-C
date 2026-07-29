namespace ProfiC.Tests;

/// <summary>
/// <para>Checks that the recorded files and the samples still describe the same set.</para>
/// <para>The golden fixtures each compare one sample against one file, so neither notices a
/// file with no sample behind it. A sample that is renamed or deleted therefore leaves its
/// recording behind, where it goes on being checked in and read by people as though it were
/// current. Comparing the sets is the only place that can be caught.</para>
/// </summary>
[TestFixture]
public sealed class GoldenCoverageTests : LexerTestBase
{
    private static string[] Recorded(string directory, string extension) =>
        Directory.Exists(directory)
            ? [.. Directory.EnumerateFiles(directory, $"*{extension}")
                           .Select(Path.GetFileNameWithoutExtension)
                           .OrderBy(n => n, StringComparer.Ordinal)!]
            : [];

    private static string[] Expected(IEnumerable<string> names) =>
        [.. names.Select(Path.GetFileNameWithoutExtension).OrderBy(n => n, StringComparer.Ordinal)!];

    [Test]
    public void EveryRecordedTokenStreamHasASampleBehindIt() => Assert.That(
        Recorded(
            Path.Combine(RepositoryRoot, "tests", "ProfiC.Tests", "TestData", "Lexing", "Golden"),
            ".tokens"),
        Is.EqualTo(Expected(SampleNames)),
        "the recorded token streams and the samples have drifted apart");

    [Test]
    public void EveryRecordedTreeHasASampleBehindIt() => Assert.That(
        Recorded(
            Path.Combine(RepositoryRoot, "tests", "ProfiC.Tests", "TestData", "Parsing", "Golden"),
            ".ast"),
        Is.EqualTo(Expected(SampleNames)),
        "the recorded syntax trees and the samples have drifted apart");

    /// <summary>
    /// Output is recorded for both kinds of runnable sample: the single files in
    /// <c>samples</c>, recorded under their own name, and the folders beneath it, recorded
    /// under the folder's.
    /// </summary>
    [Test]
    public void EveryRecordedOutputHasARunnableSampleBehindIt() => Assert.That(
        Recorded(
            Path.Combine(RepositoryRoot, "tests", "ProfiC.Tests", "TestData", "Running"),
            ".out"),
        Is.EqualTo(Expected(
            Interpreting.SampleProgramTests.RunnableSampleNames.Concat(
                Interpreting.MultiFileSampleTests.EntryPoints.Select(
                    entry => Path.GetDirectoryName(entry)!)))),
        "the recorded outputs and the runnable samples have drifted apart");
}
