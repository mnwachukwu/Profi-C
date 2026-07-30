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
            "a neighboring program's mistakes belong to it");
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
        SemanticModel model = Resolver.Resolve(compilation.Units, diagnostics, projects: compilation.Projects);
        TypeChecker.Check(compilation.Units, model, diagnostics);
        DefiniteAssignment.Analyze(compilation.Units, model, diagnostics);

        Diagnostic reported = diagnostics.Sorted().First(d => d.Id == "PC0400");

        Assert.That(Path.GetFileName(reported.FileName), Is.EqualTo("Helper.pc"));
    }

    [Test]
    public void AFileWithNoNeighborsCompilesAlone()
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
        SemanticModel model = Resolver.Resolve(compilation.Units, diagnostics, requireEntryPoint: true, compilation.Projects);
        TypeChecker.Check(compilation.Units, model, diagnostics);
        DefiniteAssignment.Analyze(compilation.Units, model, diagnostics);

        Assert.That(diagnostics.Sorted().Select(DiagnosticRenderer.Format), Is.Empty);

        StringWriter output = new();
        IReadOnlyList<CompilationUnit> lowered = Lowering.Lower(compilation.Units, model);
        ProfiC.Interpreter.Interpreter.Run(lowered, model, output);

        Assert.That(output.ToString().ReplaceLineEndings("\n"), Is.EqualTo("42\n"));
    }

    // ---- Which program a project starts at ------------------------------------------------

    /// <summary>
    /// <para>Two <c>Program</c>s in different namespaces are two types, not one name used
    /// twice, so nothing collides and the project must say which one begins.</para>
    /// <para>This became reachable when namespaces began to scope. Before that a second
    /// <c>Program</c> was caught as a duplicate name; afterwards nothing caught it, and the
    /// compilation ran whichever file happened to be listed first.</para>
    /// </summary>
    private void WriteTwoPrograms()
    {
        Write("Tools.pc",
            "namespace Tools;\n\nglobal model Program\n    function Main()\n"
            + "        Console.WriteLine(\"tools\");\n    end function\nend model\n");

        Write("App.pc",
            "namespace App;\n\nglobal model Program\n    function Main()\n"
            + "        Console.WriteLine(\"app\");\n    end function\nend model\n");
    }

    /// <summary>Compiles a project and returns what it printed, or the ids of what stopped it.</summary>
    private static (string Output, IReadOnlyList<string> Ids) BuildAndRun(string project)
    {
        DiagnosticBag diagnostics = new();
        SourceDiscovery.Compilation compilation = SourceDiscovery.Gather(project, diagnostics)!;

        SemanticModel model = Resolver.Resolve(
            compilation.Units, diagnostics, requireEntryPoint: true,
            compilation.Projects, compilation.EntryPoint);

        TypeChecker.Check(compilation.Units, model, diagnostics);
        DefiniteAssignment.Analyze(compilation.Units, model, diagnostics);

        string[] ids = [.. diagnostics.Sorted().Select(d => d.Descriptor.Id)];

        if (diagnostics.HasErrors)
        {
            return (string.Empty, ids);
        }

        StringWriter output = new();
        ProfiC.Interpreter.Interpreter.Run(
            Lowering.Lower(compilation.Units, model), model, output, TextReader.Null);

        return (output.ToString().ReplaceLineEndings("\n"), ids);
    }

    [Test]
    public void TwoProgramsWithNoEntryAreAmbiguous()
    {
        WriteTwoPrograms();

        string project = WriteProject("both.pcp", "    source Tools.pc\n    source App.pc\n");

        Assert.That(BuildAndRun(project).Ids, Does.Contain("PC0234"));
    }

    /// <summary>
    /// And the answer does not depend on the order the sources were listed, which is the whole
    /// reason the compiler must not choose for itself.
    /// </summary>
    [TestCase("    entry Tools.Program\n    source Tools.pc\n    source App.pc\n", "tools\n")]
    [TestCase("    entry Tools.Program\n    source App.pc\n    source Tools.pc\n", "tools\n")]
    [TestCase("    entry App.Program\n    source Tools.pc\n    source App.pc\n", "app\n")]
    [TestCase("    entry App.Program\n    source App.pc\n    source Tools.pc\n", "app\n")]
    public void AnEntryDecidesWhichProgramBegins(string body, string expected)
    {
        WriteTwoPrograms();

        Assert.That(BuildAndRun(WriteProject("both.pcp", body)).Output, Is.EqualTo(expected));
    }

    [Test]
    public void AnEntryNamingNoProgramIsRejected()
    {
        WriteTwoPrograms();

        string project = WriteProject("both.pcp",
            "    entry Nowhere.Program\n    source Tools.pc\n    source App.pc\n");

        Assert.That(BuildAndRun(project).Ids, Does.Contain("PC0235"));
    }

    /// <summary>Where there is nothing to choose between, saying so decides nothing.</summary>
    [Test]
    public void AnEntryWhereOnlyOneProgramExistsIsWarnedAbout()
    {
        WriteTwoPrograms();

        string project = WriteProject("one.pcp", "    entry Tools.Program\n    source Tools.pc\n");

        (string output, IReadOnlyList<string> ids) = BuildAndRun(project);

        Assert.Multiple(() =>
        {
            Assert.That(ids, Does.Contain("PC0236"));
            Assert.That(output, Is.EqualTo("tools\n"), "and it still runs");
        });
    }

    [TestCase("    entry\n    source Tools.pc\n", "PC0626", TestName = "an entry naming nothing")]
    [TestCase("    entry Tools.Program\n    entry App.Program\n    source Tools.pc\n",
              "PC0627", TestName = "two entries")]
    public void AnEntryLineIsCheckedForShape(string body, string expected)
    {
        WriteTwoPrograms();

        DiagnosticBag diagnostics = new();
        SourceDiscovery.Gather(WriteProject("bad.pcp", body), diagnostics);

        Assert.That(diagnostics.Select(d => d.Descriptor.Id), Does.Contain(expected));
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

        Resolver.Resolve(compilation.Units, diagnostics, projects: compilation.Projects);

        Diagnostic duplicate = diagnostics.Sorted().First(d => d.Id == "PC0217");

        Assert.Multiple(() =>
        {
            Assert.That(Path.GetFileName(duplicate.FileName), Is.EqualTo("B.pc"));
            Assert.That(duplicate.Message, Does.Contain("A.pc"),
                        "the message says which other file declares it");
        });
    }

    /// <summary>
    /// <para>Where the file system says two names differing only in case are two files, they
    /// are two sources, and both belong in the compilation.</para>
    /// <para>Written to ask the file system rather than to assume the platform: on a volume
    /// that folds case the second write lands on the first, and there is nothing here to test.
    /// Comparing paths the forgiving way on a system that does not fold would silently leave
    /// one of them out.</para>
    /// </summary>
    [Test]
    public void TwoNamesDifferingOnlyInCaseAreTwoSourcesWhereTheSystemSaysSo()
    {
        Write("Helper.pc", SharedModel);
        Write("HELPER.pc", SharedModel.Replace("Helper", "Shouter", StringComparison.Ordinal));

        if (Directory.GetFiles(_folder, "*.pc").Length < 2)
        {
            Assert.Ignore("this file system folds case, so there is only one file here");
        }

        string program = Write("Program.pc", ProgramCalling("Helper.Twice(21)"));

        DiagnosticBag diagnostics = new();
        SourceDiscovery.Compilation compilation = SourceDiscovery.Gather(program, diagnostics)!;

        Assert.That(
            NamesOf(compilation),
            Is.EqualTo(new[] { "HELPER.pc", "Helper.pc", "Program.pc" }),
            "both files beside the program are shared code");
    }

    /// <summary>
    /// A project listing two such files lists two sources, not one twice. Deciding sameness the
    /// forgiving way would reject the second as already present.
    /// </summary>
    [Test]
    public void AProjectMayListTwoNamesDifferingOnlyInCaseWhereTheSystemSaysSo()
    {
        Write("Helper.pc", SharedModel);
        Write("HELPER.pc", SharedModel.Replace("Helper", "Shouter", StringComparison.Ordinal));

        if (Directory.GetFiles(_folder, "*.pc").Length < 2)
        {
            Assert.Ignore("this file system folds case, so there is only one file here");
        }

        string project = Write("both.pcp",
            "project Both\n    source Helper.pc\n    source HELPER.pc\nend project\n");

        DiagnosticBag diagnostics = new();
        SourceDiscovery.Compilation? compilation = SourceDiscovery.Gather(project, diagnostics);

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.Sorted().Select(d => d.Id), Is.Empty);
            Assert.That(compilation!.Units, Has.Count.EqualTo(2));
        });
    }

    // ---- Projects a project asks for ----------------------------------------------------------

    /// <summary>Writes a project file, and returns the path to it.</summary>
    private string WriteProject(string name, string body) =>
        Write(name, $"project {Path.GetFileNameWithoutExtension(name)}\n{body}end project\n");

    /// <summary>
    /// A reference brings the referenced project's files, so its types are there to use.
    /// </summary>
    [Test]
    public void AReferenceBringsTheReferencedProjectsTypes()
    {
        Write("core/Helper.pc", SharedModel);
        WriteProject("core/core.pcp", "    source Helper.pc\n");

        Write("app/Program.pc", ProgramCalling("Helper.Twice(21)"));
        string app = WriteProject("app/app.pcp",
            "    reference ../core/core.pcp\n    source Program.pc\n");

        DiagnosticBag diagnostics = new();
        SourceDiscovery.Compilation compilation = SourceDiscovery.Gather(app, diagnostics)!;

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.Sorted().Select(d => d.Id), Is.Empty);

            Assert.That(
                compilation.Units.Select(u => Path.GetFileName(u.Source.FileName)),
                Is.EqualTo(new[] { "Helper.pc", "Program.pc" }),
                "what a project is built on arrives before the project itself");

            Assert.That(compilation.Label, Is.EqualTo("app"), "the build is named by its root");
        });
    }

    /// <summary>
    /// References are followed to closure, and reach as far as they are chained. A project uses
    /// the types of everything its references bring, which is what a reference brings them for.
    /// </summary>
    [Test]
    public void AReferenceIsFollowedThroughTheProjectItBrings()
    {
        Write("deep/Deep.pc", """
            model Deep
                public global integer function Four()
                    yield 4;
                end function
            end model
            """);

        WriteProject("deep/deep.pcp", "    source Deep.pc\n");

        Write("middle/Middle.pc", """
            model Middle
                public global integer function Eight()
                    yield Deep.Four() * 2;
                end function
            end model
            """);

        WriteProject("middle/middle.pcp",
            "    reference ../deep/deep.pcp\n    source Middle.pc\n");

        Write("app/Program.pc", ProgramCalling("Middle.Eight() + Deep.Four()"));
        string app = WriteProject("app/app.pcp",
            "    reference ../middle/middle.pcp\n    source Program.pc\n");

        DiagnosticBag diagnostics = new();
        SourceDiscovery.Compilation compilation = SourceDiscovery.Gather(app, diagnostics)!;

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.Sorted().Select(d => d.Id), Is.Empty);

            Assert.That(
                compilation.Units.Select(u => Path.GetFileName(u.Source.FileName)),
                Is.EqualTo(new[] { "Deep.pc", "Middle.pc", "Program.pc" }),
                "deepest first, and Deep arrives although the program never named it");
        });
    }

    /// <summary>
    /// Two projects referencing one third bring it once. A project reached twice is one project,
    /// which is what makes a shared project shareable.
    /// </summary>
    [Test]
    public void AProjectReferencedByTwoOthersArrivesOnce()
    {
        Write("core/Helper.pc", SharedModel);
        WriteProject("core/core.pcp", "    source Helper.pc\n");

        Write("left/Left.pc", "model Left\nend model\n");
        WriteProject("left/left.pcp", "    reference ../core/core.pcp\n    source Left.pc\n");

        Write("right/Right.pc", "model Right\nend model\n");
        WriteProject("right/right.pcp", "    reference ../core/core.pcp\n    source Right.pc\n");

        Write("app/Program.pc", ProgramCalling("Helper.Twice(21)"));
        string app = WriteProject("app/app.pcp",
            "    reference ../left/left.pcp\n    reference ../right/right.pcp\n"
            + "    source Program.pc\n");

        DiagnosticBag diagnostics = new();
        SourceDiscovery.Compilation compilation = SourceDiscovery.Gather(app, diagnostics)!;

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.Sorted().Select(d => d.Id), Is.Empty);
            Assert.That(compilation.Units, Has.Count.EqualTo(4), "Helper.pc arrives once");
        });
    }

    /// <summary>
    /// <para>Two projects referencing each other are an error, where two files importing each
    /// other are only a warning.</para>
    /// <para>The difference is what a project is. Files in a circle still all belong to one
    /// compilation, so reading them together is never in question. A reference crosses from one
    /// build to another, and a build that has to exist before itself cannot be produced.</para>
    /// </summary>
    [Test]
    public void TwoProjectsReferencingEachOtherAreRejected()
    {
        Write("left/Left.pc", "model Left\nend model\n");
        Write("right/Right.pc", "model Right\nend model\n");

        WriteProject("right/right.pcp", "    reference ../left/left.pcp\n    source Right.pc\n");
        string left = WriteProject("left/left.pcp",
            "    reference ../right/right.pcp\n    source Left.pc\n");

        DiagnosticBag diagnostics = new();
        SourceDiscovery.Gather(left, diagnostics);

        Diagnostic circle = diagnostics.Sorted().Single(d => d.Id == "PC0624");

        Assert.Multiple(() =>
        {
            Assert.That(circle.Severity, Is.EqualTo(DiagnosticSeverity.Error));

            Assert.That(
                circle.Message,
                Does.Contain("left references right, which references left"));
        });
    }

    /// <summary>A project referencing itself is a circle of one, said the same way.</summary>
    [Test]
    public void AProjectReferencingItselfIsRejected()
    {
        Write("Program.pc", ProgramCalling("\"x\""));
        string project = WriteProject("solo.pcp", "    reference solo.pcp\n    source Program.pc\n");

        DiagnosticBag diagnostics = new();
        SourceDiscovery.Gather(project, diagnostics);

        Assert.That(
            diagnostics.Sorted().Single(d => d.Id == "PC0624").Message,
            Does.Contain("solo references solo"));
    }

    /// <summary>A circle drawn through a third project is one circle, named in full.</summary>
    [Test]
    public void ACircleThroughAThirdProjectIsReportedOnce()
    {
        Write("a/A.pc", "model A\nend model\n");
        Write("b/B.pc", "model B\nend model\n");
        Write("c/C.pc", "model C\nend model\n");

        WriteProject("b/b.pcp", "    reference ../c/c.pcp\n    source B.pc\n");
        WriteProject("c/c.pcp", "    reference ../a/a.pcp\n    source C.pc\n");
        string a = WriteProject("a/a.pcp", "    reference ../b/b.pcp\n    source A.pc\n");

        DiagnosticBag diagnostics = new();
        SourceDiscovery.Gather(a, diagnostics);

        Assert.That(
            diagnostics.Sorted().Where(d => d.Id == "PC0624").Select(d => d.Message).Single(),
            Does.Contain("a references b, which references c, which references a"));
    }

    /// <summary>
    /// One file listed by two projects leaves undecided which project it belongs to. Compiling
    /// it twice would report every type in it as declared twice, which says where the copies
    /// are without saying that nothing was copied.
    /// </summary>
    [Test]
    public void OneFileListedByTwoProjectsIsRejected()
    {
        Write("core/Helper.pc", SharedModel);
        WriteProject("core/core.pcp", "    source Helper.pc\n");

        Write("app/Program.pc", ProgramCalling("Helper.Twice(21)"));
        string app = WriteProject("app/app.pcp",
            "    reference ../core/core.pcp\n    source ../core/Helper.pc\n    source Program.pc\n");

        DiagnosticBag diagnostics = new();
        SourceDiscovery.Gather(app, diagnostics);

        Assert.That(
            diagnostics.Sorted().Single(d => d.Id == "PC0625").Message,
            Does.Contain("'Helper.pc' is listed by core and by app"));
    }

    private static readonly (string Case, string Entry, string Expected)[] BadReferences =
    [
        ("missing", "    reference nowhere.pcp\n", "PC0621"),
        ("not a project", "    reference Program.pc\n", "PC0622"),
        ("no path", "    reference\n", "PC0620"),
    ];

    [TestCaseSource(nameof(BadReferences))]
    public void ABadReferenceIsReported((string Case, string Entry, string Expected) row)
    {
        Write("Program.pc", ProgramCalling("\"x\""));
        string project = WriteProject("bad.pcp", row.Entry + "    source Program.pc\n");

        DiagnosticBag diagnostics = new();
        SourceDiscovery.Gather(project, diagnostics);

        Assert.That(diagnostics.Sorted().Select(d => d.Id), Does.Contain(row.Expected), row.Case);
    }

    /// <summary>Naming one project twice is a mistake in the project file, as a source is.</summary>
    [Test]
    public void AProjectReferencedTwiceIsReported()
    {
        Write("core/Helper.pc", SharedModel);
        WriteProject("core/core.pcp", "    source Helper.pc\n");

        Write("app/Program.pc", ProgramCalling("Helper.Twice(21)"));
        string app = WriteProject("app/app.pcp",
            "    reference ../core/core.pcp\n    reference ../core/core.pcp\n"
            + "    source Program.pc\n");

        DiagnosticBag diagnostics = new();
        SourceDiscovery.Gather(app, diagnostics);

        Assert.That(diagnostics.Sorted().Select(d => d.Id), Does.Contain("PC0623"));
    }

    /// <summary>
    /// A project made only of references builds what they bring. Composition is naming
    /// something, so this is not the project that builds nothing.
    /// </summary>
    [Test]
    public void AProjectOfNothingButReferencesBuildsWhatTheyBring()
    {
        Write("core/Helper.pc", SharedModel);
        WriteProject("core/core.pcp", "    source Helper.pc\n");

        Write("app/Program.pc", ProgramCalling("Helper.Twice(21)"));
        WriteProject("app/app.pcp", "    source Program.pc\n");

        string whole = WriteProject("whole.pcp",
            "    reference core/core.pcp\n    reference app/app.pcp\n");

        DiagnosticBag diagnostics = new();
        SourceDiscovery.Compilation compilation = SourceDiscovery.Gather(whole, diagnostics)!;

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.Sorted().Select(d => d.Id), Is.Empty);
            Assert.That(compilation.Units, Has.Count.EqualTo(2));
        });
    }

    // ---- Files a file asks for ---------------------------------------------------------------

    /// <summary>An import names one file, and that file joins the compilation.</summary>
    [Test]
    public void AnImportBringsTheFileItNames()
    {
        Write("lib/Helper.pc", SharedModel);
        string program = Write("Program.pc",
            "import \"lib/Helper.pc\";\n" + ProgramCalling("Helper.Twice(21)"));

        DiagnosticBag diagnostics = new();
        SourceDiscovery.Compilation compilation = SourceDiscovery.Gather(program, diagnostics)!;

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.Sorted().Select(d => d.Id), Is.Empty);
            Assert.That(NamesOf(compilation), Is.EqualTo(new[] { "Helper.pc", "Program.pc" }));
        });
    }

    /// <summary>
    /// An imported file's own imports come too. Without that it could not compile, so this is
    /// the file carrying its dependencies rather than the program naming many.
    /// </summary>
    [Test]
    public void AnImportIsFollowedThroughTheFileItBrings()
    {
        Write("lib/Deep.pc", """
            model Deep
                public global integer function Four()
                    yield 4;
                end function
            end model
            """);

        Write("lib/Middle.pc", """
            import "Deep.pc";

            model Middle
                public global integer function Eight()
                    yield Deep.Four() * 2;
                end function
            end model
            """);

        string program = Write("Program.pc",
            "import \"lib/Middle.pc\";\n" + ProgramCalling("Middle.Eight()"));

        DiagnosticBag diagnostics = new();
        SourceDiscovery.Compilation compilation = SourceDiscovery.Gather(program, diagnostics)!;

        Assert.That(
            NamesOf(compilation),
            Is.EqualTo(new[] { "Deep.pc", "Middle.pc", "Program.pc" }),
            "Deep arrives although the program never names it");
    }

    // ---- Imports that circle ------------------------------------------------------------------

    /// <summary>
    /// <para>Two files importing each other are warned about, and still build.</para>
    /// <para>A warning rather than an error because nothing here is unbuildable: a compilation
    /// reads every file it gathers together, and reaching one twice adds nothing the first
    /// reach did not. What it costs is a reader with no file to open first.</para>
    /// </summary>
    [Test]
    public void TwoFilesImportingEachOtherAreWarnedAboutAndStillBuild()
    {
        Write("lib/A.pc", "import \"../other/B.pc\";\nmodel A\nend model\n");
        Write("other/B.pc", "import \"../lib/A.pc\";\nmodel B\nend model\n");
        string program = Write("Program.pc", "import \"lib/A.pc\";\n" + ProgramCalling("\"x\""));

        DiagnosticBag diagnostics = new();
        SourceDiscovery.Compilation compilation = SourceDiscovery.Gather(program, diagnostics)!;

        Diagnostic circle = diagnostics.Sorted().Single(d => d.Id == "PC0614");

        Assert.Multiple(() =>
        {
            Assert.That(circle.Severity, Is.EqualTo(DiagnosticSeverity.Warning));
            Assert.That(diagnostics.HasErrors, Is.False, "a circle of files still builds");

            Assert.That(
                circle.Message,
                Does.Contain("A.pc imports B.pc, which imports A.pc"),
                "the circle is read back, and the program that led into it is left out");

            Assert.That(
                NamesOf(compilation),
                Is.EqualTo(new[] { "A.pc", "B.pc", "Program.pc" }),
                "the walk still terminates");
        });
    }

    /// <summary>A file importing itself is a circle of one, said the same way.</summary>
    [Test]
    public void AFileImportingItselfIsReported()
    {
        string program = Write("Program.pc", "import \"Program.pc\";\n" + ProgramCalling("\"x\""));

        DiagnosticBag diagnostics = new();
        SourceDiscovery.Gather(program, diagnostics);

        Assert.That(
            diagnostics.Sorted().Single(d => d.Id == "PC0614").Message,
            Does.Contain("Program.pc imports Program.pc"));
    }

    /// <summary>A circle drawn through a third file is one circle, named in full.</summary>
    [Test]
    public void ACircleThroughAThirdFileIsReportedOnce()
    {
        Write("a/A.pc", "import \"../b/B.pc\";\nmodel A\nend model\n");
        Write("b/B.pc", "import \"../c/C.pc\";\nmodel B\nend model\n");
        Write("c/C.pc", "import \"../a/A.pc\";\nmodel C\nend model\n");
        string program = Write("Program.pc", "import \"a/A.pc\";\n" + ProgramCalling("\"x\""));

        DiagnosticBag diagnostics = new();
        SourceDiscovery.Gather(program, diagnostics);

        Assert.That(
            diagnostics.Sorted().Where(d => d.Id == "PC0614").Select(d => d.Message).Single(),
            Does.Contain("A.pc imports B.pc, which imports C.pc, which imports A.pc"));
    }

    /// <summary>
    /// Two files importing one third file is not a circle. Reaching a file twice is what makes
    /// shared code shared, and only reaching back to a file still waiting on this one is a circle.
    /// </summary>
    [Test]
    public void TwoFilesImportingOneThirdIsNotACircle()
    {
        Write("lib/Shared.pc", SharedModel);
        Write("lib/Left.pc", "import \"Shared.pc\";\nmodel Left\nend model\n");
        Write("lib/Right.pc", "import \"Shared.pc\";\nmodel Right\nend model\n");
        string program = Write("Program.pc",
            "import \"lib/Left.pc\";\nimport \"lib/Right.pc\";\n" + ProgramCalling("\"x\""));

        DiagnosticBag diagnostics = new();
        SourceDiscovery.Compilation compilation = SourceDiscovery.Gather(program, diagnostics)!;

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.Sorted().Select(d => d.Id), Is.Empty);
            Assert.That(compilation.Units, Has.Count.EqualTo(4));
        });
    }

    /// <summary>
    /// The circle is reported against the import that closes it, so the reader is taken to the
    /// line to delete rather than to whichever file the compiler happened to start from.
    /// </summary>
    [Test]
    public void ACircleIsReportedWhereItCloses()
    {
        Write("lib/A.pc", "import \"../other/B.pc\";\nmodel A\nend model\n");
        string closing = Write("other/B.pc", "import \"../lib/A.pc\";\nmodel B\nend model\n");
        string program = Write("Program.pc", "import \"lib/A.pc\";\n" + ProgramCalling("\"x\""));

        DiagnosticBag diagnostics = new();
        SourceDiscovery.Gather(program, diagnostics);

        Diagnostic circle = diagnostics.Sorted().Single(d => d.Id == "PC0614");

        Assert.Multiple(() =>
        {
            Assert.That(Path.GetFullPath(circle.Source!.FileName), Is.EqualTo(Path.GetFullPath(closing)));
            Assert.That(circle.Span.Start.Line, Is.EqualTo(1));
        });
    }

    /// <summary>
    /// A project's files are held to the rule too. The project already names both, so the
    /// imports say nothing a build needs — and what they do say is a circle.
    /// </summary>
    [Test]
    public void ACircleAmongAProjectsFilesIsReported()
    {
        Write("models/Account.pc", "import \"../services/Ledger.pc\";\nmodel Account\nend model\n");
        Write("services/Ledger.pc", "import \"../models/Account.pc\";\nmodel Ledger\nend model\n");
        Write("Program.pc", ProgramCalling("\"x\""));

        string project = Write("Bank.pcp", """
            project Bank
                source Program.pc
                source models
                source services
            end project
            """);

        DiagnosticBag diagnostics = new();
        SourceDiscovery.Gather(project, diagnostics);

        Assert.That(
            diagnostics.Sorted().Single(d => d.Id == "PC0614").Message,
            Does.Contain("Account.pc imports Ledger.pc, which imports Account.pc"));
    }

    /// <summary>
    /// Files beside one another are compiled together without importing at all, so writing the
    /// imports anyway draws a circle where there was none. This is the mistake the rule catches
    /// most often, and the one whose fix is to write nothing.
    /// </summary>
    [Test]
    public void NeighborsThatImportEachOtherAreReported()
    {
        Write("A.pc", "import \"B.pc\";\nmodel A\nend model\n");
        Write("B.pc", "import \"A.pc\";\nmodel B\nend model\n");
        string program = Write("Program.pc", ProgramCalling("\"x\""));

        DiagnosticBag diagnostics = new();
        SourceDiscovery.Gather(program, diagnostics);

        Assert.That(diagnostics.Sorted().Count(d => d.Id == "PC0614"), Is.EqualTo(1));
    }

    /// <summary>
    /// A file reached twice is one file. Importing what the folder rule already found says
    /// nothing, because there is nothing wrong with agreeing.
    /// </summary>
    [Test]
    public void ImportingWhatTheFolderRuleAlreadyFoundIsSilent()
    {
        Write("Helper.pc", SharedModel);
        string program = Write("Program.pc",
            "import \"Helper.pc\";\n" + ProgramCalling("Helper.Twice(21)"));

        DiagnosticBag diagnostics = new();
        SourceDiscovery.Compilation compilation = SourceDiscovery.Gather(program, diagnostics)!;

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.Sorted().Select(d => d.Id), Is.Empty);
            Assert.That(compilation.Units, Has.Count.EqualTo(2), "one file, not two");
        });
    }

    /// <summary>An import overrides the folder rule's reason to skip a file: it was asked for.</summary>
    [Test]
    public void AnImportReachesIntoAnotherFolderWithoutBringingItsNeighbors()
    {
        Write("lib/Helper.pc", SharedModel);
        Write("lib/Unrelated.pc", "model Unrelated\nend model\n");
        string program = Write("Program.pc",
            "import \"lib/Helper.pc\";\n" + ProgramCalling("Helper.Twice(21)"));

        DiagnosticBag diagnostics = new();
        SourceDiscovery.Compilation compilation = SourceDiscovery.Gather(program, diagnostics)!;

        Assert.That(
            NamesOf(compilation),
            Is.EqualTo(new[] { "Helper.pc", "Program.pc" }),
            "Unrelated.pc sits beside Helper.pc and was not named");
    }

    private static readonly (string Case, string Import, string Expected)[] BadImports =
    [
        ("missing", "import \"nowhere.pc\";", "PC0611"),
        ("not Profi-C", "import \"notes.txt\";", "PC0612"),
    ];

    [TestCaseSource(nameof(BadImports))]
    public void ABadImportIsReported((string Case, string Import, string Expected) row)
    {
        Write("notes.txt", "not a program");
        string program = Write("Program.pc", row.Import + "\n" + ProgramCalling("\"x\""));

        DiagnosticBag diagnostics = new();
        SourceDiscovery.Gather(program, diagnostics);

        Assert.That(diagnostics.Sorted().Select(d => d.Id), Does.Contain(row.Expected), row.Case);
    }

    /// <summary>
    /// An absolute path is a warning, not an error: the program is correct, and correct here.
    /// It stops being correct anywhere else, which is worth saying before the file travels.
    /// </summary>
    [Test]
    public void AnAbsoluteImportWarnsAndStillCompiles()
    {
        string helper = Write("Helper.pc", SharedModel);
        string program = Write("Program.pc",
            $"import \"{helper.Replace('\\', '/')}\";\n" + ProgramCalling("Helper.Twice(21)"));

        DiagnosticBag diagnostics = new();
        SourceDiscovery.Compilation compilation = SourceDiscovery.Gather(program, diagnostics)!;

        Diagnostic warned = diagnostics.Sorted().Single(d => d.Id == "PC0613");

        Assert.Multiple(() =>
        {
            Assert.That(warned.Severity, Is.EqualTo(DiagnosticSeverity.Warning));
            Assert.That(diagnostics.HasErrors, Is.False);
            Assert.That(compilation.Units, Has.Count.EqualTo(2));
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
            # A project across two folders.

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
