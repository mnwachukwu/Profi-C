using System.Text.Json;

namespace ProfiC.Tests.Tooling;

/// <summary>
/// <para>What <c>pc project</c> answers about which <c>.pcp</c> builds a file.</para>
/// <para>A published contract, like <c>outline</c>: an editor reads it to decide what its Run and
/// Build buttons point at. It exists so that nothing else has to read a project file — and the
/// tests that matter most here are the ones a hand-written second reader gets wrong, since those
/// are what the command is for.</para>
/// <para><b>Not parallelizable</b>, since reading what the command printed means redirecting the
/// console, which is one per process.</para>
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class ProjectSearchTests
{
    /// <summary>The answer as the command writes it.</summary>
    private sealed record Answer(string File, string? Project, int Searched);

    /// <summary>A folder laid out for one test, removed afterwards however the test ends.</summary>
    private sealed class Tree : IDisposable
    {
        public Tree() =>
            Root = Directory.CreateDirectory(
                Path.Combine(Path.GetTempPath(), $"profi-c-project-{Guid.NewGuid():N}")).FullName;

        public string Root { get; }

        /// <summary>Writes a file, making whatever folders it sits in.</summary>
        public string Write(string relative, string content)
        {
            string path = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);

            return path;
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    /// <summary>The smallest program that compiles, for a file whose contents do not matter.</summary>
    private const string AProgram = """
        shared model Program
            function Main()
            end function
        end model
        """;

    /// <summary>Asks the command about a file, and reads the answer back.</summary>
    private static Answer Asked(string path)
    {
        TextWriter was = Console.Out;
        StringWriter said = new();

        int code;

        try
        {
            Console.SetOut(said);
            code = ProfiC.Cli.Program.Run(["project", path]);
        }
        finally
        {
            Console.SetOut(was);
        }

        Assert.That(code, Is.Zero, said.ToString());

        return JsonSerializer.Deserialize<Answer>(
            said.ToString(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    /// <summary>A project that names a file outright builds it.</summary>
    [Test]
    public void AProjectClaimsAFileItNames()
    {
        using Tree tree = new();

        tree.Write("shop/shop.pcp", """
            project Shop
                source Program.pc
            end project
            """);

        string program = tree.Write("shop/Program.pc", AProgram);

        Answer answer = Asked(program);

        Assert.Multiple(() =>
        {
            Assert.That(answer.Project, Is.EqualTo(Path.Combine(tree.Root, "shop", "shop.pcp")));
            Assert.That(answer.Searched, Is.EqualTo(1));
        });
    }

    /// <summary>
    /// <para>A <c>source</c> naming a folder claims the files directly inside it, and no
    /// deeper.</para>
    /// <para>Both halves matter. Claiming only what is written by name would miss most of a real
    /// project; claiming the whole tree below would attach a file to a project that does not
    /// build it, and running that project would compile something else entirely.</para>
    /// </summary>
    [Test]
    public void AFolderClaimsWhatIsDirectlyInsideItAndNoDeeper()
    {
        using Tree tree = new();

        tree.Write("shop/shop.pcp", """
            project Shop
                source models
            end project
            """);

        string inside = tree.Write("shop/models/Product.pc", AProgram);
        string below = tree.Write("shop/models/parts/Widget.pc", AProgram);

        Assert.Multiple(() =>
        {
            Assert.That(Asked(inside).Project, Is.Not.Null);
            Assert.That(Asked(below).Project, Is.Null, "a folder does not descend");
        });
    }

    /// <summary>
    /// A project claims what its references build, since those sources are compiled into it —
    /// which is what makes running the outer project the right answer for one of them.
    /// </summary>
    [Test]
    public void AProjectClaimsWhatItReferences()
    {
        using Tree tree = new();

        tree.Write("app/app.pcp", """
            project App
                reference ../core/core.pcp
                source Program.pc
            end project
            """);

        tree.Write("app/Program.pc", AProgram);

        tree.Write("core/core.pcp", """
            project Core
                source Tally.pc
            end project
            """);

        string tally = tree.Write("core/Tally.pc", "model Tally\nend model");

        // Asked from above, so the walk reaches app.pcp rather than core.pcp — the search starts
        // beside the file, and core.pcp is what claims it there. Naming the outer one is the
        // point: it is reached through the reference.
        Assert.That(
            Asked(tally).Project,
            Is.EqualTo(Path.Combine(tree.Root, "core", "core.pcp")),
            "the nearest project that claims it wins");

        string apart = tree.Write("elsewhere/Odd.pc", AProgram);

        Assert.That(Asked(apart).Project, Is.Null);
    }

    /// <summary>
    /// <para>How many projects were read separates two different things to be told.</para>
    /// <para>"There is no project here" and "there are projects and none of them wants this file"
    /// call for different messages, and only the second has something to go and look at.</para>
    /// </summary>
    [Test]
    public void HowManyWereReadSeparatesNoProjectFromNoProjectThatWantsIt()
    {
        using Tree tree = new();

        string alone = tree.Write("alone/Program.pc", AProgram);

        Assert.That(Asked(alone).Searched, Is.Zero, "nothing above it is a project");

        tree.Write("taken/taken.pcp", """
            project Taken
                source Other.pc
            end project
            """);

        tree.Write("taken/Other.pc", AProgram);

        string unwanted = tree.Write("taken/Program.pc", AProgram);

        Answer answer = Asked(unwanted);

        Assert.Multiple(() =>
        {
            Assert.That(answer.Project, Is.Null);
            Assert.That(answer.Searched, Is.EqualTo(1), "one was read and it did not want the file");
        });
    }

    /// <summary>
    /// <para>A <c>source</c> written inside a block comment names nothing.</para>
    /// <para>The test this command exists for. Reading a project file line by line for the word
    /// <c>source</c> — which is what anything not using the compiler's reader ends up doing —
    /// gets this wrong, and gets it wrong <i>quietly</i>: a project claims a file it does not
    /// build, and running it compiles a program nobody was looking at.</para>
    /// </summary>
    [Test]
    public void ASourceInsideACommentNamesNothing()
    {
        using Tree tree = new();

        tree.Write("shop/shop.pcp", """
            project Shop
                source Program.pc
                ##
                    Left out for now.
                    source Draft.pc
                ##
            end project
            """);

        tree.Write("shop/Program.pc", AProgram);

        string draft = tree.Write("shop/Draft.pc", AProgram);

        Assert.That(Asked(draft).Project, Is.Null);
    }

    /// <summary>
    /// A <c>source</c> written after <c>end project</c> names nothing either, for the same
    /// reason: the project closed, and what follows it is not part of it.
    /// </summary>
    [Test]
    public void ASourceAfterTheEndNamesNothing()
    {
        using Tree tree = new();

        tree.Write("shop/shop.pcp", """
            project Shop
                source Program.pc
            end project
            source Stray.pc
            """);

        tree.Write("shop/Program.pc", AProgram);

        string stray = tree.Write("shop/Stray.pc", AProgram);

        Assert.That(Asked(stray).Project, Is.Null);
    }

    /// <summary>
    /// <para>A project with a mistake in it still claims what it names.</para>
    /// <para>Which files a project lists is not a question about whether it builds. Passing over
    /// a broken project would run the file on its own and report that nothing claimed it — hiding
    /// the project that plainly did, and with it the mistake worth fixing.</para>
    /// </summary>
    [Test]
    public void AProjectWithAMistakeInItStillClaimsWhatItNames()
    {
        using Tree tree = new();

        tree.Write("shop/shop.pcp", """
            project Shop
                source Program.pc
                source Missing.pc
            end project
            """);

        string program = tree.Write("shop/Program.pc", AProgram);

        Assert.That(Asked(program).Project, Is.Not.Null);
    }

    /// <summary>A project names itself, and there is nothing above it to look for.</summary>
    [Test]
    public void AProjectAnswersWithItself()
    {
        using Tree tree = new();

        string project = tree.Write("shop/shop.pcp", """
            project Shop
                source Program.pc
            end project
            """);

        tree.Write("shop/Program.pc", AProgram);

        Answer answer = Asked(project);

        Assert.Multiple(() =>
        {
            Assert.That(answer.Project, Is.EqualTo(project));
            Assert.That(answer.Searched, Is.Zero, "nothing had to be read to know that");
        });
    }

    /// <summary>
    /// The nearest project that claims the file wins, not the first one found on the way up. A
    /// project above one that already builds the file is a different build, and running it would
    /// compile more than was asked for.
    /// </summary>
    [Test]
    public void TheNearestProjectThatClaimsItWins()
    {
        using Tree tree = new();

        tree.Write("outer/outer.pcp", """
            project Outer
                reference inner/inner.pcp
            end project
            """);

        tree.Write("outer/inner/inner.pcp", """
            project Inner
                source Program.pc
            end project
            """);

        string program = tree.Write("outer/inner/Program.pc", AProgram);

        Assert.That(
            Asked(program).Project,
            Is.EqualTo(Path.Combine(tree.Root, "outer", "inner", "inner.pcp")));
    }

    /// <summary>
    /// <para>Projects referencing each other in a circle are not followed forever.</para>
    /// <para>The compiler reports the circle when it is asked to build one. This is only asked
    /// which project claims a file, and has to answer rather than hang.</para>
    /// </summary>
    [Test]
    public void ACircleOfReferencesIsNotFollowedForever()
    {
        using Tree tree = new();

        tree.Write("one/one.pcp", """
            project One
                reference ../two/two.pcp
                source A.pc
            end project
            """);

        tree.Write("one/A.pc", AProgram);

        tree.Write("two/two.pcp", """
            project Two
                reference ../one/one.pcp
                source B.pc
            end project
            """);

        tree.Write("two/B.pc", "model B\nend model");

        string apart = tree.Write("elsewhere/Odd.pc", AProgram);

        Assert.That(Asked(apart).Project, Is.Null);
    }
}
