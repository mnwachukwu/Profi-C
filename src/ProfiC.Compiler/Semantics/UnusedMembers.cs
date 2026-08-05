using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;

namespace ProfiC.Compiler.Semantics;

/// <summary>
/// <para>Finds private members that nothing reaches.</para>
/// <para>The question only has an answer for a private member. One is visible to its declaring
/// type and no further, so the compilation holding that type holds every use it can ever have,
/// and a name written nowhere means a member reached from nowhere. A protected, internal or
/// public member is reachable from code that is not here — a descendant in another file, a
/// program written against this one later — so silence about it is silence, not an answer.</para>
/// <para>Whole-compilation rather than per-file, unlike <see cref="UnusedLocals"/>. A local
/// cannot escape the body it is declared in; a private member is reached from anywhere in its
/// type, and a type may be spread across files.</para>
/// </summary>
public sealed class UnusedMembers : SyntaxVisitor
{
    private readonly SemanticModel _model;

    /// <summary>Each private member declared, and the file that declared it.</summary>
    private readonly Dictionary<Symbol, (SyntaxNode Declaration, CompilationUnit In)> _declared = [];

    private readonly HashSet<Symbol> _used = [];

    private readonly CancellationToken _cancellation;

    private CompilationUnit? _current;

    private UnusedMembers(SemanticModel model, CancellationToken cancellation)
    {
        _model = model;
        _cancellation = cancellation;
    }

    /// <summary>Reports every private member that nothing in the compilation reaches.</summary>
    public static void Analyze(
        IReadOnlyList<CompilationUnit> units,
        SemanticModel model,
        DiagnosticBag diagnostics,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(diagnostics);

        UnusedMembers pass = new(model, cancellation);

        // Every file is walked before anything is reported, because a use in the last file
        // answers for a declaration in the first.
        foreach (CompilationUnit unit in units)
        {
            cancellation.ThrowIfCancellationRequested();
            pass._current = unit;
            pass.Visit(unit);
        }

        foreach (CompilationUnit unit in units)
        {
            using DiagnosticBag.FileScope reporting = diagnostics.InFile(unit.Source);

            foreach ((Symbol member, (SyntaxNode declaration, CompilationUnit declaredIn)) in
                     pass._declared)
            {
                if (declaredIn == unit && !pass._used.Contains(member))
                {
                    diagnostics.Report(
                        DiagnosticDescriptors.MemberNeverUsed, declaration.Span, member.Name);
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

    public override void VisitFieldDecl(FieldDecl node)
    {
        if (_model.GetSymbol(node) is FieldSymbol { Visibility: Visibility.Private } field)
        {
            Declared(field, node);
        }

        base.VisitFieldDecl(node);
    }

    /// <summary>
    /// <para>A private function, unless its name is not how it is reached.</para>
    /// <para>A constructor answers to <c>new</c>, an overridable function to whatever the value
    /// turns out to be at run time, and a program's <c>Main</c> to the runtime starting it. Each
    /// would read as unused however much it is used.</para>
    /// </summary>
    public override void VisitFunctionDecl(FunctionDecl node)
    {
        if (_model.GetSymbol(node) is FunctionSymbol
            {
                Visibility: Visibility.Private,
                IsConstructor: false,
                IsOverridable: false,
            } function
            && !CouldBeStarted(function))
        {
            Declared(function, node);
        }

        base.VisitFunctionDecl(node);
    }

    /// <summary>
    /// <para>Whether a runtime could start this: a <c>Main</c> in a <c>shared model Program</c>.
    /// </para>
    /// <para><b>Every one of them, rather than the one this build begins at.</b> A compilation
    /// may hold several programs, since a namespace makes <c>Tools.Program</c> and
    /// <c>App.Program</c> two types — and which of them begins is settled by a line in a project
    /// file. Asking only about the chosen one reports the others as used by nothing, when what is
    /// true of them is that they are used exactly as the chosen one is, in the build that chooses
    /// them. Changing that line runs a different one with no code edited, and a warning that
    /// moves from file to file as a project is retargeted is describing the project rather than
    /// the code.</para>
    /// </summary>
    private static bool CouldBeStarted(FunctionSymbol function) =>
        string.Equals(function.Name, "Main", StringComparison.Ordinal)
        && function.DeclaringType is ModelSymbol { IsShared: true, Name: "Program" };

    /// <summary>
    /// <para>Every node that names something, counted as a use of what it names.</para>
    /// <para>A declaration names its own member too, and is told apart by being the very node
    /// the symbol was declared at — so a member is not kept alive by the line declaring it.
    /// </para>
    /// </summary>
    protected override void DefaultVisit(SyntaxNode node)
    {
        if (_model.GetSymbol(node) is FieldSymbol or FunctionSymbol
            && _model.GetSymbol(node) is { } member
            && !ReferenceEquals(node, member.Declaration))
        {
            _used.Add(member);
        }

        base.DefaultVisit(node);
    }

    /// <summary>
    /// Records a member to answer for later. A throwaway is passed over: one naming a member is
    /// already refused, and nothing reaching it is the consequence of that rather than a second
    /// thing to say about it.
    /// </summary>
    private void Declared(Symbol member, SyntaxNode declaration)
    {
        _cancellation.ThrowIfCancellationRequested();

        if (_current is not null && !Throwaway.Is(member.Name))
        {
            _declared[member] = (declaration, _current);
        }
    }
}
