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
}
