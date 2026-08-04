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

        /// <summary>Replaces a file, for a test that needs a different one than the fixture.</summary>
        public void Write(string name, string body) => File.WriteAllText(At(name), body);

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
    /// <summary>
    /// <para>A declaration whose name has not been typed yet is not listed.</para>
    /// <para><b>The protocol forbids an empty name, and an editor asks for this on every
    /// keystroke</b> — so a nameless entry is not a blank row, it is a rejected reply, once per
    /// character, for as long as somebody is partway through typing a declaration. Which is
    /// constantly: the parser recovers rather than stopping, so <c>model</c> with nothing after
    /// it is a declaration with an empty name.</para>
    /// </summary>
    [Test]
    public void ADeclarationWithNoNameYetIsNotListed()
    {
        SourceText source = new(
            """
            shared model Program
                function Main()
                    Console.WriteLine(1);
                end function
            end model

            model
            """,
            "Program.pc");

        DiagnosticBag aside = new();
        JsonArray listed = Answers.Symbols(Parser.Parse(source, aside), source);

        Assert.Multiple(() =>
        {
            Assert.That(listed, Has.Count.EqualTo(1), "the finished one, and not the other");
            Assert.That((string?)listed[0]!["name"], Is.EqualTo("Program"));

            foreach (JsonNode? entry in listed)
            {
                Assert.That((string?)entry!["name"], Is.Not.Empty);
            }
        });
    }

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

        (IReadOnlyList<CompilationUnit> units, SemanticModel model, CompilationUnit unit) =
            Compile(workspace, "Program.pc");

        // Line 3, column 17: inside 'counted'.
        JsonObject? hover = Answers.Hover(
            units, unit, model, unit.Source, OffsetOf(unit.Source, 3, 17));

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

        (IReadOnlyList<CompilationUnit> units, SemanticModel model, CompilationUnit unit) =
            Compile(workspace, "Program.pc");

        // Line 3, column 36: inside 'Length'.
        JsonObject? hover = Answers.Hover(
            units, unit, model, unit.Source, OffsetOf(unit.Source, 3, 36));

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

        (IReadOnlyList<CompilationUnit> units, SemanticModel model, CompilationUnit unit) =
            Compile(workspace, "Program.pc");

        Assert.That(
            Answers.Hover(units, unit, model, unit.Source, unit.Source.Text.Length + 50),
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

    /// <summary>
    /// <para>Hovering a member the language provides says what it is for.</para>
    /// <para>Nobody declares <c>Count</c>, so there is no <c>@summary:</c> above it to read — what
    /// it does is recorded in the compiler beside its shape, and this is where that reaches a
    /// reader.</para>
    /// </summary>
    [Test]
    public void HoveringAMemberTheLanguageProvidesSaysWhatItIsFor()
    {
        using Workspace workspace = new();

        (IReadOnlyList<CompilationUnit> units, SemanticModel model, CompilationUnit unit) =
            Compile(workspace, "Greeting.pc");

        // Line 3, column 21: inside 'Count' on 'word.Count'.
        JsonObject? hover = Answers.Hover(
            units, unit, model, unit.Source, OffsetOf(unit.Source, 3, 21));

        Assert.That(
            (string?)hover?["contents"]?["value"],
            Does.Contain("How many characters"));
    }

    /// <summary>
    /// <para>A call to something the language provides says what it takes and yields.</para>
    /// <para>A member the language provides is not a symbol, so without a rule of its own this
    /// falls through to the type of the expression around it — and every call to something that
    /// yields nothing then describes itself as nothing, which is true of the call and says
    /// nothing whatever about the member.</para>
    /// </summary>
    [Test]
    public void HoveringACallToSomethingProvidedSaysItsShape()
    {
        using Workspace workspace = new();

        (IReadOnlyList<CompilationUnit> units, SemanticModel model, CompilationUnit unit) =
            Compile(workspace, "Program.pc");

        // Line 4, column 17: on 'WriteLine'.
        string said = (string?)Answers.Hover(
            units, unit, model, unit.Source, OffsetOf(unit.Source, 4, 17))
            ?["contents"]?["value"] ?? string.Empty;

        Assert.Multiple(() =>
        {
            Assert.That(
                said,
                Does.Contain("function WriteLine(anything)"),
                "written the way a program would declare one");

            Assert.That(
                said,
                Does.Not.Contain("nothing"),
                "a function with no result has no type in front of it, and no word either");

            Assert.That(said, Does.Contain("Writes a value and ends the line"));

            Assert.That(
                said,
                Does.Contain("Standard.Console"),
                "where it comes from, without repeating the name already on screen");

            Assert.That(said, Does.Not.Contain("Console.WriteLine"));
        });
    }

    /// <summary>
    /// <para>A function type is named the way a program writes one.</para>
    /// <para><c>delegate</c> is how a function type is written and <c>function</c> is how one is
    /// declared. Naming a type with the other word gives back something that will not parse where
    /// a type belongs, which is the one place this text is read — and it reaches every diagnostic
    /// that names a type, not only what a reader hovers.</para>
    /// </summary>
    [Test]
    public void AFunctionTypeIsNamedAsADelegate()
    {
        using Workspace workspace = new();

        workspace.Write(
            "Program.pc",
            """
            shared model Program
                function Main()
                    integer delegate(integer) scale = (n) yield n * 2;
                    Console.WriteLine(scale(2));
                end function
            end model
            """);

        (IReadOnlyList<CompilationUnit> units, SemanticModel model, CompilationUnit unit) =
            Compile(workspace, "Program.pc");

        // Line 3, column 35: on 'scale' where it is declared.
        string said = (string?)Answers.Hover(
            units, unit, model, unit.Source, OffsetOf(unit.Source, 3, 35))
            ?["contents"]?["value"] ?? string.Empty;

        Assert.Multiple(() =>
        {
            Assert.That(said, Does.Contain("delegate(integer)"));
            Assert.That(said, Does.Not.Contain("function("));
        });
    }

    /// <summary>
    /// <para>A type the language provides says what it is for, wherever it is written.</para>
    /// <para>On the left of a declaration and after <c>as</c> it is a type and nothing else — no
    /// call resolves to it, so there is no recorded member to read a line from, and the name is
    /// all there is to go on.</para>
    /// </summary>
    [Test]
    public void HoveringATypeTheLanguageProvidesSaysWhatItIsFor()
    {
        using Workspace workspace = new();

        workspace.Write(
            "Program.pc",
            """
            shared model Program
                function Main()
                    Random counter = new Random();
                    Console.WriteLine(counter.Next());
                end function
            end model
            """);

        (IReadOnlyList<CompilationUnit> units, SemanticModel model, CompilationUnit unit) =
            Compile(workspace, "Program.pc");

        // Line 3, column 10: on 'Random' written as the local's type.
        string said = (string?)Answers.Hover(
            units, unit, model, unit.Source, OffsetOf(unit.Source, 3, 10))
            ?["contents"]?["value"] ?? string.Empty;

        Assert.That(said, Does.Contain("A source of chance"));
    }

    /// <summary>
    /// <para>Hovering something a program declared says what that program wrote about it.</para>
    /// <para>The other half, and the one that costs a reader nothing to get: a
    /// <c>@summary:</c> is already parsed and already checked, and until now was read by nobody
    /// but the compiler.</para>
    /// </summary>
    [Test]
    public void HoveringSomethingDocumentedSaysWhatWasWritten()
    {
        using Workspace workspace = new();

        workspace.Write(
            "Greeting.pc",
            """
            shared model Greeting
                # @summary: How long a word is, counted in characters.
                public shared integer function Length(string word)
                    yield word.Count;
                end function
            end model
            """);

        (IReadOnlyList<CompilationUnit> units, SemanticModel model, CompilationUnit unit) =
            Compile(workspace, "Program.pc");

        // Line 3, column 36: inside the call to 'Length'.
        JsonObject? hover = Answers.Hover(
            units, unit, model, unit.Source, OffsetOf(unit.Source, 3, 36));

        string said = (string?)hover?["contents"]?["value"] ?? string.Empty;

        Assert.Multiple(() =>
        {
            Assert.That(said, Does.Contain("function Length"), "the shape first");
            Assert.That(said, Does.Contain("How long a word is"), "then what it is for");
        });
    }

    /// <summary>
    /// <para>A parameter is documented by the label naming it, not by its function's summary.
    /// </para>
    /// <para>A parameter has no comment above it — what is written about it sits inside the
    /// comment on the function that takes it. Read as a declaration of its own it finds that
    /// comment and answers with what the whole function is for: true of the function, and
    /// nothing to do with the parameter.</para>
    /// </summary>
    [Test]
    public void HoveringAParameterSaysWhatWasWrittenAboutThatParameter()
    {
        using Workspace workspace = new();

        workspace.Write(
            "Greeting.pc",
            """
            shared model Greeting
                # @summary: How long a word is.
                # @word: the text to measure.
                # @yields: how many characters are in it.
                public shared integer function Length(string word)
                    yield word.Count;
                end function
            end model
            """);

        (IReadOnlyList<CompilationUnit> units, SemanticModel model, _) =
            Compile(workspace, "Program.pc");

        CompilationUnit greeting = units.Single(
            u => Path.GetFileName(u.Source.FileName) == "Greeting.pc");

        // Line 5, column 50: on 'word' where the parameter is declared.
        string said = (string?)Answers.Hover(
                units, greeting, model, greeting.Source, OffsetOf(greeting.Source, 5, 50))
            ?["contents"]?["value"] ?? string.Empty;

        Assert.Multiple(() =>
        {
            Assert.That(said, Does.Contain("the text to measure"));
            Assert.That(said, Does.Not.Contain("How long a word is"), "that is the function's");
        });
    }

    /// <summary>
    /// <para>What a function yields and what it raises are shown too.</para>
    /// <para>Both are written where the summary is and both are part of what somebody hovering a
    /// call came to find out. Left out, a function that documents a thrown exception looks as
    /// though nothing was written about it.</para>
    /// </summary>
    [Test]
    public void HoveringAFunctionSaysWhatItYieldsAndRaises()
    {
        using Workspace workspace = new();

        workspace.Write(
            "Greeting.pc",
            """
            shared model Greeting
                # @summary: How long a word is.
                # @yields: how many characters are in it.
                # @throws: ArgumentException where the word is empty.
                public shared integer function Length(string word)
                    yield word.Count;
                end function
            end model
            """);

        (IReadOnlyList<CompilationUnit> units, SemanticModel model, CompilationUnit unit) =
            Compile(workspace, "Program.pc");

        // Line 3, column 36: on the call to 'Length'.
        string said = (string?)Answers.Hover(
                units, unit, model, unit.Source, OffsetOf(unit.Source, 3, 36))
            ?["contents"]?["value"] ?? string.Empty;

        Assert.Multiple(() =>
        {
            Assert.That(said, Does.Contain("How long a word is"));
            Assert.That(said, Does.Contain("how many characters"));
            Assert.That(said, Does.Contain("ArgumentException"));
        });
    }

    /// <summary>
    /// <para>A type a program declared says what was written about it, and can be followed,
    /// wherever it is named.</para>
    /// <para><b>On the left of a declaration and after <c>as</c>, a name is a use of that type
    /// like any other</b> — but only the type it came to was recorded, not which declaration it
    /// reached. So the two places a type is most often written could not be followed, renamed,
    /// marked, or read the documentation of, while the same name on the right of a <c>new</c>
    /// could be all four.</para>
    /// </summary>
    [Test]
    public void ATypeIsFollowedAndDocumentedWhereverItIsNamed()
    {
        using Workspace workspace = new();

        workspace.Write(
            "Greeting.pc",
            """
            # @summary: Something that can say hello.
            model Greeting
                public function Greeting()
                end function

                public string function Words()
                    yield "hello";
                end function
            end model
            """);

        workspace.Write(
            "Program.pc",
            """
            shared model Program
                function Main()
                    Greeting first = new Greeting();
                    Model second = first;
                    Console.WriteLine(second as Greeting);
                end function
            end model
            """);

        (IReadOnlyList<CompilationUnit> units, SemanticModel model, CompilationUnit unit) =
            Compile(workspace, "Program.pc");

        string Hovered(int line, int column) =>
            (string?)Answers.Hover(
                units, unit, model, unit.Source, OffsetOf(unit.Source, line, column))
                ?["contents"]?["value"] ?? string.Empty;

        JsonArray Followed(int line, int column) =>
            Answers.Definition(units, model, unit, OffsetOf(unit.Source, line, column));

        Assert.Multiple(() =>
        {
            // Line 3, column 10: 'Greeting' written as the local's type.
            Assert.That(
                Hovered(3, 10),
                Does.Contain("Something that can say hello"),
                "on the left");
            Assert.That(Followed(3, 10), Is.Not.Empty, "and followed from there");

            // Line 5, column 38: 'Greeting' written after 'as'.
            Assert.That(Hovered(5, 38), Does.Contain("Something that can say hello"), "in a cast");
            Assert.That(Followed(5, 38), Is.Not.Empty, "and followed from there too");
        });
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

        (IReadOnlyList<CompilationUnit> units, SemanticModel model, CompilationUnit unit) =
            Compile(workspace, "Program.pc");

        // Line 3, column 39: just inside the argument list.
        JsonObject? help = Answers.Signature(
            units, unit, model, unit.Source, OffsetOf(unit.Source, 3, 39));

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

        (IReadOnlyList<CompilationUnit> units, SemanticModel model, CompilationUnit unit) =
            Compile(workspace, "Program.pc");

        JsonObject? first = Answers.Signature(
            units, unit, model, unit.Source, OffsetOf(unit.Source, 3, 39));

        // Past the comma, into the second argument.
        JsonObject? second = Answers.Signature(
            units, unit, model, unit.Source, OffsetOf(unit.Source, 3, 42));

        Assert.Multiple(() =>
        {
            Assert.That((int?)first?["activeParameter"], Is.EqualTo(0));
            Assert.That((int?)second?["activeParameter"], Is.EqualTo(1));
        });
    }

    /// <summary>
    /// <para>What the function is for, and what the parameter being written is for.</para>
    /// <para><b>Both, because they answer different halves of one question.</b> Somebody stopped
    /// halfway through a call wants to know what the thing does and what goes where the caret is,
    /// and going to find either one means leaving the line they are writing.</para>
    /// <para>Each parameter is placed by where it sits in the signature rather than by its text.
    /// Given text, an editor finds the parameter by searching the line for it — so two written
    /// the same way, which is ordinary in a function taking two of a kind, would both point at
    /// the first.</para>
    /// </summary>
    [Test]
    public void ACallSaysWhatItIsForAndWhatEachParameterIsFor()
    {
        const string Documented = """
            shared model Program
                function Main()
                    Console.WriteLine(Program.Repeat("ha", 3));
                end function

                ##
                    @summary: One string written over and over.
                    @word: what to repeat.
                    @times: how many copies to make.
                    @yields: the copies, run together.
                ##
                string function Repeat(string word, integer times)
                    string all = "";

                    loop for i = 1 to times
                        all = all + word;
                    end loop

                    yield all;
                end function
            end model
            """;

        using Workspace workspace = new();
        workspace.Write("Program.pc", Documented);

        (IReadOnlyList<CompilationUnit> units, SemanticModel model, CompilationUnit unit) =
            Compile(workspace, "Program.pc");

        // Line 3, column 48: on the 3, which is the second argument of Repeat.
        JsonObject? help = Answers.Signature(
            units, unit, model, unit.Source, OffsetOf(unit.Source, 3, 48));

        JsonObject? signature = (JsonObject?)help?["signatures"]?[0];
        string label = (string?)signature?["label"] ?? string.Empty;
        JsonArray? parameters = (JsonArray?)signature?["parameters"];

        Assert.Multiple(() =>
        {
            Assert.That(
                (string?)signature?["documentation"]?["value"],
                Does.Contain("One string written over and over"),
                "what the function is for");

            Assert.That((int?)help?["activeParameter"], Is.EqualTo(1));

            Assert.That(
                (string?)parameters?[1]?["documentation"]?["value"],
                Does.Contain("how many copies to make"),
                "and what the one being written is for");

            // Where it sits, so an editor marks exactly those characters.
            JsonArray where = (JsonArray)parameters![1]!["label"]!;
            int from = (int)where[0]!;

            Assert.That(
                label[from..(int)where[1]!],
                Is.EqualTo("integer times"),
                "placed by position rather than by searching for its text");
        });
    }

    /// <summary>
    /// <para>A call to a member the language provides says what it takes, and what it is for.
    /// </para>
    /// <para><b>These were refused outright</b>, because signature help asked for a function a
    /// program declared and a provided member is a catalog entry rather than a symbol. So
    /// <c>Console.WriteLine(</c> and <c>Math.Round(</c> showed nothing at all — and those are the
    /// calls whose documentation is best, since every one of them carries a written summary while
    /// most functions somebody writes carry none. It reads as the feature being broken rather
    /// than as a function nobody documented.</para>
    /// </summary>
    [Test]
    public void ACallToSomethingProvidedSaysWhatItTakesAndWhatItIsFor()
    {
        using Workspace workspace = new();

        workspace.Write("Program.pc", """
            shared model Program
                function Main()
                    Console.WriteLine(Math.Round(2.5, 1));
                end function
            end model
            """);

        (IReadOnlyList<CompilationUnit> units, SemanticModel model, CompilationUnit unit) =
            Compile(workspace, "Program.pc");

        // Line 3, column 44: on the 1, which is the second argument of Round.
        JsonObject? help = Answers.Signature(
            units, unit, model, unit.Source, OffsetOf(unit.Source, 3, 44));

        JsonObject? signature = (JsonObject?)help?["signatures"]?[0];

        Assert.Multiple(() =>
        {
            Assert.That((string?)signature?["label"], Does.Contain("Round"));

            Assert.That(
                (string?)signature?["documentation"]?["value"],
                Is.Not.Null.And.Not.Empty,
                "the catalog says what it is for");

            Assert.That((int?)help?["activeParameter"], Is.EqualTo(1));
        });
    }

    /// <summary>Nothing is said where the cursor is not inside a call at all.</summary>
    [Test]
    public void NoSignatureWhereThereIsNoCall()
    {
        using Workspace workspace = new();

        (IReadOnlyList<CompilationUnit> units, SemanticModel model, CompilationUnit unit) =
            Compile(workspace, "Program.pc");

        Assert.That(
            Answers.Signature(units, unit, model, unit.Source, unit.Source.Text.Length + 50),
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

    /// <summary>
    /// <para>What a hover says, in the order it says it.</para>
    /// <para>The order is a decision rather than an accident of how the string was built, so it is
    /// held here: the shape first, because that is what a reader hovered for; the prose under it;
    /// and where the thing came from last, under a rule, because it answers the question they turn
    /// to second and only when two names collide.</para>
    /// </summary>
    [Test]
    public void AHoverSaysTheShapeFirstAndWhereItCameFromLast()
    {
        using Workspace workspace = new();

        (IReadOnlyList<CompilationUnit> units, SemanticModel model, CompilationUnit unit) =
            Compile(workspace, "Program.pc");

        // Line 3, column 36: inside 'Length'.
        string said = (string?)Answers.Hover(
            units, unit, model, unit.Source, OffsetOf(unit.Source, 3, 36))
            ?["contents"]?["value"] ?? string.Empty;

        Assert.Multiple(() =>
        {
            Assert.That(said, Does.StartWith("```profi-c"), "the shape, first");
            Assert.That(said.TrimEnd(), Does.EndWith("Greeting"), "and where it came from, last");
            Assert.That(
                said,
                Does.Not.Contain("*Greeting*"),
                "in plain text — a hover is markdown, and markdown has no smaller");
        });
    }

    /// <summary>
    /// <para>A type hovers as the word that declares it, not as its own name.</para>
    /// <para><b>Its name alone is the word already under the pointer</b>, so the hover repeated
    /// what the reader could see and added nothing — and a name standing on its own is the one
    /// shape the grammar has no rule for, so it arrived uncolored beside every other hover that
    /// lit up. Which is how this was reported: as missing highlighting rather than as a missing
    /// word, since the two look the same from the outside.</para>
    /// <para>Every declared kind, because they were all the same bare name: a model, a structure
    /// and an enumeration each said only what was already on screen.</para>
    /// </summary>
    [TestCase(14, 9, "model Account")]
    [TestCase(15, 9, "structure Point")]
    [TestCase(16, 9, "enumeration Suit")]
    public void ATypeHoversAsTheWordThatDeclaresIt(int line, int column, string said)
    {
        using Workspace workspace = new();

        workspace.Write("Program.pc", """
            model Account
            end model

            structure Point
                public integer x;
            end structure

            enumeration Suit
                Hearts
            end enumeration

            shared model Program
                function Main()
                    Account[] accounts = {};
                    Point here = new Point();
                    Suit played = Suit.Hearts;

                    Console.WriteLine(accounts.Count + here.x + played);
                end function
            end model
            """);

        (IReadOnlyList<CompilationUnit> units, SemanticModel model, CompilationUnit unit) =
            Compile(workspace, "Program.pc");

        // Column 9 on each: the type at the left of the declaration, inside its name.
        Assert.That(
            (string?)Answers.Hover(
                units, unit, model, unit.Source, OffsetOf(unit.Source, line, column))
                ?["contents"]?["value"],
            Does.Contain(said));
    }

    /// <summary>
    /// <para>A member of an enumeration hovers as a program writes it.</para>
    /// <para><b>It used to hover as its bare name</b>, which is the one form that says nothing: not
    /// which enumeration it belongs to, and not that it is one — and a hover's code block is
    /// colored by the grammar, which has no rule for a name standing on its own. So the answer was
    /// the word already under the pointer, in the same color as the prose around it.</para>
    /// </summary>
    [Test]
    public void AMemberOfAnEnumerationHoversAsOneIsWritten()
    {
        using Workspace workspace = new();

        workspace.Write("Program.pc", """
            enumeration Suit
                Hearts,
                Spades
            end enumeration

            shared model Program
                function Main()
                    Suit played = Suit.Hearts;
                    Console.WriteLine(played);
                end function
            end model
            """);

        (IReadOnlyList<CompilationUnit> units, SemanticModel model, CompilationUnit unit) =
            Compile(workspace, "Program.pc");

        // Line 8, column 30: on 'Hearts' in 'Suit.Hearts'.
        string said = (string?)Answers.Hover(
            units, unit, model, unit.Source, OffsetOf(unit.Source, 8, 30))
            ?["contents"]?["value"] ?? string.Empty;

        Assert.That(
            said,
            Does.Contain("Suit.Hearts"),
            "the enumeration it belongs to, and the form a program would copy");
    }

    // ---- Which function documented a parameter -----------------------------------------------

    /// <summary>
    /// <para>Two functions that take a parameter of the same name, each documenting it.</para>
    /// <para>The reuse is the point: a parameter is documented on the function that takes it, and
    /// two functions taking a <c>label</c> is the ordinary case rather than a contrived one.</para>
    /// </summary>
    private const string Documenting = """
        shared model Program
            function Main()
                integer count = 3;

                Program.Show("counted", count);
            end function

            ##
                @summary: Writes a line, some number of times.
                @label: what to write out
                @times: how many times to write it
            ##
            function Show(string label, integer times)
                loop for i = 1 to times
                    Console.WriteLine(label);
                end loop
            end function

            ##
                @summary: Writes a line once.
                @label: something else entirely
            ##
            function Once(string label)
                string copy = label;
                Console.WriteLine(copy);
            end function
        end model
        """;

    private static string Said(Workspace workspace, int line, int column)
    {
        (IReadOnlyList<CompilationUnit> units, SemanticModel model, CompilationUnit unit) =
            Compile(workspace, "Program.pc");

        return (string?)Answers.Hover(
            units, unit, model, unit.Source, OffsetOf(unit.Source, line, column))
            ?["contents"]?["value"] ?? string.Empty;
    }

    /// <summary>
    /// <para>A parameter is documented by the function that takes it, and not by another one that
    /// happens to use the same name.</para>
    /// <para>Taken by name from every documentation comment in the file, the answer is whichever
    /// function was written highest — so the second <c>label</c> in a file documents itself with
    /// the first one's sentence, and does it silently.</para>
    /// </summary>
    [Test]
    public void AParameterIsDocumentedByItsOwnFunction()
    {
        using Workspace workspace = new();
        workspace.Write("Program.pc", Documenting);

        // Line 24, column 24: on 'label' inside Once, which is the second function taking one.
        string said = Said(workspace, 24, 24);

        Assert.Multiple(() =>
        {
            Assert.That(said, Does.Contain("something else entirely"));
            Assert.That(said, Does.Not.Contain("what to write out"));
        });
    }
}
