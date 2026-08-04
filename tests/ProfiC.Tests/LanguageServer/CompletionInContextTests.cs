using System.Text.Json.Nodes;
using ProfiC.Cli;
using ProfiC.Cli.LanguageServer;
using ProfiC.Compiler.Text;

namespace ProfiC.Tests.LanguageServer;

/// <summary>
/// <para>What is offered where the place says what it will take.</para>
/// <para>A list of every name in scope is the answer to a question nobody asked. Somebody typing
/// after <c>Animal frank = new </c> is choosing among the handful of things that could stand
/// there, and the compiler knows which those are because it is about to check exactly that.</para>
/// <para>These hold two different promises, and the difference between them is the point.
/// <b>Ordering is a suggestion</b>: what fits comes first and everything else is still on the
/// list, so being wrong about the position costs a reader nothing but a scroll. <b>After
/// <c>new</c> it is a rule</b>, because the language will not read anything but a constructible
/// type there, and so there is nothing to be wrong about.</para>
/// <para>Written the way a file being typed into looks — a line with no semicolon on it yet, a
/// <c>new</c> with nothing after it, members with no visibility written because most members have
/// none. A fixture that closes every bracket is a fixture that never needed the work.</para>
/// </summary>
[TestFixture]
public sealed class CompletionInContextTests
{
    private sealed class Workspace : IDisposable
    {
        public Workspace(string body)
        {
            Folder = Path.Combine(Path.GetTempPath(), $"profi-c-in-context-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Folder);

            File.WriteAllText(Path.Combine(Folder, "Program.pc"), body);
        }

        public string Folder { get; }

        public string At => Path.Combine(Folder, "Program.pc");

        public void Dispose() => Directory.Delete(Folder, recursive: true);
    }

    /// <summary>What is offered where the marker sits, with the marker taken out first.</summary>
    private static JsonArray? OfferedAt(string body)
    {
        const string Caret = "$";

        int offset = body.IndexOf(Caret, StringComparison.Ordinal);

        Assert.That(offset, Is.GreaterThanOrEqualTo(0), "the fixture has no cursor in it");

        string text = body.Remove(offset, Caret.Length);

        using Workspace workspace = new(text);

        return Completion.Bare(
            workspace.At, new SourceText(text, workspace.At), offset, SourceDiscovery.FromDisk);
    }

    private static string[] Labels(JsonArray? offered) =>
        [.. (offered ?? []).Select(item => (string?)item!["label"] ?? string.Empty)];

    /// <summary>
    /// The labels in the order an editor would put them: by the sort key where one was written,
    /// and by the label itself where none was — which is the rule the protocol lays down.
    /// </summary>
    private static string[] Ordered(JsonArray? offered) =>
        [.. (offered ?? [])
            .Select(item => (
                Key: (string?)item!["sortText"] ?? (string?)item["label"] ?? string.Empty,
                Label: (string?)item["label"] ?? string.Empty))
            .OrderBy(row => row.Key, StringComparer.Ordinal)
            .Select(row => row.Label)];

    /// <summary>
    /// <para>Asserts that one name is offered above another because it fits, and that the other is
    /// still offered.</para>
    /// <para><b>The sort keys are checked and not only the order.</b> Two names in a list have an
    /// order whether or not anything decided it, so an assertion about position alone passes on
    /// the alphabet — which is how the first draft of three of these passed against an
    /// implementation that was sorting nothing at all. Every caller below also names the fitting
    /// one so that it sorts <em>last</em> alphabetically, which makes the order a second, cheaper
    /// check on the same thing.</para>
    /// </summary>
    private static void Above(JsonArray? offered, string first, string second)
    {
        string[] order = Ordered(offered);

        Assert.Multiple(() =>
        {
            Assert.That(Sorting(offered, first), Is.EqualTo("0" + first), $"'{first}' fits here");
            Assert.That(
                Sorting(offered, second),
                Is.EqualTo("1" + second),
                $"'{second}' does not fit here, and is offered anyway");
            Assert.That(
                Array.IndexOf(order, first),
                Is.LessThan(Array.IndexOf(order, second)),
                "so an editor reading the keys puts them that way round");
        });
    }

    /// <summary>The sort key written for a name, or null where the name was not offered.</summary>
    private static string? Sorting(JsonArray? offered, string label) =>
        (offered ?? [])
            .Where(item => (string?)item!["label"] == label)
            .Select(item => (string?)item!["sortText"])
            .FirstOrDefault();

    // ---- After new: the one place the list is narrowed ----------------------------------------

