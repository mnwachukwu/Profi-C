namespace ProfiC.Compiler.Ast;

/// <summary>
/// <para>A walk over the syntax tree that returns nothing.</para>
/// <para>Every method defaults to visiting the node's children, so a derived visitor
/// overrides only what it cares about. This is what the resolver and the definite-assignment
/// pass want: they are interested in a handful of node kinds and should not have to restate
/// the traversal.</para>
/// </summary>
public abstract class SyntaxVisitor
{
    /// <summary>Visits a node, dispatching to the method for its kind.</summary>
    public void Visit(SyntaxNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        node.Accept(this);
    }

    /// <summary>
    /// The default behavior of every visit method: walk the children in source order.
    /// Override this to change traversal for every node at once.
    /// </summary>
    protected virtual void DefaultVisit(SyntaxNode node)
    {
        foreach (SyntaxNode child in node.Children)
        {
            child.Accept(this);
        }
    }

    // ---- Declarations -------------------------------------------------------------------

    public virtual void VisitCompilationUnit(CompilationUnit node) => DefaultVisit(node);
    public virtual void VisitUsingDirective(UsingDirective node) => DefaultVisit(node);
    public virtual void VisitImportDirective(ImportDirective node) => DefaultVisit(node);
    public virtual void VisitQualifiedName(QualifiedName node) => DefaultVisit(node);
    public virtual void VisitNamespaceDecl(NamespaceDecl node) => DefaultVisit(node);
    public virtual void VisitModelDecl(ModelDecl node) => DefaultVisit(node);
    public virtual void VisitStructureDecl(StructureDecl node) => DefaultVisit(node);
    public virtual void VisitEnumerationDecl(EnumerationDecl node) => DefaultVisit(node);
    public virtual void VisitEnumMemberDecl(EnumMemberDecl node) => DefaultVisit(node);
    public virtual void VisitFieldDecl(FieldDecl node) => DefaultVisit(node);
    public virtual void VisitFunctionDecl(FunctionDecl node) => DefaultVisit(node);
    public virtual void VisitParameterDecl(ParameterDecl node) => DefaultVisit(node);

    // ---- Types --------------------------------------------------------------------------

    public virtual void VisitNamedType(NamedTypeSyntax node) => DefaultVisit(node);
    public virtual void VisitSetType(SetTypeSyntax node) => DefaultVisit(node);
    public virtual void VisitOptionalType(OptionalTypeSyntax node) => DefaultVisit(node);
    public virtual void VisitFunctionType(FunctionTypeSyntax node) => DefaultVisit(node);

    // ---- Statements ---------------------------------------------------------------------

    public virtual void VisitWalkStmt(WalkStmt node) => DefaultVisit(node);

    public virtual void VisitBlockStmt(BlockStmt node) => DefaultVisit(node);
    public virtual void VisitVarDeclStmt(VarDeclStmt node) => DefaultVisit(node);
    public virtual void VisitLocalDeclStmt(LocalDeclStmt node) => DefaultVisit(node);
    public virtual void VisitIfStmt(IfStmt node) => DefaultVisit(node);
    public virtual void VisitElseIfClause(ElseIfClause node) => DefaultVisit(node);
    public virtual void VisitWhileStmt(WhileStmt node) => DefaultVisit(node);

    public virtual void VisitLoopUntilStmt(LoopUntilStmt node) => DefaultVisit(node);

    public virtual void VisitLoopForeverStmt(LoopForeverStmt node) => DefaultVisit(node);
    public virtual void VisitForStmt(ForStmt node) => DefaultVisit(node);
    public virtual void VisitForEachStmt(ForEachStmt node) => DefaultVisit(node);
    public virtual void VisitSwitchStmt(SwitchStmt node) => DefaultVisit(node);
    public virtual void VisitCaseGroup(CaseGroup node) => DefaultVisit(node);
    public virtual void VisitTryStmt(TryStmt node) => DefaultVisit(node);
    public virtual void VisitCatchClause(CatchClause node) => DefaultVisit(node);
    public virtual void VisitThrowStmt(ThrowStmt node) => DefaultVisit(node);
    public virtual void VisitYieldStmt(YieldStmt node) => DefaultVisit(node);
    public virtual void VisitBreakStmt(BreakStmt node) => DefaultVisit(node);
    public virtual void VisitContinueStmt(ContinueStmt node) => DefaultVisit(node);
    public virtual void VisitExpressionStmt(ExpressionStmt node) => DefaultVisit(node);
    public virtual void VisitAssignmentStmt(AssignmentStmt node) => DefaultVisit(node);

