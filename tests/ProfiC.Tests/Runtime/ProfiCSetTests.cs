using ProfiC.Runtime;

namespace ProfiC.Tests.Runtime;

/// <summary>The set type, which despite its name is an ordered list that permits duplicates.</summary>
[TestFixture]
public sealed class ProfiCSetTests
{
    private static ProfiCSet<int> Set(params int[] items) => new(items);

    [Test]
    public void ANewSetIsEmpty()
    {
        Assert.That(new ProfiCSet<int>().Count, Is.Zero);
    }

    [Test]
    public void InsertAppends()
    {
        ProfiCSet<int> set = Set(1, 2);
        set.Insert(3);

        Assert.That(set, Is.EqualTo(new[] { 1, 2, 3 }));
    }

    [Test]
    public void InsertAtShiftsTheRestAlong()
    {
        ProfiCSet<int> set = Set(1, 3);
        set.InsertAt(1, 2);

        Assert.That(set, Is.EqualTo(new[] { 1, 2, 3 }));
    }

    [Test]
    public void InsertAtTheEndIsAllowed()
    {
        ProfiCSet<int> set = Set(1, 2);
        set.InsertAt(2, 3);

        Assert.That(set, Is.EqualTo(new[] { 1, 2, 3 }));
    }

    [TestCase(-1)]
    [TestCase(5)]
    public void InsertAtOutsideTheSetIsRejected(int index)
    {
        Assert.Throws<IndexOutOfRangeException>(() => Set(1, 2).InsertAt(index, 9));
    }

    /// <summary>
    /// The only mutator that yields anything, which matches the shape of the underlying list.
    /// </summary>
    [Test]
    public void RemoveReportsWhetherItFoundAnything()
    {
        ProfiCSet<int> set = Set(1, 2, 3);

        Assert.Multiple(() =>
        {
            Assert.That(set.Remove(2), Is.True);
            Assert.That(set.Remove(9), Is.False);
            Assert.That(set, Is.EqualTo(new[] { 1, 3 }));
        });
    }

    [Test]
    public void RemoveTakesOnlyTheFirstMatch()
    {
        ProfiCSet<int> set = Set(1, 2, 2, 3);
        set.Remove(2);

        Assert.That(set, Is.EqualTo(new[] { 1, 2, 3 }));
    }

    [Test]
    public void RemoveAtTakesAPosition()
    {
        ProfiCSet<int> set = Set(1, 2, 3);
        set.RemoveAt(1);

        Assert.That(set, Is.EqualTo(new[] { 1, 3 }));
    }

    [TestCase(-1)]
    [TestCase(3)]
    public void IndexingOutsideTheSetIsRejected(int index)
    {
        ProfiCSet<int> set = Set(1, 2, 3);

        Assert.Multiple(() =>
        {
            Assert.Throws<IndexOutOfRangeException>(() => _ = set[index]);
            Assert.Throws<IndexOutOfRangeException>(() => set.RemoveAt(index));
        });
    }

    [Test]
    public void DuplicatesArePermitted()
    {
        Assert.That(Set(1, 1, 1).Count, Is.EqualTo(3));
    }

    [Test]
    public void OrderIsPreserved()
    {
        Assert.That(Set(3, 1, 2), Is.EqualTo(new[] { 3, 1, 2 }));
    }

    [Test]
    public void ContainsAndIndexOfAgree()
    {
        ProfiCSet<int> set = Set(10, 20, 30);

        Assert.Multiple(() =>
        {
            Assert.That(set.Contains(20), Is.True);
            Assert.That(set.IndexOf(20), Is.EqualTo(1));
            Assert.That(set.Contains(99), Is.False);
            Assert.That(set.IndexOf(99), Is.EqualTo(-1));
        });
    }

    [Test]
    public void ClearEmptiesTheSet()
    {
        ProfiCSet<int> set = Set(1, 2, 3);
        set.Clear();

        Assert.That(set.Count, Is.Zero);
    }

    /// <summary>
    /// Searching uses the same structural comparison as <c>==</c>, so a set of models finds
    /// an element that matches by content rather than only the identical object.
    /// </summary>
    [Test]
    public void SearchingComparesStructurally()
    {
        ProfiCSet<ProfiCSet<int>> nested = new([Set(1, 2), Set(3)]);

        Assert.That(nested.Contains(Set(1, 2)), Is.True,
                    "an equal but distinct set should be found");
    }

    [Test]
    public void ASetIsAReferenceTypeSoAssignmentAliases()
    {
        ProfiCSet<int> original = Set(1, 2);
        ProfiCSet<int> alias = original;

        alias.Insert(3);

        Assert.That(original.Count, Is.EqualTo(3), "assignment should alias, not copy");
    }

    [Test]
    public void PrintingShowsTheElementsInBraces()
    {
        Assert.That(Set(1, 2, 3).ToString(), Is.EqualTo("{1, 2, 3}"));
        Assert.That(new ProfiCSet<int>().ToString(), Is.EqualTo("{}"));
    }

