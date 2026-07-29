using ProfiC.Runtime;

namespace ProfiC.Tests.Runtime;

/// <summary>
/// <para>Structural equality, including the cases the design exists to survive.</para>
/// <para>No depth limit is used, since none can tell a cyclic graph from a legitimately deep
/// one. These hold that line from both sides: a two-node cycle must terminate, and a
/// hundred-thousand-node chain must not be mistaken for one.</para>
/// </summary>
[TestFixture]
public sealed class DeepEqualityTests
{
    /// <summary>A model with a value and a link, standing in for what the compiler emits.</summary>
    private sealed class Node(int value) : IProfiCModel
    {
        public int Value { get; set; } = value;

        public Node? Next { get; set; }

        // Emitted code gives each Profi-C type a .NET type of its own, so it answers with that.
        public object DeepTypeIdentity => typeof(Node);

        public int DeepMemberCount => 2;

        public object? GetDeepMember(int index) => index switch
        {
            0 => Value,
            1 => Next,
            _ => null,
        };
    }

    /// <summary>A different type with an identical shape, for the type-matching rule.</summary>
    private sealed class OtherNode(int value) : IProfiCModel
    {
        public int Value { get; } = value;

        public object DeepTypeIdentity => typeof(OtherNode);

        public int DeepMemberCount => 2;

        public object? GetDeepMember(int index) => index == 0 ? Value : null;
    }

    // ---- The ordinary cases ---------------------------------------------------------------

    [Test]
    public void IdenticalReferencesAreEqual()
    {
        Node node = new(1);
        Assert.That(DeepEquality.Equals(node, node), Is.True);
    }

    [Test]
    public void SeparateObjectsWithEqualContentsAreEqual()
    {
        Assert.That(DeepEquality.Equals(new Node(1), new Node(1)), Is.True);
    }

    [Test]
    public void DifferingContentsAreNotEqual()
    {
        Assert.That(DeepEquality.Equals(new Node(1), new Node(2)), Is.False);
    }

    [Test]
    public void NullIsEqualOnlyToNull()
    {
        Assert.Multiple(() =>
        {
            Assert.That(DeepEquality.Equals(null, null), Is.True);
            Assert.That(DeepEquality.Equals(new Node(1), null), Is.False);
            Assert.That(DeepEquality.Equals(null, new Node(1)), Is.False);
        });
    }

    /// <summary>
    /// A Dog and an Animal are never equal, even when every shared field agrees. C# records
    /// apply the same rule.
    /// </summary>
    [Test]
    public void RuntimeTypesMustMatch()
    {
        Assert.That(DeepEquality.Equals(new Node(1), new OtherNode(1)), Is.False);
    }

    [Test]
    public void NestedModelsCompareThroughTheirLinks()
    {
        Node a = new(1) { Next = new Node(2) { Next = new Node(3) } };
        Node b = new(1) { Next = new Node(2) { Next = new Node(3) } };
        Node c = new(1) { Next = new Node(2) { Next = new Node(4) } };

        Assert.Multiple(() =>
        {
            Assert.That(DeepEquality.Equals(a, b), Is.True);
            Assert.That(DeepEquality.Equals(a, c), Is.False);
        });
    }

    // ---- Cycles ----------------------------------------------------------------------------

    [Test]
    public void ASelfReferencingNodeTerminates()
    {
        Node a = new(1);
        a.Next = a;

        Node b = new(1);
        b.Next = b;

        Assert.That(DeepEquality.Equals(a, b), Is.True);
    }

    [Test]
    public void ATwoNodeCycleTerminates()
    {
        // The case a depth budget handles worst: two nodes burn the entire allowance before
        // anything notices they are looping.
        Node a1 = new(1);
        Node a2 = new(2);
        a1.Next = a2;
        a2.Next = a1;

        Node b1 = new(1);
        Node b2 = new(2);
        b1.Next = b2;
        b2.Next = b1;

        Assert.That(DeepEquality.Equals(a1, b1), Is.True);
    }

    [Test]
    public void CyclesWithDifferingContentsAreStillDistinguished()
    {
        // Assuming equality until contradicted must not mean assuming it forever.
        Node a = new(1);
        a.Next = a;

        Node b = new(2);
        b.Next = b;

        Assert.That(DeepEquality.Equals(a, b), Is.False);
    }

