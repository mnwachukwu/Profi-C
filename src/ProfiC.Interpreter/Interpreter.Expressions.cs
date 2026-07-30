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

        if (_globals.Lookup(symbol) is { } global)
        {
            return global.Value;
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
            ConversionOperation.IntegerToReal => (double)AsInteger(value),
            ConversionOperation.IntegerToFraction => Fraction.FromInteger(AsInteger(value)),
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
            ConversionOperation.ToStringValue => ModelOperations.ToDisplayString(value),
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
            (double a, double b) => RealOperation(binary.Operator, a, b),
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
        (long b, long e) => IntegerPower(b, e),
        (double b, double e) => Math.Pow(b, e),
        _ => null,
    };

    /// <summary>
    /// <para>A whole power of a whole number, by squaring.</para>
    /// <para>Every step is checked, so a result too large to hold stops the program rather
    /// than wrapping silently into a wrong answer.</para>
    /// </summary>
    private static long IntegerPower(long value, long exponent)
    {
        if (exponent < 0)
        {
            // Rejected while compiling wherever the exponent can be seen; a variable one
            // reaches here instead. Thrown as an ArgumentException rather than an interpreter
            // failure so that a program can catch it, exactly as it can catch dividing by a
            // variable that turned out to be zero.
            throw new ArgumentException(
                $"An integer raised to the power {exponent} is not a whole number. Raise a "
                + "fraction instead, or use Math.Pow for a real result.");
        }

        long result = 1;
        long factor = value;

        for (long remaining = exponent; remaining > 0; remaining /= 2)
        {
            if (remaining % 2 == 1)
            {
                result = checked(result * factor);
            }

            if (remaining > 1)
            {
                factor = checked(factor * factor);
            }
        }

        return result;
    }

    /// <summary>
    /// Integer arithmetic. Division truncates, which is worth knowing: one divided by three
    /// is zero, not a third.
    /// </summary>
    private static object? IntegerOperation(BinaryOperator op, long a, long b) => op switch
    {
        BinaryOperator.Add => checked(a + b),
        BinaryOperator.Subtract => checked(a - b),
        BinaryOperator.Multiply => checked(a * b),
        BinaryOperator.Divide => b == 0 ? throw new DivideByZeroException() : a / b,
        BinaryOperator.Remainder => b == 0 ? throw new DivideByZeroException() : a % b,
        BinaryOperator.LessThan => a < b,
        BinaryOperator.GreaterThan => a > b,
        BinaryOperator.LessThanOrEqual => a <= b,
        BinaryOperator.GreaterThanOrEqual => a >= b,
        _ => null,
    };

    private static object? RealOperation(BinaryOperator op, double a, double b) => op switch
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
        double => ReferenceEquals(target, PrimitiveType.Real),
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

        if (!_types.TryGetValue(construction.TypeName, out DeclaredTypeSymbol? type))
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
            RunConstructor(body, instance, arguments);
        }

        return instance;
    }

    /// <summary>Runs field initializers, walking from the base type down so a parent's run first.</summary>
    private void InitializeFields(Instance instance, DeclaredTypeSymbol type)
    {
        IEnumerable<DeclaredTypeSymbol> chain = type is ModelSymbol model
            ? model.SelfAndAncestors().Reverse()
            : [type];

        foreach (DeclaredTypeSymbol current in chain)
        {
            foreach (List<Symbol> group in current.Members.Values)
            {
                foreach (Symbol member in group)
                {
                    if (member is not FieldSymbol { IsGlobal: false } field)
                    {
                        continue;
                    }

                    instance.Fields[field] = _initializers.TryGetValue(field, out Expression? start)
                        ? Evaluate(start, _globals, instance)
                        : DefaultFor(field.Type);
                }
            }
        }
    }

    private static FunctionSymbol? FindConstructor(DeclaredTypeSymbol type, int arity) =>
        type.Lookup(type.Name)
            .OfType<FunctionSymbol>()
            .FirstOrDefault(f => f.IsConstructor && f.Parameters.Count == arity);

    private void RunConstructor(
        FunctionDecl constructor,
        Instance instance,
        IReadOnlyList<object?> arguments)
    {
        Invoke(
            new FunctionValue(
                constructor.Parameters, constructor.Body, expressionBody: null, _globals, instance),
            [],
            arguments);
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
            string text => position >= 0 && position < text.Length
                ? text[(int)position]
                : throw new IndexOutOfRangeException(
                    $"Index {position} is outside a string of {text.Length} characters."),
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
            bool throughTypeName = member.Receiver is IdentifierExpr owner
                                   && _model.GetSymbol(owner) is DeclaredTypeSymbol;

            object? subject = throughTypeName
                ? null
                : Evaluate(member.Receiver, scope, receiver);

            return Perform(constant, subject, []).Value;
        }

        // A type name on the left reaches a global member.
        if (member.Receiver is IdentifierExpr name
            && _model.GetSymbol(name) is DeclaredTypeSymbol)
        {
            return _model.GetSymbol(member) switch
            {
                FieldSymbol field => _globals.Lookup(field)?.Value,
                EnumMemberSymbol enumMember => new EnumValue(
                    enumMember.Owner.Name, enumMember.Name, enumMember.Value),

                // Functions are values, so naming one without calling it produces the function
                // itself. It closes over nothing but the globals, since a global member has no
                // instance to remember.
                FunctionSymbol function when BodyOf(function) is { } declared =>
                    new FunctionValue(
                        declared.Parameters, declared.Body, expressionBody: null, _globals, null),

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
            // later still knows which one it belongs to. Which method is the checker's
            // decision, as it is for a call.
            if (_model.GetSymbol(member) is FunctionSymbol method
                && BodyOf(method) is { } body)
            {
                return new FunctionValue(
                    body.Parameters, body.Body, expressionBody: null, _globals, instance);
            }
        }

        return null;
    }
}
