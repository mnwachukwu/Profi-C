using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Text;

namespace ProfiC.Compiler.Semantics;

/// <summary>
/// <para>Rewrites a checked tree into a simpler one, so that later phases have fewer shapes
/// to handle.</para>
/// <para>Two jobs. Conversions the program did not write become real nodes, because the type
/// checker is the only pass that ever knew where they belonged. And <c>for each</c> becomes an
/// index loop, so that iteration is implemented once here rather than separately in the
/// interpreter and again in the emitter.</para>
/// <para>Anything this pass builds is registered in the same semantic model as the tree it
/// came from, so nothing downstream meets a node it knows nothing about.</para>
/// </summary>
public sealed class Lowering
{
    private readonly SemanticModel _model;
    private int _temporaries;

    private Lowering(SemanticModel model) => _model = model;

    /// <summary>Lowers every file of a checked compilation.</summary>
    public static IReadOnlyList<CompilationUnit> Lower(
        IReadOnlyList<CompilationUnit> units,
        SemanticModel model)
    {
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(model);

        Lowering lowering = new(model);

        return [.. units.Select(unit => new CompilationUnit(
            unit.Span,
            unit.Usings,
            unit.Imports,
            [.. unit.Declarations.Select(lowering.LowerDeclaration)],
            unit.Source))];
    }

    /// <summary>Lowers one file, which is a compilation of one.</summary>
    public static CompilationUnit Lower(CompilationUnit unit, SemanticModel model)
    {
        ArgumentNullException.ThrowIfNull(unit);

        return Lower([unit], model)[0];
    }

    // ---- Declarations ---------------------------------------------------------------------

    private Declaration LowerDeclaration(Declaration declaration)
    {
        switch (declaration)
        {
            case NamespaceDecl namespaceDecl:
                return Carry(declaration, new NamespaceDecl(
                    namespaceDecl.Span,
                    namespaceDecl.Name,
                    [.. namespaceDecl.Declarations.Select(LowerDeclaration)],
                    namespaceDecl.IsFileScoped));

            case ModelDecl model:
                return Carry(declaration, new ModelDecl(
                    model.Span,
                    model.Modifiers,
                    model.Name,
                    model.BaseTypeName,
                    [.. model.Members.Select(LowerDeclaration)]));

            case StructureDecl structure:
                return Carry(declaration, new StructureDecl(
                    structure.Span,
                    structure.Modifiers,
                    structure.Name,
                    [.. structure.Members.Select(LowerDeclaration)]));

            case FunctionDecl function:
                return Carry(declaration, new FunctionDecl(
                    function.Span,
                    function.Modifiers,
                    function.ReturnType,
                    function.Name,
                    function.Parameters,
                    LowerStatements(function.Body)));

            case FieldDecl field:
                return Carry(declaration, new FieldDecl(
                    field.Span,
                    field.Modifiers,
                    field.Type,
                    field.Name,
                    field.Initializer is null ? null : LowerExpression(field.Initializer)));

            default:
                return declaration;
        }
    }

    // ---- Statements -----------------------------------------------------------------------

    private List<Statement> LowerStatements(IReadOnlyList<Statement> statements) =>
        [.. statements.Select(LowerStatement)];

