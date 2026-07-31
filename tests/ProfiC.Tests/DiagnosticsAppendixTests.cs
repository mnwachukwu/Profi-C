using System.Reflection;
using System.Text.RegularExpressions;
using ProfiC.Compiler.Diagnostics;

namespace ProfiC.Tests;

/// <summary>
/// <para>Holds Appendix A of the specification to the diagnostics the compiler actually has.
/// </para>
/// <para>The appendix is a second, hand-written list of the same thing, and adding a
/// descriptor does nothing to it. Drift here is quiet in a way most drift is not: nothing
/// fails, no colour changes, and the only sign is a reader meeting an id the document has
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

    /// <summary>
    /// Each severity is stated in the appendix, and a diagnostic changing from a warning to an
    /// error is exactly the kind of change a reader consults the appendix about.
    /// </summary>
    [Test]
    public void TheAppendixAgreesAboutWhichAreWarnings()
    {
        string specification = File.ReadAllText(SpecificationPath);
        List<string> wrong = [];

        foreach (FieldInfo field in typeof(DiagnosticDescriptors)
                     .GetFields(BindingFlags.Public | BindingFlags.Static)
                     .Where(f => f.FieldType == typeof(DiagnosticDescriptor)))
        {
            DiagnosticDescriptor descriptor = (DiagnosticDescriptor)field.GetValue(null)!;

            Match row = Regex.Match(
                specification,
                $@"^\| `{descriptor.Id}` \| (warning|error) \|",
                RegexOptions.Multiline);

            if (!row.Success)
            {
                continue;
            }

            string documented = row.Groups[1].Value;
            string actual = descriptor.DefaultSeverity == DiagnosticSeverity.Warning
                ? "warning"
                : "error";

            if (!string.Equals(documented, actual, StringComparison.Ordinal))
            {
                wrong.Add($"{descriptor.Id} is a {actual} but the appendix says {documented}");
            }
        }

        Assert.That(wrong, Is.Empty);
    }

    /// <summary>
    /// <para>The summary counts the warnings in a sentence, and says the number in words.</para>
    /// <para>It is a third hand-written list of the same thing, and the only one nothing checks
    /// — which is why it was the one that drifted. A count is also the claim a reader is least
    /// likely to verify and most likely to repeat.</para>
    /// </summary>
    [Test]
    public void TheSummaryCountsTheWarningsCorrectly()
    {
        string[] words =
        [
            "Zero", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine",
            "Ten", "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen",
            "Seventeen", "Eighteen", "Nineteen", "Twenty",
        ];

        int warnings = Descriptors()
            .Count(d => d.DefaultSeverity == DiagnosticSeverity.Warning);

        Assert.That(warnings, Is.LessThan(words.Length), "this table needs more number words");

        Assert.That(
            File.ReadAllText(SummaryPath),
            Does.Contain($"{words[warnings]} exist"),
            $"the compiler has {warnings} warnings, so the summary should say "
            + $"'{words[warnings]} exist'");
    }
}
