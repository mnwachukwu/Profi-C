using System.Diagnostics;

namespace ProfiC.Tests;

/// <summary>
/// <para>Runs the command a reader actually types, as a separate process.</para>
/// <para>Every other test in this suite reaches the compiler in process, which means all of
/// them would pass against an executable that was missing, stale, or broken — the pipeline is
/// exercised, and the thing shipped around it never is. These launch it.</para>
/// <para>Both names, because <c>pc</c> is a second executable rather than a copy of the first:
/// it forwards to the same entry point, and nothing else checks that it still does.</para>
/// </summary>
[TestFixture]
public sealed class CommandLineTests : LexerTestBase
{
    private string _folder = string.Empty;

    [SetUp]
    public void CreateFolder()
    {
        _folder = Path.Combine(
            Path.GetTempPath(), "profi-c-cli", TestContext.CurrentContext.Test.ID);

        Directory.CreateDirectory(_folder);
    }

    [TearDown]
    public void RemoveFolder()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    /// <summary>
    /// <para>The two executables, found beside this test's own output.</para>
    /// <para>Located by taking the path from the test project to where its assembly landed —
    /// <c>bin/Debug/net10.0</c> or whichever it was — and reading it under each command's
    /// project. That way the configuration and framework being tested are always the ones just
    /// built, rather than whichever was built last.</para>
    /// </summary>
    public static IEnumerable<TestCaseData> Commands
    {
        get
        {
            string tail = Path.GetRelativePath(
                Path.Combine(RepositoryRootForTests, "tests", "ProfiC.Tests"),
                AppContext.BaseDirectory);

            foreach ((string project, string name) in
                     new[] { ("ProfiC.Cli", "profi-c"), ("ProfiC.Cli.Alias", "pc") })
            {
                string path = Path.GetFullPath(Path.Combine(
                    RepositoryRootForTests, "src", project, tail,
                    OperatingSystem.IsWindows() ? name + ".exe" : name));

                yield return new TestCaseData(path).SetName($"{{m}}({name})");
            }
        }
    }

    private sealed record Result(int ExitCode, string Output, string Error);

    private static Result Run(string command, params string[] arguments)
    {
        Assert.That(File.Exists(command), Is.True, $"{command} was never built");

        ProcessStartInfo start = new()
        {
            FileName = command,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start)!;

        // Read before waiting: a process filling a redirected pipe blocks until it is drained,
        // so waiting first would hang on any output longer than the buffer.
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();

        Assert.That(
            process.WaitForExit(30_000),
            Is.True,
            $"{Path.GetFileName(command)} did not finish");

        return new Result(process.ExitCode, output.ReplaceLineEndings("\n"), error);
    }

    private string Write(string name, string contents)
    {
        string path = Path.Combine(_folder, name);
        File.WriteAllText(path, contents);
        return path;
    }

    private const string Greeting = """
        ##
            A program written for this test rather than taken from the samples, so that what is
            being checked is the command rather than the corpus beside it.
        ##

        global model Program
            function Main()
                Console.WriteLine("Hello, World!");
            end function
        end model
        """;

    // ---- What the command has to do ---------------------------------------------------------

    [TestCaseSource(nameof(Commands))]
    public void RunningAProgramPrintsItAndSucceeds(string command)
    {
        Result result = Run(command, "run", Write("Program.pc", Greeting));

        Assert.Multiple(() =>
        {
            Assert.That(result.Output, Is.EqualTo("Hello, World!\n"));
            Assert.That(result.ExitCode, Is.Zero, result.Error);
        });
    }

    [TestCaseSource(nameof(Commands))]
    public void CheckingAGoodProgramSucceeds(string command)
    {
        Result result = Run(command, "check", Write("Program.pc", Greeting));

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero, result.Error);
            Assert.That(result.Output, Does.Contain("ok"));
        });
    }

    /// <summary>
    /// A refusal has to reach the exit code, since that is the only part of it a build script
    /// or an editor's problem matcher reads.
    /// </summary>
    [TestCaseSource(nameof(Commands))]
    public void CheckingABadProgramFails(string command)
    {
        string path = Write("Program.pc", """
            global model Program
                function Main()
                    integer n = "not a number";
                end function
            end model
            """);

        Result result = Run(command, "check", path);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(1));
            Assert.That(result.Output + result.Error, Does.Contain("PC0300"));
        });
    }

    /// <summary>
    /// <para>The comment syntax, run through the shipped command rather than the lexer.</para>
    /// <para>This is the shape a stale executable takes: the compiler in the repository reads
    /// the file and the one on a reader's path does not, and every in-process test agrees with
    /// the first of them.</para>
    /// </summary>
    [TestCaseSource(nameof(Commands))]
    public void TheCommandReadsTheCommentSyntaxTheCompilerDoes(string command)
    {
        string path = Write("Program.pc", """
            # a line comment
            ##
                a block comment, whose text is not code
            ##
            global model Program
                function Main()          # and one at the end of a line
                    Console.WriteLine("read");
                end function
            end model
            """);

        Result result = Run(command, "run", path);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero, result.Output + result.Error);
            Assert.That(result.Output, Is.EqualTo("read\n"));
        });
    }

    [TestCaseSource(nameof(Commands))]
    public void AFileThatIsNotThereFailsWithoutThrowing(string command)
    {
        Result result = Run(command, "run", Path.Combine(_folder, "nowhere.pc"));

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(1));
            Assert.That(result.Output + result.Error, Does.Contain("not found"));
            Assert.That(result.Error, Does.Not.Contain("Unhandled exception"));
        });
    }

    /// <summary>Both names are the same command, so they answer the same way.</summary>
    [Test]
    public void TheAliasAnswersAsTheCommandDoes()
    {
        string[] commands = [.. Commands.Select(c => (string)c.Arguments[0]!)];
        string program = Write("Program.pc", Greeting);

        Result first = Run(commands[0], "run", program);
        Result second = Run(commands[1], "run", program);

        Assert.Multiple(() =>
        {
            Assert.That(second.Output, Is.EqualTo(first.Output));
            Assert.That(second.ExitCode, Is.EqualTo(first.ExitCode));
        });
    }
}
