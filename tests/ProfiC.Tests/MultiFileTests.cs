using ProfiC.Cli;
using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Semantics;

namespace ProfiC.Tests;

/// <summary>
/// <para>Which files a command compiles, given the one that was named.</para>
/// <para>The rule is that a file declaring <c>Program</c> is a program and everything else in
/// its folder is shared code. That lets a folder hold many programs at once, which is what a
/// folder of exercises or of half-finished ideas actually looks like, while a program split
/// across files needs nothing said to hold it together.</para>
/// </summary>
[TestFixture]
public sealed class MultiFileTests : LexerTestBase
{
    private string _folder = string.Empty;

    [SetUp]
    public void CreateFolder()
    {
        _folder = Path.Combine(Path.GetTempPath(), "profi-c-tests", TestContext.CurrentContext.Test.ID);
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

    private string Write(string name, string contents)
    {
        string path = Path.Combine(_folder, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
        return path;
    }

    private static string[] NamesOf(SourceDiscovery.Compilation compilation) =>
        [.. compilation.Units.Select(u => Path.GetFileName(u.Source.FileName)).Order(StringComparer.Ordinal)];

    private const string SharedModel = """
        model Helper
            public global integer function Twice(integer n)
                yield n * 2;
            end function
        end model
        """;

    private static string ProgramCalling(string what) =>
        $$"""
        global model Program
            function Main()
                Console.WriteLine({{what}});
            end function
        end model
        """;

    // ---- The folder rule ------------------------------------------------------------------

    [Test]
    public void SharedCodeBesideAProgramIsCompiledWithIt()
    {
        Write("Helper.pc", SharedModel);
        string program = Write("Program.pc", ProgramCalling("Helper.Twice(21)"));

        DiagnosticBag diagnostics = new();
        SourceDiscovery.Compilation? compilation = SourceDiscovery.Gather(program, diagnostics);

        Assert.That(compilation, Is.Not.Null);
        Assert.That(NamesOf(compilation!), Is.EqualTo(new[] { "Helper.pc", "Program.pc" }));
    }

    [Test]
    public void AnotherProgramInTheSameFolderIsLeftAlone()
    {
        Write("Helper.pc", SharedModel);
        Write("Other.pc", ProgramCalling("\"other\""));
        string program = Write("Program.pc", ProgramCalling("Helper.Twice(21)"));

        DiagnosticBag diagnostics = new();
        SourceDiscovery.Compilation? compilation = SourceDiscovery.Gather(program, diagnostics);

        Assert.That(NamesOf(compilation!), Is.EqualTo(new[] { "Helper.pc", "Program.pc" }));
        Assert.That(diagnostics.Sorted().Select(d => d.Id), Is.Empty);
    }

    /// <summary>
    /// Each program in a folder is its own, so a mistake in one is not visited on the others.
    /// A folder someone is working in almost always has something half-written in it.
    /// </summary>
    [Test]
    public void ABrokenProgramBesideOneDoesNotBreakIt()
    {
        Write("Helper.pc", SharedModel);
        Write("Broken.pc", "global model Program function Main() this is not a program ((( ");
        string program = Write("Program.pc", ProgramCalling("Helper.Twice(21)"));

        DiagnosticBag diagnostics = new();
        SourceDiscovery.Compilation? compilation = SourceDiscovery.Gather(program, diagnostics);

        Assert.That(NamesOf(compilation!), Is.EqualTo(new[] { "Helper.pc", "Program.pc" }));
        Assert.That(
            diagnostics.Sorted().Select(d => $"{d.FileName}: {d.Id}"),
            Is.Empty,
            "a neighbouring program's mistakes belong to it");
    }

    /// <summary>Shared code is shared, so a mistake in it is everyone's.</summary>
    [Test]
    public void AMistakeInSharedCodeIsReportedAgainstTheSharedFile()
    {
        Write("Helper.pc", """
            model Helper
                public global integer function Twice(integer n)
                    integer answer;
                    yield answer;
                end function
            end model
            """);

        string program = Write("Program.pc", ProgramCalling("Helper.Twice(21)"));

        DiagnosticBag diagnostics = new();
        SourceDiscovery.Compilation compilation = SourceDiscovery.Gather(program, diagnostics)!;
        SemanticModel model = Resolver.Resolve(compilation.Units, diagnostics);
        TypeChecker.Check(compilation.Units, model, diagnostics);
        DefiniteAssignment.Analyze(compilation.Units, model, diagnostics);

        Diagnostic reported = diagnostics.Sorted().First(d => d.Id == "PC0400");

        Assert.That(Path.GetFileName(reported.FileName), Is.EqualTo("Helper.pc"));
    }

    [Test]
    public void AFileWithNoNeighboursCompilesAlone()
    {
        string program = Write("Program.pc", ProgramCalling("\"alone\""));

        DiagnosticBag diagnostics = new();

        Assert.That(NamesOf(SourceDiscovery.Gather(program, diagnostics)!),
                    Is.EqualTo(new[] { "Program.pc" }));
    }

    /// <summary>A subfolder is another place, and is reached only by a project that names it.</summary>
    [Test]
    public void TheFolderRuleDoesNotDescend()
    {
        Write("inner/Helper.pc", SharedModel);
        string program = Write("Program.pc", ProgramCalling("\"top\""));

        DiagnosticBag diagnostics = new();

        Assert.That(NamesOf(SourceDiscovery.Gather(program, diagnostics)!),
                    Is.EqualTo(new[] { "Program.pc" }));
    }

    // ---- Compiling more than one file ------------------------------------------------------

    [Test]
    public void AProgramSplitAcrossFilesRuns()
    {
        Write("Helper.pc", SharedModel);
        string program = Write("Program.pc", ProgramCalling("Helper.Twice(21)"));

        DiagnosticBag diagnostics = new();
        SourceDiscovery.Compilation compilation = SourceDiscovery.Gather(program, diagnostics)!;
        SemanticModel model = Resolver.Resolve(compilation.Units, diagnostics, requireEntryPoint: true);
        TypeChecker.Check(compilation.Units, model, diagnostics);
        DefiniteAssignment.Analyze(compilation.Units, model, diagnostics);

        Assert.That(diagnostics.Sorted().Select(DiagnosticRenderer.Format), Is.Empty);

        StringWriter output = new();
        IReadOnlyList<CompilationUnit> lowered = Lowering.Lower(compilation.Units, model);
        ProfiC.Interpreter.Interpreter.Run(lowered, model, output);

        Assert.That(output.ToString().ReplaceLineEndings("\n"), Is.EqualTo("42\n"));
    }

    /// <summary>
    /// Two files each declaring Program cannot be one compilation. The folder rule keeps them
    /// apart, and a project that lists both is told plainly.
    /// </summary>
    [Test]
    public void TwoProgramsInOneCompilationAreRejected()
    {
        Write("A.pc", ProgramCalling("\"a\""));
        Write("B.pc", ProgramCalling("\"b\""));
        Write("both.pcp", "project Both\n    source A.pc\n    source B.pc\nend project\n");

        DiagnosticBag diagnostics = new();
        SourceDiscovery.Compilation compilation =
            SourceDiscovery.Gather(Path.Combine(_folder, "both.pcp"), diagnostics)!;

        Resolver.Resolve(compilation.Units, diagnostics);

        Diagnostic duplicate = diagnostics.Sorted().First(d => d.Id == "PC0218");

        Assert.Multiple(() =>
        {
            Assert.That(Path.GetFileName(duplicate.FileName), Is.EqualTo("B.pc"));
            Assert.That(duplicate.Message, Does.Contain("A.pc"),
                        "the message says which other file declares it");
        });
    }

    // ---- Finding the file that was named ----------------------------------------------------

    [Test]
    public void AnExtensionMayBeLeftOffASource()
    {
        Write("Program.pc", ProgramCalling("\"x\""));

        SourceDiscovery.FileTarget? target =
            SourceDiscovery.Locate(Path.Combine(_folder, "Program"), out string problem);

        Assert.Multiple(() =>
        {
            Assert.That(problem, Is.Empty);
            Assert.That(Path.GetFileName(target!.Value.Path), Is.EqualTo("Program.pc"));
            Assert.That(target.Value.IsProject, Is.False);
        });
    }

    [Test]
    public void AnExtensionMayBeLeftOffAProject()
    {
        Write("Program.pc", ProgramCalling("\"x\""));
        Write("build.pcp", "project Build\n    source Program.pc\nend project\n");

        SourceDiscovery.FileTarget? target =
            SourceDiscovery.Locate(Path.Combine(_folder, "build"), out _);

        Assert.Multiple(() =>
        {
            Assert.That(Path.GetFileName(target!.Value.Path), Is.EqualTo("build.pcp"));
            Assert.That(target.Value.IsProject, Is.True);
        });
    }

    /// <summary>
    /// The one case leaving the extension off cannot answer. Writing it is the way to say
    /// which is meant, so the message asks for that rather than choosing.
    /// </summary>
    [Test]
    public void ANameMeaningBothIsAmbiguous()
    {
        Write("Program.pc", ProgramCalling("\"x\""));
        Write("Program.pcp", "project P\n    source Program.pc\nend project\n");

        Assert.Multiple(() =>
        {
            Assert.That(
                SourceDiscovery.Locate(Path.Combine(_folder, "Program"), out string problem),
                Is.Null);

            Assert.That(problem, Does.Contain("Program.pc").And.Contain("Program.pcp"));
        });
    }

    [Test]
    public void AWrittenExtensionIsExact()
    {
        Write("Program.pc", ProgramCalling("\"x\""));
        Write("Program.pcp", "project P\n    source Program.pc\nend project\n");

        Assert.Multiple(() =>
        {
            Assert.That(
                SourceDiscovery.Locate(Path.Combine(_folder, "Program.pc"), out _)!.Value.IsProject,
                Is.False);

            Assert.That(
                SourceDiscovery.Locate(Path.Combine(_folder, "Program.pcp"), out _)!.Value.IsProject,
                Is.True);
        });
    }

    /// <summary>A file is Profi-C because it says so, not because something tried to read it.</summary>
    [Test]
    public void AnythingElseIsRefused()
    {
        Write("notes.txt", "global model Program function Main() end function end model");

        Assert.Multiple(() =>
        {
            Assert.That(
                SourceDiscovery.Locate(Path.Combine(_folder, "notes.txt"), out string problem),
                Is.Null);

            Assert.That(problem, Is.EqualTo("Not a valid Profi-C source or project file."));
        });
    }

    [Test]
    public void AMissingFileSaysBothNamesItLookedFor()
    {
        Assert.That(
            SourceDiscovery.Locate(Path.Combine(_folder, "absent"), out string problem),
            Is.Null);

        Assert.That(problem, Does.Contain("absent.pc").And.Contain("absent.pcp"));
    }

    // ---- Project files ---------------------------------------------------------------------

    [Test]
    public void AProjectCompilesWhatItLists()
    {
        Write("src/Helper.pc", SharedModel);
        Write("Program.pc", ProgramCalling("Helper.Twice(21)"));
        string project = Write("thing.pcp", """
            comment A project across two folders.

            project Thing
                source Program.pc
                source src
            end project
            """);

        DiagnosticBag diagnostics = new();
        SourceDiscovery.Compilation? compilation = SourceDiscovery.Gather(project, diagnostics);

        Assert.That(diagnostics.Sorted().Select(DiagnosticRenderer.Format), Is.Empty);
        Assert.That(compilation!.Label, Is.EqualTo("Thing"));
        Assert.That(NamesOf(compilation), Is.EqualTo(new[] { "Helper.pc", "Program.pc" }));
    }

    /// <summary>A project lists its sources in order, and that order is what gets compiled.</summary>
    [Test]
    public void AProjectKeepsTheOrderItListed()
    {
        Write("Program.pc", ProgramCalling("Helper.Twice(21)"));
        Write("Helper.pc", SharedModel);
        string project = Write("ordered.pcp",
            "project Ordered\n    source Program.pc\n    source Helper.pc\nend project\n");

        DiagnosticBag diagnostics = new();
        SourceDiscovery.Compilation compilation = SourceDiscovery.Gather(project, diagnostics)!;

        Assert.That(
            compilation.Units.Select(u => Path.GetFileName(u.Source.FileName)),
            Is.EqualTo(new[] { "Program.pc", "Helper.pc" }));
    }

    private static readonly (string Case, string Contents, string Expected)[] BadProjects =
    [
        ("no header", "source Program.pc\n", "PC0601"),
        ("no name", "project\n    source Program.pc\nend project\n", "PC0602"),
        ("not closed", "project Thing\n    source Program.pc\n", "PC0603"),
        ("unknown entry", "project Thing\n    include Program.pc\nend project\n", "PC0604"),
        ("source with no path", "project Thing\n    source\nend project\n", "PC0605"),
        ("source not found", "project Thing\n    source nowhere.pc\nend project\n", "PC0606"),
        ("wrong extension", "project Thing\n    source bad.pcp\nend project\n", "PC0607"),
        ("listed twice",
         "project Thing\n    source Program.pc\n    source Program.pc\nend project\n", "PC0608"),
        ("empty folder", "project Thing\n    source hollow\nend project\n", "PC0609"),
        ("no sources", "project Thing\nend project\n", "PC0610"),
    ];

    [TestCaseSource(nameof(BadProjects))]
    public void ABadProjectIsReported((string Case, string Contents, string Expected) row)
    {
        Write("Program.pc", ProgramCalling("\"x\""));
        Write("bad.pcp", "project Bad\nend project\n");
        Directory.CreateDirectory(Path.Combine(_folder, "hollow"));

        string project = Write("thing.pcp", row.Contents);

        DiagnosticBag diagnostics = new();

        Assert.That(SourceDiscovery.Gather(project, diagnostics), Is.Null, row.Case);
        Assert.That(diagnostics.Sorted().Select(d => d.Id), Does.Contain(row.Expected), row.Case);
    }

    /// <summary>A project file that cannot be read reports it rather than throwing.</summary>
    [Test]
    public void AMissingProjectIsReported()
    {
        DiagnosticBag diagnostics = new();

        Assert.That(
            SourceDiscovery.Gather(Path.Combine(_folder, "absent.pcp"), diagnostics),
            Is.Null);

        Assert.That(diagnostics.Sorted().Select(d => d.Id), Does.Contain("PC0600"));
    }
}
