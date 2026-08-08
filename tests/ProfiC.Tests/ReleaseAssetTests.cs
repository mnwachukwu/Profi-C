using System.Text.RegularExpressions;

namespace ProfiC.Tests;

/// <summary>
/// <para>Holds the release to the names people install from.</para>
/// <para><b>An asset name is a promise, and it is made to strangers.</b> The install scripts and
/// the site's install page both fetch <c>releases/latest/download/&lt;name&gt;</c>, which GitHub
/// keeps pointing at the newest release — so a link written once goes on working, as long as
/// every release names its files the same way. Rename one and every instruction already published
/// breaks, for everyone, at once. Nothing about renaming it fails here otherwise: the workflow
/// still runs, the archives still build, and the only symptom is a 404 on somebody else's
/// machine.</para>
/// <para>Which platforms are covered is held too, for the opposite reason: dropping one is
/// silent. The release would build, publish, and simply not have an archive for whoever needed
/// it.</para>
/// </summary>
[TestFixture]
public sealed class ReleaseAssetTests : LexerTestBase
{
    private static string WorkflowPath =>
        Path.Combine(RepositoryRoot, ".github", "workflows", "release.yml");

    private static string Workflow => File.ReadAllText(WorkflowPath);

    /// <summary>
    /// Every archive a release publishes, and the runtime each is built for.
    ///
    /// One per platform the compiler runs on, with the architecture named in the file. A name
    /// without one — `profi-c-osx.tar.gz` — cannot say which of two Macs it is for, and the
    /// reader who guesses wrong downloads seventy megabytes that will not start.
    /// </summary>
    private static readonly (string Runtime, string Asset)[] Published =
    [
        ("win-x64", "profi-c-win-x64.zip"),
        ("win-arm64", "profi-c-win-arm64.zip"),
        ("linux-x64", "profi-c-linux-x64.tar.gz"),
        ("linux-arm64", "profi-c-linux-arm64.tar.gz"),
        ("osx-x64", "profi-c-osx-x64.tar.gz"),
        ("osx-arm64", "profi-c-osx-arm64.tar.gz"),
    ];

    [Test]
    public void TheReleaseWorkflowIsThere() =>
        Assert.That(File.Exists(WorkflowPath), Is.True, $"{WorkflowPath} is what cuts a release");

    [TestCaseSource(nameof(Published))]
    public void EachPlatformIsPublishedUnderTheNameItIsInstalledBy((string Runtime, string Asset) one)
    {
        Assert.That(
            Workflow,
            Does.Contain($"publish {one.Runtime}"),
            $"no release is built for {one.Runtime}");

        Assert.That(
            Workflow,
            Does.Contain(one.Asset),
            $"{one.Runtime} is built but not published as {one.Asset}, which is the name the "
            + "install instructions already fetch");
    }

    /// <summary>
    /// Nothing is published that this list does not know about.
    ///
    /// The other direction of the same promise: a seventh archive appearing is a name somebody
    /// will link to, and a name nothing here has agreed to keep.
    /// </summary>
    [Test]
    public void NothingElseIsPublished()
    {
        string[] found = [.. Regex.Matches(Workflow, @"profi-c-[a-z0-9-]+\.(?:zip|tar\.gz)")
            .Select(match => match.Value)
            .Distinct()
            .Order()];

        Assert.That(found, Is.EqualTo(Published.Select(one => one.Asset).Order()).AsCollection);
    }

    /// <summary>
    /// A tag is what cuts a release.
    ///
    /// Held because the alternative is a release on every push to main, which turns a version
    /// number into a thing that happens to you rather than a thing you decide.
    /// </summary>
    [Test]
    public void OnlyATagCutsARelease()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Workflow, Does.Contain("tags: ['v*']"));
            Assert.That(
                Workflow,
                Does.Not.Contain("branches:"),
                "a release built on a push to a branch is a release nobody chose to make");
        });
    }

    /// <summary>
    /// <para>Nothing is released that CI has not passed, on every system CI covers.</para>
    /// <para>Held because the tempting shortcut is a release job that builds and tests inline:
    /// it looks equivalent, runs faster, and quietly tests one operating system. This repository
    /// has been wrong about paths, line endings and file-system case before, and every one of
    /// those is green on Linux — while the release would ship the Windows archive regardless.
    /// Calling the same workflow a merge has to pass also means the bar moves for both at once
    /// rather than for one.</para>
    /// </summary>
    [Test]
    public void NothingIsReleasedUntilCiHasPassedOnBothSystems()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Workflow, Does.Contain("uses: ./.github/workflows/build.yml"));
            Assert.That(Workflow, Does.Contain("os: ubuntu-latest"));
            Assert.That(Workflow, Does.Contain("os: windows-latest"));
            Assert.That(
                Workflow,
                Does.Contain("needs: [verify-linux, verify-windows]"),
                "the release job does not wait for both, so one of them failing releases anyway");
        });
    }

    /// <summary>
    /// The archives are self-contained, which is the reason there is no <c>dotnet tool</c>.
    ///
    /// A reader whose first language this is should need nothing installed first. Publishing
    /// framework-dependent would still produce an archive, still upload, and fail only on the
    /// machine of somebody who has no .NET — which is exactly the reader this is for.
    /// </summary>
    [Test]
    public void TheArchivesNeedNothingInstalledFirst() =>
        Assert.That(Workflow, Does.Contain("--self-contained true"));

    /// <summary>The changelog a release points at exists and names the version being released.</summary>
    [Test]
    public void TheChangelogNamesTheVersionInTheBuildProperties()
    {
        string changelog = Path.Combine(RepositoryRoot, "CHANGELOG.md");

        Assert.That(File.Exists(changelog), Is.True, "a release with no changelog says nothing");

        Match declared = Regex.Match(
            File.ReadAllText(Path.Combine(RepositoryRoot, "Directory.Build.props")),
            @"<VersionPrefix>([^<]+)</VersionPrefix>");

        Assert.That(declared.Success, Is.True, "Directory.Build.props declares no version");

        Assert.That(
            File.ReadAllText(changelog),
            Does.Contain($"## {declared.Groups[1].Value}"),
            "the version about to be released has no entry in the changelog");
    }
}
