using ProfiC.Compiler.Semantics;

namespace ProfiC.Compiler.Emit;

/// <summary>
/// <para>The built-in operations the emitter knows a call sequence for.</para>
/// <para>Two only, so far, and both are <c>Console</c>. That is not an arbitrary starting point:
/// a program with no output cannot be told from a program that did not run, so printing is what
/// makes every other emitted instruction checkable against the interpreter.</para>
/// <para>Named separately from the list of what is supported, because a refusal reads better
/// saying <c>Console.WriteLine</c> than saying <c>ConsoleWriteLine</c>.</para>
/// </summary>
internal static class CilBuiltIns
{
    public static bool IsSupported(BuiltInId id) =>
        id is BuiltInId.ConsoleWrite or BuiltInId.ConsoleWriteLine;

    /// <summary>How a reader wrote it, for a message about it.</summary>
    public static string NameOf(BuiltInId id) => id switch
    {
        BuiltInId.ConsoleWrite => "Console.Write",
        BuiltInId.ConsoleWriteLine => "Console.WriteLine",
        BuiltInId.ConsoleRead => "Console.Read",
        _ => $"'{id}'",
    };
}
