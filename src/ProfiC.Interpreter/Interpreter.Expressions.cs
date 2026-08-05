using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Semantics;
using ProfiC.Runtime;

namespace ProfiC.Interpreter;

public sealed partial class Interpreter
{
    private object? Evaluate(Expression expression, Environment scope, Instance? receiver) =>
        expression switch
        {
            LiteralExpr literal => LiteralDecoder.Decode(literal),
            IdentifierExpr identifier => EvaluateIdentifier(identifier, scope, receiver),
            ReceiverExpr => receiver,
            ConversionExpr conversion => EvaluateConversion(conversion, scope, receiver),
            UnaryExpr unary => EvaluateUnary(unary, scope, receiver),
            BinaryExpr binary => EvaluateBinary(binary, scope, receiver),
            TypeTestExpr test => EvaluateTypeTest(test, scope, receiver),
            TypeCastExpr cast => EvaluateTypeCast(cast, scope, receiver),
            IfExpr conditional => IsTrue(Evaluate(conditional.Condition, scope, receiver))
                ? Evaluate(conditional.ThenValue, scope, receiver)
                : Evaluate(conditional.ElseValue, scope, receiver),
            CollectionExpr collection => EvaluateCollection(collection, scope, receiver),
            NewExpr construction => EvaluateNew(construction, scope, receiver),
            CallExpr call => EvaluateCall(call, scope, receiver),
            IndexExpr index => EvaluateIndex(index, scope, receiver),
            MemberExpr member => EvaluateMember(member, scope, receiver),
            LambdaExpr lambda => new FunctionValue(
                lambda.Parameters, lambda.Body, lambda.ExpressionBody, scope, receiver),
            ParenthesizedExpr parenthesized => Evaluate(parenthesized.Inner, scope, receiver),
            _ => null,
        };

    private object? EvaluateIdentifier(
        IdentifierExpr identifier,
        Environment scope,
        Instance? receiver)
    {
        if (_model.GetSymbol(identifier) is not { } symbol)
        {
            return null;
        }

        if (scope.Lookup(symbol) is { } cell)
        {
            return cell.Value;
        }

        if (_shared.Lookup(symbol) is { } cellOnType)
        {
            return cellOnType.Value;
        }

        // A field reached without a receiver only happens inside the type that declares it.
        if (symbol is FieldSymbol field && receiver is not null
            && receiver.Fields.TryGetValue(field, out object? value))
        {
            return value;
        }

        return null;
    }

    /// <summary>
    /// <para>Performs a conversion the program did not write.</para>
    /// <para>Every one of these was decided while type checking and written into the tree, so
    /// nothing here has to work out which is needed — only carry it out.</para>
    /// </summary>
    private object? EvaluateConversion(
        ConversionExpr conversion,
        Environment scope,
        Instance? receiver)
    {
        object? value = Evaluate(conversion.Operand, scope, receiver);

        return conversion.Operation switch
        {
            ConversionOperation.IntegerToReal => (decimal)AsInteger(value),
            ConversionOperation.IntegerToFraction => Fraction.FromInteger(AsInteger(value)),
            ConversionOperation.RealToFraction => value is decimal r ? Fraction.FromReal(r) : value,
            ConversionOperation.FractionToReal => value is Fraction f ? f.ToReal() : value,
            ConversionOperation.WrapOptional => value,

            // Absence is carried across rather than converted. An optional holds nothing when
            // it is empty, so there is nothing to turn into characters, and an empty set is a
            // different answer from no set at all.
            ConversionOperation.StringToCharacters => value is string text
                ? new ProfiCSet<object?>(text.Select(c => (object?)c))
                : null,

            ConversionOperation.CharactersToString =>
                value is ProfiCSet<object?> ? CharactersToString(value) : null,
            _ => value,
        };
    }

    private static string CharactersToString(object? value) =>
        value is ProfiCSet<object?> set
            ? new string([.. set.Select(c => c is char character ? character : '\0')])
            : string.Empty;

    private object? EvaluateUnary(UnaryExpr unary, Environment scope, Instance? receiver)
    {
        object? operand = Evaluate(unary.Operand, scope, receiver);

