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
/// <para>What every name in a file is, so it can be colored for what it means.</para>
/// <para><b>Checked by decoding the answer back into positions and slicing the source with
/// them.</b> The wire format is five numbers per name, each measured from the name before it, so
/// a list in the wrong order does not fail — it silently colors the wrong characters, and a test
/// asserting on the numbers themselves would pass while the editor painted nonsense. Reading the
/// text back out is the only form of this test that can tell.</para>
/// </summary>
[TestFixture]
public sealed class SemanticTokensTests : LexerTestBase
{
    private const string Program = """
        shared model Program
            shared integer started = 0;

            function Main(string greeting)
                integer counted = Greeting.Length(greeting);
                Console.WriteLine(counted);
            end function
        end model
        """;

    private const string Greeting = """
        shared model Greeting
            public shared integer function Length(string word)
                integer total = word.Count;
                yield total;
            end function
        end model
        """;

    private sealed class Workspace : IDisposable
    {
        public Workspace(string? beside = null)
        {
            Folder = Path.Combine(Path.GetTempPath(), $"profi-c-colors-{Guid.NewGuid():N}");
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

    /// <summary>One name as the editor would read it back off the wire.</summary>
    private sealed record Colored(int Line, int Column, string Text, string Kind, string[] Traits);

    /// <summary>
    /// <para>Undoes the encoding, which is what makes this readable.</para>
    /// <para>Walks the five-number rows the way an editor does — each row's line is a distance
    /// from the row before, and its column is too whenever the two share a line — then slices the
    /// source at the result, so what comes back is the text that would be colored rather than a
    /// number that has to be trusted.</para>
    /// </summary>
    private static IReadOnlyList<Colored> Decode(JsonObject answer, SourceText source)
    {
        JsonArray data = (JsonArray)answer["data"]!;

        List<Colored> read = [];

        int line = 0;
        int column = 0;

        for (int at = 0; at < data.Count; at += 5)
        {
            int deltaLine = (int)data[at]!;
            int deltaColumn = (int)data[at + 1]!;
            int length = (int)data[at + 2]!;
            int kind = (int)data[at + 3]!;
            int traits = (int)data[at + 4]!;

            line += deltaLine;
            column = deltaLine == 0 ? column + deltaColumn : deltaColumn;

            int offset = source.OffsetOfLine(line + 1) + column;

            read.Add(new Colored(
                line,
                column,
                source.Text.Substring(offset, length),
                SemanticTokens.Kinds[kind],
                [.. SemanticTokens.Traits.Where((_, bit) => (traits & (1 << bit)) != 0)]));
        }

        return read;
    }

    private static (IReadOnlyList<Colored> Read, SourceText Source) Colors(
        string file, string? beside = null)
    {
        using Workspace workspace = new(beside);

        DiagnosticBag diagnostics = new();

        SourceDiscovery.Compilation gathered =
            SourceDiscovery.Gather(workspace.At(file), diagnostics)!;

        SemanticModel model = Resolver.Resolve(gathered.Units, diagnostics);
        TypeChecker.Check(gathered.Units, model, diagnostics);

        CompilationUnit unit = gathered.Units.Single(
            u => Path.GetFileName(u.Source.FileName) == file);

        return (Decode(SemanticTokens.Of(unit, model, unit.Source), unit.Source), unit.Source);
    }

    private static Colored The(IReadOnlyList<Colored> read, string text, int line) =>
        read.Single(c => c.Text == text && c.Line == line - 1);

    /// <summary>
    /// <para>Every name lands exactly on itself.</para>
    /// <para>The claim the rest depend on: decode the whole file, slice the source at each
    /// position, and every piece that comes back is a name rather than a bracket, a keyword, or
    /// a name with a character of the next one on the end of it.</para>
    /// </summary>
    [Test]
    public void EveryColorSitsOnAName()
    {
        (IReadOnlyList<Colored> read, _) = Colors("Program.pc");

        Assert.That(read, Is.Not.Empty);

        Assert.Multiple(() =>
        {
            foreach (Colored colored in read)
            {
                Assert.That(
                    colored.Text.All(c => char.IsLetterOrDigit(c) || c == '_'),
                    Is.True,
                    $"line {colored.Line + 1} colored '{colored.Text}'");
            }
        });
    }

    /// <summary>
    /// <para>The order is what the encoding depends on, so it is asserted directly.</para>
    /// <para>A row measured from the one before it can only be read if the rows run down the
    /// file. Out of order they produce negative distances, which no editor rejects — it colors
    /// somewhere else and says nothing.</para>
    /// </summary>
    [Test]
    public void TheColorsRunDownTheFileInOrder()
    {
        (IReadOnlyList<Colored> read, _) = Colors("Program.pc");

        Assert.Multiple(() =>
        {
            for (int at = 1; at < read.Count; at++)
            {
                Assert.That(
                    (read[at].Line, read[at].Column),
                    Is.GreaterThan((read[at - 1].Line, read[at - 1].Column)),
                    $"'{read[at].Text}' comes after '{read[at - 1].Text}'");
            }
        });
    }

    /// <summary>
    /// <para>A parameter is a parameter where it is used, not only where it is declared.</para>
    /// <para><b>The whole reason for this.</b> A grammar can color the name in a signature by
    /// where it sits; it cannot know that <c>greeting</c> four lines down is the same thing. That
    /// is a question about meaning, and it is the compiler's to answer.</para>
    /// </summary>
    [Test]
    public void AParameterIsColoredWhereItIsUsedAsWellAsWhereItIsDeclared()
    {
        (IReadOnlyList<Colored> read, _) = Colors("Program.pc");

        Colored declared = The(read, "greeting", line: 4);
        Colored used = The(read, "greeting", line: 5);

        Assert.Multiple(() =>
        {
            Assert.That(declared.Kind, Is.EqualTo("parameter"));
            Assert.That(used.Kind, Is.EqualTo("parameter"), "and the same four lines down");

            Assert.That(declared.Traits, Does.Contain("declaration"));
            Assert.That(used.Traits, Does.Not.Contain("declaration"), "a use is not a declaration");
        });
    }

    /// <summary>
    /// A local is its own kind of thing, which is what it has been waiting for: a function local
    /// carries no marker at all — no <c>this.</c>, no type name in front of it at the use — so
    /// nothing else in the file tells a reader that is what it is.
    /// </summary>
    [Test]
    public void ALocalIsColoredAsALocal()
    {
        (IReadOnlyList<Colored> read, _) = Colors("Program.pc");

        Assert.Multiple(() =>
        {
            Assert.That(The(read, "counted", line: 5).Kind, Is.EqualTo("variable"));
            Assert.That(The(read, "counted", line: 6).Kind, Is.EqualTo("variable"));
        });
    }

    /// <summary>
    /// <para>A function declared inside a body is a function, not a variable.</para>
    /// <para>It lives in the same scope a local does and is reached by the same bare name, so
    /// nothing about where it is written says which it is. What it <em>is</em> says so, and that
    /// is the compiler's to know.</para>
    /// </summary>
    [Test]
    public void ALocalFunctionIsColoredAsAFunction()
    {
        SourceText source = new(
            """
            shared model Program
                function Main()
                    integer counted = 1;

                    integer function Twice(integer value)
                        yield value + value;
                    end function

                    Console.WriteLine(Twice(counted));
                end function
            end model
            """,
            "Only.pc");

        DiagnosticBag diagnostics = new();
        CompilationUnit unit = Parser.Parse(source, diagnostics);

        SemanticModel model = Resolver.Resolve([unit], diagnostics, requireEntryPoint: false);
        TypeChecker.Check([unit], model, diagnostics);

        IReadOnlyList<Colored> read = Decode(SemanticTokens.Of(unit, model, source), source);

        Assert.Multiple(() =>
        {
            Assert.That(The(read, "Twice", line: 5).Kind, Is.EqualTo("function"), "declared");
            Assert.That(The(read, "Twice", line: 9).Kind, Is.EqualTo("function"), "called");
            Assert.That(The(read, "counted", line: 9).Kind, Is.EqualTo("variable"), "beside it");
        });
    }

    /// <summary>
    /// <para>A name being invoked reads as a function; the same name elsewhere reads as what
    /// holds it.</para>
    /// <para>A local of a delegate type genuinely <em>is</em> a local, so what it was declared as
    /// cannot decide this. What is being done with it can: the parentheses are the thing a reader
    /// is looking at, and a call should look like one whether the function came from a
    /// declaration or out of a variable.</para>
    /// </summary>
    [Test]
    public void ANameBeingInvokedIsColoredAsAFunction()
    {
        SourceText source = new(
            """
            shared model Program
                function Main()
                    integer delegate(integer) scale = (n) yield n * 2;

                    Console.WriteLine(scale(2));
                    Program.Take(scale);
                end function

                function Take(integer delegate(integer) what)
                    Console.WriteLine(what(1));
                end function
            end model
            """,
            "Only.pc");

        DiagnosticBag diagnostics = new();
        CompilationUnit unit = Parser.Parse(source, diagnostics);

        SemanticModel model = Resolver.Resolve([unit], diagnostics, requireEntryPoint: false);
        TypeChecker.Check([unit], model, diagnostics);

        IReadOnlyList<Colored> read = Decode(SemanticTokens.Of(unit, model, source), source);

        Assert.Multiple(() =>
        {
            Assert.That(The(read, "scale", line: 3).Kind, Is.EqualTo("variable"), "declared");
            Assert.That(The(read, "scale", line: 5).Kind, Is.EqualTo("function"), "invoked");
            Assert.That(
                The(read, "scale", line: 6).Kind,
                Is.EqualTo("variable"),
                "handed on rather than called");

            Assert.That(
                The(read, "what", line: 10).Kind,
                Is.EqualTo("function"),
                "and a parameter holding one, where it is called");
        });
    }

    /// <summary>
    /// <para>A dotted type name is colored as the several names it is.</para>
    /// <para><c>Shapes.Flat.Circle</c> is two namespaces and a type. Colored as one thing, the
    /// part that says where the type came from reads as part of its name — which is the opposite
    /// of what qualifying it was for.</para>
    /// </summary>
    [Test]
    public void EachPartOfADottedTypeNameIsColoredAsWhatItIs()
    {
        SourceText source = new(
            """
            namespace Shapes.Flat

                public model Circle
                end model

            end namespace

            shared model Program
                function Main()
                    Shapes.Flat.Circle here;
                    Console.WriteLine(here);
                end function
            end model
            """,
            "Only.pc");

        DiagnosticBag diagnostics = new();
        CompilationUnit unit = Parser.Parse(source, diagnostics);

        SemanticModel model = Resolver.Resolve([unit], diagnostics, requireEntryPoint: false);
        TypeChecker.Check([unit], model, diagnostics);

        IReadOnlyList<Colored> read = Decode(SemanticTokens.Of(unit, model, source), source);

        Assert.Multiple(() =>
        {
            Assert.That(The(read, "Shapes", line: 10).Kind, Is.EqualTo("namespace"));
            Assert.That(The(read, "Flat", line: 10).Kind, Is.EqualTo("namespace"));
            Assert.That(The(read, "Circle", line: 10).Kind, Is.EqualTo("class"), "the type");

            Assert.That(
                The(read, "WriteLine", line: 11).Kind,
                Is.EqualTo("method"),
                "and a member the language provides, which carries no symbol either");

            Assert.That(
                The(read, "WriteLine", line: 11).Traits,
                Does.Contain("defaultLibrary"));
        });
    }

    /// <summary>
    /// <para>A primitive written as a reserved word stays a keyword.</para>
    /// <para><c>integer</c> is a word the language reserves, not a name a program can rename or
    /// go to — and the grammar has already colored it as one. A semantic token wins where both
    /// apply, so claiming it here repaints a keyword as a type name and leaves <c>function</c>
    /// beside it looking like a different kind of thing.</para>
    /// <para>The capitalized <c>Integer</c> is a different thing entirely: a model the language
    /// provides, holding what a whole number can hold, and that one is a name.</para>
    /// </summary>
    [Test]
    public void APrimitiveWrittenAsAReservedWordIsNotColoredAsAType()
    {
        SourceText source = new(
            """
            shared model Program
                function Main()
                    integer counted = Integer.MaxValue;
                    Console.WriteLine(counted);
                end function
            end model
            """,
            "Only.pc");

        DiagnosticBag diagnostics = new();
        CompilationUnit unit = Parser.Parse(source, diagnostics);

        SemanticModel model = Resolver.Resolve([unit], diagnostics, requireEntryPoint: false);
        TypeChecker.Check([unit], model, diagnostics);

        IReadOnlyList<Colored> read = Decode(SemanticTokens.Of(unit, model, source), source);

        Assert.Multiple(() =>
        {
            Assert.That(
                read.Any(c => c.Text == "integer"),
                Is.False,
                "the reserved word is the grammar's to color");

            Assert.That(
                The(read, "Integer", line: 3).Kind,
                Is.EqualTo("class"),
                "and the model of the same name is not the same thing");
        });
    }

    /// <summary>Each kind of declared name arrives as the kind the protocol has a word for.</summary>
    [Test]
    public void EachKindOfNameIsSaidToBeWhatItIs()
    {
        (IReadOnlyList<Colored> read, _) = Colors("Program.pc");

        Assert.Multiple(() =>
        {
            Assert.That(The(read, "Program", line: 1).Kind, Is.EqualTo("class"));
            Assert.That(The(read, "started", line: 2).Kind, Is.EqualTo("property"));
            Assert.That(The(read, "Main", line: 4).Kind, Is.EqualTo("method"));
            Assert.That(The(read, "Greeting", line: 5).Kind, Is.EqualTo("class"));
            Assert.That(The(read, "Length", line: 5).Kind, Is.EqualTo("method"));
        });
    }

    /// <summary>
    /// <para>A shared field is shared where it is written, and a name the language owns says so.
    /// </para>
    /// <para>These arrive free and most themes already render them, which is the point of using
    /// the protocol's own words: a <c>constant</c> looks read-only without anybody picking a
    /// color for it.</para>
    /// </summary>
    [Test]
    public void WhatIsTrueOfANameTravelsWithIt()
    {
        (IReadOnlyList<Colored> read, _) = Colors("Program.pc");

        Assert.Multiple(() =>
        {
            Assert.That(The(read, "started", line: 2).Traits, Does.Contain("static"));

            Assert.That(
                The(read, "Console", line: 6).Traits,
                Does.Contain("defaultLibrary"),
                "no program declared it, so nothing in this program can be gone to");

            Assert.That(
                The(read, "Program", line: 1).Traits,
                Does.Not.Contain("defaultLibrary"),
                "and this one is the program's own");
        });
    }

    /// <summary>
    /// A loop variable cannot be assigned to, so it carries the trait that says so — which is
    /// how a theme renders it without this having to choose a color.
    /// </summary>
    [Test]
    public void AReadOnlyNameSaysSo()
    {
        using Workspace workspace = new();

        SourceText source = new(
            """
            shared model Program
                function Main()
                    loop each item in {1, 2}
                        Console.WriteLine(item);
                    end loop
                end function
            end model
            """,
            workspace.At("Only.pc"));

        DiagnosticBag diagnostics = new();
        CompilationUnit unit = Parser.Parse(source, diagnostics);

        SemanticModel model = Resolver.Resolve([unit], diagnostics, requireEntryPoint: false);
        TypeChecker.Check([unit], model, diagnostics);

        IReadOnlyList<Colored> read = Decode(SemanticTokens.Of(unit, model, source), source);

        Assert.That(The(read, "item", line: 4).Traits, Does.Contain("readonly"));
    }

    /// <summary>
    /// <para>A file that will not compile is still colored.</para>
    /// <para>Which is most of the time it is being looked at. The resolver recovers rather than
    /// stopping, so the names it did work out still arrive — and coloring nothing until a file is
    /// finished would mean coloring nothing while it is being written.</para>
    /// </summary>
    [Test]
    public void AFileWithAMistakeInItIsStillColored()
    {
        SourceText source = new(
            """
            shared model Program
                function Main()
                    integer counted = nothingDeclaresThis;
                    Console.WriteLine(counted);
                end function
            end model
            """,
            "Broken.pc");

        DiagnosticBag diagnostics = new();
        CompilationUnit unit = Parser.Parse(source, diagnostics);

        SemanticModel model = Resolver.Resolve([unit], diagnostics, requireEntryPoint: false);
        TypeChecker.Check([unit], model, diagnostics);

        IReadOnlyList<Colored> read = Decode(SemanticTokens.Of(unit, model, source), source);

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.HasErrors, Is.True, "the fixture is meant to be wrong");
            Assert.That(The(read, "counted", line: 3).Kind, Is.EqualTo("variable"));
            Assert.That(The(read, "counted", line: 4).Kind, Is.EqualTo("variable"));

            Assert.That(
                read.Any(c => c.Text == "nothingDeclaresThis"),
                Is.False,
                "nothing was worked out about it, so nothing is claimed about it");
        });
    }

    /// <summary>
    /// <para>Across every sample: each color lands on a name, and they run down the file.</para>
    /// <para>The sweep that covers what nobody thought to write a case for. A construct whose
    /// name span is wrong, or that produces two colors on one identifier, fails here without
    /// anyone having anticipated it — and the corpus is where every construct in the language
    /// already lives.</para>
    /// </summary>
    [TestCaseSource(nameof(SampleNames))]
    public void Sample_IsColoredWellFormed(string name)
    {
        SourceText source = LoadSample(name);
        DiagnosticBag diagnostics = new();

        CompilationUnit unit = Parser.Parse(source, diagnostics);
        SemanticModel model = Resolver.Resolve([unit], diagnostics, requireEntryPoint: false);
        TypeChecker.Check([unit], model, diagnostics);

        IReadOnlyList<Colored> read = Decode(SemanticTokens.Of(unit, model, source), source);

        Assert.Multiple(() =>
        {
            for (int at = 0; at < read.Count; at++)
            {
                Assert.That(
                    read[at].Text.All(c => char.IsLetterOrDigit(c) || c == '_'),
                    Is.True,
                    $"{name} line {read[at].Line + 1} colored '{read[at].Text}'");

                if (at > 0)
                {
                    Assert.That(
                        (read[at].Line, read[at].Column),
                        Is.GreaterThan((read[at - 1].Line, read[at - 1].Column)),
                        $"{name}: '{read[at].Text}' comes after '{read[at - 1].Text}'");
                }
            }
        });
    }

    /// <summary>
    /// <para>A constructor is colored as the type it builds, in all three places the word
    /// appears.</para>
    /// <para><b>It is the one member that does not read as a member.</b> A constructor is written
    /// with the type's own name, so the declaration, the type beside it, and the <c>new</c> below
    /// are the same word — and a reader looking at any of them is looking at the type. Colored as
    /// a method, the one that builds it reads as something other than the other two.</para>
    /// </summary>
    [Test]
    public void AConstructorIsColoredAsTheTypeItBuilds()
    {
        const string Shapes = """
            model Circle
                real radius;

                public function Circle(real r)
                    this.radius = r;
                end function
            end model

            shared model Drawing
                function Draw()
                    let here = new Circle(2.0);
                    Console.WriteLine(here);
                end function
            end model
            """;

        (IReadOnlyList<Colored> read, _) = Colors("Beside.pc", Shapes);

        Assert.Multiple(() =>
        {
            Assert.That(The(read, "Circle", 1).Kind, Is.EqualTo("class"), "the model");
            Assert.That(The(read, "Circle", 4).Kind, Is.EqualTo("class"), "the constructor");
            Assert.That(The(read, "Circle", 11).Kind, Is.EqualTo("class"), "and the new");
        });
    }

    /// <summary>
    /// The legend is what the numbers mean, so a kind named in one place and not the other would
    /// color every name in the file as whatever sits at that index instead.
    /// </summary>
    [Test]
    public void EveryKindAndTraitIsNamedOnce()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SemanticTokens.Kinds, Is.Unique);
            Assert.That(SemanticTokens.Traits, Is.Unique);
            Assert.That(
                SemanticTokens.Traits,
                Has.Count.LessThanOrEqualTo(31),
                "they are sent as a bit set");
        });
    }
}