    /// <summary>
    /// <para>Only a type that can be constructed follows <c>new</c>.</para>
    /// <para>A <c>new</c> with nothing after it is not merely a line that fails to parse: the
    /// parser reads the next word as the type name whatever it is, so this one swallows the
    /// <c>end function</c> below it and the rest of the file with it. Everything asserted here
    /// depends on that being repaired first.</para>
    /// </summary>
    [Test]
    public void OnlyAConstructibleTypeFollowsNew()
    {
        string[] offered = Labels(OfferedAt(
            """
            model Animal
            end model

            abstract model Shape
            end model

            structure Point
                integer X;
            end structure

            shared model Tools
            end model

            shared model Program
                function Main()
                    Animal frank = new $
                end function
            end model
            """));

        Assert.Multiple(() =>
        {
            Assert.That(offered, Does.Contain("Animal"));
            Assert.That(offered, Does.Contain("Point"), "a structure is constructed the same way");

            Assert.That(offered, Does.Not.Contain("Shape"), "abstract: something is left unwritten");
            Assert.That(offered, Does.Not.Contain("Tools"), "shared: there are no instances of it");
            Assert.That(offered, Does.Not.Contain("Program"), "likewise, and it is the program");
            Assert.That(offered, Does.Not.Contain("Math"), "a name to reach members through");
            Assert.That(offered, Does.Not.Contain("Console"));
            Assert.That(offered, Does.Not.Contain("frank"), "a local is not a type");
            Assert.That(offered, Does.Not.Contain("Main"));
            Assert.That(offered, Does.Not.Contain("this"));
        });
    }

    /// <summary>The type being declared is what the <c>new</c> is for, so it is offered first.</summary>
    [Test]
    public void TheDeclaredTypeIsOfferedFirstAfterNew()
    {
        JsonArray? offered = OfferedAt(
            """
            model Animal
            end model

            model Vehicle
            end model

            shared model Program
                function Main()
                    Animal frank = new $
                end function
            end model
            """);

        Assert.That(Ordered(offered).First(), Is.EqualTo("Animal"));
        Assert.That(Labels(offered), Does.Contain("Vehicle"), "the rest of the list is still there");
    }

    /// <summary>
    /// The same, with some of the name already typed — which is the state the editor asks about on
    /// every keystroke after the first.
    /// </summary>
    [Test]
    public void APartlyTypedNameStillFindsWhatIsBeingConstructed()
    {
        JsonArray? offered = OfferedAt(
            """
            model Animal
            end model

            shared model Program
                function Main()
                    Animal frank = new An$
                end function
            end model
            """);

        Assert.That(Ordered(offered).First(), Is.EqualTo("Animal"));
    }

    /// <summary>
    /// <para>A model reached through the type it descends from fits where that type is
    /// wanted.</para>
    /// <para>The whole reason this ranks rather than filters: assignability is the question, and
    /// answering it with the declared type alone would push the answer somebody actually wanted
    /// down among the exceptions.</para>
    /// </summary>
    [Test]
    public void ADescendantFitsWhereItsParentIsWanted()
    {
        JsonArray? offered = OfferedAt(
            """
            abstract model Shape
            end model

            model Circle extends Shape
            end model

            model Basket
            end model

            shared model Program
                function Main()
                    Shape drawn = new $
                end function
            end model
            """);

        Above(offered, "Circle", "Basket");
    }

    /// <summary>
    /// <para>The word inside a string is not the keyword, and the list is not narrowed there.
    /// </para>
    /// <para>Which the text alone cannot tell: read backwards from the cursor, a string holding
    /// the word looks exactly like the keyword followed by a space. The tree can, and this is what
    /// asking it is for — a reader who has a string open is not constructing anything, and being
    /// shown eleven models and none of their own names would be baffling.</para>
    /// </summary>
    [Test]
    public void TheWordInsideAStringIsNotTheKeyword()
    {
        string[] offered = Labels(OfferedAt(
            """
            model Animal
            end model

            shared model Program
                function Main()
                    Animal frank = new Animal();

                    Console.WriteLine("a new $");
                end function
            end model
            """));

        Assert.Multiple(() =>
        {
            Assert.That(offered, Does.Contain("frank"), "the ordinary list, unnarrowed");
            Assert.That(offered, Does.Contain("Console"));
        });
    }

    // ---- Everywhere else: ordering only -------------------------------------------------------

    /// <summary>What a local is declared to hold is what its initializer has to produce.</summary>
    [Test]
    public void AnInitializerOffersWhatItsTypeAccepts()
    {
        Above(
            OfferedAt(
                """
                shared model Program
                    function Main()
                        string alias = "counted";
                        boolean valid = true;
                        boolean ready = $
                    end function
                end model
                """),
            "valid",
            "alias");
    }

    /// <summary>The same for an assignment, where the type comes from what is being written to.</summary>
    [Test]
    public void AnAssignmentOffersWhatItsTargetAccepts()
    {
        Above(
            OfferedAt(
                """
                shared model Program
                    function Main()
                        string alias = "counted";
                        boolean valid = true;
                        boolean ready = false;

                        ready = $
                    end function
                end model
                """),
            "valid",
            "alias");
    }

    /// <summary>
    /// <para>A condition takes a boolean and nothing else, so booleans come first.</para>
    /// <para>Nothing has been typed after the word, so the expression the parser stood in for
    /// begins at the <c>end if</c> below rather than at the cursor. Which is why the position is
    /// claimed by where the cursor sits among the statement's parts rather than by which part
    /// contains it.</para>
    /// </summary>
    [Test]
    public void AConditionOffersBooleansFirst()
    {
        Above(
            OfferedAt(
                """
                shared model Program
                    function Main()
                        string alias = "counted";
                        boolean valid = true;

                        if $
                        end if
                    end function
                end model
                """),
            "valid",
            "alias");
    }

