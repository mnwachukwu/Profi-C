using System.Text.RegularExpressions;
using ProfiC.Compiler;
using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Documentation;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Semantics;
using ProfiC.Compiler.Text;

namespace ProfiC.Tests;

/// <summary>
/// <para>Every example in <c>docs/standard-library/</c> compiles.</para>
/// <para>A reference is read by somebody with an editor open, and its examples are read as
/// things to copy. One that does not compile is worse than no example at all: the reader has no
/// reason to doubt it, so they look for the mistake in their own program.</para>
/// <para>Written after a throwaway version of this found two on its first run — a <c>catch</c>
/// example dividing by a literal zero, which never reaches run time because <c>PC0324</c> refuses
/// it while compiling, and a fragment quietly relying on a model declared in an earlier block.
/// Neither is the kind of thing careful proofreading catches.</para>
/// <para>An example that annotates what it prints is also run, and held to it. That is the
/// second claim a reader takes on trust, and for a while nothing checked it: the pages carried
/// 39 annotated examples and one of them — <c>"banana".Remove("na")</c>, said to give
/// <c>bana</c> — had been wrong since it was written, because the member takes out every
/// appearance rather than the first.</para>
/// </summary>
[TestFixture]
public sealed class StandardLibraryExampleTests : LexerTestBase
{
    private static string Folder => Path.Combine(RepositoryRoot, "docs", "standard-library");

    /// <summary>One fenced block, and where a reader would find it.</summary>
    public sealed record Example(string Page, int Number, string Source)
    {
        /// <summary>What NUnit shows when this one fails, so a failure names the block.</summary>
        public override string ToString() => $"{Page} block {Number}";
    }

    /// <summary>
    /// <para>Every untagged fenced block, as a whole program.</para>
    /// <para>A block naming <c>shared model Program</c> is already one. Anything else is a run of
    /// statements — which is how the pages are written, since a reader looking up
    /// <c>TrimStart</c> wants the line and not a program around it — so it is given the program
    /// it was written to sit inside.</para>
    /// <para>Only untagged blocks. A fence marked <c>text</c> is notation rather than Profi-C —
    /// the signature form on the index, a refusal quoted with its identifier — and asking a
    /// compiler to read one would be asking the wrong question.</para>
    /// <para>Every fence is matched, tagged ones included, and the tagged ones are dropped
    /// after. Matching only untagged openings would leave a tagged block's closing fence looking
    /// like an opening one, so it would pair with the next real block and take the prose between
    /// them along with it.</para>
    /// </summary>
    public static IEnumerable<Example> Examples()
    {
        foreach (string path in Directory.EnumerateFiles(Folder, "*.md")
                                         .OrderBy(name => name, StringComparer.Ordinal))
        {
            string page = Path.GetFileName(path);
            int number = 0;

            foreach (Match block in Regex.Matches(
                         File.ReadAllText(path),
                         @"^```(\w*)\r?\n(.*?)^```",
                         RegexOptions.Singleline | RegexOptions.Multiline))
            {
                if (block.Groups[1].Value.Length > 0)
                {
                    continue;
                }

                yield return new Example(page, number++, AsAProgram(block.Groups[2].Value));
            }
        }
    }

    private static string AsAProgram(string code)
    {
        if (code.Contains("shared model Program", StringComparison.Ordinal))
        {
            return code;
        }

        string indented = string.Join(
            "\n",
            code.ReplaceLineEndings("\n").Split('\n').Select(line => "        " + line));

        return $"shared model Program\n    function Main()\n{indented}\n    end function\nend model\n";
    }

    [TestCaseSource(nameof(Examples))]
    public void AnExampleCompiles(Example example)
    {
        ArgumentNullException.ThrowIfNull(example);

        SourceText source = new(example.Source, example.ToString());
        DiagnosticBag diagnostics = new();

        CompilationUnit unit = Parser.Parse(source, diagnostics);
        FrontEnd.Check(unit, diagnostics, reportUnusedSuppressions: false);

        // Errors only. An example is allowed an opinion about itself — several deliberately show
        // a thing the compiler has a view on — but nothing in the reference may be wrong.
        Assert.That(
            diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                       .Select(d => $"line {d.Span.Start.Line}: {d.Id}: {d.Message}"),
            Is.Empty,
            $"{example} does not compile:\n{example.Source}");
    }

