using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;

namespace ProfiC.Compiler.Semantics;

public sealed partial class TypeChecker
{
    /// <summary>Works out an expression's type, recording it on the way.</summary>
    private TypeSymbol CheckExpression(Expression expression)
    {
        // Narrowing is applied where the type is read rather than where it is declared, since
        // what is known about an optional depends on where in the program you are standing.
        TypeSymbol type = ApplyNarrowing(expression, CheckExpressionCore(expression));
        _model.BindType(expression, type);
        return type;
    }

    private TypeSymbol CheckExpressionCore(Expression expression) => expression switch
    {
        MissingExpr => ErrorType.Instance,
        LiteralExpr literal => TypeOfLiteral(literal),
        IdentifierExpr identifier => TypeOfIdentifier(identifier),
        ReceiverExpr receiver => _model.GetType(receiver) ?? ErrorType.Instance,
        ParenthesizedExpr parenthesized => CheckExpression(parenthesized.Inner),
        UnaryExpr unary => CheckUnary(unary),
        BinaryExpr binary => CheckBinary(binary),
        TypeTestExpr test => CheckTypeTest(test),
        TypeCastExpr cast => CheckTypeCast(cast),
        IfExpr conditional => CheckConditional(conditional),
        CollectionExpr collection => CheckCollection(collection),
        NewExpr construction => CheckNew(construction),
        CallExpr call => CheckCall(call),
        IndexExpr index => CheckIndex(index),
        MemberExpr member => CheckMember(member),
        LambdaExpr lambda => CheckLambda(lambda),
        _ => ErrorType.Instance,
    };

    private static TypeSymbol TypeOfLiteral(LiteralExpr literal) => literal.Kind switch
    {
        LiteralKind.Integer => PrimitiveType.Integer,
        LiteralKind.Real => PrimitiveType.Real,
        LiteralKind.Character => PrimitiveType.Character,
        LiteralKind.String => PrimitiveType.String,
        LiteralKind.Fraction => PrimitiveType.Fraction,
        LiteralKind.Boolean => PrimitiveType.Boolean,
        _ => ErrorType.Instance,
    };

    private TypeSymbol TypeOfIdentifier(IdentifierExpr identifier) =>
        _model.GetSymbol(identifier) switch
        {
            LocalSymbol local => local.Type,
            ParameterSymbol parameter => parameter.Type,
            FieldSymbol field => field.Type,
            EnumMemberSymbol member => member.Owner,

            // A local function used by name is a value of its own signature's type.
            FunctionSymbol function => function.AsType(),

            TypeSymbol type => type,
            _ => ErrorType.Instance,
        };

    private TypeSymbol CheckUnary(UnaryExpr unary)
    {
        TypeSymbol operand = CheckExpression(unary.Operand);

        if (operand.IsError)
        {
            return ErrorType.Instance;
        }

        switch (unary.Operator)
        {
            case UnaryOperator.Not when ReferenceEquals(operand, PrimitiveType.Boolean):
                return PrimitiveType.Boolean;

            case UnaryOperator.Negate when IsNumeric(operand):
                return operand;

            default:
                Report(
                    DiagnosticDescriptors.UnaryOperatorNotDefined,
                    unary,
                    unary.Operator.Spelling(),
                    operand.WithArticle());

                return ErrorType.Instance;
        }
    }

    private TypeSymbol CheckBinary(BinaryExpr binary)
    {
        TypeSymbol left = CheckExpression(binary.Left);
        TypeSymbol right = CheckExpression(binary.Right);

        if (left.IsError || right.IsError)
        {
            return ErrorType.Instance;
        }

        switch (binary.Operator)
        {
            case BinaryOperator.And or BinaryOperator.Or:
                RequireBoolean(left, binary.Left, "logical");
                RequireBoolean(right, binary.Right, "logical");
                return PrimitiveType.Boolean;

            case BinaryOperator.Equal or BinaryOperator.NotEqual:
                return CheckEquality(binary, left, right);

            case BinaryOperator.LessThan or BinaryOperator.GreaterThan
                or BinaryOperator.LessThanOrEqual or BinaryOperator.GreaterThanOrEqual:
                return CheckComparison(binary, left, right);

            default:
                return CheckArithmetic(binary, left, right);
        }
    }