    private Statement LowerStatement(Statement statement)
    {
        switch (statement)
        {
            case ForEachStmt loop:
                return LowerForEach(loop);

            case BlockStmt block:
                return new BlockStmt(block.Span, LowerStatements(block.Statements));

            case VarDeclStmt declaration:
                return Carry(declaration, new VarDeclStmt(
                    declaration.Span,
                    declaration.Type,
                    declaration.Name,
                    declaration.Initializer is null ? null : LowerExpression(declaration.Initializer),
                    declaration.IsConstant));

            case LocalDeclStmt { Declaration: FunctionDecl function } local:
                return new LocalDeclStmt(local.Span, LowerDeclaration(function));

            case IfStmt branch:
                return new IfStmt(
                    branch.Span,
                    LowerExpression(branch.Condition),
                    LowerStatements(branch.ThenBody),
                    [.. branch.ElseIfClauses.Select(c => new ElseIfClause(
                        c.Span, LowerExpression(c.Condition), LowerStatements(c.Body)))],
                    branch.ElseBody is null ? null : LowerStatements(branch.ElseBody));

            case WhileStmt loop:
                return new WhileStmt(
                    loop.Span, LowerExpression(loop.Condition), LowerStatements(loop.Body));

            case ForStmt loop:
                return Carry(loop, new ForStmt(
                    loop.Span,
                    loop.VariableName,
                    LowerExpression(loop.Start),
                    LowerExpression(loop.Bound),
                    loop.IsInclusive,
                    loop.Step is null ? null : LowerExpression(loop.Step),
                    LowerStatements(loop.Body)));

            case SwitchStmt switchStmt:
                return new SwitchStmt(
                    switchStmt.Span,
                    LowerExpression(switchStmt.Subject),
                    [.. switchStmt.Cases.Select(c => new CaseGroup(
                        c.Span,
                        [.. c.Labels.Select(LowerExpression)],
                        LowerStatements(c.Body)))],
                    switchStmt.DefaultBody is null ? null : LowerStatements(switchStmt.DefaultBody));

            case TryStmt tryStmt:
                return new TryStmt(
                    tryStmt.Span,
                    LowerStatements(tryStmt.Body),
                    [.. tryStmt.Catches.Select(c => Carry(c, new CatchClause(
                        c.Span, c.ExceptionType, c.VariableName, LowerStatements(c.Body))))],
                    tryStmt.FinallyBody is null ? null : LowerStatements(tryStmt.FinallyBody));

            case ThrowStmt throwStmt:
                return new ThrowStmt(throwStmt.Span, LowerExpression(throwStmt.Exception));

            case YieldStmt yieldStmt:
                return new YieldStmt(
                    yieldStmt.Span,
                    yieldStmt.Value is null ? null : LowerExpression(yieldStmt.Value));

            case ExpressionStmt expression:
                return new ExpressionStmt(expression.Span, LowerExpression(expression.Expression));

            case AssignmentStmt assignment:
                return new AssignmentStmt(
                    assignment.Span,
                    LowerExpression(assignment.Target),
                    LowerExpression(assignment.Value));

            default:
                return statement;
        }
    }

    /// <summary>
    /// <para>Rewrites <c>for each x in sequence</c> as an index loop.</para>
    /// <para>The sequence is evaluated once into a temporary, which matters because the
    /// expression may have effects and must not run per iteration. The element is declared
    /// <em>inside</em> the loop body, which is what gives each iteration a fresh binding and
    /// removes the capture trap that a shared loop variable creates.</para>
    /// <para>A string works unchanged: it answers <c>Count()</c> and indexes to characters,
    /// exactly as a set does, which is why its members were made to mirror a set's.</para>
    /// </summary>
    private Statement LowerForEach(ForEachStmt loop)
    {
        SourceSpan span = loop.Span;

        TypeSymbol sequenceType = _model.GetType(loop.Sequence) ?? ErrorType.Instance;
        TypeSymbol elementType =
            (_model.GetSymbol(loop) as LocalSymbol)?.Type ?? ErrorType.Instance;

        // let <source> = <sequence>;
        LocalSymbol source = NewTemporary("source", sequenceType);
        VarDeclStmt sourceDecl = new(span, null, source.Name, LowerExpression(loop.Sequence), false);
        _model.Bind(sourceDecl, source);

        // for integer <index> = 0 until <source>.Count()
        LocalSymbol index = NewTemporary("index", PrimitiveType.Integer);

        IdentifierExpr sourceRef = Reference(span, source);
        MemberExpr countMember = new(span, sourceRef, "Count");
        _model.BindType(countMember, PrimitiveType.Integer);

        // Which Count this is depends on what is being iterated, and nothing later can work
        // that out: the synthesized node was never seen by the type checker.
        _model.BindBuiltIn(
            countMember,
            sequenceType is SetType ? BuiltInId.SetCount : BuiltInId.StringCount);

        CallExpr count = new(span, countMember, []);
        _model.BindType(count, PrimitiveType.Integer);

        LiteralExpr zero = new(span, LiteralKind.Integer, "0");
        _model.BindType(zero, PrimitiveType.Integer);

        // let x = <source>[<index>];
        //
        // Inferred rather than written, because "for each" never states the element type;
        // the type it infers is the one the checker already worked out.
        IndexExpr element = new(span, Reference(span, source), Reference(span, index));
        _model.BindType(element, elementType);

        VarDeclStmt elementDecl = new(span, null, loop.VariableName, element, isConstant: false);

        if (_model.GetSymbol(loop) is { } elementSymbol)
        {
            _model.Bind(elementDecl, elementSymbol);
        }

        ForStmt indexLoop = new(
            span,
            index.Name,
            zero,
            count,
            isInclusive: false,
            step: null,
            body: [elementDecl, .. LowerStatements(loop.Body)]);

        _model.Bind(indexLoop, index);

        // Both temporaries live in a block of their own, so neither escapes the loop.
        return new BlockStmt(span, [sourceDecl, indexLoop]);
    }

    // ---- Expressions ------------------------------------------------------------------------

