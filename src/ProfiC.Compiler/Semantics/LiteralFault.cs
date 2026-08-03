namespace ProfiC.Compiler.Semantics;

/// <summary>
/// <para>Why a number written in a program names no value.</para>
/// <para>Only numbers appear here. Everything else a literal can get wrong is wrong in its
/// shape — an unterminated string, an escape the language does not know — and the scanner reads
/// the shape, so it has already said so by the time anything asks this.</para>
/// </summary>
public enum LiteralFault
{
    /// <summary>The digits name a value, and the type holds it.</summary>
    None,

    /// <summary>The digits name a value larger than the type can hold.</summary>
    TooLarge,

    /// <summary>A fraction over zero, which is division by zero written as a number.</summary>
    OverZero,
}
