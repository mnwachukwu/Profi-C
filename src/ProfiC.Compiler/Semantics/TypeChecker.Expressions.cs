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

    /// <summary>
    /// <para>Works out an expression's type where the surrounding code already says what is
    /// wanted.</para>
    /// <para>Two constructs are treated differently, and they are the two with nothing to be
    /// on their own. A collection literal has no type beyond what is in it, so
    /// <c>{ new Rectangle(...), new Circle(...) }</c> is nothing until something says these
    /// are shapes. A lambda whose parameters are bare names is the same case one level up:
    /// <c>(n) yield n * 2</c> says what to do with an <c>n</c> without saying what an
    /// <c>n</c> is. Everything else has a type on its own terms and is checked against what
    /// is wanted afterwards.</para>
    /// </summary>
    private TypeSymbol CheckExpressionAgainst(Expression expression, TypeSymbol expected)
    {
        switch (expression)
        {
            case CollectionExpr collection when expected is SetType set:
                return CheckCollectionAgainst(collection, set);

            case LambdaExpr lambda when TargetFor(expected) is { } wanted:
                MatchParametersToTarget(lambda, wanted);

                // What the target expects back, kept for the body. Without it, a lambda that
                // yields a lambda leaves the inner one with nothing to take its parameter
                // types from, and a reader who said what the whole thing is has to say it
                // again inside — which is the one thing writing the type was meant to avoid.
                if (wanted.ReturnType is { } result)
                {
                    _wantedResults[lambda] = result;
                }

                break;
        }

        return CheckExpression(expression);
    }

    /// <summary>
    /// <para>The function type a lambda is being written into, looking through an optional to
    /// find it.</para>
    /// <para>A lambda assigned to <c>integer function(integer)?</c> is wrapped on the way in,
    /// and what it has to be is the type underneath — so the optional says what the parameters
    /// hold just as plainly as the bare form does.</para>
    /// </summary>
    private static FunctionType? TargetFor(TypeSymbol expected) => expected switch
    {
        FunctionType function => function,
        OptionalType optional => TargetFor(optional.UnderlyingType),
        _ => null,
    };

    /// <summary>
    /// <para>Reads a lambda's parameter list against the type it is being written into: a
    /// bare name is given the type the target says it has, and a written one is reported,
    /// since the target had already fixed it.</para>
    /// <para>Both halves are the same fact stated from either side — the target settles the
    /// whole list — so they belong in one place rather than in two rules that could drift.</para>
    /// <para>A count that does not match settles nothing and is left alone. The lambda then
    /// reports the parameters it was actually given, and the mismatch is said once where the
    /// two types are compared rather than a second time per parameter.</para>
    /// </summary>
    private void MatchParametersToTarget(LambdaExpr lambda, FunctionType wanted)
    {
        if (lambda.Parameters.Count != wanted.ParameterTypes.Count)
        {
            return;
        }

        for (int index = 0; index < lambda.Parameters.Count; index++)
        {
            ParameterDecl parameter = lambda.Parameters[index];

            switch (parameter.Type)
            {
                case null when _model.GetSymbol(parameter) is ParameterSymbol symbol
                               && wanted.ParameterTypes[index] is { } type:
                    symbol.Type = type;
                    break;

                // A type that failed to parse has been reported already, and telling its
                // author it was unnecessary explains nothing about what went wrong.
                case not null and not MissingType:
                    Report(
                        DiagnosticDescriptors.ParameterTypeAlreadyKnown,
                        parameter.Type,
                        parameter.Name);
                    break;
            }
        }
    }

    /// <summary>
    /// <para>True for an argument that cannot be checked until something says what it is being
    /// written into.</para>
    /// <para>Two shapes have that property. A lambda written with a bare parameter name does not
    /// know what the name stands for; an empty set literal does not know what it would hold. Both
    /// are complete as written and neither can be given a type on its own, so both wait for the
    /// parameter they are being passed to and neither steers the choice it is waiting on.</para>
    /// </summary>
    private static bool NeedsATarget(Expression expression) => expression switch
    {
        LambdaExpr lambda => lambda.Parameters.Any(p => p.Type is null),
        CollectionExpr collection => collection.Elements.Count == 0,
        _ => false,
    };

    /// <summary>
    /// Checks every element against the element type that was asked for, rather than against
    /// whichever element happened to come first. Each element converts on its own, which is
    /// also what makes <c>integer?[] xs = {1, 2}</c> work — inference could never produce it.
    /// </summary>
    private TypeSymbol CheckCollectionAgainst(CollectionExpr collection, SetType expected)
    {
        foreach (Expression element in collection.Elements)
        {
            TypeSymbol actual = CheckExpressionAgainst(element, expected.ElementType);
            RequireAssignable(actual, expected.ElementType, element);
        }

        _model.BindType(collection, expected);
        return expected;
    }

    private TypeSymbol CheckExpressionCore(Expression expression) => expression switch
    {
        MissingExpr => ErrorType.Instance,
        LiteralExpr literal => TypeOfLiteral(literal),
        InterpolatedStringExpr interpolated => TypeOfInterpolatedString(interpolated),
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

    /// <summary>
    /// <para>An interpolated string is a string, whatever it holds.</para>
    /// <para>Every hole is checked, and nothing is asked of what it yields: joining a value to
    /// a string already accepts any type, and a hole is that same join written a shorter way.
    /// A hole that says how to format itself is the exception, since only a value that answers
    /// <c>Format</c> can be asked to.</para>
    /// </summary>
    private TypeSymbol TypeOfInterpolatedString(InterpolatedStringExpr interpolated)
    {
        foreach (InterpolationPart hole in interpolated.Holes)
        {
            TypeSymbol held = CheckExpression(hole.Value);

            if (hole.Format is null || held is ErrorType)
            {
                continue;
            }

            if (BuiltInMembers.FindAll(held, "Format").Count == 0)
            {
                Report(
                    DiagnosticDescriptors.NoFormatForThisType,
                    hole,
                    held.WithArticleCapitalized(),
                    hole.Format);
            }
        }

        return PrimitiveType.String;
    }

    private static TypeSymbol TypeOfLiteral(LiteralExpr literal) => literal.Kind switch
    {
        LiteralKind.Integer => PrimitiveType.Integer,
        LiteralKind.Real => PrimitiveType.Real,
        LiteralKind.Float => PrimitiveType.Float,
        LiteralKind.Character => PrimitiveType.Character,
        LiteralKind.String or LiteralKind.BlockString => PrimitiveType.String,
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
                RequireBoolean(left, binary.Left, "An operand of 'and' or 'or'");
                RequireBoolean(right, binary.Right, "An operand of 'and' or 'or'");
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

        // An operand with no value is a different mistake from an operand of the wrong type,
        // and is reported against the side that produced nothing.
        if (ReferenceEquals(left, PrimitiveType.Void) || ReferenceEquals(right, PrimitiveType.Void))
        {
            Report(
                DiagnosticDescriptors.ValueExpected,
                ReferenceEquals(left, PrimitiveType.Void) ? binary.Left : binary.Right);

            return ErrorType.Instance;
        }

        if (binary.Operator is BinaryOperator.BitwiseAnd or BinaryOperator.BitwiseOr
                or BinaryOperator.Xor or BinaryOperator.ShiftLeft or BinaryOperator.ShiftRight)
        {
            return CheckBitwise(binary, left, right);
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

        if (binary.Operator == BinaryOperator.Power)
        {
            return CheckPower(binary, left, right);
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
        //
        // A float is exempt, because for a float this is not a mistake. Dividing by zero yields
        // Float.Infinity, its negative, or Float.NotANumber, and those are values the type has
        // and its own arithmetic produces — refusing to write the question would leave the one
        // type with an answer as the one type unable to ask. C# draws the line in the same
        // place: 'int' and 'decimal' refuse it, 'double' answers.
        if (binary.Operator is BinaryOperator.Divide or BinaryOperator.Remainder
            && !ReferenceEquals(result, PrimitiveType.Float)
            && ConstantFolder.IsZero(ConstantFolder.TryFold(binary.Right, _model)))
        {
            Report(DiagnosticDescriptors.DivisionByZero, binary.Right);
        }

        WidenOperands(binary, left, right, result);
        return result;
    }

    /// <summary>
    /// <para>The operations that work on the bits of a whole number.</para>
    /// <para>Integers on both sides and nothing else. A real or a fraction has no bits to
    /// speak of — what it holds is a value, and how that value is stored is the runtime's
    /// business rather than the program's.</para>
    /// <para>Two booleans are told something more useful than that: <c>a != b</c> already
    /// asks whether exactly one of them holds, and reaching for <c>xor</c> instead is what a
    /// C# reader does, since <c>^</c> there covers both.</para>
    /// </summary>
    private TypeSymbol CheckBitwise(BinaryExpr binary, TypeSymbol left, TypeSymbol right)
    {
        bool shift = binary.Operator is BinaryOperator.ShiftLeft or BinaryOperator.ShiftRight;

        if (!ReferenceEquals(left, PrimitiveType.Integer)
            || !ReferenceEquals(right, PrimitiveType.Integer))
        {
            if (!shift
                && ReferenceEquals(left, PrimitiveType.Boolean)
                && ReferenceEquals(right, PrimitiveType.Boolean))
            {
                Report(DiagnosticDescriptors.BitsOnBooleans, binary, binary.Operator.Spelling());
            }
            else
            {
                Report(
                    DiagnosticDescriptors.OperatorNotDefined,
                    binary,
                    binary.Operator.Spelling(),
                    left.WithArticle(),
                    right.WithArticle());
            }

            return ErrorType.Instance;
        }

        // An amount the program wrote down can be judged now, so it never has to be reached.
        if (shift
            && ConstantFolder.TryFold(binary.Right, _model) is long amount
            && amount is < 0 or > 63)
        {
            Report(DiagnosticDescriptors.ShiftOutsideTheWidth, binary.Right, amount);
        }

        return PrimitiveType.Integer;
    }

    /// <summary>
    /// <para>Raising to a power, which is the one arithmetic operator whose two sides are not
    /// the same kind of thing.</para>
    /// <para>Everywhere else the operands unify: adding an integer to a real makes both real.
    /// Here the exponent counts how many times the base is multiplied, so it stands on its own.
    /// That is what lets a fraction keep being exact — <c>(1|2) ^ 3</c> is <c>1|8</c> — while
    /// <c>(1|2) ^ (1|2)</c> has no rational answer at all and is rejected.</para>
    /// </summary>
    private TypeSymbol CheckPower(BinaryExpr binary, TypeSymbol left, TypeSymbol right)
    {
        // A whole exponent counts multiplications, so the base's own type survives it.
        if (ReferenceEquals(right, PrimitiveType.Integer))
        {
            if (ReferenceEquals(left, PrimitiveType.Fraction))
            {
                return PrimitiveType.Fraction;
            }

            // Integers stay integers, so "2 ^ 10" is 1024 rather than 1024 as a real. A
            // negative exponent has no whole answer, caught here where it can be seen.
            if (ReferenceEquals(left, PrimitiveType.Integer))
            {
                if (ConstantFolder.TryFold(binary.Right, _model) is long folded && folded < 0)
                {
                    Report(DiagnosticDescriptors.NegativeIntegerExponent, binary.Right, folded);
                }

                return PrimitiveType.Integer;
            }
        }

        // Otherwise the answer is a root, or involves a real, and is a real either way. A
        // fraction exponent is admitted here even though a fraction never widens to a real
        // elsewhere: that rule protects exactness which could have been kept, and a root has
        // none to keep. "2 ^ (1|3)" is simply a truer way of writing "2 ^ (1.0/3.0)".
        RecordConversion(binary.Left, left, PrimitiveType.Real);
        RecordConversion(binary.Right, right, PrimitiveType.Real);
        return PrimitiveType.Real;
    }

    /// <summary>
    /// <para>Records the widening each side of a mixed-numeric operator needs.</para>
    /// <para>Without this the operator's <em>type</em> is right while its operands are not, and
    /// nothing downstream can tell: the type checker is the only pass that knows one side was
    /// narrower than the answer. Adding a fraction to an integer would then reach the back end
    /// as a fraction and an integer, with no instruction to reconcile them.</para>
    /// </summary>
    private void WidenOperands(BinaryExpr binary, TypeSymbol left, TypeSymbol right, TypeSymbol unified)
    {
        if (!Conversions.SameType(left, unified))
        {
            RecordConversion(binary.Left, left, unified);
        }

        if (!Conversions.SameType(right, unified))
        {
            RecordConversion(binary.Right, right, unified);
        }
    }

    private TypeSymbol CheckComparison(BinaryExpr binary, TypeSymbol left, TypeSymbol right)
    {
        if (IsNumeric(left) && IsNumeric(right) && UnifyNumeric(left, right) is { } unified)
        {
            // Comparing across numeric types compares them as the wider one, so the same
            // widening an arithmetic operator needs applies here.
            WidenOperands(binary, left, right, unified);
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

            return PrimitiveType.Boolean;
        }

        // Two numbers of different types are equal when they denote the same value, so the
        // narrower one widens first. Everything else is compared structurally as it stands.
        if (IsNumeric(left) && IsNumeric(right) && UnifyNumeric(left, right) is { } unified)
        {
            WidenOperands(binary, left, right, unified);
        }

        return PrimitiveType.Boolean;
    }

    private TypeSymbol CheckTypeTest(TypeTestExpr test)
    {
        TypeSymbol operand = CheckExpression(test.Operand);
        TypeSymbol target = _model.GetType(test.TargetType) ?? ErrorType.Instance;

        if (!operand.IsError && !target.IsError)
        {
            SettleIfTheTypesDecide(test, operand, target);
        }

        return PrimitiveType.Boolean;
    }

    /// <summary>
    /// <para>Writes down a type test's answer when the types alone give one.</para>
    /// <para>A value that could never be the named type gives false; one that always is gives
    /// true. Anything in between is a real question about the value — a base-typed name holding
    /// a derived value, or an optional that may be empty — and is left to run.</para>
    /// </summary>
    private void SettleIfTheTypesDecide(Expression test, TypeSymbol operand, TypeSymbol target)
    {
        if (!CouldBe(operand, target))
        {
            Report(
                DiagnosticDescriptors.TypeTestIsAlwaysFalse,
                test,
                operand.WithArticleCapitalized(),
                target.WithArticle());

            _model.SettleTest(test, false);
            return;
        }

        if (Conversions.IsAssignable(operand, target))
        {
            Report(
                DiagnosticDescriptors.TypeTestIsAlwaysTrue,
                test,
                operand.WithArticleCapitalized(),
                target.WithArticle());

            _model.SettleTest(test, true);
        }
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

        // Asking whether a value is some other value type is not a question about identity or
        // inheritance, so there is no answer to settle on. An enumeration is exempt: an integer
        // names one of its members, which is a real question with a real answer.
        if (target.IsValueType && target is not EnumerationSymbol)
        {
            Report(
                DiagnosticDescriptors.CannotCastToValueType,
                cast,
                target.WithArticleCapitalized());

            return ErrorType.Instance;
        }

        SettleIfTheTypesDecide(cast, operand, target);

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
        RequireBoolean(condition, conditional.Condition, "An if expression's condition");

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
        List<TypeSymbol> arguments = [.. construction.Arguments.Select(CheckArgument)];

        if (_model.GetSymbol(construction) is not TypeSymbol type)
        {
            return ErrorType.Instance;
        }

        if (type is ModelSymbol { IsAbstract: true } abstractModel)
        {
            Report(DiagnosticDescriptors.CannotInstantiate, construction, abstractModel.Name, "abstract");
            return type;
        }

        if (type is ModelSymbol { IsShared: true } sharedModel)
        {
            Report(DiagnosticDescriptors.CannotInstantiate, construction, sharedModel.Name, "a shared model");
            return type;
        }

        if (type is DeclaredTypeSymbol declared)
        {
            CheckConstructorArguments(construction, declared, arguments);
        }

        return type;
    }

    /// <summary>
    /// <para>Chooses the constructor a <c>new</c> runs, and checks what it was given against
    /// what that constructor takes.</para>
    /// <para>A type declaring none takes nothing, so only an empty <c>new</c> fits it.
    /// Unchecked, any arguments at all would be written and dropped, which reaches as far as a
    /// string sitting in a field declared to hold an integer.</para>
    /// </summary>
    private void CheckConstructorArguments(
        NewExpr construction,
        DeclaredTypeSymbol type,
        List<TypeSymbol> arguments)
    {
        // A type the language owns declares nothing a program can read, so its forms of "new"
        // are listed in the catalog instead and chosen the same way any other overload is.
        // One that lists none cannot be constructed at all, and is said so here rather than
        // falling through to the rule below, which would let "new Math()" pass for producing
        // nothing.
        // An exception is left to the rule below, which lets every one of them take the
        // message they all carry without each having to list a form of its own.
        if (BuiltIns.FindModel(type.Name) is { } named
            && !named.MayBeConstructed
            && !BuiltInMembers.IsException(type))
        {
            Report(
                DiagnosticDescriptors.CannotInstantiate,
                construction,
                type.Name,
                "a type the language provides");

            return;
        }

        if (BuiltIns.FindModel(type.Name) is { MayBeConstructed: true } builtIn)
        {
            BuiltInMember? form =
                builtIn.Constructors.FirstOrDefault(c => AcceptsExactly(c, arguments))
                ?? builtIn.Constructors.FirstOrDefault(c => Accepts(c, arguments));

            if (form is null)
            {
                // A form taking this many arguments but refusing these ones is a mistake about
                // a type, not about a count, so it is checked against and reports as one. That
                // is the difference between being told 'DateTime takes 3 arguments' and being
                // told which argument is wrong — and with five forms to choose from, naming an
                // arbitrary one of them says nothing.
                // Where several forms take this many, the one that got closest is the one to
                // report against: it is almost always the form the writer meant.
                BuiltInMember? sameCount = builtIn.Constructors
                    .Where(c => c.ParameterTypes.Count == arguments.Count)
                    .OrderByDescending(c => c.ParameterTypes
                        .Where((p, i) => p is not null && Conversions.IsAssignable(arguments[i], p))
                        .Count())
                    .FirstOrDefault();

                if (sameCount is not null)
                {
                    CheckArgumentsAgainst(
                        construction, construction.Arguments, type.Name,
                        sameCount.ParameterTypes, arguments);

                    return;
                }

                Report(
                    DiagnosticDescriptors.WrongArgumentCount,
                    construction,
                    type.Name,
                    Wording.Either([.. builtIn.Constructors
                        .Select(c => c.ParameterTypes.Count)
                        .Distinct()
                        .OrderBy(n => n)
                        .Select(n => Wording.Count(n, "argument"))]),
                    arguments.Count);

                return;
            }

            RecordBuiltIn(construction, form);
            CheckArgumentsAgainst(
                construction, construction.Arguments, type.Name, form.ParameterTypes, arguments);

            return;
        }

        List<FunctionSymbol> constructors =
            [.. type.Lookup(type.Name).OfType<FunctionSymbol>().Where(f => f.IsConstructor)];

        if (constructors.Count == 0)
        {
            // An exception declares nothing a program can see, but it does take the message
            // every exception carries. That one form is allowed through, as it is for base(…).
            if (BuiltInMembers.IsException(type)
                && arguments is [{ } only]
                && Conversions.IsAssignable(only, PrimitiveType.String))
            {
                return;
            }

            if (arguments.Count > 0)
            {
                Report(
                    DiagnosticDescriptors.WrongArgumentCount,
                    construction,
                    type.Name,
                    Wording.Count(0, "argument"),
                    arguments.Count);
            }

            return;
        }

        if (ResolveOverload(
                construction, construction.Arguments, type.Name, constructors, arguments) is { } chosen)
        {
            // A constructor is reached like any other member. One that is private is how a type
            // says it makes its own instances, and nothing would say so if this were skipped.
            RequireVisible(construction, chosen, type.Name);
            _model.Bind(construction, chosen);
        }
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

    /// <summary>
    /// <para>The type a bare parameter name was given by the surrounding code.</para>
    /// <para>The resolver leaves one as the error type, and <see cref="MatchParametersToTarget"/>
    /// replaces it once a target is known. Still finding the error type here means no target
    /// ever arrived, which is the one case the writer has to hear about.</para>
    /// </summary>
    private TypeSymbol InferredParameterType(ParameterDecl parameter)
    {
        TypeSymbol type = _model.GetSymbol(parameter) is ParameterSymbol symbol
            ? symbol.Type
            : ErrorType.Instance;

        if (type.IsError)
        {
            Report(DiagnosticDescriptors.ParameterTypeNotInferable, parameter, parameter.Name);
        }

        return type;
    }

    private TypeSymbol CheckLambda(LambdaExpr lambda)
    {
        List<TypeSymbol> parameters = [];

        foreach (ParameterDecl parameter in lambda.Parameters)
        {
            parameters.Add(parameter.Type is null
                ? InferredParameterType(parameter)
                : _model.GetType(parameter.Type) ?? ErrorType.Instance);
        }

        if (lambda.ExpressionBody is not null)
        {
            // Checked against what the surrounding type wants back, where it said. That is
            // what carries a target through a chain of them, so 'integer delegate(integer)
            // delegate(integer)' says what both lambdas hold rather than only the outer one.
            TypeSymbol result = _wantedResults.TryGetValue(lambda, out TypeSymbol? wanted)
                ? CheckExpressionAgainst(lambda.ExpressionBody, wanted)
                : CheckExpression(lambda.ExpressionBody);

            // A lambda whose expression produces no value yields nothing, which a function type
            // spells as a null result rather than as a result of the void type. Both say the
            // same thing, and a type carrying one never matches a type carrying the other.
            return new FunctionType(
                ReferenceEquals(result, PrimitiveType.Void) ? null : result,
                parameters);
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
        HashSet<Symbol> savedNarrowing = Known();

        _currentFunction = null;
        _lambdaYields = [];

        // Nothing proven outside reaches inside, for the same reason it does not reach into a
        // nested function: a lambda is written in one place and called in another.
        _narrowed.Clear();

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
            KnowOnly(savedNarrowing);
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
        || ReferenceEquals(type, PrimitiveType.Float)
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
