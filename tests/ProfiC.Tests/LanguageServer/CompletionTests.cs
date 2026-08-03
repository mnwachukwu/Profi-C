using System.Text.Json.Nodes;
using ProfiC.Cli;
using ProfiC.Cli.LanguageServer;
using ProfiC.Compiler.Text;

namespace ProfiC.Tests.LanguageServer;

/// <summary>
/// <para>What is offered after a dot.</para>
/// <para><b>The first question that cannot be asked of the program as written.</b> Somebody who
/// has typed <c>word.</c> has written something that is not Profi-C: there is no member yet, so
/// there is no member access, so there is nothing in the tree to ask about. What makes it
/// answerable is putting a name where the member will go and compiling <em>that</em> — so the
/// compiler answers about a real program, and nothing here reasons about half-written
/// syntax.</para>
/// <para>What these hold is that the right receiver is found through that trick, that both kinds
/// of member arrive, and — the half that is easy to get wrong — that the list is empty of things
/// the next keystroke would refuse.</para>
/// </summary>
[TestFixture]
public sealed class CompletionTests
{
    /// <summary>A program with somewhere to ask from, written around a marker.</summary>
    private sealed class Workspace : IDisposable
    {
        public Workspace(string body, string? alongside = null)
        {
            Folder = Path.Combine(Path.GetTempPath(), $"profi-c-completion-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Folder);

            File.WriteAllText(Path.Combine(Folder, "Program.pc"), body);

            if (alongside is not null)
            {
                File.WriteAllText(Path.Combine(Folder, "Beside.pc"), alongside);
            }
        }

        public string Folder { get; }

        /// <summary>The program the cursor is in.</summary>
        public string At => Path.Combine(Folder, "Program.pc");

        public void Dispose() => Directory.Delete(Folder, recursive: true);
    }

    /// <summary>
    /// What is offered where the marker sits. The marker is taken out before compiling, so a
    /// test says where the cursor is by writing it in the program rather than by counting
    /// characters — which no reader could check and no edit would survive.
    /// </summary>
    private static JsonArray? OfferedAt(string body, string? alongside = null)
    {
        const string Caret = "$";

        int offset = body.IndexOf(Caret, StringComparison.Ordinal);

        Assert.That(offset, Is.GreaterThanOrEqualTo(0), "the fixture has no cursor in it");

        string text = body.Remove(offset, Caret.Length);

        using Workspace workspace = new(text, alongside);

        return Completion.After(
            workspace.At,
            new SourceText(text, workspace.At),
            offset,
            SourceDiscovery.FromDisk);
    }

    /// <summary>What is offered for a bare name where the marker sits, the same way.</summary>
    private static JsonArray? BareAt(string body, string? alongside = null)
    {
        const string Caret = "$";

        int offset = body.IndexOf(Caret, StringComparison.Ordinal);

        Assert.That(offset, Is.GreaterThanOrEqualTo(0), "the fixture has no cursor in it");

        string text = body.Remove(offset, Caret.Length);

        using Workspace workspace = new(text, alongside);

        return Completion.Bare(
            workspace.At,
            new SourceText(text, workspace.At),
            offset,
            SourceDiscovery.FromDisk);
    }

    private static string[] Labels(JsonArray? offered) =>
        [.. (offered ?? []).Select(item => (string?)item!["label"] ?? string.Empty)];

    // ---- The members the language provides ---------------------------------------------------

    [Test]
    public void AStringOffersWhatAStringAnswers()
    {
        string[] offered = Labels(OfferedAt(
            """
            shared model Program
                function Main()
                    string word = "hello";
                    Console.WriteLine(word.$);
                end function
            end model
            """));

        Assert.Multiple(() =>
        {
            Assert.That(offered, Does.Contain("Count"));
            Assert.That(offered, Does.Contain("ToUpper"));
            Assert.That(offered, Does.Contain("Capitalize"));
            Assert.That(offered, Does.Contain("ToString"), "and what every value answers");
            Assert.That(offered, Does.Not.Contain("AddDays"), "which is a moment's, not a string's");
        });
    }

