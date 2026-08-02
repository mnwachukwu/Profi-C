using System.Runtime.InteropServices;
using System.Text;

namespace ProfiC.Compiler.Emit;

/// <summary>
/// <para>The small native launcher that lets a built program be double-clicked.</para>
/// <para>An emitted assembly is started by <c>dotnet Hello.dll</c>, which is a fair thing to ask
/// of somebody who installed a compiler and an unfair thing to ask of somebody they sent the
/// program to. .NET's answer is the <i>apphost</i>: a stock executable, one per platform, whose
/// only job is to start a named assembly on the installed runtime.</para>
/// <para><b>Nothing here is compiled.</b> The apphost ships prebuilt with the SDK and in the
/// NuGet cache; making one means copying it and writing the assembly's name into a reserved
/// space inside it. That is the whole mechanism, and it is why this can be done without
/// depending on the SDK's build machinery.</para>
/// <para>The runtime is still required on the machine that runs it. A program that needs
/// nothing installed is a different and much larger thing — the framework copied in beside it —
/// and it belongs with releasing rather than with building.</para>
/// </summary>
public static class AppHost
{
    /// <summary>
    /// <para>The space inside the apphost where the assembly's name goes.</para>
    /// <para>It is the SHA-256 of "foobar", written there so that the bytes cannot occur by
    /// accident in a real binary. Around a kilobyte of zeros follows it, which is the room a
    /// path is allowed.</para>
    /// </summary>
    private const string Placeholder =
        "c3ab8ff13720e8ad9047dd39466b3c8974e592c2fa383d4a3960714caef0c4f2";

    /// <summary>How much room the apphost reserves for the name, including its terminator.</summary>
    private const int Reserved = 1024;

    /// <summary>
    /// <para>The platform to build for when nobody said: this machine's, in the most specific
    /// form a launcher is actually on hand for.</para>
    /// <para>What .NET calls the machine and what a launcher is published under are not always
    /// the same string. A runtime may report <c>ubuntu.22.04-x64</c> while every pack in
    /// existence is published as <c>linux-x64</c> — so asking for the reported name and stopping
    /// there means no launcher on the very machine the build is running on, which is the one
    /// case that should always work.</para>
    /// <para>The reported name is still preferred where it does resolve, since a launcher
    /// published for exactly this platform is a better answer than a portable one. Where neither
    /// resolves, the reported name comes back so that the refusal names the real platform.</para>
    /// </summary>
    public static string ThisPlatform
    {
        get
        {
            string reported = RuntimeInformation.RuntimeIdentifier;

            if (CanTarget(reported))
            {
                return reported;
            }

            string portable = PortablePlatform;

            return CanTarget(portable) ? portable : reported;
        }
    }