    // ---- Expressions --------------------------------------------------------------------

    public virtual void VisitLiteralExpr(LiteralExpr node) => DefaultVisit(node);
    public virtual void VisitInterpolatedStringExpr(InterpolatedStringExpr node) => DefaultVisit(node);
    public virtual void VisitInterpolationPart(InterpolationPart node) => DefaultVisit(node);
    public virtual void VisitIdentifierExpr(IdentifierExpr node) => DefaultVisit(node);
    public virtual void VisitReceiverExpr(ReceiverExpr node) => DefaultVisit(node);
    public virtual void VisitParenthesizedExpr(ParenthesizedExpr node) => DefaultVisit(node);
    public virtual void VisitUnaryExpr(UnaryExpr node) => DefaultVisit(node);
    public virtual void VisitBinaryExpr(BinaryExpr node) => DefaultVisit(node);
    public virtual void VisitTypeTestExpr(TypeTestExpr node) => DefaultVisit(node);
    public virtual void VisitTypeCastExpr(TypeCastExpr node) => DefaultVisit(node);
    public virtual void VisitIfExpr(IfExpr node) => DefaultVisit(node);
    public virtual void VisitCollectionExpr(CollectionExpr node) => DefaultVisit(node);
    public virtual void VisitNewExpr(NewExpr node) => DefaultVisit(node);
    public virtual void VisitCallExpr(CallExpr node) => DefaultVisit(node);
    public virtual void VisitIndexExpr(IndexExpr node) => DefaultVisit(node);
    public virtual void VisitMemberExpr(MemberExpr node) => DefaultVisit(node);
    public virtual void VisitLambdaExpr(LambdaExpr node) => DefaultVisit(node);

    // ---- Parse failures -----------------------------------------------------------------

    public virtual void VisitMissingExpr(MissingExpr node) => DefaultVisit(node);
    public virtual void VisitMissingType(MissingType node) => DefaultVisit(node);

    // ---- Introduced while lowering ------------------------------------------------------

    public virtual void VisitConversionExpr(ConversionExpr node) => DefaultVisit(node);
}

/// <summary>
/// <para>A walk over the syntax tree that returns a result from each node.</para>
/// <para>The type checker wants this shape, since checking an expression produces a type.
/// There is no useful default result, so <see cref="DefaultVisit"/> is abstract and every
/// derived visitor must say what an unhandled node yields.</para>
/// </summary>
public abstract class SyntaxVisitor<TResult>
{
    /// <summary>Visits a node, dispatching to the method for its kind.</summary>
    public TResult Visit(SyntaxNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.Accept(this);
    }

    /// <summary>The result for a node the derived visitor does not handle.</summary>
    protected abstract TResult DefaultVisit(SyntaxNode node);

    // ---- Declarations -------------------------------------------------------------------

    public virtual TResult VisitCompilationUnit(CompilationUnit node) => DefaultVisit(node);
    public virtual TResult VisitUsingDirective(UsingDirective node) => DefaultVisit(node);
    public virtual TResult VisitImportDirective(ImportDirective node) => DefaultVisit(node);
    public virtual TResult VisitQualifiedName(QualifiedName node) => DefaultVisit(node);
    public virtual TResult VisitNamespaceDecl(NamespaceDecl node) => DefaultVisit(node);
    public virtual TResult VisitModelDecl(ModelDecl node) => DefaultVisit(node);
    public virtual TResult VisitStructureDecl(StructureDecl node) => DefaultVisit(node);
    public virtual TResult VisitEnumerationDecl(EnumerationDecl node) => DefaultVisit(node);
    public virtual TResult VisitEnumMemberDecl(EnumMemberDecl node) => DefaultVisit(node);
    public virtual TResult VisitFieldDecl(FieldDecl node) => DefaultVisit(node);
    public virtual TResult VisitFunctionDecl(FunctionDecl node) => DefaultVisit(node);
    public virtual TResult VisitParameterDecl(ParameterDecl node) => DefaultVisit(node);

    // ---- Types --------------------------------------------------------------------------

    public virtual TResult VisitNamedType(NamedTypeSyntax node) => DefaultVisit(node);
    public virtual TResult VisitSetType(SetTypeSyntax node) => DefaultVisit(node);
    public virtual TResult VisitOptionalType(OptionalTypeSyntax node) => DefaultVisit(node);
    public virtual TResult VisitFunctionType(FunctionTypeSyntax node) => DefaultVisit(node);

