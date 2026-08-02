using System.Collections;

namespace ProfiC.Runtime;

/// <summary>
/// Lets the deep-equality walk read a set without knowing its element type.
/// </summary>
public interface IProfiCSet
{
    /// <summary>How many elements the set holds.</summary>
    int Count { get; }

    /// <summary>One element, by position.</summary>
    object? GetElement(int index);

    /// <summary>
    /// Marks a <c>for each</c> walk as begun over this set, so that a change made during one
    /// is refused rather than left to mean something the walk cannot follow. Paired with
    /// <see cref="EndWalk"/> in a finally, and counted so nested walks do not unmark each other.
    /// </summary>
    void BeginWalk();

    /// <summary>Marks a walk as finished, however it finished.</summary>
    void EndWalk();
}

/// <summary>
/// <para>Profi-C's set: an ordered, indexed, growable sequence that permits duplicates.</para>
/// <para>Despite the name it is a list, not a mathematical set. It is a reference type, so
/// assigning one aliases rather than copies, exactly as in C#.</para>
/// <para>Backed by <see cref="List{T}"/> directly. A CLR array cannot serve, since inserting
/// and removing are part of the surface and an array's length is fixed — which is also why
/// <c>new integer[10]</c> is not something the language can write.</para>
/// </summary>
public sealed class ProfiCSet<T> : IProfiCSet, IEnumerable<T>
{
    private readonly List<T> _items;

    /// <summary>Creates an empty set.</summary>
    public ProfiCSet() => _items = [];

    /// <summary>Creates a set from a literal's elements.</summary>
    public ProfiCSet(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = [.. items];
    }

    /// <summary>How many elements the set holds. A method in Profi-C, since it has no properties.</summary>
    public int Count => _items.Count;

    /// <summary>
    /// Reads or writes one element.
    /// </summary>
    /// <exception cref="IndexOutOfRangeException">The index is outside the set.</exception>
    public T this[int index]
    {
        get
        {
            CheckIndex(index);
            return _items[index];
        }

        set
        {
            CheckIndex(index);
            _items[index] = value;
        }
    }

    /// <summary>
    /// <para>How many <c>for each</c> walks are in progress over this set.</para>
    /// <para>A count rather than a flag, because a set may be walked inside its own walk —
    /// two nested loops over one set is ordinary and neither changes it. The walk that ends
    /// first must not unmark the one still running.</para>
    /// </summary>
    private int _walks;

    /// <summary>Marks a walk as begun. Paired with <see cref="EndWalk"/> in a finally.</summary>
    public void BeginWalk() => _walks++;

    /// <summary>Marks a walk as finished, however it finished.</summary>
    public void EndWalk() => _walks--;

    /// <summary>
    /// Refuses a change while the set is being walked. A walk reads the length once, so a
    /// change made during one cannot move with it — see <see cref="SequenceChangedException"/>.
    /// </summary>
    private void RequireNotBeingWalked()
    {
        if (_walks > 0)
        {
            throw new SequenceChangedException();
        }
    }

    /// <summary>Appends an element.</summary>
    public void Insert(T value)
    {
        RequireNotBeingWalked();
        _items.Add(value);
    }

    /// <summary>
    /// Inserts an element at a position, shifting the rest along.
    /// </summary>
    /// <exception cref="IndexOutOfRangeException">The index is outside the set.</exception>
    public void InsertAt(int index, T value)
    {
        RequireNotBeingWalked();

        // Inserting at the end is legal, so the bound here is one past the last element.
        if (index < 0 || index > _items.Count)
        {
            throw new IndexOutOfRangeException(
                $"Cannot insert at {index}; the set holds {_items.Count} elements.");
        }

        _items.Insert(index, value);
    }

    /// <summary>
    /// Removes the first element equal to the given value, and reports whether one was found.
    /// This is the only mutator that yields anything, matching <see cref="List{T}"/>.
    /// </summary>
    public bool Remove(T value)
    {
        RequireNotBeingWalked();

        int index = IndexOf(value);

        if (index < 0)
        {
            return false;
        }

        _items.RemoveAt(index);
        return true;
    }

    /// <summary>
    /// Removes the element at a position.
    /// </summary>
    /// <exception cref="IndexOutOfRangeException">The index is outside the set.</exception>
    public void RemoveAt(int index)
    {
        RequireNotBeingWalked();
        CheckIndex(index);
        _items.RemoveAt(index);
    }

    /// <summary>True if some element equals the given value.</summary>
    public bool Contains(T value) => IndexOf(value) >= 0;