        return (unary.Operator, operand) switch
        {
            (UnaryOperator.Not, bool flag) => !flag,
            (UnaryOperator.Negate, long number) => -number,
            (UnaryOperator.Negate, decimal number) => -number,
            (UnaryOperator.Negate, double number) => -number,
            (UnaryOperator.Negate, Fraction fraction) => -fraction,
            _ => null,
        };
    }

    private object? EvaluateBinary(BinaryExpr binary, Environment scope, Instance? receiver)
    {
        // Both boolean operators stop early, which is what lets a check guard the thing
        // after it.
        if (binary.Operator == BinaryOperator.And)
        {
            return IsTrue(Evaluate(binary.Left, scope, receiver))
                   && IsTrue(Evaluate(binary.Right, scope, receiver));
        }

        if (binary.Operator == BinaryOperator.Or)
        {
            return IsTrue(Evaluate(binary.Left, scope, receiver))
                   || IsTrue(Evaluate(binary.Right, scope, receiver));
        }

        object? left = Evaluate(binary.Left, scope, receiver);
        object? right = Evaluate(binary.Right, scope, receiver);

        switch (binary.Operator)
        {
            case BinaryOperator.Equal:
                return DeepEquality.Equals(left, right);

            case BinaryOperator.NotEqual:
                return !DeepEquality.Equals(left, right);
        }

        // Joining to a string renders the other side, whichever side that is.
        if (binary.Operator == BinaryOperator.Add && (left is string || right is string))
        {
            return ModelOperations.ToDisplayString(left) + ModelOperations.ToDisplayString(right);
        }

        // Raising to a power is the one operator whose sides may differ in type: the exponent
        // is a count, not a second value of the base's kind, so it is handled before the
        // matching-pair dispatch below could reject it.
        if (binary.Operator == BinaryOperator.Power)
        {
            return Power(left, right);
        }

        return (left, right) switch
        {
            (long a, long b) => IntegerOperation(binary.Operator, a, b),
            (decimal a, decimal b) => RealOperation(binary.Operator, a, b),
            (double a, double b) => FloatOperation(binary.Operator, a, b),
            (Fraction a, Fraction b) => FractionOperation(binary.Operator, a, b),
            (char a, char b) => CharacterOperation(binary.Operator, a, b),
            _ => null,
        };
    }

    /// <summary>
    /// <para>Raises a value to a power. The type checker has already settled which of these
    /// three shapes can arrive.</para>
    /// <para>Integers stay integers, so <c>2 ^ 10</c> is 1024 rather than 1024 rendered as a
    /// real. A fraction stays exact.</para>
    /// </summary>
    private static object? Power(object? left, object? right) => (left, right) switch
    {
        (Fraction b, long e) => Fraction.Pow(b, e),
        (long b, long e) => ProfiCArithmetic.Power(b, e),
        (decimal b, decimal e) => ProfiCMath.Pow(b, e),
        (double b, double e) => ProfiCMath.Pow(b, e),
        _ => null,
    };

    /// <summary>
    /// <para>Integer arithmetic.</para>
    /// <para>The seven that mean something other than what the CLR instruction of the same name
    /// means are <see cref="ProfiCArithmetic"/>'s, so that an emitted program answers them the same
    /// way — an overflow stops, a division by zero is refused in the language's words, and a shift
    /// past the width is refused rather than folded. The rest mean exactly what the instruction
    /// means and are written here.</para>
    /// </summary>
    private static object? IntegerOperation(BinaryOperator op, long a, long b) => op switch
    {
        BinaryOperator.Add => ProfiCArithmetic.Add(a, b),
        BinaryOperator.Subtract => ProfiCArithmetic.Subtract(a, b),
        BinaryOperator.Multiply => ProfiCArithmetic.Multiply(a, b),
        BinaryOperator.Divide => ProfiCArithmetic.Divide(a, b),
        BinaryOperator.Remainder => ProfiCArithmetic.Remainder(a, b),
        BinaryOperator.ShiftLeft => ProfiCArithmetic.ShiftLeft(a, b),
        BinaryOperator.ShiftRight => ProfiCArithmetic.ShiftRight(a, b),