    /// <summary>
    /// <para>What an example says it prints, written after the line that prints it.</para>
    /// <para>An aside is often written after an em dash — <c>1 — the first one</c> — and only
    /// what precedes it is the output.</para>
    /// </summary>
    private static readonly Regex Annotation = new(
        @"^.*Console\.Write(?:Line)?\(.*\);[ \t]*#[ \t]*(.+?)[ \t]*$",
        RegexOptions.Multiline);

    /// <summary>
    /// <para>An example prints what it says it prints.</para>
    /// <para>Compiling is the first thing a reader trusts and the annotations are the second, and
    /// they are what the page is really for: somebody looking up <c>Subset</c> reads the
    /// <c>#</c> beside the call rather than the sentence above it.</para>
    /// <para>Only the examples that annotate every line they print. A page is free to show a
    /// fragment that says nothing about its output, and one that says something about some lines
    /// and not others is not making a claim this can check — the count below is what keeps that
    /// exemption from quietly swallowing the whole folder.</para>
    /// </summary>
    [TestCaseSource(nameof(Examples))]
    public void AnExamplePrintsWhatItSaysItPrints(Example example)
    {
        ArgumentNullException.ThrowIfNull(example);

        string[] said =
            [.. Annotation.Matches(example.Source)
                          .Select(line => line.Groups[1].Value.Split('—')[0].Trim())];

        if (said.Length == 0)
        {
            Assert.Pass("nothing is claimed about what this one prints");
        }

        SourceText source = new(example.Source, example.ToString());
        DiagnosticBag diagnostics = new();

        CompilationUnit unit = Parser.Parse(source, diagnostics);
        SemanticModel model = FrontEnd.Check(
            unit, diagnostics, requireEntryPoint: true, reportUnusedSuppressions: false);

        Assert.That(
            diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Id),
            Is.Empty,
            $"{example} does not compile");

        StringWriter output = new();
        ProfiC.Interpreter.Interpreter.Run(
            Lowering.Lower(unit, model), model, output, TextReader.Null);

        string[] wrote =
            [.. output.ToString()
                      .ReplaceLineEndings("\n")
                      .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                      .Select(line => line.Trim())];

        Assert.That(wrote, Is.EqualTo(said), $"{example} does not print what it says:\n{example.Source}");
    }

    /// <summary>
    /// <para>The pages go on annotating what they print.</para>
    /// <para>Everything above skips an example that claims nothing, so the folder passing it says
    /// nothing on its own — a page rewritten to drop its <c>#</c> comments would take its
    /// examples out of the check and read exactly the same. The number is here to be noticed when
    /// it falls.</para>
    /// </summary>
    [Test]
    public void MostExamplesSayWhatTheyPrint() =>
        Assert.That(
            Examples().Count(example => Annotation.IsMatch(example.Source)),
            Is.GreaterThanOrEqualTo(35),
            "reference examples annotating their output");

    /// <summary>
    /// <para>Every reference page carries an example.</para>
    /// <para>Everything above passes vacuously against a folder of prose, and the expression that
    /// finds a block is exactly the kind of thing that stops matching without anybody noticing.
    /// </para>
    /// <para>The index is not a reference page and is left out. It carries one fenced block, and
    /// deliberately not Profi-C: it shows how a signature is written on the pages, which is
    /// notation rather than something to copy into a program.</para>
    /// </summary>
    [Test]
    public void EveryReferencePageCarriesAtLeastOneExample()
    {
        ILookup<string, Example> byPage = Examples().ToLookup(example => example.Page);

        Assert.Multiple(() =>
        {
            foreach (string path in Directory.EnumerateFiles(Folder, "*.md"))
            {
                string page = Path.GetFileName(path);

                if (page == "README.md")
                {
                    continue;
                }

                Assert.That(byPage[page], Is.Not.Empty, $"{page} shows a reader nothing to copy");
            }
        });
    }
}
