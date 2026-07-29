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
    public static string Format(SourceText source, Diagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(diagnostic);

        SourcePosition start = diagnostic.Span.Start;
        string severity = diagnostic.Severity == DiagnosticSeverity.Error ? "error" : "warning";

        return $"{source.FileName}({start.Line},{start.Column}): {severity} {diagnostic.Id}: {diagnostic.Message}";
    }

    /// <summary>
    /// <para>Describes a failure that stopped a running program, or returns null if the failure
    /// is not one a program can cause.</para>
    /// <para>The three kinds a program can cause are the language refusing to run further, an
    /// exception the language raised, and an exception the program declared and threw. Anything
    /// else is a fault in the compiler, and null tells the caller to let it travel.</para>
    /// </summary>
    public static string? DescribeFailure(SourceText source, Exception failure)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(failure);

        return failure switch
        {
            ProfiCRuntimeException => $"{source.FileName}: {failure.Message}",

            UncaughtProfiCException uncaught =>
                $"{source.FileName}: unhandled {uncaught.TypeName}: {uncaught.Text}",

            _ when BuiltInExceptions.IsBuiltIn(failure) =>
                $"{source.FileName}: unhandled {failure.GetType().Name}: {failure.Message}",

            _ => null,
        };
    }

    /// <summary>Writes every diagnostic in the bag, in source order, to standard error.</summary>
    public static void WriteAll(SourceText source, DiagnosticBag diagnostics)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(diagnostics);

        foreach (Diagnostic diagnostic in diagnostics.Sorted())
        {
            Console.Error.WriteLine(Format(source, diagnostic));
        }

        if (diagnostics.IsFull)
        {
            Console.Error.WriteLine(
                $"{source.FileName}: error: too many errors; stopped after {DiagnosticBag.MaximumDiagnostics}.");
        }
    }
}