        BinaryOperator.LessThan => a < b,
        BinaryOperator.GreaterThan => a > b,
        BinaryOperator.LessThanOrEqual => a <= b,
        BinaryOperator.GreaterThanOrEqual => a >= b,

        BinaryOperator.BitwiseAnd => a & b,
        BinaryOperator.BitwiseOr => a | b,
        BinaryOperator.Xor => a ^ b,

        _ => null,
    };

    /// <summary>
    /// Real arithmetic, which counts in tens. The five that can fail are
    /// <see cref="ProfiCArithmetic"/>'s, so that an emitted program stops in the same places and
    /// says the same thing; the comparisons mean what they say and are written here.
    /// </summary>
    private static object? RealOperation(BinaryOperator op, decimal a, decimal b) => op switch
    {
        BinaryOperator.Add => ProfiCArithmetic.Add(a, b),
        BinaryOperator.Subtract => ProfiCArithmetic.Subtract(a, b),
        BinaryOperator.Multiply => ProfiCArithmetic.Multiply(a, b),
        BinaryOperator.Divide => ProfiCArithmetic.Divide(a, b),
        BinaryOperator.Remainder => ProfiCArithmetic.Remainder(a, b),
        BinaryOperator.LessThan => a < b,
        BinaryOperator.GreaterThan => a > b,
        BinaryOperator.LessThanOrEqual => a <= b,
        BinaryOperator.GreaterThanOrEqual => a >= b,
        _ => null,
    };

    /// <summary>
    /// <para>Float arithmetic, which is binary floating point and stops at nothing.</para>
    /// <para>Dividing by zero gives an infinity here rather than raising, and a comparison against
    /// a value that is not a number is false however it is written — both are what the type is for
    /// and neither is guarded.</para>
    /// </summary>
    private static object? FloatOperation(BinaryOperator op, double a, double b) => op switch
    {
        BinaryOperator.Add => a + b,
        BinaryOperator.Subtract => a - b,
        BinaryOperator.Multiply => a * b,
        BinaryOperator.Divide => a / b,
        BinaryOperator.Remainder => a % b,
        BinaryOperator.LessThan => a < b,
        BinaryOperator.GreaterThan => a > b,
        BinaryOperator.LessThanOrEqual => a <= b,
        BinaryOperator.GreaterThanOrEqual => a >= b,
        _ => null,
    };

    private static object? FractionOperation(BinaryOperator op, Fraction a, Fraction b) => op switch
    {
        BinaryOperator.Add => a + b,
        BinaryOperator.Subtract => a - b,
        BinaryOperator.Multiply => a * b,
        BinaryOperator.Divide => a / b,
        BinaryOperator.Remainder => a % b,
        BinaryOperator.LessThan => a < b,
        BinaryOperator.GreaterThan => a > b,
        BinaryOperator.LessThanOrEqual => a <= b,
        BinaryOperator.GreaterThanOrEqual => a >= b,
        _ => null,
    };

    private static object? CharacterOperation(BinaryOperator op, char a, char b) => op switch
    {
        BinaryOperator.LessThan => a < b,
        BinaryOperator.GreaterThan => a > b,
        BinaryOperator.LessThanOrEqual => a <= b,
        BinaryOperator.GreaterThanOrEqual => a >= b,
        _ => null,
    };

    private object? EvaluateTypeTest(TypeTestExpr test, Environment scope, Instance? receiver)
    {
        object? value = Evaluate(test.Operand, scope, receiver);

        // The types settled some tests while compiling. Those are not asked again here, because
        // the value cannot answer them: a set carries no element type, a function no signature.
        if (_model.GetSettledTest(test) is { } settled)
        {
            return settled;
        }

        return _model.GetType(test.TargetType) is { } target && IsOfType(value, target);
    }

    /// <summary>
    /// <para>A cast yields an optional rather than failing, so a mismatch simply produces
    /// nothing. There is no null for it to give back instead.</para>
    /// <para>An integer against an enumeration is the one cast that produces a different value
    /// rather than the same one seen as another type: the ordinal names a member, and the
    /// member is what comes back.</para>
    /// </summary>
    private object? EvaluateTypeCast(TypeCastExpr cast, Environment scope, Instance? receiver)
    {
        object? value = Evaluate(cast.Operand, scope, receiver);

