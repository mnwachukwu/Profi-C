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

    /// <summary>Appends an element.</summary>
    public void Insert(T value) => _items.Add(value);

    /// <summary>
    /// Inserts an element at a position, shifting the rest along.
    /// </summary>
    /// <exception cref="IndexOutOfRangeException">The index is outside the set.</exception>
    public void InsertAt(int index, T value)
    {
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
    public void Clear() => _items.Clear();

    private void CheckIndex(int index)
    {
        if (index < 0 || index >= _items.Count)
        {
            throw new IndexOutOfRangeException(
                $"Index {index} is outside a set of {_items.Count} elements.");
        }
    }

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
        "{" + string.Join(", ", _items.Select(item => ModelOperations.ToDisplayString(item))) + "}";
}
