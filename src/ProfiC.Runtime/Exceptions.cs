namespace ProfiC.Runtime;

/// <summary>
/// <para>Thrown when an empty optional is unwrapped with <c>Value()</c>.</para>
/// <para>This is the only exception Profi-C names that the base class library does not
/// already have. The other five — dividing by zero, indexing out of range, an invalid cast, a
/// bad format, and a bad argument — map onto <c>System</c> types verbatim, which is what will
/// make an eventual bridge between .NET exceptions and Profi-C ones nearly free.</para>
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
    /// <summary>Maps a Profi-C exception name to the type it denotes.</summary>
    public static Type? Resolve(string profiCName) => profiCName switch
    {
        "Exception" => typeof(Exception),
        "DivideByZeroException" => typeof(DivideByZeroException),
        "IndexOutOfRangeException" => typeof(IndexOutOfRangeException),
        "EmptyOptionalException" => typeof(EmptyOptionalException),
        "InvalidCastException" => typeof(InvalidCastException),
        "FormatException" => typeof(FormatException),
        "ArgumentException" => typeof(ArgumentException),
        _ => null,
    };

    /// <summary>Every exception name the language defines.</summary>
    public static IReadOnlyList<string> Names { get; } =
    [
        "Exception",
        "DivideByZeroException",
        "IndexOutOfRangeException",
        "EmptyOptionalException",
        "InvalidCastException",
        "FormatException",
        "ArgumentException",
    ];
}