    /// <summary>
    /// Lowers an expression, then wraps it if the type checker recorded that a conversion
    /// belongs here.
    /// </summary>
    private Expression LowerExpression(Expression expression)
    {
        Expression lowered = LowerExpressionCore(expression);

        if (_model.GetConversion(expression) is not { } needed)
        {
            return lowered;
        }

        ConversionExpr conversion = new(expression.Span, lowered, needed.Operation);

        // The conversion produces the target type, not the type the operand had.
        _model.BindType(conversion, needed.Target);

        return conversion;
    }

    private Expression LowerExpressionCore(Expression expression)
    {
        switch (expression)
        {
            case ParenthesizedExpr parenthesized:
                // Parentheses said what to do first; the tree already records that, so they
                // are dropped rather than carried into every later pass.
                return LowerExpression(parenthesized.Inner);

            case UnaryExpr unary:
                return Carry(unary, new UnaryExpr(
                    unary.Span, unary.Operator, LowerExpression(unary.Operand)));

            case BinaryExpr binary:
                return Carry(binary, new BinaryExpr(
                    binary.Span,
                    LowerExpression(binary.Left),
                    binary.Operator,
                    LowerExpression(binary.Right)));

            case TypeTestExpr test:
                return Carry(test, new TypeTestExpr(
                    test.Span, LowerExpression(test.Operand), test.TargetType));

            case TypeCastExpr cast:
                return Carry(cast, new TypeCastExpr(
                    cast.Span, LowerExpression(cast.Operand), cast.TargetType));

            case IfExpr conditional:
                return Carry(conditional, new IfExpr(
                    conditional.Span,
                    LowerExpression(conditional.Condition),
                    LowerExpression(conditional.ThenValue),
                    LowerExpression(conditional.ElseValue)));

            case CollectionExpr collection:
                return Carry(collection, new CollectionExpr(
                    collection.Span, [.. collection.Elements.Select(LowerExpression)]));

            case NewExpr construction:
                return Carry(construction, new NewExpr(
                    construction.Span,
                    construction.TypeName,
                    [.. construction.Arguments.Select(LowerExpression)]));

            case CallExpr call:
                return Carry(call, new CallExpr(
                    call.Span,
                    LowerExpression(call.Callee),
                    [.. call.Arguments.Select(LowerExpression)]));

            case IndexExpr index:
                return Carry(index, new IndexExpr(
                    index.Span, LowerExpression(index.Receiver), LowerExpression(index.Index)));

            case MemberExpr member:
                return Carry(member, new MemberExpr(
                    member.Span, LowerExpression(member.Receiver), member.MemberName));

            case LambdaExpr lambda:
                return Carry(lambda, lambda.IsExpressionBodied
                    ? LambdaExpr.Inline(lambda.Span, lambda.Parameters,
                                       LowerExpression(lambda.ExpressionBody!))
                    : LambdaExpr.Block(lambda.Span, lambda.Parameters,
                                       LowerStatements(lambda.Body!)));

            default:
                return expression;
        }
    }

    // ---- Building new nodes -------------------------------------------------------------------

    /// <summary>Copies what was known about a node onto the node that replaces it.</summary>
    private T Carry<T>(SyntaxNode original, T replacement)
        where T : SyntaxNode
    {
        if (_model.GetSymbol(original) is { } symbol)
        {
            _model.Bind(replacement, symbol);
        }

        if (_model.GetType(original) is { } type)
        {
            _model.BindType(replacement, type);
        }

        // Which member the language provides a name resolved to is a decision only the type
        // checker can make, so it travels with the node rather than being worked out again
        // from a tree that no longer carries the receiver's declared type.
        if (_model.GetBuiltIn(original) is { } builtIn)
        {
            _model.BindBuiltIn(replacement, builtIn);
        }

        // A type test the types already answered travels with the node too. The back end could
        // not work it out again: a set does not carry its element type, nor a function its
        // signature.
        if (_model.GetSettledTest(original) is { } settled)
        {
            _model.SettleTest(replacement, settled);
        }

        return replacement;
    }

    /// <summary>
    /// Creates a variable the program did not write. The name uses a character no identifier
    /// may contain, so a synthesized name can never collide with one someone chose.
    /// </summary>
    private LocalSymbol NewTemporary(string purpose, TypeSymbol type) =>
        new($"<{purpose}${_temporaries++}>", type, isConstant: false);

    private IdentifierExpr Reference(SourceSpan span, LocalSymbol local)
    {
        IdentifierExpr reference = new(span, local.Name);
        _model.Bind(reference, local);
        _model.BindType(reference, local.Type);
        return reference;
    }

    private static NamedTypeSyntax IntegerTypeSyntax(SourceSpan span) => new(span, "integer");
}
