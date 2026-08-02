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

            default:
                throw Unhandled(expression.GetType().Name);
        }
    }

    /// <summary>Reads a field through the instance it belongs to, or straight off the type.</summary>
    private void EmitMemberRead(MemberExpr member)
    {
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
        if (_model.GetType(construction) is not DeclaredTypeSymbol type
            || !_types.ContainsKey(type))
        {
            throw Unhandled($"constructing '{construction.TypeName}'");
        }

        foreach (Expression argument in construction.Arguments)
        {
            EmitExpression(argument);
        }

        _il.Emit(OpCodes.Newobj, ConstructorFor(type, construction));
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

            case double value:
                _il.Emit(OpCodes.Ldc_R8, value);
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
                break;

            case ParameterSymbol parameter when _parameters.TryGetValue(parameter, out int at):
                _il.Emit(OpCodes.Ldarg, at);
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

                break;

            default:
                throw Unhandled($"the name '{name.Name}'");
        }
    }

    private void EmitUnary(UnaryExpr unary)
    {
        EmitExpression(unary.Operand);

        switch (unary.Operator)
        {
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
        }

        EmitExpression(binary.Left);
        EmitExpression(binary.Right);

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

            case BinaryOperator.Equal:
                EmitEquality(binary, wanted: true);
                break;

            case BinaryOperator.NotEqual:
                EmitEquality(binary, wanted: false);
                break;

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
    /// <para>Equality, which is reference comparison on a string and a bit comparison on
    /// anything else.</para>
    /// <para><c>ceq</c> on two strings compares references, so two equal strings built
    /// differently would come out unequal — which is not what <c>==</c> means in Profi-C, where
    /// a string is a value to a reader.</para>
    /// </summary>
    private void EmitEquality(BinaryExpr binary, bool wanted)
    {
        if (IsString(binary.Left))
        {
            _il.Emit(OpCodes.Call, StringEquals);
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

    private void EmitAsObject(Expression expression)
    {
        EmitExpression(expression);

        if (_model.GetType(expression) is { } type
            && CilTypes.Of(type) is { IsValueType: true } clr)
        {
            _il.Emit(OpCodes.Box, clr);
        }
    }

    private bool IsString(BinaryExpr binary) => IsString(binary.Left) || IsString(binary.Right);

    private bool IsString(Expression expression) =>
        ReferenceEquals(_model.GetType(expression), PrimitiveType.String);

    private void EmitConversion(ConversionExpr conversion)
    {
        EmitExpression(conversion.Operand);

        switch (conversion.Operation)
        {
            case ConversionOperation.IntegerToReal:
                _il.Emit(OpCodes.Conv_R8);
                break;

            default:
                throw Unhandled($"the conversion {conversion.Operation}");
        }
    }

    private void EmitCall(CallExpr call)
    {
        if (_model.GetBuiltIn(call.Callee) is { } builtIn)
        {
            EmitBuiltIn(call, builtIn);
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
            EmitExpression(argument);
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
        switch (builtIn)
        {
            case BuiltInId.ConsoleWrite:
            case BuiltInId.ConsoleWriteLine:
                EmitConsoleWrite(call, newline: builtIn == BuiltInId.ConsoleWriteLine);
                break;

            default:
                throw Unhandled($"a call to {CilBuiltIns.NameOf(builtIn)}");
        }
    }

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

    private static readonly MethodInfo StringEquals =
        typeof(string).GetMethod(nameof(string.Equals), [typeof(string), typeof(string)])!;

    private static readonly MethodInfo WriteOfObject =
        typeof(ProfiCConsole).GetMethod(nameof(ProfiCConsole.Write), [typeof(object)])!;

    private static readonly MethodInfo WriteLineOfObject =
        typeof(ProfiCConsole).GetMethod(nameof(ProfiCConsole.WriteLine), [typeof(object)])!;

    private static readonly MethodInfo WriteLineOfNothing =
        typeof(ProfiCConsole).GetMethod(nameof(ProfiCConsole.WriteLine), Type.EmptyTypes)!;

    /// <summary>
    /// Writing nothing without ending the line does nothing at all, and the runtime has no
    /// overload for it. Written as the empty string, which is what it amounts to.
    /// </summary>
    private static readonly MethodInfo WriteOfEmpty =
        typeof(ProfiCConsole).GetMethod(nameof(ProfiCConsole.Write), [typeof(object)])!;
}