    /// <summary>
    /// <para>Arithmetic, plus the one place a non-numeric operator lives: joining strings.</para>
    /// <para>When either side is a string, the other converts through its own rendering, so
    /// <c>"score: " + 42</c> works without asking for the conversion.</para>
    /// </summary>
    private TypeSymbol CheckArithmetic(BinaryExpr binary, TypeSymbol left, TypeSymbol right)
    {
        if (binary.Operator == BinaryOperator.Add
            && (ReferenceEquals(left, PrimitiveType.String)
                || ReferenceEquals(right, PrimitiveType.String)))
        {
            return PrimitiveType.String;
        }

        if (!IsNumeric(left) || !IsNumeric(right))
        {
            Report(
                DiagnosticDescriptors.OperatorNotDefined,
                binary,
                binary.Operator.Spelling(),
                left.WithArticle(),
                right.WithArticle());

            return ErrorType.Instance;
        }

        TypeSymbol? result = UnifyNumeric(left, right);

        if (result is null)
        {
            Report(
                DiagnosticDescriptors.OperatorNotDefined,
                binary,
                binary.Operator.Spelling(),
                left.WithArticle(),
                right.WithArticle());

            return ErrorType.Instance;
        }

        // Catching a division by an obvious zero here means the program never has to reach it.
        if (binary.Operator is BinaryOperator.Divide or BinaryOperator.Remainder
            && ConstantFolder.IsZero(ConstantFolder.TryFold(binary.Right, _model)))
        {
            Report(DiagnosticDescriptors.DivisionByZero, binary.Right);
        }

        return result;
    }

    private TypeSymbol CheckComparison(BinaryExpr binary, TypeSymbol left, TypeSymbol right)
    {
        if (IsNumeric(left) && IsNumeric(right) && UnifyNumeric(left, right) is not null)
        {
            return PrimitiveType.Boolean;
        }

        if (ReferenceEquals(left, PrimitiveType.Character)
            && ReferenceEquals(right, PrimitiveType.Character))
        {
            return PrimitiveType.Boolean;
        }

        Report(
            DiagnosticDescriptors.OperatorNotDefined,
            binary,
            binary.Operator.Spelling(),
            left.WithArticle(),
            right.WithArticle());

        return ErrorType.Instance;
    }

    /// <summary>
    /// Equality is structural and works on anything, provided the two sides could denote the
    /// same kind of thing at all.
    /// </summary>
    private TypeSymbol CheckEquality(BinaryExpr binary, TypeSymbol left, TypeSymbol right)
    {
        bool comparable = Conversions.IsAssignable(left, right)
                          || Conversions.IsAssignable(right, left);

        if (!comparable)
        {
            Report(
                DiagnosticDescriptors.OperatorNotDefined,
                binary,
                binary.Operator.Spelling(),
                left.WithArticle(),
                right.WithArticle());
        }

        return PrimitiveType.Boolean;
    }

    private TypeSymbol CheckTypeTest(TypeTestExpr test)
    {
        TypeSymbol operand = CheckExpression(test.Operand);
        TypeSymbol target = _model.GetType(test.TargetType) ?? ErrorType.Instance;

        if (!operand.IsError && !target.IsError && !CouldBe(operand, target))
        {
            Report(DiagnosticDescriptors.CannotTestOrCast, test, operand.WithArticleCapitalized(), target.WithArticle());
        }

        return PrimitiveType.Boolean;
    }