        // Settled while compiling, for the same reason a test is: one that always succeeds
        // gives the value back, one that never does gives nothing.
        if (_model.GetSettledTest(cast) is { } settled)
        {
            return settled ? value : null;
        }

        if (_model.GetType(cast.TargetType) is not { } target)
        {
            return null;
        }

        if (target is EnumerationSymbol enumeration && value is long ordinal)
        {
            return MemberWithOrdinal(enumeration, ordinal);
        }

        return IsOfType(value, target) ? value : null;
    }

    /// <summary>
    /// The member of an enumeration holding an ordinal, or nothing when none does. An ordinal
    /// with no member behind it is the whole reason a cast to an enumeration is optional.
    /// </summary>
    private static EnumValue? MemberWithOrdinal(EnumerationSymbol enumeration, long ordinal)
    {
        foreach (Symbol member in enumeration.Members.Values.SelectMany(group => group))
        {
            if (member is EnumMemberSymbol { } declared && declared.Value == ordinal)
            {
                return new EnumValue(enumeration.Name, declared.Name, declared.Value);
            }
        }

        return null;
    }

    /// <summary>Whether a value's runtime type is, or descends from, the named one.</summary>
    private static bool IsOfType(object? value, TypeSymbol target) => value switch
    {
        null => false,
        Instance instance => instance.Type is ModelSymbol model && target is ModelSymbol targetModel
            ? model.SelfAndAncestors().Contains(targetModel)
            : ReferenceEquals(instance.Type, target),

        // An exception the language provides is a real .NET exception rather than an Instance,
        // so its inheritance is the runtime's rather than a chain of symbols. The two catalogs
        // are one list, which is what makes the name written here and the object in hand
        // answerable against each other at all.
        Exception thrown => target is ModelSymbol targetException
                            && BuiltInExceptions.Resolve(targetException.Name) is { } runtimeType
                            && runtimeType.IsInstanceOfType(thrown),

        // An enumeration member carries the name of the enumeration it came from, since a
        // value crossing into the runtime keeps no symbol.
        EnumValue member => target is EnumerationSymbol enumeration
                            && string.Equals(enumeration.Name, member.TypeName, StringComparison.Ordinal),

        long => ReferenceEquals(target, PrimitiveType.Integer),
        decimal => ReferenceEquals(target, PrimitiveType.Real),
        double => ReferenceEquals(target, PrimitiveType.Float),
        bool => ReferenceEquals(target, PrimitiveType.Boolean),
        char => ReferenceEquals(target, PrimitiveType.Character),
        string => ReferenceEquals(target, PrimitiveType.String),
        Fraction => ReferenceEquals(target, PrimitiveType.Fraction),
        _ => false,
    };

    private object? EvaluateCollection(
        CollectionExpr collection,
        Environment scope,
        Instance? receiver) =>
        new ProfiCSet<object?>(
            collection.Elements.Select(e => CopyIfValue(Evaluate(e, scope, receiver))));

    private object? EvaluateNew(NewExpr construction, Environment scope, Instance? receiver)
    {
        // A type the language owns, whose forms of "new" the checker already chose among.
        if (_model.GetBuiltIn(construction) is { } built)
        {
            return Perform(
                built,
                target: null,
                [.. construction.Arguments.Select(a => Evaluate(a, scope, receiver))]).Value;
        }

        // Read from what the resolver settled rather than looked up by the name as written.
        // The two agree for a bare name and part company for a qualified one, where the text
        // is "Shapes.Circle" and no type is called that: the type is the one the resolver
        // reached by reading that name from where it was written, which it already recorded.
        //
        // Taken from the type it denotes rather than the symbol it refers to, because the
        // checker rebinds the latter to whichever constructor the arguments chose.
        // A type the language owns is built by the runtime rather than as an instance of a
        // declared model: an Exception is a real one, so that catching it and letting it reach
        // the top both work. A model extending one is declared, and is not this.
        if (_model.GetType(construction) is not DeclaredTypeSymbol type
            || ReferenceEquals(type.Container, BuiltInTypes.Standard))
        {
            return BuildBuiltInException(construction, scope, receiver);
        }