    /// <summary>
    /// A set's members depend on what it holds, so this is not a fixed list to look up: the
    /// receiver's element type decides it, and a set of optionals answers four more.
    /// </summary>
    [Test]
    public void ASetOfOptionalsOffersTheOnesOnlyItHas()
    {
        string[] offered = Labels(OfferedAt(
            """
            shared model Program
                function Main()
                    integer?[] readings = {};
                    Console.WriteLine(readings.$);
                end function
            end model
            """));

        Assert.Multiple(() =>
        {
            Assert.That(offered, Does.Contain("TrimAll"));
            Assert.That(offered, Does.Contain("Count"));
        });
    }

    /// <summary>Asking again after typing a letter is the same question about the same receiver.</summary>
    [Test]
    public void TypingPartOfTheNameStillOffersTheSameMembers()
    {
        string[] offered = Labels(OfferedAt(
            """
            shared model Program
                function Main()
                    string word = "hello";
                    Console.WriteLine(word.Cou$);
                end function
            end model
            """));

        Assert.That(offered, Does.Contain("Count"));
    }

    // ---- The members a program declared ------------------------------------------------------

    /// <summary>
    /// <para>A model offers what it declares and what it inherited.</para>
    /// <para>A reader calling a member does not care which model in the chain wrote it, so a list
    /// that stopped at the type in hand would leave out most of what an inheriting model can
    /// do.</para>
    /// </summary>
    [Test]
    public void AModelOffersItsOwnMembersAndTheOnesItInherited()
    {
        string[] offered = Labels(OfferedAt(
            """
            shared model Program
                function Main()
                    Circle here = new Circle(2.0);
                    Console.WriteLine(here.$);
                end function
            end model
            """,
            """
            model Shape
                public string function Named()
                    yield "shape";
                end function
            end model

            model Circle extends Shape
                public real Radius;

                public function Circle(real across)
                    this.Radius = across;
                end function

                public real function Area()
                    yield 3.14 * this.Radius * this.Radius;
                end function
            end model
            """));

        Assert.Multiple(() =>
        {
            Assert.That(offered, Does.Contain("Area"), "its own");
            Assert.That(offered, Does.Contain("Radius"), "and its fields");
            Assert.That(offered, Does.Contain("Named"), "and what it inherited");
            Assert.That(offered, Does.Contain("ToString"), "and what every value answers");
        });
    }

    /// <summary>
    /// <para>What a caller could not write is not offered.</para>
    /// <para>A list that suggests a private field is a list that suggests a line the next
    /// keystroke refuses, which is worse than a shorter one — the reader takes the suggestion and
    /// is then told it was wrong.</para>
    /// </summary>
    [Test]
    public void APrivateMemberIsNotOffered()
    {
        string[] offered = Labels(OfferedAt(
            """
            shared model Program
                function Main()
                    Counter counter = new Counter();
                    Console.WriteLine(counter.$);
                end function
            end model
            """,
            """
            model Counter
                integer hidden;

                public function Counter()
                    this.hidden = 0;
                end function

                public integer function Total()
                    yield this.hidden;
                end function
            end model
            """));

        Assert.Multiple(() =>
        {
            Assert.That(offered, Does.Contain("Total"));
            Assert.That(offered, Does.Not.Contain("hidden"), "private, and reachable from nowhere here");
        });
    }

    /// <summary>A name reached through a model's name offers what that name holds.</summary>
    [Test]
    public void AModelsNameOffersWhatIsReachedThroughIt()
    {
        string[] offered = Labels(OfferedAt(
            """
            shared model Program
                function Main()
                    Console.WriteLine(Math.$);
                end function
            end model
            """));

        Assert.Multiple(() =>
        {
            Assert.That(offered, Does.Contain("Sqrt"));
            Assert.That(offered, Does.Contain("Pi"));
        });
    }

    // ---- Where the question does not apply ---------------------------------------------------