    /// <summary>
    /// <para>A cast yields an optional rather than failing.</para>
    /// <para>There is no null for it to produce instead, so an optional is the natural result
    /// — and it costs no new machinery, since optionals already exist.</para>
    /// </summary>
    private TypeSymbol CheckTypeCast(TypeCastExpr cast)
    {
        TypeSymbol operand = CheckExpression(cast.Operand);
        TypeSymbol target = _model.GetType(cast.TargetType) ?? ErrorType.Instance;

        if (target.IsError || operand.IsError)
        {
            return ErrorType.Instance;
        }

        if (target.IsValueType)
        {
            // Asking whether a value is some other value type is not a question about
            // identity or inheritance, so it has no meaning here.
            if (target is not EnumerationSymbol)
            {
                Report(DiagnosticDescriptors.CannotTestOrCast, cast, operand.WithArticleCapitalized(), target.WithArticle());
                return ErrorType.Instance;
            }
        }
        else if (!CouldBe(operand, target))
        {
            Report(DiagnosticDescriptors.CannotTestOrCast, cast, operand.WithArticleCapitalized(), target.WithArticle());
            return ErrorType.Instance;
        }

        return new OptionalType(target);
    }

    /// <summary>
    /// Whether a value of one type could turn out to be another at run time: either direction
    /// of the inheritance chain counts, since a base-typed value may hold a derived one.
    /// </summary>
    private static bool CouldBe(TypeSymbol from, TypeSymbol to) =>
        Conversions.IsAssignable(from, to)
        || Conversions.IsAssignable(to, from)
        || (from is EnumerationSymbol && ReferenceEquals(to, PrimitiveType.Integer))
        || (ReferenceEquals(from, PrimitiveType.Integer) && to is EnumerationSymbol);

    /// <summary>
    /// <para>The conditional expression, which fills the role of a ternary.</para>
    /// <para>Both branches must have the same type exactly. Finding a common type instead
    /// would mean the result of <c>if c then 1 else 2.5</c> is a real, which is not what
    /// either branch says.</para>
    /// </summary>
    private TypeSymbol CheckConditional(IfExpr conditional)
    {
        TypeSymbol condition = CheckExpression(conditional.Condition);
        RequireBoolean(condition, conditional.Condition, "conditional");

        // A conditional expression narrows its branches the same way the statement does.
        NarrowingFacts facts = AnalyzeCondition(conditional.Condition);

        TypeSymbol thenType = ErrorType.Instance;
        TypeSymbol elseType = ErrorType.Instance;

        WithNarrowing(facts.WhenTrue, () => thenType = CheckExpression(conditional.ThenValue));
        WithNarrowing(facts.WhenFalse, () => elseType = CheckExpression(conditional.ElseValue));

        if (thenType.IsError || elseType.IsError)
        {
            return ErrorType.Instance;
        }

        if (!Conversions.SameType(thenType, elseType))
        {
            Report(
                DiagnosticDescriptors.ConditionalBranchesDiffer,
                conditional,
                thenType.WithArticle(),
                elseType.WithArticle());

            return ErrorType.Instance;
        }

        return thenType;
    }

    private TypeSymbol CheckCollection(CollectionExpr collection)
    {
        if (collection.Elements.Count == 0)
        {
            // An empty literal says nothing about what it holds, so the declaration must.
            return new SetType(ErrorType.Instance);
        }

        TypeSymbol element = CheckExpression(collection.Elements[0]);

        for (int i = 1; i < collection.Elements.Count; i++)
        {
            TypeSymbol other = CheckExpression(collection.Elements[i]);

            if (element.IsError || other.IsError)
            {
                continue;
            }

            if (!Conversions.IsAssignable(other, element))
            {
                // A plain error rather than a set of errors: a set of errors is what an empty
                // literal produces, and returning one here would make the declaration report
                // that it cannot infer an element type on top of the real mistake.
                Report(
                    DiagnosticDescriptors.CollectionElementsDiffer,
                    collection.Elements[i],
                    element.WithArticle(),
                    other.WithArticle());

                return ErrorType.Instance;
            }
        }

        return new SetType(element);
    }

    private TypeSymbol CheckNew(NewExpr construction)
    {
        foreach (Expression argument in construction.Arguments)
        {
            CheckExpression(argument);
        }

        if (_model.GetSymbol(construction) is not TypeSymbol type)
        {
            return ErrorType.Instance;
        }

        if (type is ModelSymbol { IsAbstract: true } abstractModel)
        {
            Report(DiagnosticDescriptors.CannotInstantiate, construction, abstractModel.Name, "abstract");
            return type;
        }

        if (type is ModelSymbol { IsGlobal: true } globalModel)
        {
            Report(DiagnosticDescriptors.CannotInstantiate, construction, globalModel.Name, "a global model");
            return type;
        }

        return type;
    }

