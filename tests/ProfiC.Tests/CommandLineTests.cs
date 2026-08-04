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

        shared model Program
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
            shared model Program
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
            shared model Program
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
            Assert.That(result.ExitCode, Is.EqualTo(2), "the line named a file, and it is not one");
            Assert.That(result.Output + result.Error, Does.Contain("not found"));
            Assert.That(result.Error, Does.Not.Contain("Unhandled exception"));
        });
    }

    // ---- Telling the ways this ends apart ----------------------------------------------------

    /// <summary>
    /// <para>The three failures a caller can do something different about.</para>
    /// <para>One code for all of them answers "did it work", which is all a person at a terminal
    /// wants. Anything scripting this wants more: a program with a mistake in it is the ordinary
    /// result of checking one and belongs to whoever wrote the code, while a command line that
    /// could not be read belongs to whoever wrote the script.</para>
    /// </summary>
    [Test]
    public void TheExitCodeSaysWhichKindOfWrong()
    {
        string command = (string)Commands.First().Arguments[0]!;
        string good = Write("Program.pc", Greeting);

        // In a folder of its own, because naming a file compiles the shared code beside it —
        // so a broken file next door makes the good one fail and the test prove nothing.
        Directory.CreateDirectory(Path.Combine(_folder, "apart"));

        string bad = Write(Path.Combine("apart", "Program.pc"), """
            shared model Program
                function Main()
                    integer n = "not a number";
                end function
            end model
            """);

        Assert.Multiple(() =>
        {
            Assert.That(Run(command, "check", good).ExitCode, Is.Zero, "nothing wrong");
            Assert.That(Run(command, "check", bad).ExitCode, Is.EqualTo(1), "the program");
            Assert.That(Run(command, "check").ExitCode, Is.EqualTo(2), "no file named");
            Assert.That(Run(command, "wat", good).ExitCode, Is.EqualTo(2), "no such command");
            Assert.That(
                Run(command, "build", good, "--nonsense").ExitCode,
                Is.EqualTo(2),
                "no such option");
        });
    }

    /// <summary>
    /// <para>A word beginning with a dash is a flag, wherever it sits.</para>
    /// <para>Read as a path, <c>build --help</c> answers "file not found: --help.pc" — a file
    /// nobody meant, about a question nobody asked, which reads as though the tool were broken
    /// rather than as though the line were.</para>
    /// </summary>
    [TestCaseSource(nameof(Commands))]
    public void AskingACommandWhatItTakesIsNotAskingForAFile(string command)
    {
        Result result = Run(command, "build", "--help");

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero, result.Error);
            Assert.That(result.Output, Does.Contain("--runtime"), "what build takes");
            Assert.That(result.Output + result.Error, Does.Not.Contain("not found"));
        });
    }

    /// <summary>
    /// <para>Every command the help lists is a command that answers.</para>
    /// <para>The half of the drift a reader meets: a name in the list that does nothing. The
    /// other half — a command that runs and is listed nowhere — is what happened to
    /// <c>format</c>, and is now impossible rather than untested, because the list and the
    /// dispatch are one table.</para>
    /// </summary>
    [Test]
    public void EveryCommandInTheHelpAnswers()
    {
        string command = (string)Commands.First().Arguments[0]!;
        Result help = Run(command, "--help");

        string[] listed =
        [
            .. help.Output.Split('\n')
                .Select(line => line.Trim().Split(' '))
                .Where(words => words.Length > 1 && words[0] is "profi-c" or "pc")
                .Select(words => words[1])
                .Where(word => !word.StartsWith('-')),
        ];

        Assert.That(listed, Is.Not.Empty, "the help lists nothing");
        Assert.That(listed, Does.Contain("format"), "the one that went missing");

        Assert.Multiple(() =>
        {
            foreach (string listedCommand in listed)
            {
                Result asked = Run(command, listedCommand, "--help");

                Assert.That(
                    asked.Output + asked.Error,
                    Does.Not.Contain("unknown command"),
                    $"'{listedCommand}' is listed and does nothing");
            }
        });
    }

    // ---- Starting something new --------------------------------------------------------------

    /// <summary>
    /// <para>What <c>new</c> writes is empty, and it runs.</para>
    /// <para><b>Both halves matter and they pull against each other.</b> Empty is the point —
    /// somebody who asked for a new program is about to write one, and every line already there
    /// is a line to read and delete. But an empty file that does not compile would be a worse
    /// start than a full one, so what "empty" means here is the smallest program the language
    /// accepts rather than nothing at all.</para>
    /// </summary>
    [TestCaseSource(nameof(Commands))]
    public void NewWritesAnEmptyProgramThatRuns(string command)
    {
        Result written = Run(command, "new", Path.Combine(_folder, "hello"));

        Assert.That(written.ExitCode, Is.Zero, written.Error);

        Result ran = Run(command, "run", Path.Combine(_folder, "hello.pc"));

        Assert.Multiple(() =>
        {
            Assert.That(ran.ExitCode, Is.Zero, ran.Error);
            Assert.That(ran.Output, Is.Empty, "it compiles, it runs, and it does nothing");
            Assert.That(
                File.ReadAllText(Path.Combine(_folder, "hello.pc")),
                Does.Not.Contain("Console"),
                "and there is nothing in it to delete");
        });
    }

    /// <summary>
    /// The other command, and the one that answers "what does this language look like" — which is
    /// a different question from "give me somewhere to start typing".
    /// </summary>
    [TestCaseSource(nameof(Commands))]
    public void SampleWritesAProgramThatDoesSomething(string command)
    {
        Result written = Run(command, "sample", Path.Combine(_folder, "tour"));

        Assert.That(written.ExitCode, Is.Zero, written.Error);

        Result ran = Run(command, "run", Path.Combine(_folder, "tour.pc"));

        Assert.Multiple(() =>
        {
            Assert.That(ran.ExitCode, Is.Zero, ran.Error);
            Assert.That(ran.Output, Does.Contain("Hello, World!"));
            Assert.That(ran.Output, Does.Contain("squared"), "and it loops");
        });
    }

    /// <summary>
    /// Nothing is written over. A tool that scaffolds is one somebody points at a folder they
    /// are already working in, and a file lost to a mistyped name is not something undo reaches.
    /// </summary>
    [TestCaseSource(nameof(Commands))]
    public void NewRefusesToWriteOverWhatIsThere(string command)
    {
        _ = Write("taken.pc", Greeting);

        Result result = Run(command, "new", Path.Combine(_folder, "taken"));

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(2));
            Assert.That(result.Error, Does.Contain("already there"));
            Assert.That(
                File.ReadAllText(Path.Combine(_folder, "taken.pc")),
                Does.Contain("Hello, World!"),
                "and the file it refused is the one that was there");
        });
    }

    /// <summary>The other form, which both commands take: a folder holding a project.</summary>
    [TestCaseSource(nameof(Commands))]
    public void NewWritesAProjectThatRuns(string command)
    {
        Result written = Run(command, "sample", Path.Combine(_folder, "store"), "--project");

        Assert.That(written.ExitCode, Is.Zero, written.Error);

        Result ran = Run(command, "run", Path.Combine(_folder, "store", "store.pcp"));

        Assert.Multiple(() =>
        {
            Assert.That(ran.ExitCode, Is.Zero, ran.Error);
            Assert.That(ran.Output, Does.Contain("Hello, World!"));
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
