using System.Text.Json.Nodes;
using ProfiC.Cli;
using ProfiC.Cli.LanguageServer;
using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Semantics;
using ProfiC.Compiler.Text;

namespace ProfiC.Tests.LanguageServer;

/// <summary>
/// <para>What an editor is told about a place in a file.</para>
/// <para>Three questions with one shape: find the syntax at the cursor, and say what the model
/// already worked out about it. None of them decides anything — a second opinion here about what
/// a name means would be a second definition of the language — so what these hold is that the
/// right node is found and the recorded answer is what comes back.</para>
/// <para>Positions are written as the caret sits: a line and a column counted from one, the way
/// a reader reads them, converted at the edge. Writing offsets into a test would make every one
/// of them unreadable and wrong after any edit to the fixture.</para>
/// </summary>
[TestFixture]
public sealed class AnswersTests
{
    private const string Program = """
        shared model Program
            function Main()
                integer counted = Greeting.Length("hello");
                Console.WriteLine(counted);
            end function
        end model
        """;

    private const string Greeting = """
        shared model Greeting
            public shared integer function Length(string word)
                yield word.Count;
            end function
        end model
        """;

    /// <summary>Two files that compile together, so a definition can be in the other one.</summary>
    private sealed class Workspace : IDisposable
    {
        public Workspace()
        {
            Folder = Path.Combine(Path.GetTempPath(), $"profi-c-answers-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Folder);

            File.WriteAllText(Path.Combine(Folder, "Program.pc"), Program);
            File.WriteAllText(Path.Combine(Folder, "Greeting.pc"), Greeting);
        }

        public string Folder { get; }

        public string At(string name) => Path.Combine(Folder, name);

        public void Dispose() => Directory.Delete(Folder, recursive: true);
    }

    /// <summary>The offset of a one-based line and column, as a reader would name them.</summary>
    private static int OffsetOf(SourceText source, int line, int column) =>
        source.OffsetOfLine(line) + column - 1;

    private static (IReadOnlyList<CompilationUnit> Units, SemanticModel Model, CompilationUnit Unit)
        Compile(Workspace workspace, string name)
    {
        DiagnosticBag diagnostics = new();

        SourceDiscovery.Compilation gathered =
            SourceDiscovery.Gather(workspace.At(name), diagnostics)!;

        SemanticModel model = Resolver.Resolve(gathered.Units, diagnostics);
        TypeChecker.Check(gathered.Units, model, diagnostics);

        CompilationUnit unit = gathered.Units.Single(
            u => Path.GetFileName(u.Source.FileName) == name);

        return (gathered.Units, model, unit);
    }

    // ---- What a file declares ----------------------------------------------------------------

    [Test]
    public void AFilesDeclarationsAreListedAsATree()
    {
        SourceText source = new(Program, "Program.pc");
        DiagnosticBag diagnostics = new();

        JsonArray symbols = Answers.Symbols(Parser.Parse(source, diagnostics), source);

        Assert.Multiple(() =>
        {
            Assert.That(symbols, Has.Count.EqualTo(1));
            Assert.That((string?)symbols[0]!["name"], Is.EqualTo("Program"));
            Assert.That((int?)symbols[0]!["kind"], Is.EqualTo(5), "a model is a Class");

            JsonArray children = (JsonArray)symbols[0]!["children"]!;

            Assert.That(children, Has.Count.EqualTo(1));
            Assert.That((string?)children[0]!["name"], Is.EqualTo("Main"));
            Assert.That((int?)children[0]!["kind"], Is.EqualTo(12), "and a function is a Function");
        });
    }

