using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Text;
using ProfiC.Interpreter;
using ProfiC.Runtime;

namespace ProfiC.Cli;

/// <summary>
/// <para>Turns diagnostics into text for a terminal.</para>
/// <para>This lives in the driver rather than the compiler on purpose. The compiler returns
/// diagnostics as data and writes nothing, which is what allows the same front end to back
/// an editor later.</para>
/// </summary>
public static class DiagnosticRenderer
{
    /// <summary>
    /// <para>Formats one diagnostic in MSBuild's canonical form:</para>
    /// <para><c>path(line,column): severity CODE: message</c></para>
    /// <para>Every .NET tool already parses this, which makes an editor problem matcher a
    /// short regular expression rather than a bespoke parser.</para>
    /// </summary>
    public static string Format(Diagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        SourcePosition start = diagnostic.Span.Start;

        string severity = diagnostic.Severity switch
        {
            DiagnosticSeverity.Error => "error",
            DiagnosticSeverity.Warning => "warning",
            _ => "opinion",
        };

        return $"{diagnostic.FileName}({start.Line},{start.Column}): {severity} {diagnostic.Id}: {diagnostic.Message}";
    }

    /// <summary>
    /// <para>Describes a failure that stopped a running program, or returns null if the failure
    /// is not one a program can cause.</para>
    /// <para>The three kinds a program can cause are the language refusing to run further, an
    /// exception the language raised, and an exception the program declared and threw. Anything
    /// else is a fault in the compiler, and null tells the caller to let it travel.</para>
    /// <para>Only the first two arms are the interpreter's. The sentence itself is
    /// <see cref="ProfiCFailure"/>'s, because an emitted program has to say the same thing at its
    /// own entry point and neither engine should own the wording.</para>
    /// </summary>
    public static string? DescribeFailure(string label, Exception failure)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(failure);

        return failure switch
        {
            ProfiCRuntimeException => $"{label}: {failure.Message}",

            // A model the program declared is an Instance here rather than an object of that
            // type, so a throw of one travels in a wrapper. Unwrapping it is the interpreter's
            // business; an emitted program throws the type itself and needs none of this.
            UncaughtProfiCException uncaught =>
                ProfiCFailure.Describe(label, uncaught.TypeName, uncaught.Text),

            _ => ProfiCFailure.Describe(label, failure),
        };
    }

    /// <summary>
    /// Writes every diagnostic in the bag to standard error, ordered by file and then by
    /// position. Each carries the file it was reported in, so a compilation of several files
    /// needs nothing more to say where a problem is.
    /// </summary>
    public static void WriteAll(DiagnosticBag diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        foreach (Diagnostic diagnostic in diagnostics.Sorted())
        {
            Console.Error.WriteLine(Format(diagnostic));
        }
    }
}
