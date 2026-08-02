using ProfiC.Compiler.Semantics;

namespace ProfiC.Compiler.Emit;

/// <summary>
/// <para>The built-in operations the emitter knows a call sequence for.</para>
/// <para><c>Console</c> came first, and not arbitrarily: a program with no output cannot be told
/// from a program that did not run, so printing is what makes every other emitted instruction
/// checkable against the interpreter.</para>
/// <para>Named separately from the list of what is supported, because a refusal reads better
/// saying <c>Console.WriteLine</c> than saying <c>ConsoleWriteLine</c>.</para>
/// </summary>
internal static class CilBuiltIns
{
    public static bool IsSupported(BuiltInId id) =>
        id is BuiltInId.ConsoleWrite or BuiltInId.ConsoleWriteLine or BuiltInId.ConsoleRead
           or BuiltInId.ReferenceEquals
        || IsOnEveryValue(id)
        || IsOnASet(id)
        || IsOnAnOptional(id);

    /// <summary>
    /// <para>What every value answers, <c>Model</c> being the root of them all.</para>
    /// <para><c>ToString</c> goes to the runtime rather than the framework, because how a value
    /// reads is the language's decision: a boolean reads as <c>true</c> and not <c>True</c>, and
    /// a set reads with its braces.</para>
    /// <para><c>Equals</c> is the same walk <c>==</c> is, and reaches the fields of an emitted
    /// model through <see cref="Runtime.IProfiCModel"/>, which every model implements.</para>
    /// </summary>
    public static bool IsOnEveryValue(BuiltInId id) =>
        id is BuiltInId.ModelToString or BuiltInId.ModelEquals;

    /// <summary>The three members an optional has, and there are only three.</summary>
    public static bool IsOnAnOptional(BuiltInId id) =>
        id is BuiltInId.OptionalHasValue
           or BuiltInId.OptionalValue
           or BuiltInId.OptionalOr;

    /// <summary>
    /// <para>The members of a set, each one call on the runtime's own.</para>
    /// <para>All of them, the <c>Trim</c> family included — that one only exists on a set of
    /// optionals, and <c>TrimAll</c> is the single member here that answers with a different
    /// kind of set than it was asked of.</para>
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
