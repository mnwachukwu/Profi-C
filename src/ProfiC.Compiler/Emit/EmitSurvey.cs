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
    /// a model the language provides that has a CLR type, or nothing.</para>
    /// <para>The middle case is the exceptions, which is how a program names its own failures.
    /// What is left is <c>Random</c> and the rest, whose parents are types in the runtime the
    /// emitter has no way to construct or call.</para>
    /// </summary>
    public override void VisitModelDecl(ModelDecl node)
    {
        ArgumentNullException.ThrowIfNull(node);

        // 'Model' is the root every model has anyway, and is System.Object here, so naming it
        // changes nothing.
        if (_model.GetSymbol(node) is ModelSymbol { BaseType: { } parent }
            && !CilTypes.IsDeclaredModel(parent)
            && CilTypes.OfBuiltInModel(parent) is null
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

    /// <summary>
    /// A <c>catch</c> is refused by the type it names, since taking a thrown value means having a
    /// CLR type for the handler. The body and the <c>try</c> itself need nothing of their own.
    /// </summary>
    public override void VisitCatchClause(CatchClause node)
    {
        ArgumentNullException.ThrowIfNull(node);

        CheckType(node, _model.GetType(node.ExceptionType), $"A catch of '{node.ExceptionType}',");

        base.VisitCatchClause(node);
    }

    /// <summary>
    /// <para>A walk is what <c>loop each</c> lowers to, so this is the only shape that reaches
    /// the emitter — a <c>ForEachStmt</c> is gone by then, and meeting one means lowering did
    /// not run.</para>
    /// <para>Nothing to refuse: what a walk needs is a set, and whether the set is one the
    /// emitter has a type for is settled where the sequence is.</para>
    /// </summary>
    public override void VisitForEachStmt(ForEachStmt node)
    {
        ArgumentNullException.ThrowIfNull(node);

        Refuse(node, "A 'loop each' that was never lowered");
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
    /// A <c>new</c> may be emitted where it makes a model the program declared, or an exception,
    /// which is a type the runtime already has. What is left — a <c>Random</c>, a
    /// <c>DateTime</c> — is a call into the runtime the emitter does not know how to make yet.
    /// </summary>
    public override void VisitNewExpr(NewExpr node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (_model.GetType(node) is not { } type
            || (!CilTypes.IsDeclaredModel(type) && CilTypes.OfBuiltInModel(type) is null))
        {
            Refuse(node, $"Constructing '{node.TypeName}'");
            return;
        }

        base.VisitNewExpr(node);
    }

    /// <summary>
    /// A literal is refused by what it holds, the same as a local — and a set with no type at all
    /// is one the checker could not settle, which it has already reported.
    /// </summary>
    public override void VisitCollectionExpr(CollectionExpr node)
    {
        ArgumentNullException.ThrowIfNull(node);

        CheckType(node, _model.GetType(node), "A set");

        base.VisitCollectionExpr(node);
    }

    /// <summary>
    /// <para>Indexing reaches a set, and a string.</para>
    /// <para>A string is refused for now: it is indexed through a different sequence entirely,
    /// since a CLR string is not the runtime's set and answers a character by another route.
    /// </para>
    /// </summary>
    public override void VisitIndexExpr(IndexExpr node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (_model.GetType(node.Receiver) is not SetType)
        {
            Refuse(node, "Indexing something that is not a set");
            return;
        }

        base.VisitIndexExpr(node);
    }

    public override void VisitInterpolatedStringExpr(InterpolatedStringExpr node)
    {
        ArgumentNullException.ThrowIfNull(node);

        Refuse(node, "An interpolated string");
    }

    /// <summary>
    /// <para>A test is refused by the type it names, since asking whether a value is one means
    /// having a CLR type to ask about.</para>
    /// <para>Nothing is asked of the operand: whatever it is, either the checker settled the
    /// answer or the value is a reference the emitter can test — and a value it could not produce
    /// at all was refused where it was written.</para>
    /// </summary>
    public override void VisitTypeTestExpr(TypeTestExpr node)
    {
        ArgumentNullException.ThrowIfNull(node);

        CheckType(node, _model.GetType(node.TargetType), "An 'is' against");

        base.VisitTypeTestExpr(node);
    }

    /// <summary>A cast is refused by the type it names, and by the optional it yields.</summary>
    public override void VisitTypeCastExpr(TypeCastExpr node)
    {
        ArgumentNullException.ThrowIfNull(node);

        CheckType(node, _model.GetType(node.TargetType), "An 'as' to");
        CheckType(node, _model.GetType(node), "The result of an 'as',");

        base.VisitTypeCastExpr(node);
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