    private TypeSymbol CheckIndex(IndexExpr index)
    {
        TypeSymbol receiver = CheckExpression(index.Receiver);
        TypeSymbol subscript = CheckExpression(index.Index);

        if (!subscript.IsError && !ReferenceEquals(subscript, PrimitiveType.Integer))
        {
            Report(DiagnosticDescriptors.IndexMustBeInteger, index.Index, subscript.WithArticle());
        }

        return receiver switch
        {
            SetType set => set.ElementType,
            PrimitiveType p when ReferenceEquals(p, PrimitiveType.String) => PrimitiveType.Character,
            _ when receiver.IsError => ErrorType.Instance,
            _ => ReportNotIndexable(index, receiver),
        };
    }

    private TypeSymbol ReportNotIndexable(IndexExpr index, TypeSymbol receiver)
    {
        Report(DiagnosticDescriptors.NotIndexable, index, receiver.WithArticleCapitalized());
        return ErrorType.Instance;
    }

    private TypeSymbol CheckLambda(LambdaExpr lambda)
    {
        List<TypeSymbol> parameters = [];

        foreach (ParameterDecl parameter in lambda.Parameters)
        {
            parameters.Add(_model.GetType(parameter.Type) ?? ErrorType.Instance);
        }

        if (lambda.ExpressionBody is not null)
        {
            TypeSymbol result = CheckExpression(lambda.ExpressionBody);
            return new FunctionType(result, parameters);
        }

        if (lambda.Body is null)
        {
            return new FunctionType(null, parameters);
        }

        // A block-bodied lambda declares no result, so its type comes from what it yields.
        // While checking the body, "yield" must be measured against the lambda rather than
        // against the function the lambda was written inside.
        FunctionSymbol? savedFunction = _currentFunction;
        List<TypeSymbol>? savedYields = _lambdaYields;

        _currentFunction = null;
        _lambdaYields = [];

        try
        {
            CheckStatements(lambda.Body);

            TypeSymbol? inferred = UnifyYields(_lambdaYields);
            return new FunctionType(inferred, parameters);
        }
        finally
        {
            _currentFunction = savedFunction;
            _lambdaYields = savedYields;
        }
    }

    /// <summary>
    /// The single type a lambda's yields agree on, or null when it yields nothing. Where they
    /// disagree the first wins; saying so properly needs the flow analysis that follows.
    /// </summary>
    private static TypeSymbol? UnifyYields(List<TypeSymbol> yields)
    {
        if (yields.Count == 0)
        {
            return null;
        }

        TypeSymbol first = yields[0];
        return yields.All(t => t.IsError || Conversions.IsAssignable(t, first)) ? first : first;
    }

    // ---- Helpers ----------------------------------------------------------------------------

    private static bool IsNumeric(TypeSymbol type) =>
        ReferenceEquals(type, PrimitiveType.Integer)
        || ReferenceEquals(type, PrimitiveType.Real)
        || ReferenceEquals(type, PrimitiveType.Fraction);

    /// <summary>
    /// The type an arithmetic operator yields for two numbers, or null when the pair has none.
    /// A fraction and a real have no common type on purpose: neither becomes the other on its
    /// own, so mixing them must be written out.
    /// </summary>
    private static TypeSymbol? UnifyNumeric(TypeSymbol left, TypeSymbol right)
    {
        if (ReferenceEquals(left, right))
        {
            return left;
        }

        if (Conversions.Classify(left, right) == ConversionKind.Implicit)
        {
            return right;
        }

        if (Conversions.Classify(right, left) == ConversionKind.Implicit)
        {
            return left;
        }

        return null;
    }

    private void RequireBoolean(TypeSymbol type, SyntaxNode node, string what)
    {
        if (!type.IsError && !ReferenceEquals(type, PrimitiveType.Boolean))
        {
            Report(DiagnosticDescriptors.ConditionMustBeBoolean, node, what, type.WithArticle());
        }
    }
}
