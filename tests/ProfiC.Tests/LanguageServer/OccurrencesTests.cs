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
/// <para>Marking every place this file writes the name under the cursor.</para>
/// <para>Rename's question without the edit, and the differences are the interesting part: it
/// answers for names rename refuses, since marking a name the language owns writes nothing
/// anywhere, and it stops at the file rather than reaching across the compilation, since what it
/// marks has to be on screen to be seen.</para>
/// </summary>
[TestFixture]
public sealed class OccurrencesTests
{
    private static (CompilationUnit Unit, SemanticModel Model, SourceText Source) Compile(
        string text)
    {
        SourceText source = new(text, "Program.pc");
        DiagnosticBag diagnostics = new();

        CompilationUnit unit = Parser.Parse(source, diagnostics);
        SemanticModel model = Resolver.Resolve([unit], diagnostics, requireEntryPoint: false);

        TypeChecker.Check([unit], model, diagnostics);

        return (unit, model, source);
    }

    /// <summary>The marks, as a line and column a reader counts from one, with what each says.</summary>
    private static IReadOnlyList<(int Line, int Column, string Kind)> Marks(JsonArray? found) =>
    [
        .. (found ?? []).Select(mark => (
            (int)mark!["range"]!["start"]!["line"]! + 1,
            (int)mark["range"]!["start"]!["character"]! + 1,
            (int)mark["kind"]! == 3 ? "written" : "read")),
    ];

    private static int OffsetOf(SourceText source, int line, int column) =>
        source.OffsetOfLine(line) + column - 1;

    private const string Counting = """
        shared model Program
            function Main()
                integer total = 0;
                total = total + 1;
                Console.WriteLine(total);
            end function
        end model
        """;

    /// <summary>
    /// <para>Every place the name is written, and what each one does with it.</para>
    /// <para>The distinction is the whole of what this adds over a text search: <c>total</c> on
    /// line 4 is written on the left of the <c>=</c> and read on the right, and an editor paints
    /// the two differently. So does its declaration, which is where it first gets a value.</para>
    /// </summary>
    [Test]
    public void EveryUseIsMarkedAndWhatItDoesIsSaid()
    {
        (CompilationUnit unit, SemanticModel model, SourceText source) = Compile(Counting);

        // Line 4, column 9: on 'total' where it is assigned.
        IReadOnlyList<(int Line, int Column, string Kind)> marks =
            Marks(Occurrences.In(unit, model, OffsetOf(source, 4, 9)));

        Assert.That(
            marks,
            Is.EqualTo(new[]
            {
                (3, 17, "written"),
                (4, 9, "written"),
                (4, 17, "read"),
                (5, 27, "read"),
            }));
    }

    // ---- Across the whole program ------------------------------------------------------------

    /// <summary>Two files that compile together, so a use can be in the other one.</summary>
    private sealed class Workspace : IDisposable
    {
        public Workspace(string program, string beside)
        {
            Folder = Path.Combine(Path.GetTempPath(), $"profi-c-uses-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Folder);

            File.WriteAllText(Path.Combine(Folder, "Program.pc"), program);
            File.WriteAllText(Path.Combine(Folder, "Greeting.pc"), beside);
        }

        public string Folder { get; }

        public string At(string name) => Path.Combine(Folder, name);

        public void Dispose() => Directory.Delete(Folder, recursive: true);
    }

    private const string Caller = """
        shared model Program
            function Main()
                Console.WriteLine(Greeting.Length("hello"));
                Console.WriteLine(Greeting.Length("goodbye"));
            end function
        end model
        """;

    private const string Callee = """
        shared model Greeting
            public shared integer function Length(string word)
                yield word.Count;
            end function
        end model
        """;

    /// <summary>Every use, as the file it is in and the line, so a list reads as one.</summary>
    private static IReadOnlyList<(string File, int Line)> Uses(
        Workspace workspace, string asking, int line, int column, bool includingDeclaration = true)
    {
        DiagnosticBag diagnostics = new();

        SourceDiscovery.Compilation gathered =
            SourceDiscovery.Gather(workspace.At("Program.pc"), diagnostics)!;

        SemanticModel model = Resolver.Resolve(gathered.Units, diagnostics);
        TypeChecker.Check(gathered.Units, model, diagnostics);

        CompilationUnit unit = gathered.Units.Single(
            u => Path.GetFileName(u.Source.FileName) == asking);

        JsonArray? found = Occurrences.Across(
            gathered.Units,
            model,
            unit,
            OffsetOf(unit.Source, line, column),
            includingDeclaration);

        return
        [
            .. (found ?? []).Select(use => (
                Path.GetFileName(new Uri((string)use!["uri"]!).LocalPath),
                (int)use["range"]!["start"]!["line"]! + 1)),
        ];
    }

