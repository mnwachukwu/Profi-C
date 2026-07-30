namespace ProfiC.Compiler.Diagnostics;

/// <summary>
/// <para>Turns values into the words a diagnostic uses to talk about them.</para>
/// <para>A message is a sentence someone reads, so it agrees with itself: one argument rather
/// than 1 argument(s), and none rather than 0. The care taken over an article before a type
/// name is the same care, and this is where the rest of it lives.</para>
/// </summary>
public static class Wording
{
    /// <summary>
    /// <para>A count and the thing counted: <c>no arguments</c>, <c>1 argument</c>,
    /// <c>3 arguments</c>.</para>
    /// <para>The plural is the singular with an <c>s</c> unless one is given, which covers
    /// every noun a diagnostic has needed so far and leaves room for one that does not.</para>
    /// </summary>
    public static string Count(int amount, string singular, string? plural = null) => amount switch
    {
        0 => $"no {plural ?? singular + "s"}",
        1 => $"1 {singular}",
        _ => $"{amount} {plural ?? singular + "s"}",
    };

    /// <summary>
    /// <para>A run of names as a reader would say them: <c>Hearts</c>, <c>Hearts and
    /// Spades</c>, <c>Hearts, Spades and Clubs</c>.</para>
    /// <para>A diagnostic that lists things is read aloud like any other sentence, and a bare
    /// comma-separated run reads as output rather than as English.</para>
    /// </summary>
    public static string List(IReadOnlyList<string> items) => items.Count switch
    {
        0 => string.Empty,
        1 => items[0],
        _ => string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1],
    };

    /// <summary>
    /// The same, for choices rather than for members of a group: "one, three or six". A reader
    /// told a thing takes "1, 3 and 6 arguments" would reasonably ask for all ten.
    /// </summary>
    public static string Either(IReadOnlyList<string> items) => items.Count switch
    {
        0 => string.Empty,
        1 => items[0],
        _ => string.Join(", ", items.Take(items.Count - 1)) + " or " + items[^1],
    };
}