    /// <summary>
    /// <para>A file that will not compile still has a shape.</para>
    /// <para>Which is when an outline is wanted most: a file is being written, so it is broken,
    /// and the reader still wants to know where they are in it. The parser recovers, so this is
    /// built from the parse alone with nothing resolved.</para>
    /// </summary>
    [Test]
    public void AFileThatWillNotCompileStillOutlines()
    {
        SourceText source = new(
            """
            shared model Program
                function Main()
                    integer n = ;
                end function
            end model
            """,
            "Broken.pc");

        DiagnosticBag diagnostics = new();
        JsonArray symbols = Answers.Symbols(Parser.Parse(source, diagnostics), source);

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.HasErrors, Is.True, "the fixture is meant to be broken");
            Assert.That((string?)symbols[0]!["name"], Is.EqualTo("Program"));
            Assert.That((JsonArray)symbols[0]!["children"]!, Has.Count.EqualTo(1));
        });
    }

    // ---- What is under the cursor ------------------------------------------------------------

    /// <summary>A local says its type and its name, which is how a program would declare it.</summary>
    [Test]
    public void HoveringALocalSaysItsType()
    {
        using Workspace workspace = new();

        (_, SemanticModel model, CompilationUnit unit) = Compile(workspace, "Program.pc");

        // Line 3, column 17: inside 'counted'.
        JsonObject? hover = Answers.Hover(
            unit, model, unit.Source, OffsetOf(unit.Source, 3, 17));

        Assert.That(
            (string?)hover?["contents"]?["value"],
            Does.Contain("integer counted"));
    }

    /// <summary>
    /// A function says what it yields and what it takes, not only its name — which is most of
    /// what somebody hovering over a call wants to know.
    /// </summary>
    [Test]
    public void HoveringACallSaysWhatItYieldsAndTakes()
    {
        using Workspace workspace = new();

        (_, SemanticModel model, CompilationUnit unit) = Compile(workspace, "Program.pc");

        // Line 3, column 36: inside 'Length'.
        JsonObject? hover = Answers.Hover(
            unit, model, unit.Source, OffsetOf(unit.Source, 3, 36));

        string said = (string?)hover?["contents"]?["value"] ?? string.Empty;

        Assert.Multiple(() =>
        {
            Assert.That(said, Does.Contain("Length"));
            Assert.That(said, Does.Contain("integer"), "what it yields");
            Assert.That(said, Does.Contain("string word"), "and what it takes");
        });
    }

    /// <summary>
    /// <para>Nothing is said where nothing was recorded.</para>
    /// <para>A tooltip over every character in the file is worse than none over some of them.
    /// Somewhere with no node at all — past the end of the text — has nothing to say.</para>
    /// </summary>
    [Test]
    public void HoveringWhereThereIsNothingSaysNothing()
    {
        using Workspace workspace = new();

        (_, SemanticModel model, CompilationUnit unit) = Compile(workspace, "Program.pc");

        Assert.That(
            Answers.Hover(unit, model, unit.Source, unit.Source.Text.Length + 50),
            Is.Null);
    }

    // ---- Where a name was declared -----------------------------------------------------------

    /// <summary>
    /// <para>A name declared in another file is found in that file.</para>
    /// <para>The claim worth making: a program is a compilation, so following a name has to
    /// cross files. Answering only within the open one would send a reader nowhere for most of
    /// what they click.</para>
    /// </summary>
    [Test]
    public void ANameDeclaredElsewhereIsFoundThere()
    {
        using Workspace workspace = new();

        (IReadOnlyList<CompilationUnit> units, SemanticModel model, CompilationUnit unit) =
            Compile(workspace, "Program.pc");

        // Line 3, column 36: inside 'Length', which Greeting.pc declares.
        JsonArray found = Answers.Definition(
            units, model, unit, OffsetOf(unit.Source, 3, 36));

        Assert.Multiple(() =>
        {
            Assert.That(found, Has.Count.EqualTo(1));
            Assert.That((string?)found[0]!["uri"], Does.Contain("Greeting.pc"));

            // Line 2 as a reader counts, which is 1 here.
            Assert.That((int?)found[0]!["range"]!["start"]!["line"], Is.EqualTo(1));
        });
    }

    /// <summary>A local is declared in the file it is used in, and found there.</summary>
    [Test]
    public void ALocalIsFoundWhereItWasDeclared()
    {
        using Workspace workspace = new();

        (IReadOnlyList<CompilationUnit> units, SemanticModel model, CompilationUnit unit) =
            Compile(workspace, "Program.pc");

        // Line 4, column 27: inside 'counted' where it is used.
        JsonArray found = Answers.Definition(
            units, model, unit, OffsetOf(unit.Source, 4, 27));

        Assert.Multiple(() =>
        {
            Assert.That(found, Has.Count.EqualTo(1));
            Assert.That((string?)found[0]!["uri"], Does.Contain("Program.pc"));
            Assert.That(
                (int?)found[0]!["range"]!["start"]!["line"],
                Is.EqualTo(2),
                "the line it was declared on, not the one it is used on");
        });
    }

    /// <summary>
    /// <para>Following a name lands on the name, not on the fifteen lines around it.</para>
    /// <para>The whole declaration would be a correct answer to "where is this" and a poor one to
    /// look at: an editor reveals and selects what it is given, so answering with the declaration
    /// highlights the body of a function to say where its name is.</para>
    /// </summary>
    [Test]
    public void FollowingANameSelectsTheNameAlone()
    {
        using Workspace workspace = new();

        (IReadOnlyList<CompilationUnit> units, SemanticModel model, CompilationUnit unit) =
            Compile(workspace, "Program.pc");

        // Line 3, column 36: inside 'Length', which Greeting.pc declares.
        JsonObject range = (JsonObject)Answers.Definition(
            units, model, unit, OffsetOf(unit.Source, 3, 36))[0]!["range"]!;

        Assert.Multiple(() =>
        {
            // 'Length' on line 2 of Greeting.pc, at columns 36 through 41 as a reader counts.
            Assert.That((int?)range["start"]!["line"], Is.EqualTo(1));
            Assert.That((int?)range["start"]!["character"], Is.EqualTo(35));
            Assert.That((int?)range["end"]!["line"], Is.EqualTo(1), "one line, not the whole body");
            Assert.That((int?)range["end"]!["character"], Is.EqualTo(41));
        });
    }

    /// <summary>
    /// <para>A member the language owns leads nowhere, rather than to whatever encloses it.</para>
    /// <para><b>The case that says why the search stops where it does.</b> <c>Count</c> has no
    /// declaration in any file, so looking outward from it finds nothing until it reaches the
    /// function the line sits in — and following that would answer "where is Count declared" by
    /// jumping to <c>Length</c>, which is both wrong and confident.</para>
    /// </summary>
    [Test]
    public void AMemberTheLanguageOwnsLeadsNowhere()
    {
        using Workspace workspace = new();

        (IReadOnlyList<CompilationUnit> units, SemanticModel model, CompilationUnit unit) =
            Compile(workspace, "Greeting.pc");

        // Line 3, column 21: inside 'Count' on 'word.Count'.
        Assert.That(
            Answers.Definition(units, model, unit, OffsetOf(unit.Source, 3, 21)),
            Is.Empty);
    }

    // ---- What a call takes -------------------------------------------------------------------

    private const string Adding = """
        shared model Program
            function Main()
                Console.WriteLine(Program.Add(1, 2));
            end function

            integer function Add(integer left, integer right)
                yield left + right;
            end function
        end model
        """;

    /// <summary>
    /// A call says what it takes and what it yields, which is what somebody typing arguments
    /// cannot see — they are looking at the call rather than the declaration.
    /// </summary>
    [Test]
    public void ACallSaysWhatItTakes()
    {
        using Workspace workspace = new();

        File.WriteAllText(workspace.At("Program.pc"), Adding);

        (_, SemanticModel model, CompilationUnit unit) = Compile(workspace, "Program.pc");

        // Line 3, column 39: just inside the argument list.
        JsonObject? help = Answers.Signature(
            unit, model, unit.Source, OffsetOf(unit.Source, 3, 39));

        Assert.Multiple(() =>
        {
            Assert.That(
                (string?)help?["signatures"]?[0]?["label"],
                Does.Contain("Add(integer left, integer right)"));

            Assert.That(
                (JsonArray?)help?["signatures"]?[0]?["parameters"], Has.Count.EqualTo(2));
        });
    }

    /// <summary>
    /// <para>Which parameter is highlighted follows the cursor along the arguments.</para>
    /// <para>Counted from where the arguments are rather than by looking for commas, since a
    /// comma inside a nested call or a string belongs to something else — and that is exactly
    /// where getting it wrong would be least forgivable.</para>
    /// </summary>
    [Test]
    public void TheParameterHighlightedFollowsTheCursor()
    {
        using Workspace workspace = new();

        File.WriteAllText(workspace.At("Program.pc"), Adding);

        (_, SemanticModel model, CompilationUnit unit) = Compile(workspace, "Program.pc");

        JsonObject? first = Answers.Signature(
            unit, model, unit.Source, OffsetOf(unit.Source, 3, 39));

        // Past the comma, into the second argument.
        JsonObject? second = Answers.Signature(
            unit, model, unit.Source, OffsetOf(unit.Source, 3, 42));

        Assert.Multiple(() =>
        {
            Assert.That((int?)first?["activeParameter"], Is.EqualTo(0));
            Assert.That((int?)second?["activeParameter"], Is.EqualTo(1));
        });
    }

    /// <summary>Nothing is said where the cursor is not inside a call at all.</summary>
    [Test]
    public void NoSignatureWhereThereIsNoCall()
    {
        using Workspace workspace = new();

        (_, SemanticModel model, CompilationUnit unit) = Compile(workspace, "Program.pc");

        Assert.That(
            Answers.Signature(unit, model, unit.Source, unit.Source.Text.Length + 50),
            Is.Null);
    }

    /// <summary>Somewhere that refers to nothing leads nowhere, rather than to a guess.</summary>
    [Test]
    public void SomewhereThatNamesNothingLeadsNowhere()
    {
        using Workspace workspace = new();

        (IReadOnlyList<CompilationUnit> units, SemanticModel model, CompilationUnit unit) =
            Compile(workspace, "Program.pc");

        Assert.That(
            Answers.Definition(units, model, unit, unit.Source.Text.Length + 50),
            Is.Empty);
    }
}