        Instance instance = new(type) { Renderer = RendererFor(type) };

        // Fields start at their initializers, which run before any constructor body.
        InitializeFields(instance, type);

        List<object?> arguments =
            [.. construction.Arguments.Select(a => Evaluate(a, scope, receiver))];

        if (FindConstructor(type, arguments.Count) is { } constructor
            && BodyOf(constructor) is { } body)
        {
            RunConstructor(body, instance, arguments, type);
        }
        else
        {
            // A model that wrote no constructor has none to run, and still has a parent to
            // build. Without this the parent's constructor is skipped for exactly the models
            // that added nothing of their own, which is the least likely place to look.
            BuildTheParent(instance, type);
        }

        return instance;
    }

    /// <summary>
    /// <para>Runs field initializers, walking from the type outward so a child's run first.</para>
    /// <para>Nearest first, which is C#'s order and is the order the emitter has to produce: a
    /// constructor's first act is reaching its parent's, so everything the child wrote as a
    /// starting value has already run by the time the parent's constructor body does. Nothing
    /// can observe which of the two sets ran first except a side effect, and a reader who
    /// learned one order here would have to unlearn it.</para>
    /// </summary>
    private void InitializeFields(Instance instance, DeclaredTypeSymbol type)
    {
        IEnumerable<DeclaredTypeSymbol> chain = type is ModelSymbol model
            ? model.SelfAndAncestors()
            : [type];

        foreach (DeclaredTypeSymbol current in chain)
        {
            foreach (List<Symbol> group in current.Members.Values)
            {
                foreach (Symbol member in group)
                {
                    if (member is not FieldSymbol { IsShared: false } field)
                    {
                        continue;
                    }

                    instance.Fields[field] = _initializers.TryGetValue(field, out Expression? start)
                        ? Evaluate(start, _shared, instance)
                        : DefaultFor(field.Type);
                }
            }
        }
    }

    private static FunctionSymbol? FindConstructor(DeclaredTypeSymbol type, int arity) =>
        type.Lookup(type.Name)
            .OfType<FunctionSymbol>()
            .FirstOrDefault(f => f.IsConstructor && f.Parameters.Count == arity);

    /// <summary>
    /// <para>Runs one constructor body, remembering whose it is.</para>
    /// <para><paramref name="declaring"/> is what <c>base(...)</c> inside the body means by
    /// "the parent". It cannot be read off the instance: the instance keeps the type it was
    /// made as the whole way up the chain, so a body asking the instance for its parent gets
    /// the same answer at every level and calls the same constructor forever.</para>
    /// </summary>
    private void RunConstructor(
        FunctionDecl constructor,
        Instance instance,
        IReadOnlyList<object?> arguments,
        DeclaredTypeSymbol declaring)
    {
        DeclaredTypeSymbol? outer = _constructing;
        _constructing = declaring;

        try
        {
            // A constructor that says nothing about its parent still has one to build. Written
            // here rather than left out, because leaving it out is silent: the parent's fields
            // keep whatever their starting values were, and a name the parent's constructor
            // would have settled reads as empty from every corner of the program.
            if (!OpensWithBaseCall(constructor))
            {
                BuildTheParent(instance, declaring);
            }

            Invoke(
                new FunctionValue(
                    constructor.Parameters, constructor.Body, expressionBody: null, _shared, instance),
                [],
                arguments);
        }
        finally
        {
            _constructing = outer;
        }
    }

    /// <summary>
    /// Whether a constructor reaches its parent itself. Only the first statement is looked at,
    /// since <c>PC0248</c> is what allows a <c>base(...)</c> to be anywhere else.
    /// </summary>
    private static bool OpensWithBaseCall(FunctionDecl constructor) =>
        constructor.Body is [ExpressionStmt
        {
            Expression: CallExpr { Callee: ReceiverExpr { Receiver: ReceiverKind.Base } },
        }, ..];