    // ---- Statements ---------------------------------------------------------------------

    public virtual TResult VisitWalkStmt(WalkStmt node) => DefaultVisit(node);

    public virtual TResult VisitBlockStmt(BlockStmt node) => DefaultVisit(node);
    public virtual TResult VisitVarDeclStmt(VarDeclStmt node) => DefaultVisit(node);
    public virtual TResult VisitLocalDeclStmt(LocalDeclStmt node) => DefaultVisit(node);
    public virtual TResult VisitIfStmt(IfStmt node) => DefaultVisit(node);
    public virtual TResult VisitElseIfClause(ElseIfClause node) => DefaultVisit(node);
    public virtual TResult VisitWhileStmt(WhileStmt node) => DefaultVisit(node);

    public virtual TResult VisitLoopUntilStmt(LoopUntilStmt node) => DefaultVisit(node);

    public virtual TResult VisitLoopForeverStmt(LoopForeverStmt node) => DefaultVisit(node);
    public virtual TResult VisitForStmt(ForStmt node) => DefaultVisit(node);
    public virtual TResult VisitForEachStmt(ForEachStmt node) => DefaultVisit(node);
    public virtual TResult VisitSwitchStmt(SwitchStmt node) => DefaultVisit(node);
    public virtual TResult VisitCaseGroup(CaseGroup node) => DefaultVisit(node);
    public virtual TResult VisitTryStmt(TryStmt node) => DefaultVisit(node);
    public virtual TResult VisitCatchClause(CatchClause node) => DefaultVisit(node);
    public virtual TResult VisitThrowStmt(ThrowStmt node) => DefaultVisit(node);
    public virtual TResult VisitYieldStmt(YieldStmt node) => DefaultVisit(node);
    public virtual TResult VisitBreakStmt(BreakStmt node) => DefaultVisit(node);
    public virtual TResult VisitContinueStmt(ContinueStmt node) => DefaultVisit(node);
    public virtual TResult VisitExpressionStmt(ExpressionStmt node) => DefaultVisit(node);
    public virtual TResult VisitAssignmentStmt(AssignmentStmt node) => DefaultVisit(node);

    // ---- Expressions --------------------------------------------------------------------

    public virtual TResult VisitLiteralExpr(LiteralExpr node) => DefaultVisit(node);
    public virtual TResult VisitInterpolatedStringExpr(InterpolatedStringExpr node) => DefaultVisit(node);
    public virtual TResult VisitInterpolationPart(InterpolationPart node) => DefaultVisit(node);
    public virtual TResult VisitIdentifierExpr(IdentifierExpr node) => DefaultVisit(node);
    public virtual TResult VisitReceiverExpr(ReceiverExpr node) => DefaultVisit(node);
    public virtual TResult VisitParenthesizedExpr(ParenthesizedExpr node) => DefaultVisit(node);
    public virtual TResult VisitUnaryExpr(UnaryExpr node) => DefaultVisit(node);
    public virtual TResult VisitBinaryExpr(BinaryExpr node) => DefaultVisit(node);
    public virtual TResult VisitTypeTestExpr(TypeTestExpr node) => DefaultVisit(node);
    public virtual TResult VisitTypeCastExpr(TypeCastExpr node) => DefaultVisit(node);
    public virtual TResult VisitIfExpr(IfExpr node) => DefaultVisit(node);
    public virtual TResult VisitCollectionExpr(CollectionExpr node) => DefaultVisit(node);
    public virtual TResult VisitNewExpr(NewExpr node) => DefaultVisit(node);
    public virtual TResult VisitCallExpr(CallExpr node) => DefaultVisit(node);
    public virtual TResult VisitIndexExpr(IndexExpr node) => DefaultVisit(node);
    public virtual TResult VisitMemberExpr(MemberExpr node) => DefaultVisit(node);
    public virtual TResult VisitLambdaExpr(LambdaExpr node) => DefaultVisit(node);

    // ---- Parse failures -----------------------------------------------------------------

    public virtual TResult VisitMissingExpr(MissingExpr node) => DefaultVisit(node);
    public virtual TResult VisitMissingType(MissingType node) => DefaultVisit(node);

    // ---- Introduced while lowering ------------------------------------------------------

    public virtual TResult VisitConversionExpr(ConversionExpr node) => DefaultVisit(node);
}
