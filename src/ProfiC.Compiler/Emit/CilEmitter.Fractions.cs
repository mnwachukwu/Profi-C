using System.Reflection;
using System.Reflection.Emit;
using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Semantics;
using ProfiC.Runtime;

namespace ProfiC.Compiler.Emit;

/// <summary>
/// <para>Fractions, which become <see cref="Fraction"/> — the very struct the interpreter holds.
/// </para>
/// <para><b>Every operation is a call, as a real's is</b>, and for the same reason: the CLR knows
/// integers and binary floating point, and a fraction is neither. What differs is that a
/// fraction's operators are the runtime's own rather than the framework's, so the exactness and
/// the reducing are already inside them and nothing here has to know how a ratio is kept.</para>
/// <para>A literal is two whole numbers and a constructor. There is nothing to fold at emit time:
/// the constructor reduces, so <c>2|4</c> and <c>1|2</c> arrive as one value without the emitter
/// deciding anything.</para>
/// </summary>
public sealed partial class CilEmitter
{
    /// <summary>
    /// Pushes a fraction literal: its two parts, then the constructor that reduces them.
    /// </summary>
    private void EmitFraction(Fraction value)
    {
        _il.Emit(OpCodes.Ldc_I8, value.Numerator);
        _il.Emit(OpCodes.Ldc_I8, value.Denominator);
        _il.Emit(OpCodes.Newobj, FractionFromParts);
    }

    /// <summary>
    /// <para>The method a fraction operator means.</para>
    /// <para>All of them, comparison included — a fraction has no instruction of its own any more
    /// than a real does. The remainder is here too, which a reader may not expect a ratio to have:
    /// it is what one leaves after dividing a whole number of times.</para>
    /// </summary>
    private static MethodInfo? FractionMethod(BinaryOperator op) => op switch
    {
        BinaryOperator.Add => OnFractions("op_Addition"),
        BinaryOperator.Subtract => OnFractions("op_Subtraction"),
        BinaryOperator.Multiply => OnFractions("op_Multiply"),
        BinaryOperator.Divide => OnFractions("op_Division"),
        BinaryOperator.Remainder => OnFractions("op_Modulus"),

        BinaryOperator.LessThan => OnFractions("op_LessThan"),
        BinaryOperator.GreaterThan => OnFractions("op_GreaterThan"),
        BinaryOperator.LessThanOrEqual => OnFractions("op_LessThanOrEqual"),
        BinaryOperator.GreaterThanOrEqual => OnFractions("op_GreaterThanOrEqual"),

        _ => null,
    };

    private static MethodInfo OnFractions(string name) =>
        typeof(Fraction).GetMethod(name, [typeof(Fraction), typeof(Fraction)])!;

    /// <summary>
    /// <para>The members a fraction answers, each one call on the runtime's own.</para>
    /// <para><c>ToReal</c> and <c>ToFloat</c> are the two conversions written out — a third has no
    /// decimal form that ends, in tens or in binary — and <c>Reciprocal</c> is the one a real
    /// cannot do exactly at all.</para>
    /// </summary>
    private void EmitFractionMember(MemberExpr member, IReadOnlyList<Expression> arguments, BuiltInId id)
    {
        // The one member here written on a float rather than on a fraction. It takes its
        // receiver by value, so it is settled before the address is taken below.
        if (id == BuiltInId.FloatToFraction)
        {
            EmitExpression(member.Receiver);
            _il.Emit(OpCodes.Call, FractionFromFloat);
            return;
        }

        EmitAddressOfFraction(member.Receiver);

        switch (id)
        {
            case BuiltInId.FractionToReal:
                _il.Emit(OpCodes.Call, FractionMember(nameof(Fraction.ToReal)));
                return;

            case BuiltInId.FractionToFloat:
                _il.Emit(OpCodes.Call, FractionMember(nameof(Fraction.ToFloat)));
                return;

            case BuiltInId.FractionReciprocal:
                _il.Emit(OpCodes.Call, FractionMember(nameof(Fraction.Reciprocal)));
                return;

            // Read rather than worked out: both are already what a fraction is kept in, and both
            // are the width an integer is, so nothing is converted on the way back.
            case BuiltInId.FractionNumerator:
                _il.Emit(OpCodes.Call, FractionMember($"get_{nameof(Fraction.Numerator)}"));
                return;

            case BuiltInId.FractionDenominator:
                _il.Emit(OpCodes.Call, FractionMember($"get_{nameof(Fraction.Denominator)}"));
                return;

            // Formatted through its real value, which is what a pattern describes: there is no
            // pattern language for a ratio, and "0.75" is what a reader asking for two places
            // after the point meant by it.
            case BuiltInId.FractionFormat:
                _il.Emit(OpCodes.Call, FractionMember(nameof(Fraction.ToReal)));
                EmitExpression(arguments[0]);
                _il.Emit(OpCodes.Call, FormatReal);
                return;

            default:
                throw Unhandled($"the fraction member '{id}'");
        }
    }

