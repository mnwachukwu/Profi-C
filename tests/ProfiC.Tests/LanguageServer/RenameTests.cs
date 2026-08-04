using System.Text.Json.Nodes;
using ProfiC.Cli;
using ProfiC.Cli.LanguageServer;
using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Semantics;
using ProfiC.Compiler.Text;

namespace ProfiC.Tests.LanguageServer;

/// <summary>
/// <para>Changing a name everywhere it is written.</para>
/// <para><b>The one answer that edits somebody's file, which is what these are for.</b> A hover
/// that is wrong is a tooltip nobody reads twice. A rename that is wrong writes over code, and a
/// reader who accepts it has no way back. So what is held here is not only that the right names
/// change, but that every edit is exactly the identifier and nothing around it — the edits are
/// applied to the text and the result compared, because a range that is off by one is invisible
/// in a list of numbers and obvious in the file it produced.</para>
/// </summary>
[TestFixture]
public sealed class RenameTests
{
    private const string Program = """
        shared model Program
            function Main()
                integer counted = Greeting.Length("hello");
                Console.WriteLine(counted + counted);
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

    private sealed class Workspace : IDisposable
    {
        public Workspace(string? beside = null)
        {
            Folder = Path.Combine(Path.GetTempPath(), $"profi-c-rename-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Folder);

            File.WriteAllText(Path.Combine(Folder, "Program.pc"), Program);
            File.WriteAllText(Path.Combine(Folder, "Greeting.pc"), Greeting);

            if (beside is not null)
            {
                File.WriteAllText(Path.Combine(Folder, "Beside.pc"), beside);
            }
        }

        public string Folder { get; }

        public string At(string name) => Path.Combine(Folder, name);

        public void Dispose() => Directory.Delete(Folder, recursive: true);
    }

    private static (IReadOnlyList<CompilationUnit> Units, SemanticModel Model, CompilationUnit Unit)
        Compile(Workspace workspace)
    {
        DiagnosticBag diagnostics = new();

        SourceDiscovery.Compilation gathered =
            SourceDiscovery.Gather(workspace.At("Program.pc"), diagnostics)!;

        SemanticModel model = Resolver.Resolve(gathered.Units, diagnostics);
        TypeChecker.Check(gathered.Units, model, diagnostics);

        return (
            gathered.Units,
            model,
            gathered.Units.Single(u => Path.GetFileName(u.Source.FileName) == "Program.pc"));
    }

    private static int OffsetOf(SourceText source, int line, int column) =>
        source.OffsetOfLine(line) + column - 1;

    /// <summary>
    /// <para>The file as it would be after the edits, which is the only readable way to check
    /// them.</para>
    /// <para>Applied back to front so that each range still means what it meant: editing from the
    /// top would move every offset below it.</para>
    /// </summary>
    private static string Applied(string text, JsonArray edits)
    {
        SourceText source = new(text, "<test>");

        List<(int At, int Length, string With)> ordered =
        [
            .. edits.Select(edit =>
            {
                int line = (int)edit!["range"]!["start"]!["line"]! + 1;
                int character = (int)edit["range"]!["start"]!["character"]! + 1;
                int endCharacter = (int)edit["range"]!["end"]!["character"]! + 1;

                int at = source.OffsetOfLine(line) + character - 1;

                return (at, endCharacter - character, (string)edit["newText"]!);
            }),
        ];

        string edited = text;

        foreach ((int at, int length, string with) in ordered.OrderByDescending(e => e.At))
        {
            edited = edited.Remove(at, length).Insert(at, with);
        }

        return edited;
    }

    private static JsonArray EditsIn(JsonObject? change, string file) =>
        (JsonArray?)change?["changes"]?[ProfiC.Cli.LanguageServer.Conversions.UriOf(file)] ?? [];

    /// <summary>
    /// <para>A local is renamed where it is declared and everywhere it is used.</para>
    /// <para>Three places on two lines, one of them twice — which is what catches an
    /// implementation that stops at the first use on a line.</para>
    /// </summary>
    [Test]
    public void ALocalIsRenamedEverywhereItIsWritten()
    {
        using Workspace workspace = new();

        (IReadOnlyList<CompilationUnit> units, SemanticModel model, CompilationUnit unit) =
            Compile(workspace);

        // Line 3, column 17: inside 'counted' where it is declared.
        JsonObject? change = Rename.Edits(
            units, model, unit, OffsetOf(unit.Source, 3, 17), "total");

        JsonArray edits = EditsIn(change, workspace.At("Program.pc"));

        Assert.Multiple(() =>
        {
            Assert.That(edits, Has.Count.EqualTo(3));

            Assert.That(
                Applied(Program, edits),
                Is.EqualTo("""
                    shared model Program
                        function Main()
                            integer total = Greeting.Length("hello");
                            Console.WriteLine(total + total);
                        end function
                    end model
                    """));
        });
    }

    /// <summary>
    /// <para>Renaming a model rewrites every <c>new</c> that builds one.</para>
    /// <para><b>The name in a <c>new</c> is a name, and for a long time nothing recorded that it
    /// was.</b> A node whose name was never written down is one rename walks past, so the
    /// declaration and the written type changed and the construction did not — leaving a program
    /// that says <c>Ring here = new Circle();</c> and does not compile, from an edit a reader
    /// accepted because they had no way to see what it would do.</para>
    /// </summary>
    [Test]
    public void AModelIsRenamedWhereItIsConstructed()
    {
        const string Shapes = """
            model Circle
            end model

