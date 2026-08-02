using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Semantics;

namespace ProfiC.Compiler.Emit;

/// <summary>
/// <para>Decides whether a program is one the emitter can turn into CIL, before any of it is
/// written.</para>
/// <para><b>Separate from the emitter, and ahead of it, on purpose.</b> An emitter that
/// discovered its limits partway through would already have defined half a type when it found
/// out, and would have to either unwind or leave the rest out. Leaving the rest out is the worse
/// of the two: an assembly missing a method still verifies, still loads, and fails only when a
/// run reaches the gap. Refusing first means a build either produces a whole assembly or
/// produces no file at all.</para>
/// <para><b>This is the authority, and the emitter trusts it.</b> The two must agree about the
/// subset, and the way to make that true is for one of them to decide. Where the emitter meets
/// something this pass allowed, that is a fault in the compiler rather than a program to
/// report, and it says so.</para>
/// <para>Everything here is temporary by construction: each refusal names a thing the emitter
/// does not do <em>yet</em>, and closing one is deleting a case from this file.</para>
/// </summary>
internal sealed class EmitSurvey : SyntaxVisitor
{
    private readonly SemanticModel _model;
    private readonly DiagnosticBag _diagnostics;

    private EmitSurvey(SemanticModel model, DiagnosticBag diagnostics)
    {
        _model = model;
        _diagnostics = diagnostics;
    }

    /// <summary>
    /// Whether every unit can be emitted, reporting what cannot. Reports everything it finds
    /// rather than stopping at the first, since a reader deciding whether to wait for the back
    /// end wants to know what it is waiting on.
    /// </summary>
    public static bool CanEmit(
        IReadOnlyList<CompilationUnit> units,
        SemanticModel model,
        DiagnosticBag diagnostics)
    {
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(diagnostics);

        int before = diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);

        foreach (CompilationUnit unit in units)
        {
            using DiagnosticBag.FileScope reporting = diagnostics.InFile(unit.Source);

            new EmitSurvey(model, diagnostics).Visit(unit);
        }

