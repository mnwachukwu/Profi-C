using System.Text.RegularExpressions;
using ProfiC.Compiler;
using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Text;

namespace ProfiC.Tests;

/// <summary>
/// <para>Every Profi-C block in <c>docs/side-by-side.md</c> compiles.</para>
/// <para>The specification's examples are checked and the reference's are checked and run. This
/// document had neither, and it is the one most exposed to drift: every construct is written
/// twice, and only the C# half has a reader who would notice. Pointing the same harness at it
/// found three blocks that could not compile — one naming a field no type had, one putting a
/// <c>throw</c> where no statement may go, and one writing a file's <c>using</c> and
/// <c>import</c> under its <c>namespace</c>, which is C#'s order and the reverse of this
/// language's.</para>
/// <para>The last is the one worth having a test for. It is not a typo: it is what somebody
/// writing this document would reach for, because the C# beside it is correct that way.</para>
/// </summary>
[TestFixture]
public sealed class SideBySideExampleTests : LexerTestBase
{
    private static string Path => System.IO.Path.Combine(RepositoryRoot, "docs", "side-by-side.md");

    /// <summary>One block, and where a reader would find it.</summary>
    public sealed record Example(int Number, int Line, string Source)
    {
        public override string ToString() => $"block {Number} at line {Line}";
    }

    /// <summary>
    /// <para>Every untagged block, given the surroundings its shape needs.</para>
    /// <para>The blocks here are excerpts rather than programs — a loop beside the C# loop, a
    /// model beside the C# class — so what has to be built around one depends on what it is. A
    /// declaration sits beside a <c>Program</c>, a function sits inside one, and statements sit
    /// in its <c>Main</c>.</para>
    /// </summary>
    public static IEnumerable<Example> Examples()
    {
        string text = File.ReadAllText(Path);
        int number = 0;

        foreach (Match block in Regex.Matches(
                     text, @"^```(\w*)\r?\n(.*?)^```", RegexOptions.Singleline | RegexOptions.Multiline))
        {
            if (block.Groups[1].Value.Length > 0)
            {
                continue;
            }

            number++;

            string code = block.Groups[2].Value.ReplaceLineEndings("\n").TrimEnd('\n');
            int line = text[..block.Index].Count(c => c == '\n') + 1;

            yield return new Example(number, line, Surrounded(code));
        }
    }

    private static string Surrounded(string code)
    {
        string first =
            code.Split('\n')
                .FirstOrDefault(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith('#'), string.Empty)
                .Trim();

        // Already whole: it declares the program, or it opens with a directive and so is being
        // shown as a file.
        if (code.Contains("shared model Program", StringComparison.Ordinal)
            || Regex.IsMatch(first, @"^(using|import|namespace)\b"))
        {
            return code;
        }

        if (Regex.IsMatch(first, @"^(public |internal |protected |shared |sealed |abstract )*(model|structure|enumeration)\b"))
        {
            return $"{code}\n\nshared model Program\n    function Main()\n    end function\nend model\n";
        }

        string indented(string by) => string.Join("\n", code.Split('\n').Select(l => by + l));

        return first.Contains("function", StringComparison.Ordinal)
            ? $"shared model Program\n    function Main()\n    end function\n\n{indented("    ")}\nend model\n"
            : $"shared model Program\n    function Main()\n{indented("        ")}\n    end function\nend model\n";
    }

    /// <summary>
    /// <para>What an excerpt is allowed not to have brought with it: a value it never declares
    /// (<c>PC0200</c>) and a namespace it never declares (<c>PC0227</c>).</para>
    /// <para>Both say the same thing — this name is not in this file — and both are the document's
    /// shape rather than its mistakes. A block showing a loop over <c>grades</c> does not stop to
    /// declare <c>grades</c>, and the one showing a file header names a namespace some other file
    /// would hold. Declaring them would turn a document of twenty-line comparisons into one of
    /// forty-line ones, and the brevity is what lets two languages be read at a glance.</para>
    /// </summary>
    private static readonly HashSet<string> Supplied =
        new(StringComparer.Ordinal) { "PC0200", "PC0227" };

    /// <summary>
    /// <para>A block compiles, but for the names it deliberately does not declare.</para>
    /// <para>What that leaves checked is everything naming does not reach: that it still parses,
    /// that every member named is still there, that the types still agree, that a directive is
    /// still written where the language puts it. Those are what rots when the language moves,
    /// and a name a reader supplies is not.</para>
    /// </summary>
    [TestCaseSource(nameof(Examples))]
    public void ABlockCompiles(Example example)
    {
        ArgumentNullException.ThrowIfNull(example);

        SourceText source = new(example.Source, example.ToString());
        DiagnosticBag diagnostics = new();

        CompilationUnit unit = Parser.Parse(source, diagnostics);
        FrontEnd.Check(unit, diagnostics, reportUnusedSuppressions: false);

        Assert.That(
            diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error && !Supplied.Contains(d.Id))
                       .Select(d => $"line {d.Span.Start.Line}: {d.Id}: {d.Message}"),
            Is.Empty,
            $"{example} does not compile:\n{example.Source}");
    }

    /// <summary>
    /// <para>The document still holds the blocks this checks.</para>
    /// <para>Everything above passes over an empty list, and the expression that finds a block is
    /// the kind of thing that stops matching without anybody noticing — which is how this document
    /// went unchecked in the first place.</para>
    /// </summary>
    [Test]
    public void TheDocumentIsFullOfProfiC() =>
        Assert.That(Examples().Count(), Is.GreaterThanOrEqualTo(25));
}