    /// <summary>
    /// <para>A name declared in one file and used in another is found in both.</para>
    /// <para>Which is the whole reason this is not document highlight asked twice: a reader
    /// wanting to know where a function is called does not mean "in the file I am looking at",
    /// and the answer that stops there is the one that reads as though there were no callers.
    /// </para>
    /// </summary>
    [Test]
    public void EveryUseIsFoundAcrossEveryFile()
    {
        using Workspace workspace = new(Caller, Callee);

        // Line 2, column 40: on 'Length' where it is declared.
        Assert.That(
            Uses(workspace, "Greeting.pc", 2, 40),
            Is.EquivalentTo(new[]
            {
                ("Greeting.pc", 2),
                ("Program.pc", 3),
                ("Program.pc", 4),
            }));
    }

    /// <summary>
    /// Asked for the callers rather than for every appearance, which is a different question and
    /// the one an editor asks when it is filling a "find usages" list.
    /// </summary>
    [Test]
    public void TheDeclarationIsLeftOutWhenItWasNotAskedFor()
    {
        using Workspace workspace = new(Caller, Callee);

        Assert.That(
            Uses(workspace, "Greeting.pc", 2, 40, includingDeclaration: false),
            Is.EquivalentTo(new[] { ("Program.pc", 3), ("Program.pc", 4) }));
    }

    /// <summary>
    /// <para>A name the language owns is answered for, which renaming refuses.</para>
    /// <para>The difference is what each one does afterwards. Renaming <c>WriteLine</c> would edit
    /// the uses and leave the declaration where it is, since there is not one; listing them writes
    /// nothing anywhere, and "where is this called" is a fair question about it.</para>
    /// </summary>
    [Test]
    public void ANameTheLanguageOwnsIsAnsweredFor()
    {
        using Workspace workspace = new(Caller, Callee);

        // Line 3, column 17: on 'WriteLine'.
        Assert.That(
            Uses(workspace, "Program.pc", 3, 17),
            Is.EquivalentTo(new[] { ("Program.pc", 3), ("Program.pc", 4) }));
    }

    /// <summary>
    /// <para>Building a model is a use of it.</para>
    /// <para>Marked because the name in a <c>new</c> is recorded as a name. Where it is not, this
    /// walks past it — and a reader asking where a model is used is shown the declaration and the
    /// type beside it while the line that actually makes one goes unmarked.</para>
    /// </summary>
    [Test]
    public void ConstructingAModelIsAUseOfIt()
    {
        const string Building = """
            model Circle
            end model

            shared model Program
                function Main()
                    Circle drawn = new Circle();
                    Console.WriteLine(drawn);
                end function
            end model
            """;

        (CompilationUnit unit, SemanticModel model, SourceText source) = Compile(Building);

        // Line 1, column 7: on 'Circle' where the model is declared.
        Assert.That(
            Marks(Occurrences.In(unit, model, OffsetOf(source, 1, 7)))
                .Select(mark => (mark.Line, mark.Column)),
            Is.EqualTo(new[] { (1, 7), (6, 9), (6, 28) }));
    }

    /// <summary>
    /// <para>Asked from a use rather than from the declaration, and the answer is the same.
    /// </para>
    /// <para>It has to be: what is marked is decided by which symbol the resolver bound the name
    /// to, and every one of these is bound to the same one. Where the cursor happens to sit is
    /// not part of the question.</para>
    /// </summary>
    [Test]
    public void TheAnswerDoesNotDependOnWhichUseIsAskedFrom()
    {
        (CompilationUnit unit, SemanticModel model, SourceText source) = Compile(Counting);

        Assert.That(
            Marks(Occurrences.In(unit, model, OffsetOf(source, 5, 27))),
            Is.EqualTo(Marks(Occurrences.In(unit, model, OffsetOf(source, 3, 17)))));
    }

