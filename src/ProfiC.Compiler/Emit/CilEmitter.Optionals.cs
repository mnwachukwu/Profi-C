using System.Reflection;
using System.Reflection.Emit;
using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Semantics;

namespace ProfiC.Compiler.Emit;

/// <summary>
/// <para>Optionals, which become <see cref="Runtime.Optional{T}"/>.</para>
/// <para>One shape whatever they hold. C# has two — a <c>Nullable</c> for a value type and the
/// reference itself for the rest — because it has a null to reuse; Profi-C does not, and the
/// single shape is what lets every member below be emitted without asking which kind it has.
/// </para>
/// <para><b>Nothing here checks presence.</b> Reading an optional the compiler cannot prove
/// present is <c>PC0401</c>, refused long before this, so <c>Value</c> is a plain read and the
/// exception behind it is for a claim that turned out false rather than a check somebody
/// forgot.</para>
/// </summary>
public sealed partial class CilEmitter
{
    /// <summary>
    /// <para>Wraps a present value into an optional.</para>
    /// <para>The conversion the checker records wherever a definite value is used where one that
    /// may be absent is wanted — an argument, a yield, an assignment. There is no unwrapping
    /// conversion to pair with it: going the other way is <c>Value</c>, which a reader writes.
    /// </para>
    /// </summary>
    private void EmitWrapOptional(ConversionExpr conversion)
    {
        if (_model.GetType(conversion) is not OptionalType optional)
        {
            throw Unhandled("wrapping something whose optional type is unknown");
        }

        Type built = TypeOf(optional, "an optional");

        EmitExpression(conversion.Operand);

        _il.Emit(OpCodes.Call, OptionalMethod(built, "Of"));
    }

    /// <summary>
    /// <para>The three members an optional has, and there are only three.</para>
    /// <para><c>HasValue</c> and <c>Value</c> are properties on the struct, so each needs the
    /// optional in a place that has an address — a struct member is called on a reference to it,
    /// not on a copy pushed by value.</para>
    /// </summary>
    private void EmitOptionalMember(
        MemberExpr member,
        IReadOnlyList<Expression> arguments,
        BuiltInId id)
    {
        // Reached on a receiver a guard has already narrowed, which is the ordinary shape:
        // 'if maybe.HasValue()' and then 'maybe.Value()' inside it. Reading the name has
        // already taken the value out, so there is no optional here any more and each member
        // answers about a value that is certainly present.
        if (_model.GetType(member.Receiver) is not OptionalType)
        {
            EmitNarrowedMember(member, arguments, id);
            return;
        }

        Type built = OptionalTypeOf(member.Receiver);

        switch (id)
        {
            case BuiltInId.OptionalHasValue:
                EmitAddressOf(member.Receiver, built);
                _il.Emit(OpCodes.Call, OptionalMethod(built, "get_HasValue"));
                return;

            case BuiltInId.OptionalValue:
                EmitAddressOf(member.Receiver, built);
                _il.Emit(OpCodes.Call, OptionalMethod(built, "get_Value"));
                return;

            case BuiltInId.OptionalOr:
                EmitOr(member, arguments[0], built);
                return;

            default:
                throw Unhandled($"the optional member '{id}'");
        }
    }

    /// <summary>
    /// <para>A member reached on an optional a guard has proved present.</para>
    /// <para>Each answers without looking: the value is there, so <c>HasValue</c> is true,
    /// <c>Value</c> is what was read, and <c>Or</c> never reaches its fallback. Writing any of
    /// them here is redundant rather than wrong, and a reader who does should get the same
    /// answer as one who does not.</para>
    /// </summary>
    private void EmitNarrowedMember(
        MemberExpr member,
        IReadOnlyList<Expression> arguments,
        BuiltInId id)
    {
        switch (id)
        {
            case BuiltInId.OptionalHasValue:
                _il.Emit(OpCodes.Ldc_I4_1);
                return;

            // Both are the value itself. Reading the name unwrapped it, so what is wanted is
            // exactly what emitting the receiver produces.
            case BuiltInId.OptionalValue:
            case BuiltInId.OptionalOr:
                EmitExpression(member.Receiver);
                return;

            default:
                throw Unhandled($"the optional member '{id}' on a narrowed receiver");
        }
    }

    /// <summary>
    /// <para><c>Or</c>, emitted as a branch rather than as a call.</para>
    /// <para><b>The fallback must not run unless it is needed</b>, which is the whole of what
    /// <c>Or</c> promises — and a call would have evaluated it before entering. The runtime's own
    /// <c>Or</c> takes a thunk for that reason, but building one means a lambda, and this is both
    /// simpler and free.</para>
    /// <para>Both forms are one sequence. Given a plain value the branch yields that value and
    /// the chain ends; given another optional it yields that optional and the chain goes on. What
    /// differs is only the type on each arm, which the checker has already made agree.</para>
    /// </summary>
    private void EmitOr(MemberExpr member, Expression fallback, Type built)
    {
        Label otherwise = _il.DefineLabel();
        Label done = _il.DefineLabel();

        LocalBuilder held = _il.DeclareLocal(built);

        EmitExpression(member.Receiver);
        _il.Emit(OpCodes.Stloc, held);

        _il.Emit(OpCodes.Ldloca, held);
        _il.Emit(OpCodes.Call, OptionalMethod(built, "get_HasValue"));
        _il.Emit(OpCodes.Brfalse, otherwise);

        // Present. Given an optional fallback the answer stays optional, so the whole thing is
        // what comes back rather than what it holds.
        if (_model.GetType(fallback) is OptionalType)
        {
            _il.Emit(OpCodes.Ldloc, held);
        }
        else
        {
            _il.Emit(OpCodes.Ldloca, held);
            _il.Emit(OpCodes.Call, OptionalMethod(built, "get_Value"));
        }

        _il.Emit(OpCodes.Br, done);

        _il.MarkLabel(otherwise);
        EmitExpression(fallback);

        _il.MarkLabel(done);
    }

    /// <summary>
    /// <para>Puts the optional somewhere with an address, since its members are on a struct.</para>
    /// <para>A local of its own rather than an address taken of whatever was written: the
    /// receiver may be any expression at all, and only a local is guaranteed to be somewhere
    /// that can be pointed at.</para>
    /// </summary>
    private void EmitAddressOf(Expression receiver, Type built)
    {
        LocalBuilder slot = _il.DeclareLocal(built);

        EmitExpression(receiver);
        _il.Emit(OpCodes.Stloc, slot);
        _il.Emit(OpCodes.Ldloca, slot);
    }

    private Type OptionalTypeOf(Expression expression) =>
        _model.GetType(expression) is OptionalType optional
            ? TypeOf(optional, "an optional")
            : throw Unhandled("reaching a member of something that is not an optional");

    /// <summary>
    /// A method on a constructed optional, reached the way the underlying type allows — the same
    /// two routes a set's members need, and for the same reason.
    /// </summary>
    private static MethodInfo OptionalMethod(Type built, string name)
    {
        MethodInfo definition = typeof(Runtime.Optional<>)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Single(method => method.Name == name);

        return HoldsATypeBeingBuilt(built)
            ? TypeBuilder.GetMethod(built, definition)
            : built.GetMethod(
                name,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)!;
    }
}
