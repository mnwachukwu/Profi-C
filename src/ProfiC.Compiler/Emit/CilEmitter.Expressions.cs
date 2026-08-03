using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Semantics;
using ProfiC.Runtime;

namespace ProfiC.Compiler.Emit;

public sealed partial class CilEmitter
{
    /// <summary>
    /// Pushes the value of an expression onto the stack. Post-order: every operand is emitted
    /// before the operator that consumes it, which is all a stack machine asks for.
    /// </summary>
    private void EmitExpression(Expression expression)
    {
        switch (expression)
        {
            case ParenthesizedExpr parenthesized:
                EmitExpression(parenthesized.Inner);
                break;

            case LiteralExpr literal:
                EmitLiteral(literal);
                break;

            case IdentifierExpr name:
                EmitLoad(name);
                break;

            case UnaryExpr unary:
                EmitUnary(unary);
                break;

            case BinaryExpr binary:
                EmitBinary(binary);
                break;

            case ConversionExpr conversion:
                EmitConversion(conversion);
                break;

            case CallExpr call:
                EmitCall(call);
                break;

            case ReceiverExpr:
                _il.Emit(OpCodes.Ldarg_0);
                break;

            case MemberExpr member:
                EmitMemberRead(member);
                break;

            case NewExpr construction:
                EmitNew(construction);
                break;

            case CollectionExpr collection:
                EmitCollection(collection);
                break;

            case IndexExpr index:
                EmitIndexRead(index);
                break;

            case IfExpr conditional:
                EmitConditional(conditional);
                break;

            case TypeTestExpr test:
                EmitTypeTest(test);
                break;

            case TypeCastExpr cast:
                EmitTypeCast(cast);
                break;

            default:
                throw Unhandled(expression.GetType().Name);
        }
    }

    /// <summary>
    /// <para>Reads a field through the instance it belongs to, or straight off the type — or a
    /// member of the language that is a value rather than something to call.</para>
    /// <para><c>Count</c>, on a set and on a string alike, is the second kind. It reaches here
    /// rather than through the call path because it is written without parentheses, which is the
    /// whole difference between the two and the only thing that decides which way it arrives.
    /// </para>
    /// </summary>
    private void EmitMemberRead(MemberExpr member)
    {
        switch (_model.GetBuiltIn(member))
        {
            case { } onASet when CilBuiltIns.IsOnASet(onASet):
                EmitSetMember(member, [], onASet);
                return;

            case { } onAString when CilBuiltIns.IsOnAString(onAString):
                EmitStringMember(member, [], onAString);
                return;

            case { } onMath when CilBuiltIns.IsOnMath(onMath):
                EmitMathMember([], onMath);
                return;

            case BuiltInId.ExceptionMessage:
                EmitExceptionMessage(member);
                return;

            case { } bound when CilBuiltIns.IsABound(bound):
                EmitBound(bound);
                return;

            // Read rather than called — 'DateTime.Now', 'length.TotalHours', 'Directory.Current'
            // — which is the same sequence a call is, with nothing after the receiver.
            case { } provided when CilBuiltIns.IsOnAMoment(provided)
                                   || CilBuiltIns.IsOnAFile(provided)
                                   || CilBuiltIns.IsOnAGenerator(provided):
                EmitProvidedMember(member, [], provided);
                return;
        }

        // A member of an enumeration, which is a constant rather than anything to read at run
        // time — the type name in front of it names no value to reach through.
        if (_model.GetSymbol(member) is EnumMemberSymbol declared)
        {
            EmitEnumerationMember(declared);
            return;
        }

        // A function named rather than called, which is what every function value is after
        // closure conversion. The receiver is what it ends up bound to, and is nothing where the
        // name in front is a type.
        if (_model.GetSymbol(member) is FunctionSymbol named)
        {
            EmitFunctionValue(
                IsThroughATypeName(member.Receiver) ? null : member.Receiver,
                named,
                _model.GetType(member) ?? throw Unhandled($"the type of '{member.MemberName}'"));

            return;
        }

        if (_model.GetSymbol(member) is not FieldSymbol field
            || !_fields.TryGetValue(field, out FieldBuilder? slot))
        {
            throw Unhandled($"reading '{member.MemberName}'");
        }

        if (field.IsShared)
        {
            _il.Emit(OpCodes.Ldsfld, slot);
            return;
        }

        EmitExpression(member.Receiver);
        _il.Emit(OpCodes.Ldfld, slot);
    }

    /// <summary>
    /// <para>Makes an instance: the arguments, then <c>newobj</c>, which allocates and runs the
    /// constructor in one instruction.</para>
    /// <para>The type comes from what the resolver settled rather than from the name as
    /// written, because the two part company for a qualified name — the text says
    /// <c>Shapes.Circle</c> and no type is called that.</para>
    /// </summary>
    private void EmitNew(NewExpr construction)
    {
        // One of the types the language provides that holds a value — a moment, a day, a length,
        // a generator. Built by a runtime factory rather than by a constructor, so that a date
        // nobody could write is refused in the language's own words.
        if (_model.GetBuiltIn(construction) is { } making)
        {
            EmitProvidedConstruction(construction, making);
            return;
        }

        if (_model.GetType(construction) is not DeclaredTypeSymbol type)
        {
            throw Unhandled($"constructing '{construction.TypeName}'");
        }

        // One the language provides — an exception — is a type in the runtime, made by the
        // constructor the argument count picks rather than by one this build defined.
        if (!_types.ContainsKey(type))
        {
            if (CilTypes.OfBuiltInModel(type) is not { } provided)
            {
                throw Unhandled($"constructing '{construction.TypeName}'");
            }

            EmitArguments(construction.Arguments);
            _il.Emit(OpCodes.Newobj, BuiltInConstructor(provided, construction.Arguments.Count));
            return;
        }

        EmitArguments(construction.Arguments);
        _il.Emit(OpCodes.Newobj, ConstructorFor(type, construction));
    }

