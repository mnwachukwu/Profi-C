using System.Text.Json.Nodes;
using ProfiC.Cli.LanguageServer;
using ProfiC.Compiler.Diagnostics;

namespace ProfiC.Tests.LanguageServer;

/// <summary>
/// <para>Turning what an editor calls a place into what the compiler calls one.</para>
/// <para><b>Written against the URIs editors actually send, not against the ones this code
/// produces.</b> That distinction is the whole reason this file exists: every other test here
/// made a URI with <see cref="Conversions.UriOf"/> and handed it back, so both ends were this
/// codebase's and they agreed with each other while agreeing with no editor. VS Code escapes the
/// colon in a drive letter; nothing here ever did; and the round trip passed for months while the
/// server could not open a file on Windows.</para>
/// </summary>
[TestFixture]
public sealed class ConversionsTests
{
    /// <summary>
    /// <para>A drive letter arrives escaped, and still names the same file.</para>
    /// <para><c>Uri.LocalPath</c> reads <c>file:///D:/x</c> as a drive and <c>file:///d%3A/x</c>
    /// as a rooted path, keeping the leading slash — the same file written two ways, answered
    /// two ways. The second is what VS Code sends. Left alone it makes
    /// <c>Path.GetFullPath</c> read the drive as a folder name under the current drive, so a file
    /// in <c>D:\Repos\samples</c> is looked for in <c>D:\d:\Repos\samples</c>, which exists
    /// nowhere.</para>
    /// <para><b>Windows only, because a drive letter is.</b> Elsewhere <c>D:</c> is an ordinary
    /// folder name: nothing roots the path, so the expected side gains the working directory in
    /// front of it, and the actual side keeps the backslashes <c>Uri.LocalPath</c> produced
    /// because a backslash is not a separator there. Both sides are then nonsense about a URI no
    /// editor on that platform sends.</para>
    /// </summary>
    [Platform("Win", Reason = "a drive letter is a Windows idea, and this is about reading one")]
    [TestCase("file:///d%3A/Repos/Profi-C/samples/hello.pc")]
    [TestCase("file:///D%3A/Repos/Profi-C/samples/hello.pc")]
    [TestCase("file:///D:/Repos/Profi-C/samples/hello.pc")]
    public void ADriveLetterIsReadWhicheverWayItIsWritten(string uri)
    {
        Assert.That(
            Conversions.PathOf(uri),
            Is.EqualTo(Path.GetFullPath("D:/Repos/Profi-C/samples/hello.pc")).IgnoreCase);
    }

    /// <summary>A space arrives escaped too, and is a space again on the other side.</summary>
    [Platform("Win", Reason = "written with a drive letter; the same claim is made below without one")]
    [Test]
    public void AnEscapedSpaceIsASpace() =>
        Assert.That(
            Conversions.PathOf("file:///d%3A/My%20Programs/hello.pc"),
            Is.EqualTo(Path.GetFullPath("D:/My Programs/hello.pc")).IgnoreCase);

    /// <summary>
    /// <para>The same, for a path with no drive in it at all.</para>
    /// <para>Here so that this file says something on every platform rather than skipping itself
    /// away: the escaping is what is being tested, and a rooted path is what an editor sends
    /// anywhere that has no drives.</para>
    /// </summary>
    [Platform("Linux,MacOsX", Reason = "a rooted path with no drive is what those platforms use")]
    [Test]
    public void AnEscapedSpaceIsASpaceWithNoDriveEither() =>
        Assert.That(
            Conversions.PathOf("file:///home/matt/My%20Programs/hello.pc"),
            Is.EqualTo("/home/matt/My Programs/hello.pc"));

    /// <summary>
    /// <para>The answer is spelled the way the rest of the compiler spells a path.</para>
    /// <para>Not a tidiness point. Everything that matches an open document against a compilation
    /// compares it to a full path, and that comparison ignores case but not separators — so a
    /// path carrying the URI's forward slashes matches no file, and every question about a place
    /// answers null while diagnostics carry on working.</para>
    /// </summary>
    [Test]
    public void APathIsSpelledTheWayThePathsItIsComparedAgainstAre()
    {
        string path = Conversions.PathOf("file:///d%3A/Repos/Profi-C/samples/hello.pc")!;

        Assert.That(path, Is.EqualTo(Path.GetFullPath(path)));
    }

