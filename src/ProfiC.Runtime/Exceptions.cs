namespace ProfiC.Runtime;

/// <summary>
/// <para>Thrown when an empty optional is unwrapped with <c>Value()</c>.</para>
/// <para>This is the only exception Profi-C names that the base class library does not
/// already have. The other six — dividing by zero, indexing out of range, an invalid cast, a
/// bad format, a bad argument, and an overflow — map onto <c>System</c> types verbatim, which
/// is what will make an eventual bridge between .NET exceptions and Profi-C ones nearly
/// free.</para>
/// <para>Reaching this is rare by design: optional access is checked while compiling, and
/// <c>Value()</c> is the deliberate escape hatch, as Kotlin's <c>!!</c> is.</para>
/// </summary>
public sealed class EmptyOptionalException : InvalidOperationException
{
    public EmptyOptionalException()
        : base("Cannot read the value of an empty optional.")
    {
    }

    public EmptyOptionalException(string message)
        : base(message)
    {
    }

    public EmptyOptionalException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// <para>The exceptions a Profi-C program can name, and what each is at run time.</para>
/// <para>Recorded here so that one place answers the question, rather than the mapping being
/// implicit in the emitter.</para>
/// </summary>
public static class BuiltInExceptions
{
    /// <summary>
    /// Every exception name the language defines, paired with the type it denotes. The name a
    /// program writes after <c>catch</c> and the type that travels at run time are the same
    /// entry, so a name the language can raise is a name the language can catch.
    /// </summary>
    private static readonly (string Name, Type Type)[] Catalog =
    [
        ("Exception", typeof(Exception)),
        ("DivideByZeroException", typeof(DivideByZeroException)),
        ("IndexOutOfRangeException", typeof(IndexOutOfRangeException)),
        ("EmptyOptionalException", typeof(EmptyOptionalException)),
        ("InvalidCastException", typeof(InvalidCastException)),
        ("FormatException", typeof(FormatException)),
        ("ArgumentException", typeof(ArgumentException)),
        ("OverflowException", typeof(OverflowException)),
    ];

    /// <summary>Every exception name the language defines.</summary>
    public static IReadOnlyList<string> Names { get; } = [.. Catalog.Select(entry => entry.Name)];

    /// <summary>Maps a Profi-C exception name to the type it denotes.</summary>
    public static Type? Resolve(string profiCName)
    {
        foreach ((string name, Type type) in Catalog)
        {
            if (name == profiCName)
            {
                return type;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether an exception that reached the top of a program is one the language raises, and so
    /// one a program could have caught. Anything else is a fault in the compiler itself.
    /// </summary>
    public static bool IsBuiltIn(Exception thrown)
    {
        ArgumentNullException.ThrowIfNull(thrown);

        foreach ((_, Type type) in Catalog)
        {
            if (type != typeof(Exception) && type.IsInstanceOfType(thrown))
            {
                return true;
            }
        }

        return false;
    }
}
