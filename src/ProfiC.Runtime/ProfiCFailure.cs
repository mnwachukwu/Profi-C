using System.Reflection;

namespace ProfiC.Runtime;

/// <summary>
/// <para>How a program that stopped says so, in one place both engines call.</para>
/// <para><b>A program fails the same way wherever it runs.</b> The two reach the top of a failing
/// program by different routes — the interpreter catches what it was walking, an emitted program
/// catches at its own entry point — but what a reader is told is one decision, and this is where it
/// is made. Left to itself an emitted program falls to the CLR's own handler, which names the type,
/// prints the frames beneath it, and tells somebody learning the language about machinery that is
/// not theirs.</para>
/// <para><b>It names the program and not a line in it.</b> A diagnostic while compiling can point
/// at a position because the source is right there being read; a program that was built and handed
/// on has no source beside it, and sending its reader to a line of a file they may not have is
/// worse than telling them plainly what stopped.</para>
/// <para><b>A fault in the compiler is not described here, and travels.</b> That is why this
/// answers null rather than a sentence for everything: a null reference raised inside the emitter's
/// own output is not the program's mistake, and dressing it as one would hide the only trace of it
/// from the person who could fix it.</para>
/// </summary>
public static class ProfiCFailure
{
    /// <summary>
    /// <para>What stopped a program, as a reader should see it.</para>
    /// <para>The uncatchable ones are named without the word <c>unhandled</c>. That word implies a
    /// handler was the missing piece, and for those no clause could have taken it.</para>
    /// </summary>
    /// <param name="label">What to call the program — the file it was written in.</param>
    /// <param name="typeName">What was thrown, by the name the language knows it as.</param>
    /// <param name="message">What it carried.</param>
    public static string Describe(string label, string typeName, string message) =>
        BuiltInExceptions.MayBeCaught(typeName)
            ? $"{label}: unhandled {typeName}: {message}"
            : $"{label}: {typeName}: {message}";

    /// <summary>
    /// The same, for a failure that arrived as a .NET exception — or null where it is not one the
    /// program could have caused.
    /// </summary>
    public static string? Describe(string label, Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return RaisedByTheProgram(failure)
            ? Describe(label, failure.GetType().Name, failure.Message)
            : null;
    }

    /// <summary>
    /// <para>Describes a failure and writes it, answering whether it was the program's.</para>
    /// <para>What an emitted program calls at its entry point, and the whole of what it needs.
    /// False means let the failure travel, which is the CLR printing its frames for a fault
    /// nobody wrote.</para>
    /// </summary>
    public static bool Report(string label, Exception failure)
    {
        if (Describe(label, failure) is not { } described)
        {
            return false;
        }

        Console.Error.WriteLine(described);
        return true;
    }

    /// <summary>
    /// <para>Whether a failure is one the program could have caused.</para>
    /// <para>Two kinds qualify. One the language raises is in the catalog, which is the same list
    /// a <c>catch</c> clause matches names against. One the program declared lives in the program's
    /// own assembly — a model extending <c>Exception</c> becomes a type there, and nothing the
    /// program did not write does.</para>
    /// <para>Everything left is a fault in the compiler. Every .NET exception answers to
    /// <c>Exception</c>, so without this test one of ours would be handed to the reader as though
    /// they had caused it.</para>
    /// </summary>
    private static bool RaisedByTheProgram(Exception failure) =>
        BuiltInExceptions.IsBuiltIn(failure)
        || (Assembly.GetEntryAssembly() is { } program
            && ReferenceEquals(failure.GetType().Assembly, program));
}