    /// <summary>
    /// <para>Two locals of the same name in different scopes are two names.</para>
    /// <para><b>What a text search cannot do.</b> Both are spelled <c>value</c> and neither has
    /// anything to do with the other; marking all four would be worse than marking none, because
    /// it would say they were related.</para>
    /// </summary>
    [Test]
    public void TwoNamesSpelledTheSameAreNotOneName()
    {
        (CompilationUnit unit, SemanticModel model, SourceText source) = Compile("""
            shared model Program
                function First()
                    integer value = 1;
                    Console.WriteLine(value);
                end function

                function Second()
                    integer value = 2;
                    Console.WriteLine(value);
                end function
            end model
            """);

        IReadOnlyList<(int Line, int Column, string Kind)> marks =
            Marks(Occurrences.In(unit, model, OffsetOf(source, 3, 17)));

        Assert.That(marks, Is.EqualTo(new[] { (3, 17, "written"), (4, 27, "read") }));
    }

    /// <summary>
    /// <para>A name the language owns is marked, where renaming it is refused.</para>
    /// <para>The two questions come apart here, and correctly. Renaming <c>Count</c> would edit
    /// the uses and leave the compiler's declaration where it is; marking them writes
    /// nothing.</para>
    /// </summary>
    [Test]
    public void AMemberTheLanguageOwnsIsStillMarked()
    {
        (CompilationUnit unit, SemanticModel model, SourceText source) = Compile("""
            shared model Program
                function Main()
                    string word = "hello";
                    Console.WriteLine(word.Count + word.Count);
                end function
            end model
            """);

        // Line 4, column 32: inside the first 'Count'.
        JsonArray? found = Occurrences.In(unit, model, OffsetOf(source, 4, 32));

        Assert.Multiple(() =>
        {
            Assert.That(Marks(found), Has.Count.EqualTo(2), "both, and nothing else");
            Assert.That(
                Rename.Edits([unit], model, unit, OffsetOf(source, 4, 32), "Size"),
                Is.Null,
                "and renaming it is still refused");
        });
    }

    /// <summary>
    /// A function is marked where it is declared and where it is called, which is the case that
    /// catches an implementation marking only the nodes that happen to be plain names.
    /// </summary>
    [Test]
    public void AFunctionIsMarkedAtItsDeclarationAndItsCalls()
    {
        (CompilationUnit unit, SemanticModel model, SourceText source) = Compile("""
            shared model Program
                function Main()
                    Console.WriteLine(Program.Twice(2));
                    Console.WriteLine(Program.Twice(3));
                end function

                integer function Twice(integer value)
                    yield value + value;
                end function
            end model
            """);

        IReadOnlyList<(int Line, int Column, string Kind)> marks =
            Marks(Occurrences.In(unit, model, OffsetOf(source, 7, 22)));

        Assert.Multiple(() =>
        {
            Assert.That(marks, Has.Count.EqualTo(3));
            Assert.That(marks[0].Kind, Is.EqualTo("read"), "the call on line 3");
            Assert.That(marks[2], Is.EqualTo((7, 22, "written")), "and the declaration");
        });
    }

    /// <summary>
    /// <para>A cursor that is in no name marks nothing, and says nothing rather than nothing at
    /// all.</para>
    /// <para>Null instead of an empty list, so that moving the caret onto a keyword leaves what
    /// was marked alone rather than clearing it — an editor asks this on every movement, and
    /// clearing is what an empty list means.</para>
    /// </summary>
    [Test]
    public void ACursorInNoNameMarksNothing()
    {
        (CompilationUnit unit, SemanticModel model, SourceText source) = Compile(Counting);

        // Line 3, column 9: on the word 'integer'.
        Assert.That(Occurrences.In(unit, model, OffsetOf(source, 3, 9)), Is.Null);
    }

    /// <summary>
    /// <para>The marks run down the file, which is not required and is what a reader expects.
    /// </para>
    /// <para>The protocol does not ask for an order, so nothing would break — but an editor
    /// stepping through them with a keystroke follows the order it was given, and one that
    /// jumped about would be its own bug report.</para>
    /// </summary>
    [Test]
    public void TheMarksRunDownTheFile()
    {
        (CompilationUnit unit, SemanticModel model, SourceText source) = Compile(Counting);

        IReadOnlyList<(int Line, int Column, string Kind)> marks =
            Marks(Occurrences.In(unit, model, OffsetOf(source, 4, 9)));

        Assert.That(marks.Select(m => (m.Line, m.Column)), Is.Ordered);
    }
}