    /// <summary>
    /// <para>This machine as an operating system and an architecture, which is how .NET
    /// publishes a launcher.</para>
    /// <para>Worked out rather than read, because the reported name may carry a distribution and
    /// a version that no pack is published under.</para>
    /// </summary>
    public static string PortablePlatform
    {
        get
        {
            string system =
                OperatingSystem.IsWindows() ? "win"
                : OperatingSystem.IsMacOS() ? "osx"
                : OperatingSystem.IsLinux() ? "linux"
                : RuntimeInformation.RuntimeIdentifier.Split('-')[0];

            string architecture = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "x64",
                Architecture.X86 => "x86",
                Architecture.Arm64 => "arm64",
                Architecture.Arm => "arm",
                { } other => other.ToString().ToLowerInvariant(),
            };

            return $"{system}-{architecture}";
        }
    }

    /// <summary>Whether a platform wants a <c>.exe</c> on the end of an executable's name.</summary>
    public static bool IsWindows(string runtimeIdentifier) =>
        runtimeIdentifier.StartsWith("win", StringComparison.OrdinalIgnoreCase);

    /// <summary>The name a launcher takes for a program, on the platform it is built for.</summary>
    public static string NameFor(string program, string runtimeIdentifier) =>
        IsWindows(runtimeIdentifier) ? program + ".exe" : program;

    /// <summary>
    /// <para>Makes a launcher beside an assembly, or says why it could not.</para>
    /// <para>The assembly is named relatively, so the pair can be copied anywhere together —
    /// which is what makes the output folder the thing somebody sends.</para>
    /// </summary>
    /// <returns>The launcher's path, or null with the reason in <paramref name="problem"/>.</returns>
    public static string? Create(string assemblyPath, string runtimeIdentifier, out string problem)
    {
        ArgumentNullException.ThrowIfNull(assemblyPath);
        ArgumentNullException.ThrowIfNull(runtimeIdentifier);

        if (Template(runtimeIdentifier) is not { } template)
        {
            problem =
                $"no launcher for '{runtimeIdentifier}' is installed. "
                + $"'dotnet publish -r {runtimeIdentifier}' on any project fetches one, "
                + "or build without --runtime to target this machine.";

            return null;
        }

        string folder = Path.GetDirectoryName(Path.GetFullPath(assemblyPath)) ?? ".";
        string program = Path.GetFileNameWithoutExtension(assemblyPath);
        string launcher = Path.Combine(folder, NameFor(program, runtimeIdentifier));

        byte[] host = File.ReadAllBytes(template);
        byte[] marker = Encoding.UTF8.GetBytes(Placeholder);

        int at = IndexOf(host, marker);

        if (at < 0)
        {
            problem = $"the launcher at '{template}' is not one this knows how to name";
            return null;
        }

        byte[] name = Encoding.UTF8.GetBytes(Path.GetFileName(assemblyPath));

        if (name.Length + 1 > Reserved)
        {
            problem = $"'{Path.GetFileName(assemblyPath)}' is too long a name for a launcher";
            return null;
        }

        // Cleared before writing, so the reserved run holds the name and then nothing. The
        // launcher reads to the first null and would start correctly either way — what this
        // avoids is leaving most of the placeholder in the file behind the terminator, where it
        // reads as a launcher nobody has named yet.
        Array.Clear(host, at, Math.Min(Reserved, host.Length - at));
        name.CopyTo(host, at);

        File.WriteAllBytes(launcher, host);
        MakeRunnable(launcher, runtimeIdentifier);

        problem = string.Empty;
        return launcher;
    }

    /// <summary>
    /// <para>Marks a launcher executable, where the platform being built for has such a notion
    /// and the one building does too.</para>
    /// <para>Building a Linux program on Windows cannot set the bit, because the file system has
    /// nowhere to put it. The launcher is still correct; whoever receives it needs one
    /// <c>chmod +x</c>, and saying so is better than a file that silently will not start.</para>
    /// </summary>
    private static void MakeRunnable(string launcher, string runtimeIdentifier)
    {
        if (IsWindows(runtimeIdentifier) || OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(
            launcher,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }

    /// <summary>Whether a launcher can be made for a platform without fetching anything.</summary>
    public static bool CanTarget(string runtimeIdentifier) => Template(runtimeIdentifier) is not null;

    /// <summary>
    /// <para>The platforms a launcher is already on hand for, in order.</para>
    /// <para>Offered in a message rather than as a promise about .NET: which ones are here
    /// depends on what has been installed and what has been fetched before, and telling somebody
    /// the list is more use than telling them the one they asked for is missing.</para>
    /// </summary>
    public static IReadOnlyList<string> Installed()
    {
        HashSet<string> found = new(StringComparer.OrdinalIgnoreCase);

        foreach (string where in Sources())
        {
            if (!Directory.Exists(where))
            {
                continue;
            }

            foreach (string pack in Directory.EnumerateDirectories(where, Prefix + "*"))
            {
                found.Add(Path.GetFileName(pack)[Prefix.Length..]);
            }
        }

        return [.. found.OrderBy(rid => rid, StringComparer.Ordinal)];
    }

    private const string Prefix = "Microsoft.NETCore.App.Host.";

    /// <summary>
    /// <para>Where a prebuilt launcher may be found: beside the SDK, and in the NuGet cache.
    /// </para>
    /// <para>Both, because they hold different platforms. The SDK carries the ones for the
    /// machine it was installed on; the cache carries whatever any project has ever published
    /// for, which is how a Windows machine comes to have a Linux launcher.</para>
    /// </summary>
    private static IEnumerable<string> Sources()
    {
        if (DotnetRoot() is { } root)
        {
            yield return Path.Combine(root, "packs");
        }

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (home.Length > 0)
        {
            yield return Path.Combine(home, ".nuget", "packages");
        }
    }

    /// <summary>
    /// Where .NET is installed. Taken from the runtime this compiler is itself running on, which
    /// is the one place that cannot be wrong — the environment variable is consulted first only
    /// because somebody who sets it means it.
    /// </summary>
    private static string? DotnetRoot()
    {
        if (Environment.GetEnvironmentVariable("DOTNET_ROOT") is { Length: > 0 } named)
        {
            return named;
        }

        // .../shared/Microsoft.NETCore.App/<version>/ — three above it is the installation.
        DirectoryInfo? directory = new(RuntimeEnvironment.GetRuntimeDirectory());

        for (int up = 0; up < 3 && directory is not null; up++)
        {
            directory = directory.Parent;
        }

        return directory?.FullName;
    }

    /// <summary>
    /// The newest launcher on hand for a platform, or null where there is none. Newest by
    /// version rather than by name, so that 10.0.10 is not passed over for 10.0.3.
    /// </summary>
    private static string? Template(string runtimeIdentifier)
    {
        string file = IsWindows(runtimeIdentifier) ? "apphost.exe" : "apphost";

        return Sources()
            .Where(Directory.Exists)
            .SelectMany(where => Directory.EnumerateDirectories(
                where, Prefix + runtimeIdentifier, SearchOption.TopDirectoryOnly))
            .SelectMany(pack => Directory.EnumerateDirectories(pack))
            .Select(version => (
                Version: Read(Path.GetFileName(version)),
                Path: Path.Combine(
                    version, "runtimes", runtimeIdentifier, "native", file)))
            .Where(found => File.Exists(found.Path))
            .OrderByDescending(found => found.Version)
            .Select(found => found.Path)
            .FirstOrDefault();
    }

    /// <summary>
    /// A pack's version, for choosing the newest. A folder name that is not a version sorts
    /// last rather than throwing — a stray directory in the cache is not this compiler's
    /// problem to report.
    /// </summary>
    private static Version Read(string folder) =>
        Version.TryParse(folder.Split('-')[0], out Version? version) ? version : new Version(0, 0);

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (int at = 0; at + needle.Length <= haystack.Length; at++)
        {
            int matched = 0;

            while (matched < needle.Length && haystack[at + matched] == needle[matched])
            {
                matched++;
            }

            if (matched == needle.Length)
            {
                return at;
            }
        }

        return -1;
    }
}
