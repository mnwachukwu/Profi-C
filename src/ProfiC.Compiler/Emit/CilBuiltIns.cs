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
        id is BuiltInId.ConsoleWrite or BuiltInId.ConsoleWriteLine or BuiltInId.ConsoleRead
        || IsOnASet(id)
        || IsOnAnOptional(id);

    /// <summary>The three members an optional has, and there are only three.</summary>
    public static bool IsOnAnOptional(BuiltInId id) =>
        id is BuiltInId.OptionalHasValue
           or BuiltInId.OptionalValue
           or BuiltInId.OptionalOr;

    /// <summary>
    /// <para>The members of a set, each one call on the runtime's own.</para>
    /// <para>What is missing is the <c>Trim</c> family, which is only on a set of optionals — and
    /// an optional is not something the emitter has a type for yet, so there is no such set to
    /// call it on.</para>
    /// </summary>
    public static bool IsOnASet(BuiltInId id) =>
        id is BuiltInId.SetCount
           or BuiltInId.SetInsert
           or BuiltInId.SetInsertAt
           or BuiltInId.SetRemove
           or BuiltInId.SetRemoveAt
           or BuiltInId.SetContains
           or BuiltInId.SetIndexOf
           or BuiltInId.SetClear
           or BuiltInId.SetSubsetFrom
           or BuiltInId.SetSubsetBetween
           or BuiltInId.SetUnion
           or BuiltInId.SetIntersect
           or BuiltInId.SetExcept
           or BuiltInId.SetDistinct
           or BuiltInId.SetJoin
           or BuiltInId.SetTrim
           or BuiltInId.SetTrimStart
           or BuiltInId.SetTrimEnd
           or BuiltInId.SetTrimAll;

    /// <summary>How a reader wrote it, for a message about it.</summary>
    public static string NameOf(BuiltInId id) => id switch
    {
        BuiltInId.ConsoleWrite => "Console.Write",
        BuiltInId.ConsoleWriteLine => "Console.WriteLine",
        BuiltInId.ConsoleRead => "Console.Read",
        _ => $"'{id}'",
    };
}
