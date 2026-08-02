using System.Text.Json;

namespace ProfiC.Tests.Tooling;

/// <summary>
/// <para>What <c>pc outline</c> tells an editor a file declares.</para>
/// <para>This is a published contract rather than an internal shape: an editor draws breadcrumbs
/// and an Outline view from it, and reads it by name. Changing a kind or dropping a position is
/// not refactoring — it is breaking something in another repository that has no way to notice
/// until a member stops appearing.</para>
/// <para><b>Not parallelizable</b>, since reading what the command printed means redirecting the
/// console, which is one per process.</para>
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class OutlineTests
{
    /// <summary>One entry as the command writes it.</summary>
    private sealed record Entry(
        string Name,
        string Kind,
        string Detail,
        int Line,
        int Column,
        int EndLine,
        int EndColumn,
        Entry[] Children);

    /// <summary>Outlines a program written to a temporary file, and reads the answer back.</summary>
    private static Entry[] Outline(string program)
    {
        string folder = Path.Combine(Path.GetTempPath(), $"profi-c-outline-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);

        try
        {
            string path = Path.Combine(folder, "Outlined.pc");
            File.WriteAllText(path, program);

            TextWriter was = Console.Out;
            StringWriter said = new();

            int code;

            try
            {
                Console.SetOut(said);
                code = ProfiC.Cli.Program.Run(["outline", path]);
            }
            finally
            {
                Console.SetOut(was);
            }

            Assert.That(code, Is.Zero, said.ToString());

            return JsonSerializer.Deserialize<Entry[]>(
                said.ToString(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>Every entry in the tree, flattened, for asking whether something is in it.</summary>
    private static IEnumerable<Entry> Everything(IEnumerable<Entry> entries) =>
        entries.SelectMany(entry => Everything(entry.Children).Prepend(entry));

    private const string ALittleOfEverything = """
        namespace Shop

        enumeration Color
            Red,
            Green
        end enumeration

        structure Point
            integer x;
        end structure

        model Basket

            integer count = 0;
            shared integer made = 0;

            public function Basket()
                this.count = 0;
            end function

            public integer function Count()
                yield this.count;
            end function

        end model

        shared model Program
            function Main()
                Console.WriteLine("hello");
            end function
        end model

        end namespace
        """;

    /// <summary>
    /// <para>Each kind of declaration is named as its own kind.</para>
    /// <para>The kinds are what an editor draws an icon from, so a model showing as a method is
    /// wrong in a way nothing else here would catch.</para>
    /// </summary>
    [Test]
    public void EachDeclarationIsNamedAsItsOwnKind()
    {
        Dictionary<string, string> kinds = Everything(Outline(ALittleOfEverything))
            .GroupBy(entry => entry.Name)
            .ToDictionary(group => group.Key, group => group.First().Kind);

        Assert.Multiple(() =>
        {
            Assert.That(kinds["Shop"], Is.EqualTo("namespace"));
            Assert.That(kinds["Color"], Is.EqualTo("enumeration"));
            Assert.That(kinds["Red"], Is.EqualTo("enumMember"));
            Assert.That(kinds["Point"], Is.EqualTo("structure"));
            Assert.That(kinds["Basket"], Is.EqualTo("model"));
            Assert.That(kinds["count"], Is.EqualTo("field"));
            Assert.That(kinds["made"], Is.EqualTo("field"));
            Assert.That(kinds["Count"], Is.EqualTo("function"));
        });
    }

    /// <summary>
    /// <para>A function named for the type that holds it is a constructor.</para>
    /// <para>Decided here rather than in the editor, which would otherwise need to know a rule
    /// about Profi-C to draw the right icon — and would be the second place that knew it.</para>
    /// </summary>
    [Test]
    public void AFunctionNamedForItsModelIsAConstructor()
    {
        Entry[] outlined = Outline(ALittleOfEverything);

        Entry basket = Everything(outlined).Single(e => e.Name == "Basket" && e.Kind == "model");

        Assert.Multiple(() =>
        {
            Assert.That(
                basket.Children.Single(m => m.Name == "Basket").Kind,
                Is.EqualTo("constructor"));

            Assert.That(
                basket.Children.Single(m => m.Name == "Count").Kind,
                Is.EqualTo("function"),
                "and a function that is not is not");
        });
    }

    /// <summary>Declarations nest, so that a breadcrumb can say which model a member is in.</summary>
    [Test]
    public void DeclarationsNestInsideWhatHoldsThem()
    {
        Entry[] outlined = Outline(ALittleOfEverything);

        Assert.Multiple(() =>
        {
            Assert.That(outlined, Has.Length.EqualTo(1), "one namespace holds the whole file");
            Assert.That(outlined[0].Name, Is.EqualTo("Shop"));

            Assert.That(
                outlined[0].Children.Select(child => child.Name),
                Is.EqualTo(new[] { "Color", "Point", "Basket", "Program" }),
                "in the order they were written");

            Assert.That(
                outlined[0].Children.Single(c => c.Name == "Color").Children.Select(m => m.Name),
                Is.EqualTo(new[] { "Red", "Green" }));
        });
    }

    /// <summary>
    /// <para>Every entry says where it starts and where it ends.</para>
    /// <para>One-based, as every position a reader sees in Profi-C is — an editor counting from
    /// zero converts at its own boundary rather than being handed a convention that matches no
    /// diagnostic. The end matters as much as the start: it is what lets an editor say which
    /// declaration the cursor is inside.</para>
    /// </summary>
    [Test]
    public void EveryEntrySaysWhereItStartsAndEnds()
    {
        Assert.Multiple(() =>
        {
            foreach (Entry entry in Everything(Outline(ALittleOfEverything)))
            {
                Assert.That(entry.Line, Is.GreaterThan(0), $"{entry.Name} line");
                Assert.That(entry.Column, Is.GreaterThan(0), $"{entry.Name} column");

                Assert.That(entry.EndLine, Is.GreaterThanOrEqualTo(entry.Line),
                            $"{entry.Name} ends before it starts");
            }
        });
    }

    /// <summary>A child is inside its parent, or an editor cannot tell where the cursor is.</summary>
    [Test]
    public void AChildLiesInsideItsParent()
    {
        Assert.Multiple(() =>
        {
            foreach (Entry parent in Everything(Outline(ALittleOfEverything)))
            {
                foreach (Entry child in parent.Children)
                {
                    Assert.That(child.Line, Is.GreaterThanOrEqualTo(parent.Line),
                                $"{child.Name} starts above {parent.Name}");

                    Assert.That(child.EndLine, Is.LessThanOrEqualTo(parent.EndLine),
                                $"{child.Name} ends below {parent.Name}");
                }
            }
        });
    }

    /// <summary>
    /// <para>A file that does not compile still outlines.</para>
    /// <para>The case the whole thing is for. An outline is wanted most while a file is being
    /// written, which is exactly when it is broken — so this is parsed and nothing more, and the
    /// parser recovers where the rest of the front end would refuse.</para>
    /// </summary>
    [Test]
    public void AFileThatDoesNotCompileStillOutlines()
    {
        Entry[] outlined = Outline("""
            shared model Program

                function Main()
                    integer x = ;
                end function

                function Other()
                end function

            end model
            """);

        Assert.That(
            Everything(outlined).Select(entry => entry.Name),
            Is.EqualTo(new[] { "Program", "Main", "Other" }),
            "the shape around a mistake is still the shape");
    }

    /// <summary>What was written before a declaration is carried along, for an editor to show.</summary>
    [Test]
    public void ModifiersAreCarriedAsDetail()
    {
        Entry program = Everything(Outline(ALittleOfEverything)).Single(e => e.Name == "Program");

        Assert.That(program.Detail, Does.Contain("shared"));
    }
}