        return diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error) == before;
    }

    /// <summary>Reports one thing the emitter cannot do, named as a reader would say it.</summary>
    private void Refuse(SyntaxNode node, string what) =>
        _diagnostics.Report(DiagnosticDescriptors.CannotEmitYet, node.Span, what);

    // ---- Declarations -----------------------------------------------------------------------

    /// <summary>
    /// <para>A model may be emitted when what it extends is another model this program declares,
    /// or nothing.</para>
    /// <para>What is left is a model built on one the language provides — an exception. That
    /// parent is a type in the runtime rather than one being written here, so deriving from it
    /// means knowing how to reach its constructor and its members, which is the same work
    /// <c>throw</c> and <c>try</c> are waiting on.</para>
    /// </summary>
    public override void VisitModelDecl(ModelDecl node)
    {
        ArgumentNullException.ThrowIfNull(node);

        // 'Model' is the root every model has anyway, and is System.Object here, so naming it
        // changes nothing. Any other built-in parent is an exception.
        if (_model.GetSymbol(node) is ModelSymbol { BaseType: { } parent }
            && !CilTypes.IsDeclaredModel(parent)
            && !ReferenceEquals(parent, BuiltInTypes.Of("Model")))
        {
            Refuse(node, $"The model '{node.Name}', which extends '{parent.Name}'");
            return;
        }

        base.VisitModelDecl(node);
    }

    public override void VisitStructureDecl(StructureDecl node)
    {
        ArgumentNullException.ThrowIfNull(node);

        Refuse(node, $"The structure '{node.Name}'");
    }

    public override void VisitEnumerationDecl(EnumerationDecl node)
    {
        ArgumentNullException.ThrowIfNull(node);

        Refuse(node, $"The enumeration '{node.Name}'");
    }

    /// <summary>A field is refused by the type it holds, the same as a local.</summary>
    public override void VisitFieldDecl(FieldDecl node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (_model.GetSymbol(node) is FieldSymbol field)
        {
            CheckType(node, field.Type, $"The field '{node.Name}'");
        }

        base.VisitFieldDecl(node);
    }

    /// <summary>
    /// <para>A function may be emitted when it has a body and every type it names is one the
    /// emitter has a CLR type for.</para>
    /// <para>Nothing here asks whether it is shared, and that is not an omission. A function is
    /// only reached from a model this pass already accepted, and the only models it accepts are
    /// shared ones — which have no instances by definition, so a member of one has no receiver
    /// to be called on. An instance member written on a shared model is <c>PC0211</c> and has
    /// been refused long before this.</para>
    /// </summary>
    public override void VisitFunctionDecl(FunctionDecl node)
    {
        ArgumentNullException.ThrowIfNull(node);

        // An abstract function is declared and left open on purpose, and becomes an abstract
        // method — which has no body in the metadata either. Any other function without one
        // has nothing to emit.
        if (node.Body is null && !node.Modifiers.HasFlag(DeclarationModifiers.Abstract))
        {
            Refuse(node, $"The function '{node.Name}', which declares no body");
            return;
        }

        if (_model.GetSymbol(node) is FunctionSymbol function)
        {
            CheckType(node, function.ReturnType, $"The result of '{node.Name}'");

            foreach (ParameterSymbol parameter in function.Parameters)
            {
                CheckType(node, parameter.Type, $"The parameter '{parameter.Name}'");
            }
        }

        base.VisitFunctionDecl(node);
    }

    /// <summary>Refuses a type the emitter has no CLR type for, saying which it was.</summary>
    private void CheckType(SyntaxNode where, TypeSymbol? type, string what)
    {
        if (type is not null && !CilTypes.IsSupported(type))
        {
            Refuse(where, $"{what}, {type.WithArticle()}");
        }
    }

    // ---- Statements -------------------------------------------------------------------------

    public override void VisitSwitchStmt(SwitchStmt node)
    {
        ArgumentNullException.ThrowIfNull(node);

        Refuse(node, "A switch");
    }

    public override void VisitTryStmt(TryStmt node)
    {
        ArgumentNullException.ThrowIfNull(node);

        Refuse(node, "A try");
    }

    public override void VisitThrowStmt(ThrowStmt node)
    {
        ArgumentNullException.ThrowIfNull(node);

        Refuse(node, "A throw");
    }

    /// <summary>
    /// A walk is what <c>loop each</c> lowers to, so this is the only place it can be met and
    /// the name a reader would recognize is the one they wrote.
    /// </summary>
    public override void VisitWalkStmt(WalkStmt node)
    {
        ArgumentNullException.ThrowIfNull(node);

        Refuse(node, "A 'loop each'");
    }

    public override void VisitForEachStmt(ForEachStmt node)
    {
        ArgumentNullException.ThrowIfNull(node);

        Refuse(node, "A 'loop each'");
    }

    public override void VisitLocalDeclStmt(LocalDeclStmt node)
    {
        ArgumentNullException.ThrowIfNull(node);

        Refuse(node, "A function declared inside another");
    }

    /// <summary>
    /// A local is refused by the type it holds. Asked of the symbol the declaration introduced
    /// rather than of the declaration, which is a statement and has no type of its own — and
    /// answering null there let a fraction and a lambda through to the emitter, where the guard
    /// caught them.
    /// </summary>
    public override void VisitVarDeclStmt(VarDeclStmt node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (_model.GetSymbol(node) is LocalSymbol local)
        {
            CheckType(node, local.Type, $"The local '{node.Name}'");
        }

        base.VisitVarDeclStmt(node);
    }

    // ---- Expressions ------------------------------------------------------------------------

    public override void VisitLambdaExpr(LambdaExpr node)
    {
        ArgumentNullException.ThrowIfNull(node);

        Refuse(node, "A lambda");
    }

    /// <summary>
    /// A <c>new</c> may be emitted where it makes a model the program declared. One that makes
    /// something the language provides — an exception, a <c>Random</c> — is a call into the
    /// runtime the emitter does not know how to make yet.
    /// </summary>
    public override void VisitNewExpr(NewExpr node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (_model.GetType(node) is not { } type || !CilTypes.IsDeclaredModel(type))
        {
            Refuse(node, $"Constructing '{node.TypeName}'");
            return;
        }

        base.VisitNewExpr(node);
    }

    public override void VisitCollectionExpr(CollectionExpr node)
    {
        ArgumentNullException.ThrowIfNull(node);

        Refuse(node, "A set");
    }

    public override void VisitIndexExpr(IndexExpr node)
    {
        ArgumentNullException.ThrowIfNull(node);

        Refuse(node, "Indexing");
    }

    public override void VisitInterpolatedStringExpr(InterpolatedStringExpr node)
    {
        ArgumentNullException.ThrowIfNull(node);

        Refuse(node, "An interpolated string");
    }

    public override void VisitIfExpr(IfExpr node)
    {
        ArgumentNullException.ThrowIfNull(node);

        Refuse(node, "An 'if' written as an expression");
    }

    public override void VisitTypeTestExpr(TypeTestExpr node)
    {
        ArgumentNullException.ThrowIfNull(node);

        Refuse(node, "An 'is'");
    }

    public override void VisitTypeCastExpr(TypeCastExpr node)
    {
        ArgumentNullException.ThrowIfNull(node);

        Refuse(node, "An 'as'");
    }

    /// <summary>
    /// <para><c>this</c> needs nothing checked: it is the receiver, which the CLR puts in
    /// argument zero of every instance method.</para>
    /// <para>Overridden all the same rather than left to the base, so that adding a refusal here
    /// later is an edit to a method that exists.</para>
    /// </summary>
    public override void VisitReceiverExpr(ReceiverExpr node)
    {
        ArgumentNullException.ThrowIfNull(node);

        base.VisitReceiverExpr(node);
    }

    /// <summary>
    /// A member reached through something is a field the emitter knows, or a call — and a call
    /// is checked where the call is. Anything else reaching here is a member of a built-in,
    /// which the emitter has no sequence for.
    /// </summary>
    public override void VisitMemberExpr(MemberExpr node)
    {
        ArgumentNullException.ThrowIfNull(node);

        // A member of a built-in read rather than called — a set's Count, Math.Pi. It resolves
        // to no symbol of the program's, so without this it passes as though it were nothing at
        // all and reaches the emitter, which has no sequence for it and throws.
        if (_model.GetBuiltIn(node) is { } builtIn && !CilBuiltIns.IsSupported(builtIn))
        {
            Refuse(node, $"Reading {CilBuiltIns.NameOf(builtIn)}");
            return;
        }

        switch (_model.GetSymbol(node))
        {
            case FieldSymbol field:
                CheckType(node, field.Type, $"The field '{node.MemberName}'");
                break;

            // A function reached here is the callee of a call, which VisitCallExpr settles, and
            // a type is the name in front of a shared member.
            case FunctionSymbol or TypeSymbol or null:
                break;

            case { } other:
                Refuse(node, $"The {other.Kind} '{other.Name}'");
                return;
        }

        base.VisitMemberExpr(node);
    }

    public override void VisitConversionExpr(ConversionExpr node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (!CilConversions.IsSupported(node.Operation))
        {
            Refuse(node, $"A conversion to {node.Operation}");
            return;
        }

        base.VisitConversionExpr(node);
    }

    public override void VisitBinaryExpr(BinaryExpr node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (node.Operator == BinaryOperator.Power)
        {
            Refuse(node, "A '^'");
            return;
        }

        base.VisitBinaryExpr(node);
    }

    /// <summary>
    /// <para>A call may be emitted when what it calls is a shared function being emitted, or one
    /// of the built-ins the emitter knows an instruction sequence for.</para>
    /// <para>Checked here rather than left to the emitter because the answer is not local to the
    /// call: it depends on what the receiver resolved to, which is the semantic model's to say.
    /// </para>
    /// </summary>
    public override void VisitCallExpr(CallExpr node)
    {
        ArgumentNullException.ThrowIfNull(node);

        // 'base(...)' calls nothing: it names the parent's constructor, which the checker bound
        // to the call rather than to the callee, and which the emitter chains to. Whether that
        // parent can be emitted at all is settled where the model is.
        if (node.Callee is ReceiverExpr { Receiver: ReceiverKind.Base })
        {
            base.VisitCallExpr(node);
            return;
        }

        if (_model.GetBuiltIn(node.Callee) is { } builtIn)
        {
            if (!CilBuiltIns.IsSupported(builtIn))
            {
                Refuse(node, $"A call to {CilBuiltIns.NameOf(builtIn)}");
                return;
            }
        }
        else if (_model.GetSymbol(node.Callee) is not FunctionSymbol)
        {
            Refuse(node, "A call to something other than a function");
            return;
        }

        base.VisitCallExpr(node);
    }

    /// <summary>
    /// A name that reached here is a local, a parameter, or the type in front of a shared call.
    /// A field is not — nothing declares one yet — so anything else is refused by what it is.
    /// </summary>
    public override void VisitIdentifierExpr(IdentifierExpr node)
    {
        ArgumentNullException.ThrowIfNull(node);

        switch (_model.GetSymbol(node))
        {
            case LocalSymbol or ParameterSymbol or TypeSymbol or null:
                break;

            case { } other:
                Refuse(node, $"The {other.Kind} '{other.Name}'");
                break;
        }
    }
}
