using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Semantics;
using ProfiC.Compiler.Text;

namespace ProfiC.Tests.Semantics;

/// <summary>
/// <para>How far a declaration reaches, and what happens when something reaches further.</para>
/// <para>Two defaults, and they are one rule applied twice: a declaration with no word belongs
/// to the smallest thing that could own it. A member's owner is its type, so silence means
/// private. A type's owner is its project, so silence means internal.</para>
/// </summary>
[TestFixture]
public sealed class VisibilityTests
{
    /// <summary>Checks one file, which belongs to the single project such a compilation has.</summary>
    private static string[] Check(string source) => Check(source, projects: null);

    private static string[] Check(string source, IReadOnlyDictionary<SourceText, string>? projects)
    {
        DiagnosticBag diagnostics = new();
        CompilationUnit unit = Parser.Parse(new SourceText(source, "<test>"), diagnostics);
        SemanticModel model = Resolver.Resolve([unit], diagnostics, projects: projects);
        TypeChecker.Check(unit, model, diagnostics);

        return [.. diagnostics.Sorted().Select(d => d.Id)];
    }

    /// <summary>Checks two files placed in two named projects, as a reference would place them.</summary>
    private static string[] CheckAcrossProjects(string first, string second)
    {
        DiagnosticBag diagnostics = new();

        CompilationUnit one = Parser.Parse(new SourceText(first, "Core.pc"), diagnostics);
        CompilationUnit two = Parser.Parse(new SourceText(second, "App.pc"), diagnostics);

        Dictionary<SourceText, string> projects = new()
        {
            [one.Source] = "Core",
            [two.Source] = "App",
        };

        SemanticModel model = Resolver.Resolve([one, two], diagnostics, projects: projects);
        TypeChecker.Check([one, two], model, diagnostics);

        return [.. diagnostics.Sorted().Select(d => d.Id)];
    }

    /// <summary>
    /// <para>Runs files placed in named projects and returns what they printed.</para>
    /// <para>Accepting a program and running it correctly are different claims, and a
    /// visibility rule is only worth having if the second holds: a member the compiler agrees
    /// may be reached must actually carry its value when the program reaches it.</para>
    /// </summary>
    private static string RunAcrossProjects(
        IReadOnlyDictionary<string, string> filesByProject,
        params string[] sources)
    {
        DiagnosticBag diagnostics = new();
        List<CompilationUnit> units = [];
        Dictionary<SourceText, string> projects = [];

        foreach ((string file, string project) in filesByProject)
        {
            CompilationUnit unit = Parser.Parse(
                new SourceText(sources[units.Count], file), diagnostics);

            units.Add(unit);
            projects[unit.Source] = project;
        }

        SemanticModel model = Resolver.Resolve(
            units, diagnostics, projects: projects, requireEntryPoint: true);

        TypeChecker.Check(units, model, diagnostics);
        DefiniteAssignment.Analyze(units, model, diagnostics);

        Assert.That(
            diagnostics.Select(d => $"{d.Id}: {d.Message}"),
            Is.Empty,
            "the program should check cleanly before it is run");

        StringWriter output = new();
        ProfiC.Interpreter.Interpreter.Run(Lowering.Lower(units, model), model, output);

        return output.ToString().ReplaceLineEndings("\n");
    }

    // ---- What internal reaches, and what it carries when it gets there ----------------------

    /// <summary>
    /// The value arrives. Accepting the read is half the rule; the other half is that the
    /// member holds what it was given, across a file boundary the compiler had to allow.
    /// </summary>
    [Test]
    public void AnInternalMemberCarriesItsValueAcrossFilesInOneProject() => Assert.That(
        RunAcrossProjects(
            new Dictionary<string, string> { ["Box.pc"] = "Core", ["Program.pc"] = "Core" },
            """
            public model Box
                internal integer count;

                public function Box(integer count)
                    this.count = count;
                end function
            end model
            """,
            """
            shared model Program
                function Main()
                    Console.WriteLine(new Box(3).count);
                end function
            end model
            """),
        Is.EqualTo("3\n"));

