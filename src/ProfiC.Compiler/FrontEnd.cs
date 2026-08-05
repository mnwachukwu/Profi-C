using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Documentation;
using ProfiC.Compiler.Semantics;
using ProfiC.Compiler.Text;

namespace ProfiC.Compiler;

/// <summary>
/// <para>Everything the compiler has to say about a program, in one call.</para>
/// <para><b>A new pass belongs here and nowhere else.</b> Before this existed the order below
/// was written out at every site that wanted it — the CLI, the language server, and each test
/// harness that reads a whole program — so adding a pass meant finding all of them. Missing one
/// does not report a missing pass: it reports whatever that pass would have said as absent, and
/// a file that silenced the pass as an unused directive. Both look like a problem with the
/// program rather than with the compiler.</para>
/// <para>Parsing is not here. A compilation is gathered file by file, and where the files come
/// from — one path, a folder, a project — is the caller's question, answered before this.</para>
/// </summary>
public static class FrontEnd
{
    /// <summary>
    /// <para>Resolves, checks, and reports on a whole compilation, yielding the model the rest
    /// of the pipeline reads.</para>
    /// <para>The passes run in this order because each needs what the one before it settled:
    /// nothing has a type until names are bound, no flow question can be asked until types are
    /// known, and whether a directive silenced anything is answerable only once everything
    /// else has reported.</para>
    /// <para><c>reportUnusedSuppressions</c> is false where the compilation is partial. A
    /// language server runs this on every keystroke over a file that is halfway typed, and a
    /// directive naming something the reader has not finished causing would read there as
    /// silencing nothing.</para>
    /// </summary>
    public static SemanticModel Check(
        IReadOnlyList<CompilationUnit> units,
        DiagnosticBag diagnostics,
        bool requireEntryPoint = false,
        IReadOnlyDictionary<SourceText, string>? projects = null,
        string? entryPoint = null,
        bool reportUnusedSuppressions = true,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(diagnostics);

        SemanticModel model = Resolver.Resolve(
            units, diagnostics, requireEntryPoint, projects, entryPoint, cancellation);

        TypeChecker.Check(units, model, diagnostics, cancellation);
        DefiniteAssignment.Analyze(units, model, diagnostics, cancellation);
        UnusedLocals.Analyze(units, model, diagnostics, cancellation);
        UnusedMembers.Analyze(units, model, diagnostics, cancellation);

        foreach (CompilationUnit unit in units)
        {
            DocumentationChecker.Check(unit, diagnostics);
        }

        if (reportUnusedSuppressions)
        {
            diagnostics.ReportUnusedSuppressions();
        }

        return model;
    }

    /// <summary>One file, which is a compilation of one.</summary>
    public static SemanticModel Check(
        CompilationUnit unit,
        DiagnosticBag diagnostics,
        bool requireEntryPoint = false,
        bool reportUnusedSuppressions = true,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(unit);

        return Check(
            [unit],
            diagnostics,
            requireEntryPoint,
            reportUnusedSuppressions: reportUnusedSuppressions,
            cancellation: cancellation);
    }
}
