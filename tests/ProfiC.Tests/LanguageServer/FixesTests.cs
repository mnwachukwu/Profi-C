using System.Text.Json.Nodes;
using ProfiC.Cli.LanguageServer;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Lexing;
using ProfiC.Compiler.Text;
using ProfiC.Services;

namespace ProfiC.Tests.LanguageServer;

/// <summary>
/// <para>The one-click fixes offered beside a problem.</para>
/// <para><b>What is fixable is the compiler's answer, not this one's.</b> A diagnostic carries
/// the text that would settle it where one substitution does the whole job, and carries nothing
/// where the rewrite needs to know something the compiler does not. Working the fix out from the
/// message would tie it to the wording of a sentence; working it out from the source would be a
/// second copy of the same table.</para>
/// <para>So the half worth holding hardest is the negative one: <c>+=</c> has a good message and
/// no fix, and offering one anyway would put a button on advice that cannot be carried out
/// mechanically.</para>
/// </summary>
[TestFixture]
public sealed class FixesTests
{
    private const string Uri = "file:///work/Program.pc";

    /// <summary>Everything the scanner said about a snippet.</summary>
    private static IReadOnlyList<Diagnostic> Scanned(string text)
    {
        DiagnosticBag found = new();

        _ = new Lexer(new SourceText(text, "Program.pc"), found).Scan();

        return [.. found];
    }

    /// <summary>
    /// The editor's copy of one diagnostic, as it sends it back when asking what can be done.
    /// Built from the compiler's, since that is what an editor is holding: what it was published.
    /// </summary>
    private static JsonArray AsSent(Diagnostic diagnostic) =>
        new(new JsonObject
        {
            ["code"] = diagnostic.Id,
            ["range"] = Lsp.RangeOf(diagnostic.Span, null),
            ["message"] = diagnostic.Message,
        });

    private static JsonArray OfferedFor(string text)
    {
        IReadOnlyList<Diagnostic> found = Scanned(text);

        Assert.That(found, Is.Not.Empty, "the snippet was meant to be reported");

        return Fixes.For(Uri, AsSent(found[0]), found);
    }

    [TestCase("boolean both = a && b;", "and")]
    [TestCase("boolean either = a || b;", "or")]
    [TestCase("integer raised = a ** b;", "^")]
    [TestCase("boolean opposite = !a;", "not")]
    public void AnOperatorWithAWordForItIsOffered(string written, string expected)
    {
        JsonArray offered = OfferedFor(written);

        Assert.Multiple(() =>
        {
            Assert.That(offered, Has.Count.EqualTo(1));
            Assert.That((string?)offered[0]!["title"], Is.EqualTo($"Replace with '{expected}'"));
            Assert.That((string?)offered[0]!["kind"], Is.EqualTo("quickfix"));

            JsonArray edits = (JsonArray)offered[0]!["edit"]!["changes"]![Uri]!;

            Assert.That(edits, Has.Count.EqualTo(1));
            Assert.That((string?)edits[0]!["newText"], Is.EqualTo(expected));
        });
    }

    /// <summary>
    /// <para>Advice that cannot be carried out mechanically is offered as advice and nothing
    /// else.</para>
    /// <para><c>x += 1</c> becomes <c>x = x + 1</c>, which needs to know what <c>x</c> is — the
    /// scanner does not, and neither does anything reading its output. A button that produced
    /// <c>x = 1</c> would be worse than no button.</para>
    /// </summary>
    [TestCase("counted += 1;")]
    [TestCase("counted++;")]
    [TestCase("integer function f() => 1;")]
    public void AdviceThatIsARewriteIsNotOfferedAsAFix(string written) =>
        Assert.That(OfferedFor(written), Is.Empty);

    /// <summary>
    /// <para>A fix is offered against the problem the editor is asking about, and no other.</para>
    /// <para>Two problems on one line is the case that catches a match made on the identifier
    /// alone: both are <c>PC0006</c>, and only the one under the cursor should get a button.
    /// </para>
    /// </summary>
    [Test]
    public void OnlyTheProblemAskedAboutIsOffered()
    {
        IReadOnlyList<Diagnostic> found = Scanned("boolean mixed = a && b || c;");

        Assert.That(found, Has.Count.EqualTo(2), "the snippet was meant to have two");

        JsonArray offered = Fixes.For(Uri, AsSent(found[1]), found);

        Assert.Multiple(() =>
        {
            Assert.That(offered, Has.Count.EqualTo(1));
            Assert.That((string?)offered[0]!["title"], Is.EqualTo("Replace with 'or'"));
        });
    }

    /// <summary>Nothing is offered where the editor asks about nothing.</summary>
    [Test]
    public void NothingIsOfferedForNoDiagnostics() =>
        Assert.That(Fixes.For(Uri, null, Scanned("integer counted = 1;")), Is.Empty);
}