    /// <summary>
    /// <para>With no project file there is nothing to be inside of, so every file belongs to
    /// the one unnamed project a compilation always has and <c>internal</c> reaches all of
    /// them.</para>
    /// <para>This is what keeps the default from costing a beginner anything: a folder of
    /// files nobody divided meets no boundary, because none was drawn.</para>
    /// </summary>
    [Test]
    public void WithNoProjectFileInternalReachesEveryFile() => Assert.That(
        RunAcrossProjects(
            new Dictionary<string, string> { ["Helper.pc"] = "", ["Program.pc"] = "" },
            """
            internal model Helper
                internal shared integer function Twice(integer n)
                    yield n * 2;
                end function
            end model
            """,
            """
            shared model Program
                function Main()
                    Console.WriteLine(Helper.Twice(21));
                end function
            end model
            """),
        Is.EqualTo("42\n"));

    // ---- A member reaches its own type by default -------------------------------------------

    /// <summary>
    /// A field with no word written is private, and reaching one from elsewhere is reported.
    /// </summary>
    [Test]
    public void AFieldWithNoWordIsPrivate() => Assert.That(
        Check("""
            model Box
                integer hidden;

                public function Box()
                    this.hidden = 1;
                end function
            end model

            shared model Program
                function Main()
                    Box b = new Box();
                    Console.WriteLine(b.hidden);
                end function
            end model
            """),
        Is.EqualTo(new[] { "PC0339" }));

    [Test]
    public void AFunctionWithNoWordIsPrivate() => Assert.That(
        Check("""
            model Box
                integer function Secret()
                    yield 7;
                end function
            end model

            shared model Program
                function Main()
                    Console.WriteLine(new Box().Secret());
                end function
            end model
            """),
        Is.EqualTo(new[] { "PC0339" }));

    /// <summary>A type reaches its own private members, which is the point of having them.</summary>
    [Test]
    public void ATypeReachesItsOwnPrivateMembers() => Assert.That(
        Check("""
            model Box
                integer hidden;

                public function Box()
                    this.hidden = 1;
                end function

                public integer function Read()
                    yield this.hidden + this.Twice();
                end function

                integer function Twice()
                    yield this.hidden * 2;
                end function
            end model

            shared model Program
                function Main()
                    Console.WriteLine(new Box().Read());
                end function
            end model
            """),
        Is.Empty);

    // ---- Protected reaches the line of descent ----------------------------------------------

    [Test]
    public void ProtectedReachesAModelThatExtendsIt() => Assert.That(
        Check("""
            model Animal
                protected string name;

                public function Animal(string called)
                    this.name = called;
                end function
            end model

            model Dog extends Animal
                public function Dog()
                    base("dog");
                end function

                public string function Speak()
                    yield this.name;
                end function
            end model

            shared model Program
                function Main()
                    Console.WriteLine(new Dog().Speak());
                end function
            end model
            """),
        Is.Empty);

    /// <summary>
    /// And no further. Protected names a line of descent, and a model standing beside one is
    /// not on it however near it sits.
    /// </summary>
    [Test]
    public void ProtectedDoesNotReachAModelBesideIt() => Assert.That(
        Check("""
            model Animal
                protected string name;

                public function Animal(string called)
                    this.name = called;
                end function
            end model

            shared model Program
                function Main()
                    Console.WriteLine(new Animal("cat").name);
                end function
            end model
            """),
        Is.EqualTo(new[] { "PC0339" }));

    // ---- Internal reaches the project -------------------------------------------------------

    /// <summary>
    /// An internal member is reachable from any type in the same project, which is what makes
    /// it different from private.
    /// </summary>
    [Test]
    public void InternalReachesAnotherTypeInTheSameProject() => Assert.That(
        Check("""
            model Box
                internal integer count;

                public function Box()
                    this.count = 3;
                end function
            end model

            shared model Program
                function Main()
                    Console.WriteLine(new Box().count);
                end function
            end model
            """),
        Is.Empty);