    /// <summary>The same, for the loop that tests before its body.</summary>
    [Test]
    public void AWhileLoopOffersBooleansFirst()
    {
        Above(
            OfferedAt(
                """
                shared model Program
                    function Main()
                        string alias = "counted";
                        boolean valid = true;

                        loop while $
                        end loop
                    end function
                end model
                """),
            "valid",
            "alias");
    }

    /// <summary>What a function takes at the argument being written.</summary>
    [Test]
    public void AnArgumentOffersWhatTheParameterTakes()
    {
        Above(
            OfferedAt(
                """
                shared model Program
                    function Main()
                        integer count = 3;
                        string word = "counted";

                        Program.Show($
                    end function

                    function Show(string text)
                        Console.WriteLine(text);
                    end function
                end model
                """),
            "word",
            "count");
    }

    /// <summary>What the function around a <c>yield</c> promised to produce.</summary>
    [Test]
    public void AYieldOffersWhatTheFunctionPromised()
    {
        Above(
            OfferedAt(
                """
                shared model Program
                    string function Named()
                        integer count = 3;
                        string word = "counted";

                        yield $
                    end function
                end model
                """),
            "word",
            "count");
    }

    /// <summary>
    /// <para>Inside a lambda, what the lambda yields — not what the call it was handed to takes.
    /// </para>
    /// <para>Both are known and they are different types, so this is the case where getting it
    /// wrong is worse than saying nothing: the walk outward reaches the call first, and a call
    /// wanting a <c>boolean delegate(string)</c> at that argument would put every function-valued
    /// name in the file above the boolean that actually belongs there.</para>
    /// </summary>
    [Test]
    public void InsideALambdaItIsWhatTheLambdaYields()
    {
        Above(
            OfferedAt(
                """
                shared model Program
                    function Main()
                        integer count = 3;
                        boolean valid = true;

                        Program.Keep((word) yield $);
                    end function

                    function Keep(boolean delegate(string) test)
                        Console.WriteLine(test("counted"));
                    end function
                end model
                """),
            "valid",
            "count");
    }

    // ---- After a dot, which is the list that is long ------------------------------------------

    /// <summary>What is offered after a dot where the marker sits.</summary>
    private static JsonArray? MembersAt(string body)
    {
        const string Caret = "$";

        int offset = body.IndexOf(Caret, StringComparison.Ordinal);

        Assert.That(offset, Is.GreaterThanOrEqualTo(0), "the fixture has no cursor in it");

        string text = body.Remove(offset, Caret.Length);

        using Workspace workspace = new(text);

        return Completion.After(
            workspace.At, new SourceText(text, workspace.At), offset, SourceDiscovery.FromDisk);
    }

    /// <summary>
    /// <para>A condition puts the members that answer yes or no at the top.</para>
    /// <para>What the whole thing was for. A string answers more than thirty members and four of
    /// them are any use inside an <c>if</c>, so this is where ordering stops being a nicety.
    /// </para>
    /// </summary>
    [Test]
    public void AConditionOffersTheMembersThatAnswerYesOrNo()
    {
        Above(
            MembersAt(
                """
                shared model Program
                    function Main()
                        string word = "counted";

                        if word.$
                        end if
                    end function
                end model
                """),
            "Contains",
            "Count");
    }

    /// <summary>The same machinery, reading the type off a declaration instead.</summary>
    [Test]
    public void AnInitializerOffersTheMembersThatProduceItsType()
    {
        Above(
            MembersAt(
                """
                shared model Program
                    function Main()
                        string word = "counted";
                        string part = word.$
                    end function
                end model
                """),
            "Substring",
            "Count");
    }

    /// <summary>
    /// A member list with nothing to sort it by is left alone, the same as a bare one.
    /// </summary>
    [Test]
    public void AMemberListWithNoExpectationIsNotSorted()
    {
        JsonArray? offered = MembersAt(
            """
            shared model Program
                function Main()
                    string word = "counted";
                    word.$
                end function
            end model
            """);

        Assert.That(Labels(offered), Does.Contain("Count"));
        Assert.That(
            (offered ?? []).Where(item => item!["sortText"] is not null),
            Is.Empty);
    }

    // ---- Where nothing is known ---------------------------------------------------------------

    /// <summary>
    /// <para>A place with no expectation is left in whatever order the editor would have chosen.
    /// </para>
    /// <para>Writing a sort key that says nothing is not the same as writing none: the first
    /// commits the list to an order it has no reason for, and the reader cannot tell it apart from
    /// one that does.</para>
    /// </summary>
    [Test]
    public void AStatementOnItsOwnIsNotSorted()
    {
        JsonArray? offered = OfferedAt(
            """
            shared model Program
                function Main()
                    Console.WriteLine("counted");
                    $
                end function
            end model
            """);

        Assert.That(Labels(offered), Is.Not.Empty);
        Assert.That(
            (offered ?? []).Where(item => item!["sortText"] is not null),
            Is.Empty,
            "nothing here says what belongs in it, so nothing should claim to");
    }
}