            shared model Drawing
                function Draw()
                    Circle here = new Circle();
                    Console.WriteLine(here);
                end function
            end model
            """;

        using Workspace workspace = new(Shapes);

        (IReadOnlyList<CompilationUnit> units, SemanticModel model, _) = Compile(workspace);

        CompilationUnit beside =
            units.Single(u => Path.GetFileName(u.Source.FileName) == "Beside.pc");

        // Line 1, column 7: on 'Circle' where the model is declared.
        JsonObject? change = Rename.Edits(
            units, model, beside, OffsetOf(beside.Source, 1, 7), "Ring");

        JsonArray edits = EditsIn(change, workspace.At("Beside.pc"));

        Assert.Multiple(() =>
        {
            Assert.That(edits, Has.Count.EqualTo(3), "declared, written as a type, and constructed");

            Assert.That(
                Applied(Shapes, edits),
                Is.EqualTo("""
                    model Ring
                    end model

                    shared model Drawing
                        function Draw()
                            Ring here = new Ring();
                            Console.WriteLine(here);
                        end function
                    end model
                    """));
        });
    }

    /// <summary>
    /// <para>A function is renamed in the file that declares it and in the file that calls it.
    /// </para>
    /// <para>A program is a compilation, so this is the ordinary case rather than the awkward
    /// one — and an implementation that only edited the open file would leave the program not
    /// compiling.</para>
    /// </summary>
    [Test]
    public void AFunctionIsRenamedAcrossEveryFile()
    {
        using Workspace workspace = new();

        (IReadOnlyList<CompilationUnit> units, SemanticModel model, CompilationUnit unit) =
            Compile(workspace);

        // Line 3, column 36: inside 'Length', which Greeting.pc declares.
        JsonObject? change = Rename.Edits(
            units, model, unit, OffsetOf(unit.Source, 3, 36), "Size");

        Assert.Multiple(() =>
        {
            Assert.That(
                Applied(Program, EditsIn(change, workspace.At("Program.pc"))),
                Does.Contain("Greeting.Size(\"hello\")"));

            Assert.That(
                Applied(Greeting, EditsIn(change, workspace.At("Greeting.pc"))),
                Does.Contain("integer function Size(string word)"));
        });
    }

    /// <summary>
    /// <para>What the language owns is not renameable.</para>
    /// <para><c>Count</c> is the compiler's, not this program's. Renaming it would edit the uses
    /// and leave the declaration where it is — a program that does not compile, arrived at by a
    /// command that looked like it worked.</para>
    /// </summary>
    [Test]
    public void AMemberTheLanguageProvidesIsNotRenamed()
    {
        using Workspace workspace = new();

        (IReadOnlyList<CompilationUnit> units, SemanticModel model, _) = Compile(workspace);

        CompilationUnit greeting = units.Single(
            u => Path.GetFileName(u.Source.FileName) == "Greeting.pc");

        // Line 3, column 21: inside 'Count' on 'word.Count'.
        Assert.That(
            Rename.Edits(units, model, greeting, OffsetOf(greeting.Source, 3, 21), "Size"),
            Is.Null);
    }

    /// <summary>Somewhere that names nothing cannot be renamed, and says so before asking.</summary>
    [Test]
    public void SomewhereThatNamesNothingIsNotRenameable()
    {
        using Workspace workspace = new();

        (_, SemanticModel model, CompilationUnit unit) = Compile(workspace);

        Assert.That(
            Rename.Prepare(unit, model, unit.Source.Text.Length + 50),
            Is.Null);
    }

    /// <summary>
    /// Preparing says where the name is and what it is called, so an editor can highlight it and
    /// fill the box before anybody types.
    /// </summary>
    [Test]
    public void PreparingSaysWhereTheNameIsAndWhatItIs()
    {
        using Workspace workspace = new();

        (_, SemanticModel model, CompilationUnit unit) = Compile(workspace);

        JsonObject? prepared = Rename.Prepare(unit, model, OffsetOf(unit.Source, 3, 17));

        Assert.Multiple(() =>
        {
            Assert.That((string?)prepared?["placeholder"], Is.EqualTo("counted"));

            // Line 3 as a reader counts, and the name alone rather than the declaration.
            Assert.That((int?)prepared?["range"]?["start"]?["line"], Is.EqualTo(2));
            Assert.That((int?)prepared?["range"]?["start"]?["character"], Is.EqualTo(16));
            Assert.That((int?)prepared?["range"]?["end"]?["character"], Is.EqualTo(23));
        });
    }
}