    /// <summary>
    /// <para>A character and a string are quoted inside a set, as each is written in source.
    /// </para>
    /// <para>Without it the delimiter cannot be told from the same characters inside a value:
    /// one string holding a comma would print exactly as two strings do, and a set of spaces
    /// would print as a row of nothing.</para>
    /// </summary>
    [Test]
    public void PrintingQuotesWhateverCouldBeConfusedWithTheDelimiter()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                new ProfiCSet<object?>(["a, b"]).ToString(),
                Is.EqualTo("{\"a, b\"}"));

            Assert.That(
                new ProfiCSet<object?>(["a", "b"]).ToString(),
                Is.EqualTo("{\"a\", \"b\"}"),
                "one string holding a comma must not read as two strings");

            Assert.That(
                new ProfiCSet<object?>([',', ' ']).ToString(),
                Is.EqualTo("{',', ' '}"));

            Assert.That(
                new ProfiCSet<object?>([true, 1L, null]).ToString(),
                Is.EqualTo("{true, 1, empty}"),
                "nothing else gains quotes it did not have");
        });
    }

    /// <summary>A value printed on its own has nothing beside it to be confused with.</summary>
    [Test]
    public void PrintingAValueOnItsOwnDoesNotQuoteIt() =>
        Assert.Multiple(() =>
        {
            Assert.That(ModelOperations.ToDisplayString("plain"), Is.EqualTo("plain"));
            Assert.That(ModelOperations.ToDisplayString(','), Is.EqualTo(","));
        });

    // ---- Trimming the empties out of a set of optionals ---------------------------------------

    /// <summary>
    /// <para>The three that take from the ends stop at the first element holding something, so
    /// an empty between two values survives all of them.</para>
    /// <para>Written over the typed shape, which is what an emitted program holds.</para>
    /// </summary>
    [Test]
    public void TrimmingTakesFromTheEndsAndLeavesTheMiddleAlone()
    {
        ProfiCSet<Optional<long>> sparse = new(
        [
            Optional<long>.Empty,
            Optional<long>.Of(1),
            Optional<long>.Empty,
            Optional<long>.Of(2),
            Optional<long>.Empty,
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(sparse.Trim().Count, Is.EqualTo(3), "both ends");
            Assert.That(sparse.TrimStart().Count, Is.EqualTo(4), "the front only");
            Assert.That(sparse.TrimEnd().Count, Is.EqualTo(4), "the back only");
            Assert.That(sparse.Count, Is.EqualTo(5), "and the set asked is left as it was");
        });
    }

    /// <summary>
    /// <para>The same three over the untyped shape, which is what the interpreter holds: an
    /// empty optional is a null there rather than an <see cref="Optional{T}"/> saying so.</para>
    /// <para>The pair matters more than either half. One definition serves both engines only if
    /// it recognizes both ways of being empty, and nothing else in the suite would notice if it
    /// stopped recognizing one.</para>
    /// </summary>
    [Test]
    public void AnEmptyIsRecognizedWhicheverWayTheEngineHoldsIt()
    {
        ProfiCSet<object?> sparse = new([null, 1L, null, 2L, null]);

        Assert.Multiple(() =>
        {
            Assert.That(sparse.Trim().Count, Is.EqualTo(3));
            Assert.That(sparse.TrimStart().Count, Is.EqualTo(4));
            Assert.That(sparse.TrimEnd().Count, Is.EqualTo(4));
            Assert.That(sparse.TrimAll().Count, Is.EqualTo(2));
        });
    }

    /// <summary>
    /// <c>TrimAll</c> for a typed set drops the empties and unwraps what is left, so the answer
    /// is a set of values rather than a set of optionals that happen all to be full.
    /// </summary>
    [Test]
    public void WithoutEmptiesDropsThemAllAndUnwrapsTheRest()
    {
        ProfiCSet<Optional<long>> sparse = new(
        [
            Optional<long>.Empty,
            Optional<long>.Of(1),
            Optional<long>.Empty,
            Optional<long>.Of(2),
        ]);

        ProfiCSet<long> kept = ProfiCSet.WithoutEmpties(sparse);

        Assert.Multiple(() =>
        {
            Assert.That(kept.Count, Is.EqualTo(2));
            Assert.That(kept[0], Is.EqualTo(1L));
            Assert.That(kept[1], Is.EqualTo(2L));
        });
    }

    /// <summary>A set with nothing absent in it is returned whole by all four.</summary>
    [Test]
    public void TrimmingASetWithNothingAbsentChangesNothing()
    {
        ProfiCSet<object?> full = new([1L, 2L, 3L]);

        Assert.Multiple(() =>
        {
            Assert.That(full.Trim().Count, Is.EqualTo(3));
            Assert.That(full.TrimAll().Count, Is.EqualTo(3));
        });
    }

    /// <summary>
    /// A set of nothing but empties trims to nothing rather than running off either end.
    /// </summary>
    [Test]
    public void ASetOfNothingButEmptiesTrimsToNothing()
    {
        ProfiCSet<object?> empties = new([null, null, null]);

        Assert.Multiple(() =>
        {
            Assert.That(empties.Trim().Count, Is.Zero);
            Assert.That(empties.TrimStart().Count, Is.Zero);
            Assert.That(empties.TrimEnd().Count, Is.Zero);
            Assert.That(empties.TrimAll().Count, Is.Zero);
        });
    }
}