    [Test]
    public void CyclesOfDifferentLengthsAreCompared()
    {
        Node a = new(1);
        a.Next = a;

        Node b1 = new(1);
        Node b2 = new(1);
        b1.Next = b2;
        b2.Next = b1;

        // Coinductively these are the same infinite unrolling, so they compare equal.
        Assert.That(DeepEquality.Equals(a, b1), Is.True);
    }

    // ---- Depth ------------------------------------------------------------------------------

    /// <summary>
    /// A long chain is finite and correct but would fail any reasonable depth cap, and a
    /// recursive walk would exhaust the stack on it. This is why the traversal is iterative.
    /// </summary>
    [Test]
    public void ALongAcyclicChainIsNotMistakenForACycle()
    {
        static Node Chain(int length)
        {
            Node head = new(0);
            Node current = head;

            for (int i = 1; i < length; i++)
            {
                current.Next = new Node(i);
                current = current.Next;
            }

            return head;
        }

        Assert.That(DeepEquality.Equals(Chain(100_000), Chain(100_000)), Is.True);
    }

    [Test]
    public void ALongChainDifferingAtItsEndIsNotEqual()
    {
        static Node Chain(int length, int lastValue)
        {
            Node head = new(0);
            Node current = head;

            for (int i = 1; i < length; i++)
            {
                current.Next = new Node(i == length - 1 ? lastValue : i);
                current = current.Next;
            }

            return head;
        }

        Assert.That(DeepEquality.Equals(Chain(50_000, 1), Chain(50_000, 2)), Is.False);
    }

    [Test]
    public void ShallowComparisonsStillWorkBeforeTrackingBegins()
    {
        // The visited set is allocated only once a comparison proves to be more than shallow.
        // These stay under that threshold and must be correct anyway.
        Assert.Multiple(() =>
        {
            Assert.That(DeepEquality.Equals(new Node(1), new Node(1)), Is.True);
            Assert.That(DeepEquality.Equals(new Node(1), new Node(2)), Is.False);
        });
    }

    // ---- Values and sets ---------------------------------------------------------------------

    [TestCase(1, 1, true)]
    [TestCase(1, 2, false)]
    public void IntegersCompareByValue(int a, int b, bool expected)
    {
        Assert.That(DeepEquality.Equals(a, b), Is.EqualTo(expected));
    }

    [Test]
    public void StringsCompareByValue()
    {
        Assert.Multiple(() =>
        {
            Assert.That(DeepEquality.Equals("abc", string.Concat("ab", "c")), Is.True);
            Assert.That(DeepEquality.Equals("abc", "abd"), Is.False);
        });
    }

    [Test]
    public void FractionsCompareByValue()
    {
        Assert.That(DeepEquality.Equals(new Fraction(1, 2), new Fraction(2, 4)), Is.True);
    }

    [Test]
    public void SetsCompareElementWise()
    {
        ProfiCSet<int> a = new([1, 2, 3]);
        ProfiCSet<int> b = new([1, 2, 3]);
        ProfiCSet<int> c = new([1, 2, 4]);
        ProfiCSet<int> shorter = new([1, 2]);

        Assert.Multiple(() =>
        {
            Assert.That(DeepEquality.Equals(a, b), Is.True);
            Assert.That(DeepEquality.Equals(a, c), Is.False);
            Assert.That(DeepEquality.Equals(a, shorter), Is.False);
        });
    }

    [Test]
    public void SetsOfModelsCompareEachElementDeeply()
    {
        ProfiCSet<Node> a = new([new Node(1), new Node(2)]);
        ProfiCSet<Node> b = new([new Node(1), new Node(2)]);

        Assert.That(DeepEquality.Equals(a, b), Is.True);
    }

    [Test]
    public void SetsAreOrderedSoOrderMatters()
    {
        ProfiCSet<int> a = new([1, 2]);
        ProfiCSet<int> b = new([2, 1]);

        Assert.That(DeepEquality.Equals(a, b), Is.False);
    }

    [Test]
    public void ASetContainingItselfTerminates()
    {
        ProfiCSet<object> a = [];
        a.Insert(a);

        ProfiCSet<object> b = [];
        b.Insert(b);

        Assert.That(DeepEquality.Equals(a, b), Is.True);
    }
}
