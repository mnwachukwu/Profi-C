using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;

namespace ProfiC.Compiler.Semantics;

/// <summary>
/// <para>Finds locals nothing ever reads.</para>
/// <para>A local exists to be read. One that never is, is either a result worked out and then
/// forgotten — a mistake the compiler can see and the reader cannot — or a value deliberately
/// dropped, which is what <see cref="Throwaway"/> is for. The two look identical in the source
/// until one of them is written as <c>_</c>, so this is the pass that makes writing one
/// worthwhile.</para>
/// <para>Reading is what counts. Assigning is not reading: a local written twice and read never
/// has done nothing, exactly as one nothing mentions again has. That is why the target of an
/// assignment is skipped here rather than walked, while everything around it — an index, a
/// receiver, the value — is walked normally, since those are read to work out where to store.
/// </para>
/// </summary>
public sealed class UnusedLocals : SyntaxVisitor
{
    private readonly SemanticModel _model;

    /// <summary>Each local declared in this file, and the declaration to report against.</summary>
    private readonly Dictionary<LocalSymbol, SyntaxNode> _declared = [];

    private readonly HashSet<LocalSymbol> _read = [];

    /// <summary>
    /// What stops this partway through, for a language server whose reader has typed again.
    /// Checked once per declaration, as definite assignment checks it.
    /// </summary>
    private readonly CancellationToken _cancellation;

    private UnusedLocals(SemanticModel model, CancellationToken cancellation)
    {
        _model = model;
        _cancellation = cancellation;
    }

    /// <summary>Reports every local that nothing reads, across a whole compilation.</summary>
    public static void Analyze(
        IReadOnlyList<CompilationUnit> units,
        SemanticModel model,
        DiagnosticBag diagnostics,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(diagnostics);

        foreach (CompilationUnit unit in units)
        {
            cancellation.ThrowIfCancellationRequested();

            // Per file, so that what is reported is reported against the file it was written
            // in, and so the two collections never grow past one file's worth.
            UnusedLocals pass = new(model, cancellation);
            pass.Visit(unit);

            using DiagnosticBag.FileScope reporting = diagnostics.InFile(unit.Source);

            foreach ((LocalSymbol local, SyntaxNode declaration) in pass._declared)
            {
                if (!pass._read.Contains(local))
                {
                    diagnostics.Report(
                        DiagnosticDescriptors.LocalNeverRead, declaration.Span, local.Name);
                }
            }
        }
    }

    /// <summary>Analyzes one file, which is a compilation of one.</summary>
    public static void Analyze(
        CompilationUnit unit,
        SemanticModel model,
        DiagnosticBag diagnostics,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(unit);

        Analyze([unit], model, diagnostics, cancellation);
    }

    public override void VisitVarDeclStmt(VarDeclStmt node)
    {
        _cancellation.ThrowIfCancellationRequested();
        Declared(node);
        base.VisitVarDeclStmt(node);
    }

    public override void VisitForStmt(ForStmt node)
    {
        Declared(node);
        base.VisitForStmt(node);
    }

    public override void VisitForEachStmt(ForEachStmt node)
    {
        Declared(node);
        base.VisitForEachStmt(node);
    }

    public override void VisitCatchClause(CatchClause node)
    {
        Declared(node);
        base.VisitCatchClause(node);
    }

    public override void VisitIdentifierExpr(IdentifierExpr node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (_model.GetSymbol(node) is LocalSymbol local)
        {
            _read.Add(local);
        }

        base.VisitIdentifierExpr(node);
    }

    /// <summary>
    /// Walks an assignment without counting a bare name on the left as a read. Anything else on
    /// the left — an index, a member's receiver — is walked, because working out where to store
    /// means reading it.
    /// </summary>
    public override void VisitAssignmentStmt(AssignmentStmt node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (node.Target is not IdentifierExpr)
        {
            Visit(node.Target);
        }

        Visit(node.Value);
    }

    /// <summary>
    /// Records a local the declaration bound. A throwaway is skipped: it says in the source
    /// that nothing will read it, which is the whole answer this pass is looking for.
    /// </summary>
    private void Declared(SyntaxNode declaration)
    {
        if (_model.GetSymbol(declaration) is LocalSymbol local
            && !Throwaway.Is(local.Name))
        {
            _declared[local] = declaration;
        }
    }
}