    /// <summary>
    /// <para>Runs the parent's constructor for a child that did not write <c>base(...)</c>.</para>
    /// <para>The one it takes is the one that needs nothing, which is the only one a child could
    /// have meant without saying — and <c>PC0250</c> has already refused the program where the
    /// parent has none. A parent that declares no constructor at all has nothing to run and its
    /// own parent to think about, so the walk carries on upward.</para>
    /// </summary>
    private void BuildTheParent(Instance instance, DeclaredTypeSymbol declaring)
    {
        if (declaring is not ModelSymbol { BaseType: { } parent })
        {
            return;
        }

        if (FindConstructor(parent, 0) is { } takingNothing && BodyOf(takingNothing) is { } body)
        {
            RunConstructor(body, instance, [], parent);
            return;
        }

        BuildTheParent(instance, parent);
    }

    /// <summary>Constructs one of the exceptions the language provides.</summary>
    private object? BuildBuiltInException(
        NewExpr construction,
        Environment scope,
        Instance? receiver)
    {
        if (BuiltInExceptions.Resolve(construction.TypeName) is not { } type)
        {
            return new ProfiCRuntimeException($"Cannot construct '{construction.TypeName}'.");
        }

        // The one-argument form carries a message, which is the whole point of writing one.
        string? message = construction.Arguments.Count > 0
            ? AsText(Evaluate(construction.Arguments[0], scope, receiver))
            : null;

        object? built = message is null
            ? Activator.CreateInstance(type)
            : Activator.CreateInstance(type, message);

        return built as Exception
            ?? (object)new ProfiCRuntimeException($"Cannot construct '{construction.TypeName}'.");
    }

    private object? EvaluateIndex(IndexExpr index, Environment scope, Instance? receiver)
    {
        object? target = Evaluate(index.Receiver, scope, receiver);
        long position = AsInteger(Evaluate(index.Index, scope, receiver));

        return target switch
        {
            ProfiCSet<object?> set => set[(int)position],
            string text => ProfiCText.At(text, position),
            _ => null,
        };
    }

    private object? EvaluateMember(MemberExpr member, Environment scope, Instance? receiver)
    {
        // A value the language provides, such as Math.Pi or a moment's Year. Written without
        // parentheses, so it arrives here rather than through a call, and the checker has
        // already said which. One reached through a type name has nothing on the left to
        // read; one reached through a value is asking about that value.
        if (_model.GetBuiltIn(member) is { } constant)
        {
            bool throughTypeName = TypeNamedBy(member.Receiver) is not null;

            object? subject = throughTypeName
                ? null
                : Evaluate(member.Receiver, scope, receiver);

            return Perform(constant, subject, []).Value;
        }

        // A type name on the left reaches a shared member.
        if (TypeNamedBy(member.Receiver) is not null)
        {
            return _model.GetSymbol(member) switch
            {
                FieldSymbol field => _shared.Lookup(field)?.Value,
                EnumMemberSymbol enumMember => new EnumValue(
                    enumMember.Owner.Name, enumMember.Name, enumMember.Value),

                // Functions are values, so naming one without calling it produces the function
                // itself. It closes over the shared storage and nothing else, since a shared
                // member has no instance to remember.
                FunctionSymbol function when BodyOf(function) is { } declared =>
                    new FunctionValue(
                        declared.Parameters, declared.Body, expressionBody: null, _shared, null),

                _ => null,
            };
        }

        object? target = Evaluate(member.Receiver, scope, receiver);

        if (target is Instance instance)
        {
            if (_model.GetSymbol(member) is FieldSymbol field2
                && instance.Fields.TryGetValue(field2, out object? value))
            {
                return value;
            }

            // A method named through a value is that method bound to the value, so calling it
            // later still knows which one it belongs to — and which method that is is decided
            // by what the value turned out to be, exactly as it is for a call. The name the
            // checker settled says only which one was written; binding that one would make
            // 'pet.Speaks' and 'pet.Speaks()' answer differently about the same pet.
            if (_model.GetSymbol(member) is FunctionSymbol method
                && FindMethod(instance.Type, method, member.MemberName, method.Parameters.Count) is { } found
                && BodyOf(found) is { } body)
            {
                return new FunctionValue(
                    body.Parameters, body.Body, expressionBody: null, _shared, instance);
            }
        }

        return null;
    }
}