    /// <summary>
    /// <para>Nothing at all where the cursor does not follow a dot.</para>
    /// <para>Null rather than an empty list, and the difference matters to a reader: an editor
    /// shows an empty list as "no suggestions" and shows nothing for the other. Bare names are
    /// not offered yet, so saying "none" would be a claim rather than a silence.</para>
    /// </summary>
    [Test]
    public void NothingIsOfferedWhereThereIsNoDot() =>
        Assert.That(
            OfferedAt(
                """
                shared model Program
                    function Main()
                        integer counted = 1$;
                    end function
                end model
                """),
            Is.Null);

    /// <summary>
    /// <para>A decimal point is not a member access.</para>
    /// <para><c>1.5</c> has a dot in it with a digit on either side. Skipping back over a run of
    /// name characters and finding a dot would call that a member access, so the run has to be a
    /// name — which one beginning with a digit is not.</para>
    /// </summary>
    [Test]
    public void ADecimalPointOffersNothing() =>
        Assert.That(
            OfferedAt(
                """
                shared model Program
                    function Main()
                        real measured = 1.5$;
                    end function
                end model
                """),
            Is.Null);

    /// <summary>
    /// A receiver with no type to speak of — a name nothing declares — has no members to offer,
    /// and says so rather than offering everything.
    /// </summary>
    [Test]
    public void AReceiverThatNamesNothingOffersNothing() =>
        Assert.That(
            OfferedAt(
                """
                shared model Program
                    function Main()
                        Console.WriteLine(nothingHere.$);
                    end function
                end model
                """),
            Is.Null);

    // ---- A name with nothing in front of it --------------------------------------------------

    /// <summary>
    /// <para>The locals and parameters in force, and nothing from a scope that has closed.</para>
    /// <para>The claim that matters is the second one. Offering every local in the function would
    /// be easy and would suggest names that are out of reach — <c>hidden</c> below belongs to a
    /// block that ended, and writing it is an error the compiler reports.</para>
    /// </summary>
    [Test]
    public void ALocalInForceIsOfferedAndOneOutOfScopeIsNot()
    {
        string[] offered = Labels(BareAt(
            """
            shared model Program
                function Main(string greeting)
                    integer counted = 1;

                    loop each item in {1, 2}
                        integer doubled = item + item;
                        Console.WriteLine(doubled);
                    end loop

                    Console.WriteLine($);
                end function
            end model
            """));

        Assert.Multiple(() =>
        {
            Assert.That(offered, Does.Contain("counted"));
            Assert.That(offered, Does.Contain("greeting"), "a parameter is a name like any other");
            Assert.That(offered, Does.Not.Contain("doubled"), "that block has closed");
            Assert.That(offered, Does.Not.Contain("item"), "and so has the loop");
        });
    }

    /// <summary>A loop's variable is in force inside the loop, which is the only place it is.</summary>
    [Test]
    public void ALoopVariableIsOfferedInsideTheLoop()
    {
        string[] offered = Labels(BareAt(
            """
            shared model Program
                function Main()
                    loop each item in {1, 2}
                        Console.WriteLine($);
                    end loop
                end function
            end model
            """));

        Assert.That(offered, Does.Contain("item"));
    }

    /// <summary>
    /// <para>A local declared below the cursor is not offered; a local function is.</para>
    /// <para>Two rules that look like one. A local does not exist until its declaration runs, so
    /// naming one above it is an error. A local <em>function</em> may be called before it is
    /// declared, which is deliberate — so leaving it out would hide a name that works.</para>
    /// </summary>
    [Test]
    public void WhatIsDeclaredBelowIsOfferedOnlyIfItIsAFunction()
    {
        string[] offered = Labels(BareAt(
            """
            shared model Program
                function Main()
                    Console.WriteLine($);

                    integer later = 2;

                    integer function Twice(integer value)
                        yield value + value;
                    end function
                end function
            end model
            """));

        Assert.Multiple(() =>
        {
            Assert.That(offered, Does.Not.Contain("later"));
            Assert.That(offered, Does.Contain("Twice"));
        });
    }