    /// <summary>
    /// The position of the first element equal to the given value, or -1.
    /// </summary>
    /// <remarks>
    /// Comparison is deep, so a set of models finds an element that matches structurally
    /// rather than only the identical object.
    /// </remarks>
    public int IndexOf(T value)
    {
        for (int i = 0; i < _items.Count; i++)
        {
            if (DeepEquality.Equals(_items[i], value))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Removes every element.</summary>
    public void Clear()
    {
        RequireNotBeingWalked();
        _items.Clear();
    }

    private void CheckIndex(int index)
    {
        if (index < 0 || index >= _items.Count)
        {
            throw new IndexOutOfRangeException(
                $"Index {index} is outside a set of {_items.Count} elements.");
        }
    }

    // ---- Reading a set into a new one ----------------------------------------------------
    //
    // Every one of these gives back a new set and leaves this one alone, which is what makes
    // them safe to write in the middle of a walk. They live here rather than in either engine
    // because both need them and neither should be the one that decides: an interpreter and an
    // emitter with their own 'Distinct' agree until the day one of them is changed.

    /// <summary>
    /// <para>A run of this set, from <paramref name="start"/> up to but not including
    /// <paramref name="end"/>.</para>
    /// <para>The end is exclusive, which is the reading <c>until</c> has in a loop, and is what
    /// makes <c>Subset(0, n)</c> and <c>Subset(n, Count)</c> put the whole set back together.
    /// </para>
    /// </summary>
    /// <exception cref="IndexOutOfRangeException">The run is not inside the set.</exception>
    public ProfiCSet<T> Subset(int start, int end)
    {
        if (start < 0 || start > _items.Count || end < start || end > _items.Count)
        {
            throw new IndexOutOfRangeException(
                $"Cannot take the run from {start} to {end} of a set of {_items.Count} elements.");
        }

        return new ProfiCSet<T>(_items.Skip(start).Take(end - start));
    }

    /// <summary>A run from <paramref name="start"/> to the end.</summary>
    public ProfiCSet<T> Subset(int start) => Subset(start, _items.Count);

    /// <summary>
    /// <para>This set followed by another, end to end.</para>
    /// <para><b>Appends rather than merges.</b> A Profi-C set keeps its order and allows a value
    /// twice, so what was in both is in the answer twice — this is not the union of mathematics,
    /// and <see cref="Distinct"/> is how to ask for that.</para>
    /// </summary>
    public ProfiCSet<T> Union(ProfiCSet<T> other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return new ProfiCSet<T>(_items.Concat(other._items));
    }

    /// <summary>What is in both, in this set's order.</summary>
    public ProfiCSet<T> Intersect(ProfiCSet<T> other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return new ProfiCSet<T>(_items.Where(other.Contains));
    }

    /// <summary>
    /// What this set has that the other does not. The counterpart of <see cref="Intersect"/>:
    /// between them they divide this set in two.
    /// </summary>
    public ProfiCSet<T> Except(ProfiCSet<T> other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return new ProfiCSet<T>(_items.Where(element => !other.Contains(element)));
    }

    /// <summary>
    /// <para>One of each, in the order the values were first met.</para>
    /// <para>Compared deeply, as <see cref="Contains"/> is, so two models holding the same fields
    /// count as one value. This is what turns a Profi-C set into the set of mathematics, which it
    /// is not until asked.</para>
    /// </summary>
    public ProfiCSet<T> Distinct()
    {
        ProfiCSet<T> kept = new();

        foreach (T element in _items)
        {
            if (!kept.Contains(element))
            {
                kept.Insert(element);
            }
        }

        return kept;
    }

    /// <summary>
    /// <para>Every element written out, with <paramref name="separator"/> between.</para>
    /// <para>Each is written the way it would be written on its own, so any set joins and not
    /// only a set of strings — which is what a reader joining numbers expects, and what they
    /// would otherwise have to write a loop for.</para>
    /// </summary>
    public string Join(string separator) =>
        string.Join(separator, _items.Select(element => ModelOperations.ToDisplayString(element)));

    // ---- Deep equality ------------------------------------------------------------------

    object? IProfiCSet.GetElement(int index) => _items[index];

    /// <summary>Sets compare element by element, each element itself compared deeply.</summary>
    public override bool Equals(object? obj) => DeepEquality.Equals(this, obj);

    /// <summary>
    /// Sets are mutable, so there is no stable hash to give. Returning identity keeps a set
    /// usable as a dictionary key without ever claiming two equal sets hash alike.
    /// </summary>
    public override int GetHashCode() =>
        System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);

    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() =>
        "{" + string.Join(", ", _items.Select(item => ModelOperations.ToElementString(item))) + "}";
}