    /// <summary>
    /// A buffer that was never saved names no file, and says so rather than inventing a path that
    /// would then be looked for.
    /// </summary>
    [TestCase("untitled:Untitled-1")]
    [TestCase("vscode-remote://ssh/home/matt/x.pc")]
    [TestCase("")]
    [TestCase(null)]
    public void SomethingThatIsNotAFileIsNotAPath(string? uri) =>
        Assert.That(Conversions.PathOf(uri), Is.Null);

    /// <summary>
    /// <para>What this produces, this can read back.</para>
    /// <para>Worth keeping even though it is the weaker claim: it is the one that would catch a
    /// change to how the server names files to itself, which is how the outline and the document
    /// store find each other.</para>
    /// </summary>
    [Test]
    public void APathSurvivesBeingWrittenAsAUriAndReadBack()
    {
        string path = Path.Combine(Path.GetTempPath(), "profi c", "hello.pc");

        Assert.That(
            Conversions.PathOf(Conversions.UriOf(path)),
            Is.EqualTo(path).IgnoreCase);
    }

    // ---- Fading what nothing uses ----------------------------------------------------------

    /// <summary>
    /// <para>Tag 1 is the protocol's "unnecessary", and it is the whole of how an editor knows
    /// to fade a span rather than underline it.</para>
    /// <para>Asserted on the wire rather than on the descriptor, because the descriptor saying
    /// so and the editor being told are two different things, and only the second is what a
    /// reader sees.</para>
    /// </summary>
    [TestCaseSource(nameof(FadedDiagnostics))]
    public void SomethingNothingUsesIsSentAsUnnecessary(DiagnosticDescriptor descriptor)
    {
        DiagnosticBag diagnostics = new();
        diagnostics.Report(descriptor, default);

        JsonObject written = Conversions.DiagnosticOf(diagnostics.Sorted()[0], source: null);

        Assert.That((JsonArray?)written["tags"], Is.Not.Null, $"{descriptor.Id} should fade");
        Assert.That((int?)written["tags"]![0], Is.EqualTo(1));
    }

    /// <summary>
    /// Everything else carries no tag at all. An editor fades what it is told to fade, so a tag
    /// on every diagnostic would fade the file.
    /// </summary>
    [TestCaseSource(nameof(UnfadedDiagnostics))]
    public void SomethingThatMayBeWrongIsNotSentAsUnnecessary(DiagnosticDescriptor descriptor)
    {
        DiagnosticBag diagnostics = new();
        diagnostics.Report(descriptor, default);

        Assert.That(
            Conversions.DiagnosticOf(diagnostics.Sorted()[0], source: null)["tags"],
            Is.Null,
            $"{descriptor.Id} should not fade");
    }

    public static IEnumerable<DiagnosticDescriptor> FadedDiagnostics() =>
        DiagnosticDescriptors.ById.Values.Where(d => d.Unused);

    public static IEnumerable<DiagnosticDescriptor> UnfadedDiagnostics() =>
        DiagnosticDescriptors.ById.Values.Where(d => !d.Unused);

    /// <summary>
    /// <para>The two lists are what they are meant to be, named rather than counted.</para>
    /// <para>Both tests above pass against an empty list, and the property they read is one
    /// nothing else would notice going wrong. These are the ones that would fail if a descriptor
    /// were marked the wrong way round.</para>
    /// </summary>
    [Test]
    public void TheRightThingsFade() => Assert.Multiple(() =>
    {
        Assert.That(
            FadedDiagnostics().Select(d => d.Id),
            Does.Contain("PC0409").And.Contain("PC0410").And.Contain("PC0403"),
            "a local nothing reads, a member nothing uses, and code nothing reaches");

        Assert.That(
            UnfadedDiagnostics().Select(d => d.Id),
            Does.Contain("PC0406"),
            "a loop nothing can end is missing something, not carrying something spare");

        Assert.That(
            FadedDiagnostics().Where(d => d.DefaultSeverity == DiagnosticSeverity.Error),
            Is.Empty,
            "nothing that stops the build is merely unnecessary");
    });
}