    [Test]
    public void InternalDoesNotReachAnotherProject() => Assert.That(
        CheckAcrossProjects(
            """
            public model Box
                internal integer count;

                public function Box()
                    this.count = 3;
                end function
            end model
            """,
            """
            shared model Program
                function Main()
                    Console.WriteLine(new Box().count);
                end function
            end model
            """),
        Is.EqualTo(new[] { "PC0339" }));

    [Test]
    public void PublicReachesAnotherProject() => Assert.That(
        CheckAcrossProjects(
            """
            public model Box
                public integer count;

                public function Box()
                    this.count = 3;
                end function
            end model
            """,
            """
            shared model Program
                function Main()
                    Console.WriteLine(new Box().count);
                end function
            end model
            """),
        Is.Empty);

    // ---- A type reaches its own project by default ------------------------------------------

    /// <summary>
    /// A type with no word written is internal, so another project cannot name it. This is the
    /// half of the rule that makes a project reference a boundary rather than more sources.
    /// </summary>
    [Test]
    public void ATypeWithNoWordIsInternalToItsProject() => Assert.That(
        CheckAcrossProjects(
            """
            model Helper
                public shared integer function Twice(integer n)
                    yield n * 2;
                end function
            end model
            """,
            """
            shared model Program
                function Main()
                    Console.WriteLine(Helper.Twice(21));
                end function
            end model
            """),
        Is.EqualTo(new[] { "PC0221" }));

    /// <summary>
    /// Every way of naming a type is held to it. A boundary that stops one spelling and not
    /// another is not a boundary.
    /// </summary>
    [TestCase("Helper h;", TestName = "in a signature")]
    [TestCase("Console.WriteLine(Helper.Twice(1));", TestName = "as a receiver")]
    [TestCase("Helper h = new Helper();", TestName = "after new")]
    public void AnInternalTypeIsOutOfReachHoweverItIsNamed(string written) => Assert.That(
        CheckAcrossProjects(
            """
            model Helper
                public function Helper()
                end function

                public shared integer function Twice(integer n)
                    yield n * 2;
                end function
            end model
            """,
            $$"""
            shared model Program
                function Main()
                    {{written}}
                end function
            end model
            """),
        Does.Contain("PC0221"));

    [Test]
    public void APublicTypeIsNamedFromAnotherProject() => Assert.That(
        CheckAcrossProjects(
            """
            public model Helper
                public shared integer function Twice(integer n)
                    yield n * 2;
                end function
            end model
            """,
            """
            shared model Program
                function Main()
                    Console.WriteLine(Helper.Twice(21));
                end function
            end model
            """),
        Is.Empty);

    /// <summary>
    /// A compilation nobody divided is one project, so an unmarked type is reachable from
    /// everything in it. The rule is the same; there is simply one project to be in.
    /// </summary>
    [Test]
    public void OneUndividedCompilationIsOneProject() => Assert.That(
        Check("""
            model Helper
                public shared integer function Twice(integer n)
                    yield n * 2;
                end function
            end model

            shared model Program
                function Main()
                    Console.WriteLine(Helper.Twice(21));
                end function
            end model
            """),
        Is.Empty);

    // ---- Words that cannot be combined ------------------------------------------------------

    [Test]
    public void TwoVisibilitiesOnOneDeclarationAreRejected() => Assert.That(
        Check("""
            model Box
                public internal integer count;
            end model
            """),
        Does.Contain("PC0219"));

    /// <summary>
    /// Protected names a line of descent from the type that declares a member, and a type has
    /// no declaring type. The word has nothing to name.
    /// </summary>
    [Test]
    public void ProtectedOnATypeIsRejected() => Assert.That(
        Check("""
            protected model Box
            end model
            """),
        Does.Contain("PC0220"));

    /// <summary>A constructor is reached like any other member, so a private one is enforced.</summary>
    [Test]
    public void APrivateConstructorCannotBeReached() => Assert.That(
        Check("""
            model Box
                function Box()
                end function
            end model

            shared model Program
                function Main()
                    Box b = new Box();
                end function
            end model
            """),
        Is.EqualTo(new[] { "PC0339" }));
}
