using System.Text.Json;
using ProfiC.Compiler.Emit;

namespace ProfiC.Tests.Emitting;

/// <summary>
/// <para>Where <c>build</c> puts what it makes.</para>
/// <para>Driven through the command rather than through the emitter, because that is where the
/// question lives: <see cref="ProfiC.Compiler.Emit.CilEmitter"/> writes wherever it is told, and
/// what is worth pinning is what tells it — the default, the flag that changes it, and the
/// refusals for an argument that says nothing useful.</para>
/// <para><b>Not parallelizable.</b> Reading what the command printed means redirecting the
/// console, which is one per process — a fixture running beside this one would have its own
/// output captured here and lose it.</para>
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class BuildCommandTests
{
    private const string Hello = """
        shared model Program
            function Main()
                Console.WriteLine("built and run");
            end function
        end model
        """;

    /// <summary>A folder holding one program, removed however the test ends.</summary>
    private sealed class Workspace : IDisposable
    {
        public Workspace()
        {
            Folder = Path.Combine(Path.GetTempPath(), $"profi-c-build-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Folder);

            Program = Path.Combine(Folder, "Hello.pc");
            File.WriteAllText(Program, Hello);
        }

        public string Folder { get; }

        public string Program { get; }

        public void Dispose() => Directory.Delete(Folder, recursive: true);
    }

    /// <summary>Runs the command and gives back its exit code and everything it said.</summary>
    private static (int Code, string Said) Build(params string[] args)
    {
        TextWriter wasOut = Console.Out;
        TextWriter wasError = Console.Error;

        StringWriter said = new();

        try
        {
            Console.SetOut(said);
            Console.SetError(said);

            return (ProfiC.Cli.Program.Run(args), said.ToString());
        }
        finally
        {
            Console.SetOut(wasOut);
            Console.SetError(wasError);
        }
    }

    /// <summary>
    /// <para>With nothing said about it, a build lands in a <c>bin</c> beside the program.</para>
    /// <para>Beside the program rather than beside the reader, so that building the same file
    /// from two different directories puts the result in one place.</para>
    /// </summary>
    [Test]
    public void WithNothingSaidTheBuildLandsInBin()
    {
        using Workspace workspace = new();

        (int code, string said) = Build("build", workspace.Program);

        string bin = Path.Combine(workspace.Folder, "bin");

        Assert.Multiple(() =>
        {
            Assert.That(code, Is.Zero, said);

            Assert.That(File.Exists(Path.Combine(bin, "Hello.dll")), Is.True, said);

            Assert.That(Directory.GetFiles(workspace.Folder).Select(Path.GetFileName),
                        Is.EqualTo(new[] { "Hello.pc" }),
                        "nothing a tool made should sit beside what somebody wrote");
        });
    }

    /// <summary>
    /// <para>Everything a build makes goes together.</para>
    /// <para>Three files, not one: the assembly will not start without the runtime configuration
    /// naming a framework, and will fail at its first printed value without the runtime beside
    /// it. Putting them in one folder is what makes the folder the thing you can copy.</para>
    /// </summary>
    [Test]
    public void EverythingNeededToRunGoesTogether()
    {
        using Workspace workspace = new();

        (int code, string said) = Build("build", workspace.Program);

        string[] written =
        [
            .. Directory.GetFiles(Path.Combine(workspace.Folder, "bin"))
                        .Select(Path.GetFileName)!,
        ];

        Assert.Multiple(() =>
        {
            Assert.That(code, Is.Zero, said);
            Assert.That(written, Does.Contain("Hello.dll"));
            Assert.That(written, Does.Contain("Hello.runtimeconfig.json"));
            Assert.That(written, Does.Contain("ProfiC.Runtime.dll"));
        });
    }

    /// <summary><c>--out</c> sends it somewhere else, and nothing goes to the default.</summary>
    [Test]
    public void OutSendsItElsewhere()
    {
        using Workspace workspace = new();

        string elsewhere = Path.Combine(workspace.Folder, "somewhere", "deeper");

        (int code, string said) = Build("build", workspace.Program, "--out", elsewhere);

        Assert.Multiple(() =>
        {
            Assert.That(code, Is.Zero, said);

            Assert.That(File.Exists(Path.Combine(elsewhere, "Hello.dll")), Is.True, said);

            Assert.That(Directory.Exists(Path.Combine(workspace.Folder, "bin")), Is.False,
                        "the default should not also have been written");
        });
    }

    /// <summary>
    /// The folder is made where it does not exist, including the folders above it. Asking
    /// somebody to create a directory before a compiler will write to it is a step with no
    /// purpose.
    /// </summary>
    [Test]
    public void OutMakesTheFolderItWasGiven()
    {
        using Workspace workspace = new();

        string missing = Path.Combine(workspace.Folder, "one", "two", "three");

        (int code, string said) = Build("build", workspace.Program, "--out", missing);

        Assert.Multiple(() =>
        {
            Assert.That(code, Is.Zero, said);
            Assert.That(File.Exists(Path.Combine(missing, "Hello.dll")), Is.True, said);
        });
    }

    /// <summary>
    /// <para>The line it prints is a path from where the reader is standing, so it can be
    /// pasted.</para>
    /// <para>It named the bare file until a build stopped landing in the folder it was run from.
    /// A wrong instruction printed by the tool that has just succeeded is worse than none.</para>
    /// </summary>
    [Test]
    public void ItSaysHowToRunWhatItBuilt()
    {
        using Workspace workspace = new();

        (int code, string said) = Build("build", workspace.Program);

        string expected = Path.GetRelativePath(
            ".",
            Path.Combine(workspace.Folder, "bin", AppHost.NameFor("Hello", AppHost.ThisPlatform)));

        Assert.Multiple(() =>
        {
            Assert.That(code, Is.Zero, said);
            Assert.That(said, Does.Contain($"Run it with: {expected}"), said);
        });
    }

    // ---- The launcher -----------------------------------------------------------------------

    /// <summary>
    /// <para>A build makes something that can be started without naming <c>dotnet</c>.</para>
    /// <para>Which is the whole point of it: typing <c>dotnet Hello.dll</c> is fair to ask of
    /// somebody who installed a compiler and unfair to ask of whoever they send the program to.
    /// </para>
    /// </summary>
    [Test]
    public void ABuildMakesSomethingThatCanBeStarted()
    {
        using Workspace workspace = new();

        (int code, string said) = Build("build", workspace.Program);

        string launcher = Path.Combine(
            workspace.Folder, "bin", AppHost.NameFor("Hello", AppHost.ThisPlatform));

        Assert.Multiple(() =>
        {
            Assert.That(code, Is.Zero, said);
            Assert.That(File.Exists(launcher), Is.True, said);

            Assert.That(said, Does.Contain(Path.GetRelativePath(".", launcher)),
                        "and the line it prints names the launcher rather than the assembly");
        });
    }

    /// <summary>
    /// <para>The launcher holds the assembly's name, and then nothing.</para>
    /// <para>Checked in the bytes because there is no other way to see it: a launcher still
    /// carrying its placeholder is a well-formed executable that looks for a file called
    /// <c>c3ab8ff1...</c>, and that only shows when somebody runs it.</para>
    /// <para>Searched as bytes rather than as decoded text, and the distinction is not
    /// pedantry. An apphost holds the placeholder three times over: once where the name goes,
    /// and once as each half of itself, kept apart so that the pattern it searches for cannot
    /// occur in the very constant it is built from. The halves are separated by padding, which
    /// on some platforms is nothing but zeros — and a text search that is not ordinal treats a
    /// zero as ignorable and matches straight across the gap. That reports a placeholder in a
    /// launcher that was named correctly, and it does so on one platform and not another.</para>
    /// <para>The trailing zeros are checked too, and not out of tidiness. Writing the name over
    /// the front of the placeholder leaves the rest of it behind the terminator — which starts
    /// correctly, so every other check here passes, while the file still contains most of a
    /// marker that means "not named yet".</para>
    /// </summary>
    [Test]
    public void TheLauncherHoldsTheAssemblysNameAndNothingElse()
    {
        using Workspace workspace = new();

        (int code, string said) = Build("build", workspace.Program);

        string launcher = Path.Combine(
            workspace.Folder, "bin", AppHost.NameFor("Hello", AppHost.ThisPlatform));

        Assert.That(code, Is.Zero, said);

        byte[] bytes = File.ReadAllBytes(launcher);
        byte[] name = System.Text.Encoding.UTF8.GetBytes("Hello.dll\0");
        byte[] placeholder = System.Text.Encoding.UTF8.GetBytes(
            "c3ab8ff13720e8ad9047dd39466b3c8974e592c2fa383d4a3960714caef0c4f2");

        int at = Find(bytes, name);

        Assert.Multiple(() =>
        {
            Assert.That(at, Is.GreaterThanOrEqualTo(0), "the assembly should be named in it");

            Assert.That(Find(bytes, placeholder), Is.LessThan(0), "and the placeholder written over");

            Assert.That(
                bytes.Skip(at + name.Length).Take(64).Where(b => b != 0),
                Is.Empty,
                "with nothing of the placeholder trailing the name");
        });
    }

    private static int Find(byte[] haystack, byte[] needle)
    {
        for (int at = 0; at + needle.Length <= haystack.Length; at++)
        {
            if (haystack.Skip(at).Take(needle.Length).SequenceEqual(needle))
            {
                return at;
            }
        }

        return -1;
    }

    /// <summary>
    /// <para>A build for another platform makes that platform's launcher, named its way.</para>
    /// <para>Skipped where nothing for that platform has ever been fetched, since which ones are
    /// on hand is a fact about the machine rather than about the compiler.</para>
    /// </summary>
    [Test]
    public void BuildingForAnotherPlatformMakesItsLauncher()
    {
        if (!AppHost.CanTarget("linux-x64"))
        {
            Assert.Ignore("no linux-x64 launcher is installed to build with");
        }

        using Workspace workspace = new();

        (int code, string said) = Build("build", workspace.Program, "--runtime", "linux-x64");

        string bin = Path.Combine(workspace.Folder, "bin");

        Assert.Multiple(() =>
        {
            Assert.That(code, Is.Zero, said);

            Assert.That(File.Exists(Path.Combine(bin, "hello")) || File.Exists(Path.Combine(bin, "Hello")),
                        Is.True,
                        "a Linux program carries no extension");

            Assert.That(File.Exists(Path.Combine(bin, "Hello.exe")), Is.False,
                        "and is not given a Windows one");
        });
    }

    /// <summary>
    /// A platform nothing is installed for is refused, and the refusal says which ones are on
    /// hand and how to get another — a reader who asked for one is owed both.
    /// </summary>
    [Test]
    public void APlatformWithNoLauncherIsRefusedHelpfully()
    {
        using Workspace workspace = new();

        (int code, string said) = Build("build", workspace.Program, "--runtime", "sinclair-z80");

        Assert.Multiple(() =>
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(said, Does.Contain("sinclair-z80"));
            Assert.That(said, Does.Contain(AppHost.ThisPlatform), "the ones on hand are listed");
            Assert.That(said, Does.Contain("dotnet publish"), "and how to get another");
            Assert.That(Directory.Exists(Path.Combine(workspace.Folder, "bin")), Is.False);
        });
    }

    /// <summary>
    /// <para>The platform chosen by default is one a launcher can actually be made for.</para>
    /// <para>The case worth guarding: .NET may report a machine as <c>ubuntu.22.04-x64</c> while
    /// every launcher is published as <c>linux-x64</c>. Taking the reported name and stopping
    /// there leaves no launcher on the very machine the build is running on — which is the one
    /// case that has to work, and which nothing else here would notice, since a missing launcher
    /// is a warning rather than a failure.</para>
    /// </summary>
    [Test]
    public void TheDefaultPlatformIsOneThatCanBeBuiltFor()
    {
        if (AppHost.Installed().Count == 0)
        {
            Assert.Ignore("no launchers are installed at all, so there is nothing to choose from");
        }

        Assert.That(AppHost.CanTarget(AppHost.ThisPlatform), Is.True,
                    $"chose '{AppHost.ThisPlatform}' of {string.Join(", ", AppHost.Installed())}");
    }

    /// <summary>
    /// The portable form describes this machine, and is the shape a launcher is published under:
    /// an operating system and an architecture, and nothing else.
    /// </summary>
    [Test]
    public void ThePortablePlatformIsAnOperatingSystemAndAnArchitecture()
    {
        string portable = AppHost.PortablePlatform;

        Assert.Multiple(() =>
        {
            Assert.That(portable.Split('-'), Has.Length.EqualTo(2), portable);

            Assert.That(
                portable.Split('-')[0],
                Is.EqualTo(OperatingSystem.IsWindows() ? "win"
                    : OperatingSystem.IsMacOS() ? "osx" : "linux"));

            Assert.That(portable, Does.Not.Contain("."),
                        "a distribution or a version is what makes a name nothing is published under");
        });
    }

    /// <summary>
    /// <para>The platforms a build can target are published for tooling to read.</para>
    /// <para>An editor offering the choice cannot work the list out: it depends on which
    /// launchers the SDK installed and which any project has ever published for, both facts
    /// about the machine. So the compiler says, the same way <c>vocabulary</c> says what the
    /// language reserves — one place knows, and nothing keeps a second list to drift.</para>
    /// </summary>
    [Test]
    public void ThePlatformsArePublishedForToolingToRead()
    {
        (int code, string said) = Build("platforms");

        Assert.That(code, Is.Zero, said);

        using JsonDocument published = JsonDocument.Parse(said);

        string[] installed =
        [
            .. published.RootElement.GetProperty("installed")
                        .EnumerateArray()
                        .Select(rid => rid.GetString()!),
        ];

        Assert.Multiple(() =>
        {
            Assert.That(
                published.RootElement.GetProperty("default").GetString(),
                Is.EqualTo(AppHost.ThisPlatform),
                "the default is the one a build with no --runtime uses");

            Assert.That(installed, Is.EqualTo(AppHost.Installed()),
                        "and the list is the one --runtime will accept");

            Assert.That(installed, Is.EqualTo(installed.Order(StringComparer.Ordinal)),
                        "ordered, so a menu built from it does not shuffle between runs");
        });
    }

    [Test]
    public void RuntimeWithNoPlatformIsRefused()
    {
        using Workspace workspace = new();

        (int code, string said) = Build("build", workspace.Program, "--runtime");

        Assert.Multiple(() =>
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(said, Does.Contain("--runtime"));
        });
    }

    [Test]
    public void OutWithNoFolderIsRefused()
    {
        using Workspace workspace = new();

        (int code, string said) = Build("build", workspace.Program, "--out");

        Assert.Multiple(() =>
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(said, Does.Contain("--out"));
            Assert.That(Directory.Exists(Path.Combine(workspace.Folder, "bin")), Is.False);
        });
    }

    /// <summary>
    /// An argument the command does not know is refused rather than passed over. Ignoring one
    /// means a reader who mistyped <c>--out</c> watches the build succeed and then hunts for
    /// files that went where they did not ask.
    /// </summary>
    [Test]
    public void AnArgumentItDoesNotKnowIsRefused()
    {
        using Workspace workspace = new();

        (int code, string said) = Build("build", workspace.Program, "--outt", "somewhere");

        Assert.Multiple(() =>
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(said, Does.Contain("--outt"));
            Assert.That(Directory.Exists(Path.Combine(workspace.Folder, "bin")), Is.False);
        });
    }
}
