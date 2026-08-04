using System.Text.RegularExpressions;
using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Documentation;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Semantics;
using ProfiC.Compiler.Text;

namespace ProfiC.Tests;

/// <summary>
/// <para>Every example in the specification compiles.</para>
/// <para><b>The specification is the document that is right when the others disagree</b>, and
/// nothing was checking it. Its code blocks were as correct as the last person to read them, which
/// is how section 8.2 came to promise early-exit narrowing the compiler did not do — a claim a
/// compiled example would have refused the day it was written.</para>
/// <para>The rule is the one <see cref="StandardLibraryExampleTests"/> follows, so there is one
/// convention across the documentation rather than two: <b>an untagged block is Profi-C and has to
/// compile; a tagged one is notation and is left alone.</b> Notation is not rare here the way it is
/// in a reference — the specification quotes grammar, operator tables, project files, reserved
/// words, shell commands, and code annotated with the diagnostic it raises, none of which is
/// something to copy into a program.</para>
/// <para>Untagged is the demanding default on purpose. A block added later without a tag is
/// checked, so the drift this exists to stop cannot creep back in one edit at a time.</para>
/// </summary>
[TestFixture]
public sealed partial class LanguageSpecExampleTests : LexerTestBase
{
    private static string Path => System.IO.Path.Combine(
        RepositoryRoot, "docs", "language-spec.md");

    /// <summary>One fenced block, and the line a reader would find it on.</summary>
    public sealed record Example(int Line, string Source, string Written)
    {
        public override string ToString() => $"language-spec.md line {Line}";
    }

    /// <summary>
    /// <para>Every untagged fenced block, as something a compiler can be asked about.</para>
    /// <para>Every fence is matched, tagged ones included, and the tagged ones dropped after —
    /// matching only untagged openings would leave a tagged block's closing fence looking like an
    /// opening one, so it would pair with the next real block and swallow the prose between
    /// them.</para>
    /// </summary>
    public static IEnumerable<Example> Examples()
    {
        string text = File.ReadAllText(Path).ReplaceLineEndings("\n");

        foreach (Match block in Regex.Matches(
                     text,
                     @"^```(\w*)\n(.*?)^```",
                     RegexOptions.Singleline | RegexOptions.Multiline))
        {
            if (block.Groups[1].Value.Length > 0)
            {
                continue;
            }

            string written = block.Groups[2].Value;

            yield return new Example(
                text[..block.Index].Count(c => c == '\n') + 1, AsAProgram(written), written);
        }
    }

    /// <summary>
    /// <para>What to compile for a block, which is one of three things.</para>
    /// <para>A block naming <c>shared model Program</c> is already a program. A block opening with
    /// a declaration is a file — a source file holding no <c>Program</c> is shared code, which is
    /// exactly what a specification quotes when it shows what a model looks like. Anything else is
    /// a run of statements, and is given the program it was written to sit inside.</para>
    /// </summary>
    private static string AsAProgram(string code)
    {
        if (code.Contains("shared model Program", StringComparison.Ordinal))
        {
            return code;
        }

        if (Declaring(code))
        {
            return code;
        }

        string indented = string.Join(
            "\n", code.Split('\n').Select(line => "        " + line));

        return $"shared model Program\n    function Main()\n{indented}\n    end function\nend model\n";
    }

    /// <summary>
    /// <para>Whether a block declares rather than does, decided by its first line of code.</para>
    /// <para>Comments are skipped, both forms — a specification's examples are half explanation,
    /// and the one showing how to document a model opens with the documentation.</para>
    /// <para>Walked rather than matched. A pattern that skips a run of either kind of comment
    /// wants nested quantifiers around a lazy <c>.*?</c>, which is the shape that backtracks for
    /// a very long time on a block it turns out not to match — measured at eighty seconds across
    /// this file before it was written out longhand.</para>
    /// </summary>
    private static bool Declaring(string code)
    {
        bool commenting = false;

        foreach (string line in code.Split('\n'))
        {
            string trimmed = line.Trim();

            if (commenting)
            {
                commenting = !trimmed.EndsWith("##", StringComparison.Ordinal);
                continue;
            }

            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                // A block comment opens and closes with the same mark, so one that closes on its
                // own line is still open after the line that opened it.
                commenting = trimmed.StartsWith("##", StringComparison.Ordinal) && trimmed == "##";
                continue;
            }

            return Opening().IsMatch(trimmed);
        }

        return false;
    }

    [GeneratedRegex(
        @"\A(public |internal |protected |shared |abstract |sealed )*"
        + @"(model|structure|enumeration|namespace|import|using)\b")]
    private static partial Regex Opening();

    [TestCaseSource(nameof(Examples))]
    public void AnExampleCompiles(Example example)
    {
        ArgumentNullException.ThrowIfNull(example);

        SourceText source = new(example.Source, example.ToString());
        DiagnosticBag diagnostics = new();

        CompilationUnit unit = Parser.Parse(source, diagnostics);
        SemanticModel model = Resolver.Resolve(unit, diagnostics, requireEntryPoint: false);

        TypeChecker.Check(unit, model, diagnostics);
        DefiniteAssignment.Analyze(unit, model, diagnostics);
        DocumentationChecker.Check(unit, diagnostics);

        // Errors only. The specification shows plenty the compiler has an opinion about — that is
        // half of what a specification is for — but nothing in it may be wrong.
        Assert.That(
            diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                       .Select(d => $"line {d.Span.Start.Line}: {d.Id}: {d.Message}"),
            Is.Empty,
            $"{example} does not compile:\n{example.Written}");
    }

    /// <summary>
    /// <para>The specification still has examples in it.</para>
    /// <para>Everything above passes vacuously against a document with no untagged blocks left,
    /// and the expression that finds one is exactly the kind of thing that stops matching without
    /// anybody noticing.</para>
    /// </summary>
    [Test]
    public void TheSpecificationStillCarriesExamples()
    {
        Assert.That(Examples().Count(), Is.GreaterThan(20));
    }
}
