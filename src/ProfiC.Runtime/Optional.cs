namespace ProfiC.Runtime;

/// <summary>
/// <para>Lets rendering and the deep-equality walk read an optional without knowing what it
/// holds.</para>
/// <para>The same arrangement <see cref="IProfiCSet"/> uses, and for the same reason: an
/// <c>Optional&lt;T&gt;</c> arrives at those as a boxed <c>object</c> with its type argument
/// unknown, and asking it anything through reflection would be both slow and a second place
/// that knew the shape.</para>
/// </summary>
public interface IProfiCOptional
{
    /// <summary>True when a value is present.</summary>
    bool HasValue { get; }

    /// <summary>What is held, or null where nothing is.</summary>
    object? GetValue();
}

/// <summary>
/// <para>A value that may be absent. Profi-C has no null; this replaces it.</para>
/// <para>The three members are all there is: <c>HasValue</c> tests presence, <c>Or</c>
/// supplies a fallback, and <c>Value</c> asserts presence. Reading one the compiler cannot
/// prove present is rejected while compiling, so <c>Value</c> throwing is the exception rather
/// than the rule.</para>
/// <para>A struct, so an optional local costs nothing and an absent one allocates nothing.
/// </para>
/// </summary>
public readonly struct Optional<T> : IEquatable<Optional<T>>, IProfiCOptional
{
    private readonly T _value;

    private Optional(T value, bool hasValue)
    {
        _value = value;
        HasValue = hasValue;
    }

    /// <summary>The absent optional.</summary>
    public static Optional<T> Empty => default;

    /// <summary>Wraps a present value.</summary>
    public static Optional<T> Of(T value) => new(value, hasValue: true);

    /// <summary>True when a value is present.</summary>
    public bool HasValue { get; }

    /// <summary>
    /// The value, if present.
    /// </summary>
    /// <exception cref="EmptyOptionalException">The optional is empty.</exception>
    public T Value => HasValue ? _value : throw new EmptyOptionalException();

    /// <summary>
    /// <para>The value if present, otherwise the fallback.</para>
    /// <para>Taking the fallback lazily is what makes this short-circuit. Written as
    /// <c>a.Or(b)</c> it looks like an ordinary call, in which the argument would already have
    /// been evaluated; the compiler passes a thunk so that it is not.</para>
    /// </summary>
    public T Or(Func<T> fallback)
    {
        ArgumentNullException.ThrowIfNull(fallback);
        return HasValue ? _value : fallback();
    }

    /// <summary>The value if present, otherwise an already-computed fallback.</summary>
    public T Or(T fallback) => HasValue ? _value : fallback;

    /// <summary>
    /// <para>Chains onto another optional, which is what lets <c>a.Or(b).Or(c)</c> work.</para>
    /// <para>The result is still optional, since every candidate may be absent; only a
    /// non-optional fallback ends the chain with a definite value.</para>
    /// </summary>
    public Optional<T> Or(Optional<T> fallback) => HasValue ? this : fallback;

    /// <summary>Tries to read the value without throwing, for use inside the runtime.</summary>
    public bool TryGetValue(out T value)
    {
        value = _value;
        return HasValue;
    }

    public bool Equals(Optional<T> other) =>
        HasValue == other.HasValue
        && (!HasValue || EqualityComparer<T>.Default.Equals(_value, other._value));

    public override bool Equals(object? obj) => obj is Optional<T> other && Equals(other);

    public override int GetHashCode() => HasValue ? HashCode.Combine(true, _value) : 0;

    public static bool operator ==(Optional<T> left, Optional<T> right) => left.Equals(right);

    public static bool operator !=(Optional<T> left, Optional<T> right) => !left.Equals(right);

    public override string ToString() => HasValue ? _value?.ToString() ?? string.Empty : "empty";

    object? IProfiCOptional.GetValue() => HasValue ? _value : null;
}

/// <summary>Helpers for building optionals without naming their type.</summary>
public static class Optional
{
    /// <summary>Wraps a present value.</summary>
    public static Optional<T> Of<T>(T value) => Optional<T>.Of(value);

    /// <summary>The absent optional of some type.</summary>
    public static Optional<T> Empty<T>() => Optional<T>.Empty;

    /// <summary>
    /// <para>Wraps a .NET reference that may be null.</para>
    /// <para>This is where null stops. Every reference arriving from .NET becomes an optional
    /// at the boundary, so null never enters the language's own type system.</para>
    /// </summary>
    public static Optional<T> FromNullable<T>(T? value)
        where T : class =>
        value is null ? Optional<T>.Empty : Optional<T>.Of(value);
}