    private void EmitArguments(IReadOnlyList<Expression> arguments)
    {
        foreach (Expression argument in arguments)
        {
            EmitValueInto(argument);
        }
    }

    /// <summary>
    /// <para>The constructor a <c>new</c> chose.</para>
    /// <para>Taken from the symbol the checker rebound the expression to, which is the
    /// overload the arguments selected. Falling back to the type's only constructor covers the
    /// model that declared none, where there is nothing to choose between.</para>
    /// </summary>
    private System.Reflection.ConstructorInfo ConstructorFor(
        DeclaredTypeSymbol type,
        NewExpr construction)
    {
        if (_model.GetSymbol(construction) is FunctionSymbol chosen
            && _constructors.TryGetValue(chosen, out ConstructorBuilder? built))
        {
            return built;
        }

        // The one made for a model that wrote none. Looked up rather than asked of the type,
        // which cannot answer until it is created — and by then this body is already written.
        return _defaultConstructors.TryGetValue(type, out ConstructorBuilder? supplied)
            ? supplied
            : throw Unhandled($"a constructor for '{type.Name}'");
    }

    private void EmitLiteral(LiteralExpr literal)
    {
        switch (LiteralDecoder.Decode(literal))
        {
            case long value:
                _il.Emit(OpCodes.Ldc_I8, value);
                break;

            case decimal value:
                EmitReal(value);
                break;

            case double value:
                _il.Emit(OpCodes.Ldc_R8, value);
                break;

            case Fraction value:
                EmitFraction(value);
                break;

            case bool value:
                _il.Emit(value ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
                break;

            case char value:
                _il.Emit(OpCodes.Ldc_I4, value);
                break;

            case string value:
                _il.Emit(OpCodes.Ldstr, value);
                break;

            case { } other:
                throw Unhandled($"the literal {other.GetType().Name}");

            default:
                throw Unhandled($"the literal '{literal.Text}'");
        }
    }

    private void EmitLoad(IdentifierExpr name)
    {
        switch (_model.GetSymbol(name))
        {
            case LocalSymbol local when _locals.TryGetValue(local, out LocalBuilder? slot):
                _il.Emit(OpCodes.Ldloc, slot);
                UnwrapIfNarrowed(local.Type, name);
                break;

            case ParameterSymbol parameter when _parameters.TryGetValue(parameter, out int at):
                _il.Emit(OpCodes.Ldarg, at);
                UnwrapIfNarrowed(parameter.Type, name);
                break;

            // A field named on its own, which a field initializer may do without writing a
            // receiver. Its own instance is the one it belongs to.
            case FieldSymbol field when _fields.TryGetValue(field, out FieldBuilder? slot):
                if (field.IsShared)
                {
                    _il.Emit(OpCodes.Ldsfld, slot);
                }
                else
                {
                    _il.Emit(OpCodes.Ldarg_0);
                    _il.Emit(OpCodes.Ldfld, slot);
                }

                UnwrapIfNarrowed(field.Type, name);
                break;

            default:
                throw Unhandled($"the name '{name.Name}'");
        }
    }

    /// <summary>
    /// <para>Reads the value out of an optional the compiler has proved is present.</para>
    /// <para><b>Narrowing leaves no mark on the tree</b>, which is what makes this necessary.
    /// Inside <c>if maybe.HasValue()</c> the checker records every read of <c>maybe</c> as the
    /// definite type, while the local it reads still holds an optional — a difference the
    /// interpreter never notices, being untyped, and the emitter cannot survive.</para>
    /// <para>So the two are compared here: where a name was declared optional and this reading of
    /// it was not, the guard is what stands between them, and the value is taken out. That the
    /// value is there has been settled by <c>PC0401</c> long before.</para>
    /// </summary>
    private void UnwrapIfNarrowed(TypeSymbol? declared, Expression read)
    {
        if (declared is not OptionalType optional || _model.GetType(read) is OptionalType)
        {
            return;
        }

        Type built = TypeOf(optional, "an optional");
        LocalBuilder slot = _il.DeclareLocal(built);

        _il.Emit(OpCodes.Stloc, slot);
        _il.Emit(OpCodes.Ldloca, slot);
        _il.Emit(OpCodes.Call, OptionalMethod(built, "get_Value"));
    }

    private void EmitUnary(UnaryExpr unary)
    {
        EmitExpression(unary.Operand);

        switch (unary.Operator)
        {
            // A real has no 'neg' any more than it has an 'add' — the instruction knows integers
            // and binary floating point, and a decimal is neither.
            case UnaryOperator.Negate when IsReal(unary.Operand):
                _il.Emit(OpCodes.Call, RealNegate);
                break;

            case UnaryOperator.Negate when IsFraction(unary.Operand):
                _il.Emit(OpCodes.Call, FractionNegate);
                break;

            case UnaryOperator.Negate:
                _il.Emit(OpCodes.Neg);
                break;

            // No instruction inverts a boolean: 'not' is "equals false", which is what comparing
            // against zero does, and it leaves 0 or 1 rather than flipping every bit.
            case UnaryOperator.Not:
                _il.Emit(OpCodes.Ldc_I4_0);
                _il.Emit(OpCodes.Ceq);
                break;

            default:
                throw Unhandled($"the operator '{unary.Operator}'");
        }
    }

    private void EmitBinary(BinaryExpr binary)
    {
        switch (binary.Operator)
        {
            case BinaryOperator.And:
            case BinaryOperator.Or:
                EmitShortCircuit(binary);
                return;

            case BinaryOperator.Add when IsString(binary):
                EmitConcatenation(binary);
                return;

            // The one operator whose sides may differ in type, so it is settled before the
            // matching-pair dispatch below.
            case BinaryOperator.Power:
                EmitPower(binary);
                return;

            // Settled here rather than below because how the operands are pushed depends on the
            // answer: a deep comparison is a call taking two objects, so a value going into one
            // has to be boxed on the way.
            case BinaryOperator.Equal:
            case BinaryOperator.NotEqual:
                EmitComparison(binary);
                return;
        }

        EmitExpression(binary.Left);
        EmitExpression(binary.Right);

        // Seven of the integer operators mean something the instruction does not, so each is a
        // call into the runtime rather than one opcode. See ProfiCArithmetic: emitted as 'add'
        // and the rest, a sum past the end of an integer comes back negative, a division by zero
        // carries the framework's wording, and a shift of 64 quietly means a shift of none.
        if (IsInteger(binary.Left) && ArithmeticMethod(binary.Operator) is { } shared)
        {
            _il.Emit(OpCodes.Call, shared);
            return;
        }

        // <b>A real has no instructions at all.</b> The CLR knows integers and binary floating
        // point; a decimal is a library type, so every operator on one is a call — the four the
        // runtime guards, and the comparisons, which the framework's own operators answer.
        if (IsReal(binary.Left) && RealMethod(binary.Operator) is { } onReals)
        {
            _il.Emit(OpCodes.Call, onReals);
            return;
        }

        // A fraction has none either, and its operators are the runtime's own — so the reducing
        // and the exactness live inside them rather than here.
        if (IsFraction(binary.Left) && FractionMethod(binary.Operator) is { } onFractions)
        {
            _il.Emit(OpCodes.Call, onFractions);
            return;
        }

        switch (binary.Operator)
        {
            case BinaryOperator.Add: _il.Emit(OpCodes.Add); break;
            case BinaryOperator.Subtract: _il.Emit(OpCodes.Sub); break;
            case BinaryOperator.Multiply: _il.Emit(OpCodes.Mul); break;
            case BinaryOperator.Divide: _il.Emit(OpCodes.Div); break;
            case BinaryOperator.Remainder: _il.Emit(OpCodes.Rem); break;
            case BinaryOperator.BitwiseAnd: _il.Emit(OpCodes.And); break;
            case BinaryOperator.BitwiseOr: _il.Emit(OpCodes.Or); break;
            case BinaryOperator.Xor: _il.Emit(OpCodes.Xor); break;
            case BinaryOperator.ShiftLeft: _il.Emit(OpCodes.Shl); break;
            case BinaryOperator.ShiftRight: _il.Emit(OpCodes.Shr); break;

            case BinaryOperator.LessThan:
                _il.Emit(OpCodes.Clt);
                break;

            case BinaryOperator.GreaterThan:
                _il.Emit(OpCodes.Cgt);
                break;

            // No instruction for these two, so each is the strict one inverted: "not greater"
            // is "less or equal". Inverting with 'equals false' rather than 'not', which on an
            // integer flips every bit and would make 0 into -1.
            case BinaryOperator.LessThanOrEqual:
                _il.Emit(OpCodes.Cgt);
                _il.Emit(OpCodes.Ldc_I4_0);
                _il.Emit(OpCodes.Ceq);
                break;

            case BinaryOperator.GreaterThanOrEqual:
                _il.Emit(OpCodes.Clt);
                _il.Emit(OpCodes.Ldc_I4_0);
                _il.Emit(OpCodes.Ceq);
                break;

            default:
                throw Unhandled($"the operator '{binary.Operator}'");
        }
    }

    /// <summary>
    /// <para>Equality, which in this language compares what a value holds rather than where it
    /// lives.</para>
    /// <para>Three sequences, because the answer is the same and the way to it is not. A number
    /// or a boolean is one instruction. A string is a call, since two equal strings are rarely
    /// the same object. Anything else — a model, a set — is the runtime's deep walk, and
    /// <c>ceq</c> there would compare references and find two equal values different.</para>
    /// </summary>
    /// <summary>
    /// <para><c>==</c> and <c>/=</c>: the two operands, then whichever comparison their types
    /// call for.</para>
    /// <para><b>A deep comparison is a call taking two objects</b>, so a value type going into
    /// one is boxed on the way — a moment, a day, a length of time. Pushed unboxed the assembly
    /// does not verify at all, which is a loud failure and the good case; what makes this worth
    /// its own step is that the decision about how to push cannot be made after pushing.</para>
    /// </summary>
    private void EmitComparison(BinaryExpr binary)
    {
        if (IsComparedDeeply(binary.Left) || IsComparedDeeply(binary.Right))
        {
            EmitAsObject(binary.Left);
            EmitAsObject(binary.Right);
        }
        else
        {
            EmitExpression(binary.Left);
            EmitExpression(binary.Right);
        }

        EmitEqualityOf(binary.Left, binary.Right, binary.Operator == BinaryOperator.Equal);
    }

    /// <summary>
    /// <para>Compares two values already on the stack, by whichever of the four sequences their
    /// types call for.</para>
    /// <para>Written against a pair of expressions rather than against the operator, because a
    /// <c>switch</c> asks the same question of a subject and a label and has to get the same
    /// answer — one place deciding, so the two cannot part company.</para>
    /// </summary>
    private void EmitEqualityOf(Expression left, Expression right, bool wanted)
    {
        if (IsString(left))
        {
            _il.Emit(OpCodes.Call, StringEquals);
        }
        else if (IsReal(left) || IsReal(right))
        {
            // A decimal has no 'ceq' either, for the same reason it has no 'add'.
            _il.Emit(OpCodes.Call, RealEquals);
        }
        else if (IsFraction(left) || IsFraction(right))
        {
            // Compared by cross-multiplying rather than field by field, which is what makes
            // 2|4 and 1|2 equal without either being reduced first.
            _il.Emit(OpCodes.Call, FractionEquals);
        }
        else if (IsComparedDeeply(left) || IsComparedDeeply(right))
        {
            _il.Emit(OpCodes.Call, DeepEquals);
        }
        else
        {
            _il.Emit(OpCodes.Ceq);
        }

        if (!wanted)
        {
            _il.Emit(OpCodes.Ldc_I4_0);
            _il.Emit(OpCodes.Ceq);
        }
    }

    /// <summary>
    /// <para><c>if ... then ... else ...</c> written where a value belongs, which is what this
    /// language has in place of a ternary.</para>
    /// <para>A branch, so only the side taken is evaluated. That is a rule about the language
    /// rather than an economy: the other side may be the one that would have failed, and
    /// <c>if here then here.Value() else 0</c> is exactly the shape that rests on it.</para>
    /// <para>Nothing converts either arm, because there is nothing to convert. The checker
    /// requires the two to have the same type exactly — <c>if c then 1 else 2.5</c> is refused
    /// rather than made a real — so both leave the same thing on the stack by construction.</para>
    /// </summary>
    private void EmitConditional(IfExpr conditional)
    {
        Label otherwise = _il.DefineLabel();
        Label done = _il.DefineLabel();

        EmitExpression(conditional.Condition);
        _il.Emit(OpCodes.Brfalse, otherwise);

        EmitExpression(conditional.ThenValue);
        _il.Emit(OpCodes.Br, done);

        _il.MarkLabel(otherwise);
        EmitExpression(conditional.ElseValue);

        _il.MarkLabel(done);
    }

    /// <summary>
    /// <para><c>and</c> and <c>or</c>, which do not evaluate their right side unless they must.
    /// </para>
    /// <para>Short-circuiting is what makes a guard work — <c>if here and here.Value() &gt; 0</c>
    /// — so it is a rule about the language rather than an optimization, and the branch is how
    /// a stack machine says it.</para>
    /// </summary>
    private void EmitShortCircuit(BinaryExpr binary)
    {
        Label settled = _il.DefineLabel();
        Label done = _il.DefineLabel();

        bool isAnd = binary.Operator == BinaryOperator.And;

        EmitExpression(binary.Left);
        _il.Emit(isAnd ? OpCodes.Brfalse : OpCodes.Brtrue, settled);

        EmitExpression(binary.Right);
        _il.Emit(OpCodes.Br, done);

        // Reached only when the left side already decided it: false for 'and', true for 'or'.
        _il.MarkLabel(settled);
        _il.Emit(isAnd ? OpCodes.Ldc_I4_0 : OpCodes.Ldc_I4_1);

        _il.MarkLabel(done);
    }

    /// <summary>
    /// <para>Joining a string to something, where the something may be any type.</para>
    /// <para>The other side is converted by the operation rather than by a recorded conversion,
    /// which is why nothing in the tree marks it: <c>"n is " + 5</c> is a string join, not an
    /// addition that happens to have a string in it.</para>
    /// <para>Each side is rendered by the runtime rather than by <c>String.Concat</c>, which
    /// would call <c>ToString</c> and put <c>True</c> in a Profi-C string. How a value reads is
    /// one decision, made in one place, whether it is printed or joined.</para>
    /// </summary>
    private void EmitConcatenation(BinaryExpr binary)
    {
        EmitAsText(binary.Left);
        EmitAsText(binary.Right);
        _il.Emit(OpCodes.Call, ConcatTwo);
    }

    /// <summary>Pushes an expression's value as the text the language renders it as.</summary>
    private void EmitAsText(Expression expression)
    {
        EmitAsObject(expression);
        _il.Emit(OpCodes.Call, ToDisplayString);
    }

    /// <summary>
    /// <para>Puts a value where an <c>object</c> is wanted, boxing it if it is not one.</para>
    /// <para>Asked of the CLR type rather than of a list of primitives, because an optional is a
    /// struct too — and the version that asked only about primitives passed one unboxed, which
    /// the runtime rejects as an invalid program rather than as a wrong answer.</para>
    /// </summary>
    private void EmitAsObject(Expression expression)
    {
        EmitExpression(expression);

        if (_model.GetType(expression) is { } type
            && TypeOf(type, "a value") is { IsValueType: true } clr)
        {
            _il.Emit(OpCodes.Box, clr);
        }
    }

    /// <summary>
    /// <para>The runtime method an integer operator means, or null where the instruction already
    /// means it.</para>
    /// <para>Comparison and the bitwise three are absent on purpose: those mean exactly what the
    /// instruction means, so routing them through a call would buy nothing.</para>
    /// </summary>
    private static MethodInfo? ArithmeticMethod(BinaryOperator op) => op switch
    {
        BinaryOperator.Add => Arithmetic(nameof(ProfiCArithmetic.Add)),
        BinaryOperator.Subtract => Arithmetic(nameof(ProfiCArithmetic.Subtract)),
        BinaryOperator.Multiply => Arithmetic(nameof(ProfiCArithmetic.Multiply)),
        BinaryOperator.Divide => Arithmetic(nameof(ProfiCArithmetic.Divide)),
        BinaryOperator.Remainder => Arithmetic(nameof(ProfiCArithmetic.Remainder)),
        BinaryOperator.ShiftLeft => Arithmetic(nameof(ProfiCArithmetic.ShiftLeft)),
        BinaryOperator.ShiftRight => Arithmetic(nameof(ProfiCArithmetic.ShiftRight)),
        _ => null,
    };

    private static MethodInfo Arithmetic(string name) =>
        typeof(ProfiCArithmetic).GetMethod(name, [typeof(long), typeof(long)])!;

    /// <summary>
    /// <para>The method a <c>real</c> operator means. Every one of them is a call.</para>
    /// <para>The four that can fail go to the runtime, so that a division by zero and a result
    /// too large are refused in the language's words. The comparisons go to the framework's own
    /// operators, which is what a decimal has instead of <c>clt</c> and <c>cgt</c> — those
    /// instructions know integers and binary floating point, and a decimal is neither.</para>
    /// </summary>
    private static MethodInfo? RealMethod(BinaryOperator op) => op switch
    {
        BinaryOperator.Add => OnReals(nameof(ProfiCArithmetic.Add)),
        BinaryOperator.Subtract => OnReals(nameof(ProfiCArithmetic.Subtract)),
        BinaryOperator.Multiply => OnReals(nameof(ProfiCArithmetic.Multiply)),
        BinaryOperator.Divide => OnReals(nameof(ProfiCArithmetic.Divide)),
        BinaryOperator.Remainder => OnReals(nameof(ProfiCArithmetic.Remainder)),

        BinaryOperator.LessThan => Comparing("op_LessThan"),
        BinaryOperator.GreaterThan => Comparing("op_GreaterThan"),
        BinaryOperator.LessThanOrEqual => Comparing("op_LessThanOrEqual"),
        BinaryOperator.GreaterThanOrEqual => Comparing("op_GreaterThanOrEqual"),

        _ => null,
    };

    private static MethodInfo OnReals(string name) =>
        typeof(ProfiCArithmetic).GetMethod(name, [typeof(decimal), typeof(decimal)])!;

    private static MethodInfo Comparing(string name) =>
        typeof(decimal).GetMethod(name, [typeof(decimal), typeof(decimal)])!;

    private bool IsInteger(Expression expression) =>
        ReferenceEquals(_model.GetType(expression), PrimitiveType.Integer);

    private bool IsReal(Expression expression) =>
        ReferenceEquals(_model.GetType(expression), PrimitiveType.Real);

    private bool IsFraction(Expression expression) =>
        ReferenceEquals(_model.GetType(expression), PrimitiveType.Fraction);

    private bool IsString(BinaryExpr binary) => IsString(binary.Left) || IsString(binary.Right);

    private bool IsString(Expression expression) =>
        ReferenceEquals(_model.GetType(expression), PrimitiveType.String);

    /// <summary>
    /// <para>Whether a value is one the runtime compares by walking it.</para>
    /// <para>The shapes that hold other values: a model and a set. Both arrive as references, so
    /// they are already what the walk takes and need no boxing on the way in.</para>
    /// </summary>
    /// <summary>
    /// <para>Whether <c>==</c> on this is the runtime's walk rather than an instruction.</para>
    /// <para>A structure is here for the same reason a model is: two of them holding equal fields
    /// are equal, whether or not they are the same object. That it is emitted as a class makes
    /// the mistake easy — <c>ceq</c> would compile, run, and answer false for two points at the
    /// same place.</para>
    /// </summary>
    private bool IsComparedDeeply(Expression expression) =>
        _model.GetType(expression) is ModelSymbol or StructureSymbol or SetType;

    private void EmitConversion(ConversionExpr conversion)
    {
        // Wrapping emits its own operand, since what it wraps into decides the call that
        // follows and the operand has to sit beneath it.
        if (conversion.Operation == ConversionOperation.WrapOptional)
        {
            EmitWrapOptional(conversion);
            return;
        }

        // A conversion recorded between two optionals is a conversion of the value one holds,
        // and no work at all on one holding nothing. The checker writes the operation down
        // seeing through both sides, so this is where the seeing-through is done: without it
        // a string? reaching a character[]? would hand the optional itself to a method
        // expecting the string inside it.
        if (_model.GetType(conversion.Operand) is OptionalType from
            && _model.GetType(conversion) is OptionalType to)
        {
            EmitConversionThroughOptional(conversion, from, to);
            return;
        }

        EmitExpression(conversion.Operand);
        EmitConversionStep(conversion.Operation);
    }

    /// <summary>
    /// <para>Converts what an optional holds, and carries absence across untouched.</para>
    /// <para>Absence is not a value to convert. An empty <c>string?</c> becoming an empty
    /// <c>character[]?</c> has no characters to produce, and the answer is an optional holding
    /// nothing rather than one holding an empty set — which are different answers, and a reader
    /// asking <c>HasValue</c> can tell them apart.</para>
    /// </summary>
    private void EmitConversionThroughOptional(
        ConversionExpr conversion,
        OptionalType from,
        OptionalType to)
    {
        Type source = TypeOf(from, "an optional");
        Type answer = TypeOf(to, "an optional");

        LocalBuilder held = _il.DeclareLocal(source);

        EmitExpression(conversion.Operand);
        _il.Emit(OpCodes.Stloc, held);

        Label absent = _il.DefineLabel();
        Label done = _il.DefineLabel();

        _il.Emit(OpCodes.Ldloca, held);
        _il.Emit(OpCodes.Call, OptionalMethod(source, "get_HasValue"));
        _il.Emit(OpCodes.Brfalse, absent);

        _il.Emit(OpCodes.Ldloca, held);
        _il.Emit(OpCodes.Call, OptionalMethod(source, "get_Value"));
        EmitConversionStep(conversion.Operation);
        _il.Emit(OpCodes.Call, OptionalMethod(answer, "Of"));
        _il.Emit(OpCodes.Br, done);

        _il.MarkLabel(absent);
        EmitEmptyOptional(answer);

        _il.MarkLabel(done);
    }

    /// <summary>
    /// The conversion itself, for a value already on the stack. Split out from the walk so that
    /// the same instruction sequence serves a plain value and the value inside an optional.
    /// </summary>
    private void EmitConversionStep(ConversionOperation operation)
    {
        switch (operation)
        {
            // Not 'conv.r8', which produces binary floating point — the type a real stopped
            // being. The widening is a constructor call, and it is exact: every whole number a
            // long holds is a decimal.
            case ConversionOperation.IntegerToReal:
                _il.Emit(OpCodes.Newobj, RealFromInteger);
                break;

            // Both widenings into a fraction, and both exact. A real reaching one can outgrow the
            // whole numbers a fraction is made of, which is PC0346 where it is written down and
            // stops here where it arrived in a variable.
            case ConversionOperation.IntegerToFraction:
                _il.Emit(OpCodes.Call, FractionFromInteger);
                break;

            case ConversionOperation.RealToFraction:
                _il.Emit(OpCodes.Call, FractionFromReal);
                break;

            // Between a string and its characters, each way one call. Not a widening like the
            // three above: nothing is lost or gained, and the same characters come back — which
            // is what lets the language convert both ways without either being written.
            case ConversionOperation.StringToCharacters:
                _il.Emit(OpCodes.Call, TextToCharacters);
                break;

            case ConversionOperation.CharactersToString:
                _il.Emit(OpCodes.Call, TextFromCharacters);
                break;

            default:
                throw Unhandled($"the conversion {operation}");
        }
    }

    private void EmitCall(CallExpr call)
    {
        if (_model.GetBuiltIn(call.Callee) is { } builtIn)
        {
            EmitBuiltIn(call, builtIn);
            return;
        }

        // A value being called rather than a function being called: a local, a parameter, or a
        // field holding one. What it is bound to travels inside it, so there is no receiver to
        // put on the stack and nothing here decides which body runs.
        if (_model.GetType(call.Callee) is FunctionType held)
        {
            EmitCallThroughValue(call, held);
            return;
        }

        if (_model.GetSymbol(call.Callee) is not FunctionSymbol function
            || !_functions.TryGetValue(function, out MethodBuilder? method))
        {
            throw Unhandled("a call to something that was never defined");
        }

        // An instance method is reached through something, and that something goes on the stack
        // before the arguments. A shared one is reached through its type, which is a name rather
        // than a value and puts nothing there.
        if (!method.IsStatic)
        {
            EmitReceiver(call.Callee is MemberExpr member ? member.Receiver : null);
        }

        foreach (Expression argument in call.Arguments)
        {
            EmitValueInto(argument);
        }

        // 'callvirt' on an instance method, which dispatches on what the receiver turned out to
        // be — so an override wins over the version the declaring type wrote. It also makes a
        // call on a missing receiver fail where the call is written rather than inside the
        // method, which is true even of a method nothing overrides.
        //
        // 'base.Member()' is the exception, and 'call' is the whole of what makes it mean
        // anything: written inside an override, a virtual call would find that same override and
        // go round forever. Reaching past the child is what 'base' is for.
        bool reachingPastTheChild =
            call.Callee is MemberExpr { Receiver: ReceiverExpr { Receiver: ReceiverKind.Base } };

        _il.Emit(
            method.IsStatic || reachingPastTheChild ? OpCodes.Call : OpCodes.Callvirt,
            method);
    }

    /// <summary>
    /// <para>The built-ins the emitter has an instruction sequence for.</para>
    /// <para>They call the runtime's <c>Console</c> rather than the framework's. That is not
    /// indirection for its own sake: how a value reads is the language's decision, and
    /// <c>ToDisplayString</c> is where it is made — a boolean prints <c>true</c>, not
    /// <c>True</c>, and a fraction prints <c>1|2</c>. Calling <c>System.Console</c> would
    /// render every value the way the framework happens to, which agreed with the interpreter
    /// for integers and disagreed for the first boolean tried.</para>
    /// </summary>
    private void EmitBuiltIn(CallExpr call, BuiltInId builtIn)
    {
        // A set's members are reached through the value on the left, so they need the receiver
        // the call was written on rather than a sequence of their own.
        if (CilBuiltIns.IsOnASet(builtIn))
        {
            if (call.Callee is not MemberExpr onASet)
            {
                throw Unhandled($"'{builtIn}' reached through nothing");
            }

            EmitSetMember(onASet, call.Arguments, builtIn);
            return;
        }

        if (CilBuiltIns.IsOnAString(builtIn))
        {
            if (call.Callee is not MemberExpr onAString)
            {
                throw Unhandled($"'{builtIn}' reached through nothing");
            }

            EmitStringMember(onAString, call.Arguments, builtIn);
            return;
        }

        // Math needs no receiver: it is a name in front of a dot rather than a value, so every
        // member of it is emitted from its arguments alone.
        if (CilBuiltIns.IsOnMath(builtIn))
        {
            EmitMathMember(call.Arguments, builtIn);
            return;
        }

        // Making a fraction is reached through the name 'Fraction' rather than through a value,
        // so it takes no receiver — unlike every other member here.
        if (builtIn is BuiltInId.FractionCreate or BuiltInId.FractionCreateWhole)
        {
            EmitArguments(call.Arguments);

            // Two parts is the constructor, which reduces; one is a whole number over one, and
            // that is a call rather than a construction.
            if (builtIn == BuiltInId.FractionCreate)
            {
                _il.Emit(OpCodes.Newobj, FractionFromParts);
            }
            else
            {
                _il.Emit(OpCodes.Call, FractionFromInteger);
            }

            return;
        }

        if (CilBuiltIns.IsOnAFraction(builtIn))
        {
            if (call.Callee is not MemberExpr onAFraction)
            {
                throw Unhandled($"'{builtIn}' reached through nothing");
            }

            EmitFractionMember(onAFraction, call.Arguments, builtIn);
            return;
        }

        if (CilBuiltIns.IsOnAnOptional(builtIn))
        {
            if (call.Callee is not MemberExpr onAnOptional)
            {
                throw Unhandled($"'{builtIn}' reached through nothing");
            }

            EmitOptionalMember(onAnOptional, call.Arguments, builtIn);
            return;
        }

        if (CilBuiltIns.IsOnAMoment(builtIn)
            || CilBuiltIns.IsOnAFile(builtIn)
            || CilBuiltIns.IsOnAGenerator(builtIn))
        {
            if (call.Callee is not MemberExpr provided)
            {
                throw Unhandled($"'{builtIn}' reached through nothing");
            }

            EmitProvidedMember(provided, call.Arguments, builtIn);
            return;
        }

        switch (builtIn)
        {
            case BuiltInId.ConsoleWrite:
            case BuiltInId.ConsoleWriteLine:
                EmitConsoleWrite(call, newline: builtIn == BuiltInId.ConsoleWriteLine);
                break;

            case BuiltInId.ConsoleRead:
                _il.Emit(OpCodes.Call, ReadLine);
                break;

            // Written on a value rather than taking one, so the receiver is what is loaded. The
            // runtime's rather than the framework's, for the same reason Console is: how a value
            // reads is the language's decision, and the two answers differ.
            case BuiltInId.ModelToString:
                EmitAsObject(ReceiverOf(call, builtIn));
                _il.Emit(OpCodes.Call, ToDisplayString);
                break;

            // The other question: not whether two values hold the same thing, but whether there
            // is one of them. The one place identity is what is being asked about, so it is the
            // one place the framework's own answer is the right one.
            case BuiltInId.ReferenceEquals:
                EmitAsObject(call.Arguments[0]);
                EmitAsObject(call.Arguments[1]);
                _il.Emit(OpCodes.Call, SameObject);
                break;

            // A number written by a pattern. The receiver is the number, and which of the two
            // runtime methods it reaches is decided by the type it was written on rather than by
            // the pattern, which says nothing about what it is formatting.
            case BuiltInId.IntegerFormat:
            case BuiltInId.RealFormat:
            case BuiltInId.FloatFormat:
                EmitExpression(ReceiverOf(call, builtIn));
                EmitExpression(call.Arguments[0]);
                _il.Emit(OpCodes.Call, FormatFor(builtIn));
                break;

            // Crossing between the two kinds of decimal-point number, each one call. Going out
            // always answers and loses digits; coming back can fail three ways, and the runtime
            // says which in the language's own words.
            case BuiltInId.RealToFloat:
            case BuiltInId.FloatToReal:
                EmitExpression(ReceiverOf(call, builtIn));
                _il.Emit(
                    OpCodes.Call,
                    builtIn == BuiltInId.RealToFloat ? RealAsFloat : FloatAsReal);
                break;

            // From a whole number, which is the one crossing that is an instruction: a long
            // widens to binary floating point without a call, and cannot fail.
            case BuiltInId.IntegerToFloat:
                EmitExpression(ReceiverOf(call, builtIn));
                _il.Emit(OpCodes.Conv_R8);
                break;

            // The one crossing that is no instructions at all. A member of an enumeration is
            // represented as its ordinal, so the value already on the stack is the answer and
            // the type it counted as was only ever metadata.
            case BuiltInId.EnumerationToInteger:
                EmitExpression(ReceiverOf(call, builtIn));
                break;

            // The same walk '==' is, written as a call. Both sides are boxed rather than left as
            // they are, since a number reaching it has to arrive as something to look inside.
            case BuiltInId.ModelEquals:
                EmitAsObject(ReceiverOf(call, builtIn));
                EmitAsObject(call.Arguments[0]);
                _il.Emit(OpCodes.Call, DeepEquals);
                break;

            case BuiltInId.ExceptionMessage:
                EmitExceptionMessage(
                    call.Callee as MemberExpr
                    ?? throw Unhandled($"'{builtIn}' reached through nothing"));
                break;

            default:
                throw Unhandled($"a call to {CilBuiltIns.NameOf(builtIn)}");
        }
    }

    /// <summary>
    /// The value a member was written on. A member reached through nothing is not something the
    /// checker produces, so this says so rather than emitting a sequence with a hole in it.
    /// </summary>
    private Expression ReceiverOf(CallExpr call, BuiltInId builtIn) =>
        call.Callee is MemberExpr member
            ? member.Receiver
            : throw Unhandled($"'{builtIn}' reached through nothing");

    private void EmitConsoleWrite(CallExpr call, bool newline)
    {
        if (call.Arguments.Count == 0)
        {
            if (newline)
            {
                _il.Emit(OpCodes.Call, WriteLineOfNothing);
                return;
            }

            _il.Emit(OpCodes.Ldstr, string.Empty);
            _il.Emit(OpCodes.Call, WriteOfEmpty);
            return;
        }

        EmitAsObject(call.Arguments[0]);
        _il.Emit(OpCodes.Call, newline ? WriteLineOfObject : WriteOfObject);
    }

    // ---- The runtime methods emitted code calls ---------------------------------------------

    private static readonly MethodInfo ConcatTwo =
        typeof(string).GetMethod(nameof(string.Concat), [typeof(string), typeof(string)])!;

    private static readonly MethodInfo ToDisplayString =
        typeof(ModelOperations).GetMethod(
            nameof(ModelOperations.ToDisplayString), [typeof(object)])!;

    private static readonly MethodInfo SameObject =
        typeof(object).GetMethod(
            nameof(ReferenceEquals), [typeof(object), typeof(object)])!;

    private static readonly MethodInfo DeepEquals =
        typeof(ModelOperations).GetMethod(
            nameof(ModelOperations.DeepEquals), [typeof(object), typeof(object)])!;

    private static readonly MethodInfo StringEquals =
        typeof(string).GetMethod(nameof(string.Equals), [typeof(string), typeof(string)])!;

    private static readonly MethodInfo RealEquals =
        typeof(decimal).GetMethod("op_Equality", [typeof(decimal), typeof(decimal)])!;

    private static readonly System.Reflection.ConstructorInfo RealFromInteger =
        typeof(decimal).GetConstructor([typeof(long)])!;

    private static readonly MethodInfo RealNegate =
        typeof(decimal).GetMethod("op_UnaryNegation", [typeof(decimal)])!;

    private static readonly MethodInfo FormatInteger =
        typeof(ProfiCText).GetMethod(
            nameof(ProfiCText.Format), [typeof(long), typeof(string)])!;

    private static readonly MethodInfo FormatReal =
        typeof(ProfiCText).GetMethod(
            nameof(ProfiCText.Format), [typeof(decimal), typeof(string)])!;

    private static readonly MethodInfo FormatFloat =
        typeof(ProfiCText).GetMethod(
            nameof(ProfiCText.Format), [typeof(double), typeof(string)])!;

    /// <summary>Which of the three <c>Format</c> overloads a number reaches, by its type.</summary>
    private static MethodInfo FormatFor(BuiltInId id) => id switch
    {
        BuiltInId.IntegerFormat => FormatInteger,
        BuiltInId.RealFormat => FormatReal,
        _ => FormatFloat,
    };

    private static readonly MethodInfo RealAsFloat =
        typeof(ProfiCArithmetic).GetMethod(
            nameof(ProfiCArithmetic.ToFloat), [typeof(decimal)])!;

    private static readonly MethodInfo FloatAsReal =
        typeof(ProfiCArithmetic).GetMethod(
            nameof(ProfiCArithmetic.ToReal), [typeof(double)])!;

    private static readonly MethodInfo WriteOfObject =
        typeof(ProfiCConsole).GetMethod(nameof(ProfiCConsole.Write), [typeof(object)])!;

    private static readonly MethodInfo WriteLineOfObject =
        typeof(ProfiCConsole).GetMethod(nameof(ProfiCConsole.WriteLine), [typeof(object)])!;

    private static readonly MethodInfo WriteLineOfNothing =
        typeof(ProfiCConsole).GetMethod(nameof(ProfiCConsole.WriteLine), Type.EmptyTypes)!;

    /// <summary>
    /// <para>Reading a line, which already gives back an optional.</para>
    /// <para>The boundary where a .NET reference that may be absent becomes an optional and
    /// stops being null — so the emitter needs no wrapping of its own, and this is the one
    /// built-in that hands an empty optional to a program that asked for nothing else.</para>
    /// </summary>
    private static readonly MethodInfo ReadLine =
        typeof(ProfiCConsole).GetMethod(nameof(ProfiCConsole.Read), Type.EmptyTypes)!;

    /// <summary>
    /// Writing nothing without ending the line does nothing at all, and the runtime has no
    /// overload for it. Written as the empty string, which is what it amounts to.
    /// </summary>
    private static readonly MethodInfo WriteOfEmpty =
        typeof(ProfiCConsole).GetMethod(nameof(ProfiCConsole.Write), [typeof(object)])!;
}