    /// <summary>
    /// <para>Puts a fraction somewhere with an address, since its members are on a struct.</para>
    /// <para>A local of its own rather than an address taken of whatever was written, for the
    /// same reason an optional needs one: the receiver may be any expression at all, and only a
    /// local is guaranteed to be somewhere that can be pointed at.</para>
    /// </summary>
    private void EmitAddressOfFraction(Expression receiver)
    {
        EmitExpression(receiver);
        TakeAddressOfFraction();
    }

    /// <summary>
    /// <para>The address of the fraction already on the stack.</para>
    /// <para>A fraction's members are instance methods on a struct, so they are reached through
    /// a managed pointer rather than through the value — and a value has no address until it is
    /// put somewhere that has one. Held apart from the call above because a fraction does not
    /// always arrive as an expression to emit: a conversion is handed one already computed.
    /// </para>
    /// </summary>
    private void TakeAddressOfFraction()
    {
        LocalBuilder slot = _il.DeclareLocal(typeof(Fraction));

        _il.Emit(OpCodes.Stloc, slot);
        _il.Emit(OpCodes.Ldloca, slot);
    }

    private static MethodInfo FractionMember(string name) =>
        typeof(Fraction).GetMethod(name, Type.EmptyTypes)!;

    private static readonly System.Reflection.ConstructorInfo FractionFromParts =
        typeof(Fraction).GetConstructor([typeof(long), typeof(long)])!;

    private static readonly MethodInfo FractionFromInteger =
        typeof(Fraction).GetMethod(nameof(Fraction.FromInteger), [typeof(long)])!;

    private static readonly MethodInfo FractionFromReal =
        typeof(Fraction).GetMethod(nameof(Fraction.FromReal), [typeof(decimal)])!;

    private static readonly MethodInfo FractionFromFloat =
        typeof(Fraction).GetMethod(nameof(Fraction.FromFloat), [typeof(double)])!;

    private static readonly MethodInfo FractionNegate =
        typeof(Fraction).GetMethod("op_UnaryNegation", [typeof(Fraction)])!;

    private static readonly MethodInfo FractionEquals =
        typeof(Fraction).GetMethod("op_Equality", [typeof(Fraction), typeof(Fraction)])!;

    /// <summary>Raising to a power, which keeps a fraction exact where the exponent is whole.</summary>
    private static readonly MethodInfo FractionPower =
        typeof(Fraction).GetMethod(nameof(Fraction.Pow), [typeof(Fraction), typeof(long)])!;

    /// <summary>
    /// <para><c>^</c>, which is the one operator whose two sides may be different types — the
    /// exponent is a count rather than a second value of the base's kind.</para>
    /// <para>Three shapes, and the checker has already settled which arrives. A whole exponent
    /// keeps the base's type, so an integer stays an integer and a fraction stays exact; anything
    /// else is a root, which has no exact form to keep and answers in reals.</para>
    /// </summary>
    private void EmitPower(BinaryExpr binary)
    {
        EmitExpression(binary.Left);
        EmitExpression(binary.Right);

        _il.Emit(OpCodes.Call, PowerMethod(_model.GetType(binary)));
    }

    private static MethodInfo PowerMethod(TypeSymbol? answer)
    {
        if (ReferenceEquals(answer, PrimitiveType.Integer))
        {
            return typeof(ProfiCArithmetic).GetMethod(
                nameof(ProfiCArithmetic.Power), [typeof(long), typeof(long)])!;
        }

        if (ReferenceEquals(answer, PrimitiveType.Fraction))
        {
            return FractionPower;
        }

        return typeof(ProfiCMath).GetMethod(
            nameof(ProfiCMath.Pow), [typeof(decimal), typeof(decimal)])!;
    }
}