    /// <summary>
    /// <para>A type is offered, because a bare name is how a shared member is reached.</para>
    /// <para><c>Math.Abs(x)</c> begins with a type name, so a list without type names would fail
    /// the reader at the first character of the most common thing they write. Both halves are
    /// here: what the language provides, and what the program declared — in this case next door,
    /// since a program is a compilation.</para>
    /// </summary>
    [Test]
    public void ATypeIsOfferedWhereverABareNameCanGo()
    {
        string[] offered = Labels(
            BareAt(
                """
                shared model Program
                    function Main()
                        Console.WriteLine($);
                    end function
                end model
                """,
                alongside: """
                shared model Beside
                    public shared integer function Twice(integer value)
                        yield value + value;
                    end function
                end model
                """));

        Assert.Multiple(() =>
        {
            Assert.That(offered, Does.Contain("Math"));
            Assert.That(offered, Does.Contain("Console"));
            Assert.That(offered, Does.Contain("Beside"), "declared in the file next door");
            Assert.That(offered, Does.Contain("Program"), "and the one being written in");
        });
    }

    /// <summary>
    /// <para><c>this</c> where it means something, and not where it does not.</para>
    /// <para>Worth offering in this language more than in most: every field is reached through
    /// <c>this.</c>, so it is the first thing typed on a great many lines.</para>
    /// </summary>
    [Test]
    public void ThisIsOfferedInsideAnInstanceMemberOnly()
    {
        string[] inside = Labels(BareAt(
            """
            model Counter
                integer count;

                public function Bump()
                    Console.WriteLine($);
                end function
            end model
            """));

        string[] shared = Labels(BareAt(
            """
            shared model Program
                function Main()
                    Console.WriteLine($);
                end function
            end model
            """));

        Assert.Multiple(() =>
        {
            Assert.That(inside, Does.Contain("this"));
            Assert.That(shared, Does.Not.Contain("this"), "there is no instance to speak of");
            Assert.That(inside, Does.Not.Contain("base"), "and nothing to inherit from");
        });
    }

    /// <summary>
    /// A name inside a lambda sees both the lambda's parameters and the locals around it, which
    /// is what capture means and what a reader writing one expects to be offered.
    /// </summary>
    [Test]
    public void ALambdaSeesItsOwnParametersAndWhatSurroundsThem()
    {
        string[] offered = Labels(BareAt(
            """
            shared model Program
                function Main()
                    integer factor = 3;
                    integer delegate(integer) scale = (value) yield $value * factor;
                    Console.WriteLine(scale(2));
                end function
            end model
            """));

        Assert.Multiple(() =>
        {
            Assert.That(offered, Does.Contain("value"));
            Assert.That(offered, Does.Contain("factor"));
        });
    }

    /// <summary>
    /// <para>A line that does not parse still knows what is in scope.</para>
    /// <para>The case this has to survive, since it is the state a file is in while somebody
    /// types. A scope is a stretch of the file rather than a piece of syntax, so the names in
    /// force do not depend on the line being finished — or on it being Profi-C at all.</para>
    /// </summary>
    [Test]
    public void AHalfWrittenLineStillKnowsWhatIsInScope()
    {
        string[] offered = Labels(BareAt(
            """
            shared model Program
                function Main()
                    integer counted = 1;
                    integer doubled = coun$
                end function
            end model
            """));

        Assert.That(offered, Does.Contain("counted"));
    }

    /// <summary>Where a member is being written, this half says nothing — the other half answers.</summary>
    [Test]
    public void NothingIsOfferedForABareNameAfterADot() =>
        Assert.That(
            BareAt(
                """
                shared model Program
                    function Main()
                        string word = "hello";
                        Console.WriteLine(word.$);
                    end function
                end model
                """),
            Is.Null);

    /// <summary>
    /// Between declarations there is nowhere to write a name, and nothing is offered rather than
    /// everything the file could reach.
    /// </summary>
    [Test]
    public void NothingIsOfferedOutsideEveryBody() =>
        Assert.That(
            BareAt(
                """
                shared model Program
                    function Main()
                        Console.WriteLine(1);
                    end function
                end model
                $
                """),
            Is.Null);
}
