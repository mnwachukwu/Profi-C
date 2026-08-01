using System.Reflection;
using System.Text.RegularExpressions;
using ProfiC.Compiler.Diagnostics;

namespace ProfiC.Tests;

/// <summary>
/// <para>Holds Appendix A of the specification to the diagnostics the compiler actually has.
/// </para>
/// <para>The appendix is a second, hand-written list of the same thing, and adding a
/// descriptor does nothing to it. Drift here is quiet in a way most drift is not: nothing
/// fails, no color changes, and the only sign is a reader meeting an id the document has
/// never heard of — at the moment they most need it to have.</para>
/// <para>The same argument as <see cref="EditorGrammarTests"/>, applied to prose.</para>
/// </summary>
[TestFixture]
public sealed class DiagnosticsAppendixTests : LexerTestBase
{
    private static string SpecificationPath =>
        Path.Combine(RepositoryRoot, "docs", "language-spec.md");

    private static string SummaryPath =>
        Path.Combine(RepositoryRoot, "docs", "language-summary.md");

    private static IEnumerable<DiagnosticDescriptor> Descriptors() =>
        typeof(DiagnosticDescriptors)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(DiagnosticDescriptor))
            .Select(field => (DiagnosticDescriptor)field.GetValue(null)!);

    /// <summary>Every id the compiler can report, read off the descriptors themselves.</summary>
    private static SortedSet<string> Declared()
    {
        SortedSet<string> ids = new(StringComparer.Ordinal);

        foreach (FieldInfo field in typeof(DiagnosticDescriptors)
                     .GetFields(BindingFlags.Public | BindingFlags.Static)
                     .Where(f => f.FieldType == typeof(DiagnosticDescriptor)))
        {
            ids.Add(((DiagnosticDescriptor)field.GetValue(null)!).Id);
        }

        return ids;
    }

    /// <summary>Every id the appendix lists, read off its table rows.</summary>
    private static SortedSet<string> Documented() =>
        new(Regex.Matches(File.ReadAllText(SpecificationPath), @"^\| `(PC\d{4})`",
                          RegexOptions.Multiline)
                 .Select(m => m.Groups[1].Value),
            StringComparer.Ordinal);

    [Test]
    public void EveryDiagnosticTheCompilerHasIsInTheAppendix() => Assert.That(
        Declared().Except(Documented()),
        Is.Empty,
        "diagnostics the compiler reports that Appendix A does not list");

    [Test]
    public void TheAppendixListsNoDiagnosticTheCompilerLacks() => Assert.That(
        Documented().Except(Declared()),
        Is.Empty,
        "diagnostics Appendix A lists that the compiler cannot report");

    /// <summary>The word the appendix and the renderer both use for a severity.</summary>
    private static string Spelled(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Error => "error",
        DiagnosticSeverity.Warning => "warning",
        _ => "opinion",
    };

    /// <summary>
    /// <para>Each severity is stated in the appendix, and a diagnostic moving between them is
    /// exactly the kind of change a reader consults the appendix about.</para>
    /// <para>A row whose second cell holds no severity the compiler knows is a failure rather
    /// than a row to pass over. Skipping it would mean a misspelled severity — or one the
    /// alternation below was never widened for — reads as agreement.</para>
    /// </summary>
    [Test]
    public void TheAppendixAgreesAboutEverySeverity()
    {
        string specification = File.ReadAllText(SpecificationPath);
        List<string> wrong = [];

        foreach (DiagnosticDescriptor descriptor in Descriptors())
        {
            Match row = Regex.Match(
                specification,
                $@"^\| `{descriptor.Id}` \| (opinion|warning|error) \|",
                RegexOptions.Multiline);

            string actual = Spelled(descriptor.DefaultSeverity);

            if (!row.Success)
            {
                wrong.Add($"{descriptor.Id} is a{(actual == "warning" ? "" : "n")} {actual}, "
                          + "but its appendix row states no severity the compiler knows");
                continue;
            }

            string documented = row.Groups[1].Value;

            if (!string.Equals(documented, actual, StringComparison.Ordinal))
            {
                wrong.Add($"{descriptor.Id} is a {actual} but the appendix says {documented}");
            }
        }

        Assert.That(wrong.Order(StringComparer.Ordinal), Is.Empty);
    }

    /// <summary>
    /// <para>The summary counts the warnings and the opinions in two sentences, each saying its
    /// number in words.</para>
    /// <para>It is a third hand-written list of the same thing, and the only one nothing checks
    /// — which is why it was the one that drifted. A count is also the claim a reader is least
    /// likely to verify and most likely to repeat.</para>
    /// <para>Each count is read from its own sentence rather than looked for anywhere in the
    /// file. The two happen to be equal, so a search of the whole document would find the other
    /// one's number and agree with itself.</para>
    /// </summary>
    [TestCase(DiagnosticSeverity.Warning, "Warnings are few")]
    [TestCase(DiagnosticSeverity.Opinion, "Opinions are the language")]
    public void TheSummaryCountsThemCorrectly(DiagnosticSeverity severity, string opening)
    {
        string[] words =
        [
            "Zero", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine",
            "Ten", "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen",
            "Seventeen", "Eighteen", "Nineteen", "Twenty",
        ];

        int reported = Descriptors().Count(d => d.DefaultSeverity == severity);

        Assert.That(reported, Is.LessThan(words.Length), "this table needs more number words");

        Match sentence = Regex.Match(
            File.ReadAllText(SummaryPath),
            $@"\*\*{Regex.Escape(opening)}[^*]*\*\* (\w+) exist");

        Assert.That(
            sentence.Success,
            $"the summary should carry a sentence opening '{opening}' and counting them");

        Assert.That(
            sentence.Groups[1].Value,
            Is.EqualTo(words[reported]),
            $"the compiler has {reported} of {Spelled(severity)} severity, so the sentence "
            + $"opening '{opening}' should say '{words[reported]} exist'");
    }
}
