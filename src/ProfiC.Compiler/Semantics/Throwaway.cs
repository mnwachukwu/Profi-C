namespace ProfiC.Compiler.Semantics;

/// <summary>
/// <para>The throwaway: a single underscore written where a name belongs, binding nothing.</para>
/// <para>It is for the places the grammar asks for a name and the program has no use for one —
/// the element of a loop that only counts, an exception handled rather than read, a parameter a
/// signature obliges but the body ignores. Writing one says so. An invented name says the
/// opposite, and has to be read through to find out it was never used.</para>
/// <para>Because it binds nothing, several in one body are not a clash, and none of them can be
/// read. It is only the bare underscore: <c>_count</c> and <c>_x</c> are ordinary names.</para>
/// </summary>
public static class Throwaway
{
    /// <summary>How a throwaway is written.</summary>
    public const string Name = "_";

    /// <summary>Whether a name, as written, is the throwaway.</summary>
    public static bool Is(string name) => string.Equals(name, Name, StringComparison.Ordinal);
}
