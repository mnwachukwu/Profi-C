using ProfiC.Compiler;
using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Text;

namespace ProfiC.Tests.Semantics;

/// <summary>
/// <para>A type declared inside another: how it is named, and who may name it.</para>
/// <para>All three answers used to be the opposite of what C# gives, which is what makes them
/// worth pinning rather than trusting. A dotted name did not resolve at all; a bare one resolved
/// from anywhere in the program, because nested types were held in one flat map keyed by their
/// short name; and the visibility written on one was not enforced. Nesting therefore said
/// nothing about where a type belonged — it was a way of writing a top-level type indented.
/// </para>
/// <para>The flat map also meant two containers could not each hold a <c>Node</c>. That one is
/// covered here and in <c>samples/nesting.pc</c>, since it is the case a reader is most likely
/// to write and the one a lookup by short name silently gets wrong.</para>
/// </summary>
[TestFixture]
public sealed class NestedTypeTests
{
    private static string[] IdsIn(string program)
    {
        DiagnosticBag diagnostics = new();

        CompilationUnit unit = Parser.Parse(new SourceText(program, "<test>"), diagnostics);
        FrontEnd.Check(unit, diagnostics, reportUnusedSuppressions: false);

        return [.. diagnostics.Sorted().Select(d => d.Id)];
    }

    /// <summary>A container with one public nested model, and a body that tries to reach it.</summary>
    private static string Holding(string body, string visibility = "public") => $$"""
        model Tree
            {{visibility}} model Node
                public integer value;

                public function Node(integer held)
                    this.value = held;
                end function
            end model

            public shared integer function Own()
                yield new Node(1).value;
            end function
        end model

        shared model Program
            function Main()
        {{body}}
            end function
        end model
        """;

    // ---- How it is named -------------------------------------------------------------------

    [Test]
    public void TheContainerIsWrittenFirst() => Assert.That(
        IdsIn(Holding("        Console.WriteLine(new Tree.Node(1).value);")),
        Is.Empty);

    /// <summary>Inside the container the bare name is enough, which is what nesting is for.</summary>
    [Test]
    public void ABareNameReachesItFromInsideTheContainer() =>
        Assert.That(IdsIn(Holding("        Console.WriteLine(Tree.Own());")), Is.Empty);

    /// <summary>
    /// And from nowhere else. The name belongs to the container, so a program that never says
    /// which container it means has not named anything.
    /// </summary>
    [Test]
    public void ABareNameDoesNotReachItFromOutside() => Assert.That(
        IdsIn(Holding("        Console.WriteLine(new Node(1).value);")),
        Does.Contain("PC0201"));

    /// <summary>
    /// Two containers, each with a Node. Neither name gives way — which a lookup keyed by the
    /// short name cannot do, since one of them would have to overwrite the other.
    /// </summary>
    [Test]
    public void TwoContainersMayEachHoldTheSameName() => Assert.That(
        IdsIn("""
            model Tree
                public model Node
                    public integer value;

                    public function Node(integer held)
                        this.value = held;
                    end function
                end model
            end model

            model Ledger
                public model Node
                    public string label;

                    public function Node(string named)
                        this.label = named;
                    end function
                end model
            end model

            shared model Program
                function Main()
                    Console.WriteLine(new Tree.Node(1).value);
                    Console.WriteLine(new Ledger.Node("first").label);
                end function
            end model
            """),
        Is.Empty);

    // ---- However deep it goes --------------------------------------------------------------

    /// <summary>
    /// <para>Three deep, named from each of the four places that can name it.</para>
    /// <para>The rule does not change with depth, and that is the claim: a name is read outward
    /// from where it is written, so each vantage point writes the part of the path that is not
    /// already around it. Worth holding at three rather than two, since two cannot tell a walk
    /// outward apart from a single step to the container.</para>
    /// </summary>
    [TestCase("        Console.WriteLine(new Outer.Middle.Inner(1).value);", TestName = "from outside Outer")]
    [TestCase("        Console.WriteLine(Outer.FromOuter(1).value);", TestName = "from inside Outer")]
    [TestCase("        Console.WriteLine(Outer.Middle.FromMiddle(1).value);", TestName = "from inside Middle")]
    [TestCase("        Console.WriteLine(Outer.Middle.Inner.Beside(1).value);", TestName = "from inside Inner")]
    public void ThreeDeepIsNamedFromWhereverItIsWritten(string body) => Assert.That(
        IdsIn($$"""
            shared model Outer
                public model Middle
                    public model Inner
                        public integer value;

                        public function Inner(integer held)
                            this.value = held;
                        end function

                        public shared Inner function Beside(integer held)
                            yield new Inner(held);
                        end function
                    end model

                    public shared Inner function FromMiddle(integer held)
                        yield new Inner(held);
                    end function
                end model

                public Middle.Inner function FromOuter(integer held)
                    yield new Middle.Inner(held);
                end function
            end model

            shared model Program
                function Main()
            {{body}}
                end function
            end model
            """),
        Is.Empty);

    /// <summary>
    /// The innermost name still does not escape. Three levels of container is three levels the
    /// name is kept inside, not a depth at which it starts leaking.
    /// </summary>
    [Test]
    public void ThreeDeepStillDoesNotReachOutward() => Assert.That(
        IdsIn("""
            shared model Outer
                public model Middle
                    public model Inner
                        public integer value;

                        public function Inner(integer held)
                            this.value = held;
                        end function
                    end model
                end model
            end model

            shared model Program
                function Main()
                    Console.WriteLine(new Inner(1).value);
                end function
            end model
            """),
        Does.Contain("PC0201"));

    // ---- Who may name it -------------------------------------------------------------------

    /// <summary>
    /// A nested type is a member, so saying nothing means the container alone — the default
    /// every other member has, and the one C# gives a nested class.
    /// </summary>
    [Test]
    public void SayingNothingKeepsItToTheContainer() => Assert.That(
        IdsIn(Holding("        Console.WriteLine(new Tree.Node(1).value);", visibility: "")),
        Does.Contain("PC0339"));

    /// <summary>Its own container reaches it regardless, which is the point of keeping it there.</summary>
    [Test]
    public void TheContainerReachesItsOwnRegardless() => Assert.That(
        IdsIn(Holding("        Console.WriteLine(Tree.Own());", visibility: "")),
        Is.Empty);
}
