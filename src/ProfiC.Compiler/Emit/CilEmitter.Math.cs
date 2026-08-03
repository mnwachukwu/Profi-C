using System.Reflection;
using System.Reflection.Emit;
using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Semantics;
using ProfiC.Runtime;

namespace ProfiC.Compiler.Emit;

/// <summary>
/// <para>What the language's <c>Math</c> answers, which is one call into
/// <see cref="ProfiCMath"/> for all but the two constants.</para>
/// <para><b>Not <c>System.Math</c>, even where the two agree today.</b> Four members genuinely
/// differ — a half rounds away from zero rather than to its even neighbor, <c>Floor</c> and its
/// family answer with an integer, a root is corrected where it is exact, and a factorial that
/// overflows says so — and a member with nothing to decide goes the same way anyway, so that no
/// part of <c>Math</c> can be reached one way by the interpreter and another here.</para>
/// </summary>
public sealed partial class CilEmitter
{
    /// <summary>
    /// <para>One member of <c>Math</c>: the arguments, then the call. Nothing is loaded ahead of
    /// them, since <c>Math</c> is a name rather than a value and every member of it is shared.
    /// </para>
    /// <para>The two written without parentheses — <c>Pi</c> and <c>E</c> — are constants, so each
    /// arrives as the number itself rather than as a field to read.</para>
    /// </summary>
    private void EmitMathMember(IReadOnlyList<Expression> arguments, BuiltInId id)
    {
        switch (id)
        {
            // A decimal is not a number the CLR loads with one instruction — there is no
            // 'ldc' for it — so each is built from the five parts a decimal is made of.
            case BuiltInId.MathPi:
                EmitReal(ProfiCMath.Pi);
                return;

            case BuiltInId.MathE:
                EmitReal(ProfiCMath.E);
                return;

            default:
                foreach (Expression argument in arguments)
                {
                    EmitExpression(argument);
                }

                _il.Emit(OpCodes.Call, MathMethod(id));
                return;
        }
    }

    /// <summary>
    /// <para>The runtime method behind one member of <c>Math</c>.</para>
    /// <para>Several share a name and are told apart by what they take — <c>Abs</c> has three
    /// forms and <c>Round</c> four — so choosing between them is one question answered in one
    /// place, the same as the string family. The fraction forms are absent because nothing can
    /// reach them: there is no way to make a fraction in an emitted program yet.</para>
    /// </summary>
    private static MethodInfo MathMethod(BuiltInId id) => id switch
    {
        BuiltInId.MathSqrt => Arithmetic(nameof(ProfiCMath.Sqrt), typeof(decimal)),
        BuiltInId.MathCbrt => Arithmetic(nameof(ProfiCMath.Cbrt), typeof(decimal)),
        BuiltInId.MathRoot => Arithmetic(nameof(ProfiCMath.Root), typeof(decimal), typeof(decimal)),
        BuiltInId.MathPow => Arithmetic(nameof(ProfiCMath.Pow), typeof(decimal), typeof(decimal)),
        BuiltInId.MathFactorial => Arithmetic(nameof(ProfiCMath.Factorial), typeof(long)),

        BuiltInId.MathLog => Arithmetic(nameof(ProfiCMath.Log), typeof(decimal)),
        BuiltInId.MathLogInBase =>
            Arithmetic(nameof(ProfiCMath.Log), typeof(decimal), typeof(decimal)),
        BuiltInId.MathLog10 => Arithmetic(nameof(ProfiCMath.Log10), typeof(decimal)),
        BuiltInId.MathLog2 => Arithmetic(nameof(ProfiCMath.Log2), typeof(decimal)),

        BuiltInId.MathSin => Arithmetic(nameof(ProfiCMath.Sin), typeof(decimal)),
        BuiltInId.MathCos => Arithmetic(nameof(ProfiCMath.Cos), typeof(decimal)),
        BuiltInId.MathTan => Arithmetic(nameof(ProfiCMath.Tan), typeof(decimal)),
        BuiltInId.MathAsin => Arithmetic(nameof(ProfiCMath.Asin), typeof(decimal)),
        BuiltInId.MathAcos => Arithmetic(nameof(ProfiCMath.Acos), typeof(decimal)),
        BuiltInId.MathAtan => Arithmetic(nameof(ProfiCMath.Atan), typeof(decimal)),
        BuiltInId.MathAtan2 =>
            Arithmetic(nameof(ProfiCMath.Atan2), typeof(decimal), typeof(decimal)),

        BuiltInId.MathSinh => Arithmetic(nameof(ProfiCMath.Sinh), typeof(decimal)),
        BuiltInId.MathCosh => Arithmetic(nameof(ProfiCMath.Cosh), typeof(decimal)),
        BuiltInId.MathTanh => Arithmetic(nameof(ProfiCMath.Tanh), typeof(decimal)),
        BuiltInId.MathAsinh => Arithmetic(nameof(ProfiCMath.Asinh), typeof(decimal)),
        BuiltInId.MathAcosh => Arithmetic(nameof(ProfiCMath.Acosh), typeof(decimal)),
        BuiltInId.MathAtanh => Arithmetic(nameof(ProfiCMath.Atanh), typeof(decimal)),

        BuiltInId.MathAbsInteger => Arithmetic(nameof(ProfiCMath.Abs), typeof(long)),
        BuiltInId.MathAbsReal => Arithmetic(nameof(ProfiCMath.Abs), typeof(decimal)),

        BuiltInId.MathFloorReal => Arithmetic(nameof(ProfiCMath.Floor), typeof(decimal)),
        BuiltInId.MathCeilingReal => Arithmetic(nameof(ProfiCMath.Ceiling), typeof(decimal)),
        BuiltInId.MathRoundReal => Arithmetic(nameof(ProfiCMath.Round), typeof(decimal)),
        BuiltInId.MathRoundRealPlaces =>
            Arithmetic(nameof(ProfiCMath.Round), typeof(decimal), typeof(long)),

        BuiltInId.MathMinInteger => Arithmetic(nameof(ProfiCMath.Min), typeof(long), typeof(long)),
        BuiltInId.MathMinReal => Arithmetic(nameof(ProfiCMath.Min), typeof(decimal), typeof(decimal)),
        BuiltInId.MathMaxInteger => Arithmetic(nameof(ProfiCMath.Max), typeof(long), typeof(long)),
        BuiltInId.MathMaxReal => Arithmetic(nameof(ProfiCMath.Max), typeof(decimal), typeof(decimal)),

        // The binary half of each pair, which is the framework's own answer under the
        // language's name — a float is what those functions were written for.
        BuiltInId.MathSqrtFloat => Arithmetic(nameof(ProfiCMath.Sqrt), typeof(double)),
        BuiltInId.MathCbrtFloat => Arithmetic(nameof(ProfiCMath.Cbrt), typeof(double)),
        BuiltInId.MathRootFloat =>
            Arithmetic(nameof(ProfiCMath.Root), typeof(double), typeof(double)),
        BuiltInId.MathPowFloat =>
            Arithmetic(nameof(ProfiCMath.Pow), typeof(double), typeof(double)),

        BuiltInId.MathLogFloat => Arithmetic(nameof(ProfiCMath.Log), typeof(double)),
        BuiltInId.MathLogInBaseFloat =>
            Arithmetic(nameof(ProfiCMath.Log), typeof(double), typeof(double)),
        BuiltInId.MathLog10Float => Arithmetic(nameof(ProfiCMath.Log10), typeof(double)),
        BuiltInId.MathLog2Float => Arithmetic(nameof(ProfiCMath.Log2), typeof(double)),

        BuiltInId.MathSinFloat => Arithmetic(nameof(ProfiCMath.Sin), typeof(double)),
        BuiltInId.MathCosFloat => Arithmetic(nameof(ProfiCMath.Cos), typeof(double)),
        BuiltInId.MathTanFloat => Arithmetic(nameof(ProfiCMath.Tan), typeof(double)),
        BuiltInId.MathAsinFloat => Arithmetic(nameof(ProfiCMath.Asin), typeof(double)),
        BuiltInId.MathAcosFloat => Arithmetic(nameof(ProfiCMath.Acos), typeof(double)),
        BuiltInId.MathAtanFloat => Arithmetic(nameof(ProfiCMath.Atan), typeof(double)),
        BuiltInId.MathAtan2Float =>
            Arithmetic(nameof(ProfiCMath.Atan2), typeof(double), typeof(double)),

        BuiltInId.MathSinhFloat => Arithmetic(nameof(ProfiCMath.Sinh), typeof(double)),
        BuiltInId.MathCoshFloat => Arithmetic(nameof(ProfiCMath.Cosh), typeof(double)),
        BuiltInId.MathTanhFloat => Arithmetic(nameof(ProfiCMath.Tanh), typeof(double)),
        BuiltInId.MathAsinhFloat => Arithmetic(nameof(ProfiCMath.Asinh), typeof(double)),
        BuiltInId.MathAcoshFloat => Arithmetic(nameof(ProfiCMath.Acosh), typeof(double)),
        BuiltInId.MathAtanhFloat => Arithmetic(nameof(ProfiCMath.Atanh), typeof(double)),

        BuiltInId.MathAbsFloat => Arithmetic(nameof(ProfiCMath.Abs), typeof(double)),
        BuiltInId.MathFloorFloat => Arithmetic(nameof(ProfiCMath.Floor), typeof(double)),
        BuiltInId.MathCeilingFloat => Arithmetic(nameof(ProfiCMath.Ceiling), typeof(double)),
        BuiltInId.MathRoundFloat => Arithmetic(nameof(ProfiCMath.Round), typeof(double)),
        BuiltInId.MathRoundFloatPlaces =>
            Arithmetic(nameof(ProfiCMath.Round), typeof(double), typeof(long)),
        BuiltInId.MathMinFloat =>
            Arithmetic(nameof(ProfiCMath.Min), typeof(double), typeof(double)),
        BuiltInId.MathMaxFloat =>
            Arithmetic(nameof(ProfiCMath.Max), typeof(double), typeof(double)),

        // The exact forms. There are no transcendental ones and cannot be: a square root leaves
        // the rationals, so those answer in reals whatever they were given.
        BuiltInId.MathAbsFraction => Arithmetic(nameof(ProfiCMath.Abs), typeof(Fraction)),
        BuiltInId.MathFloorFraction => Arithmetic(nameof(ProfiCMath.Floor), typeof(Fraction)),
        BuiltInId.MathCeilingFraction => Arithmetic(nameof(ProfiCMath.Ceiling), typeof(Fraction)),
        BuiltInId.MathRoundFraction => Arithmetic(nameof(ProfiCMath.Round), typeof(Fraction)),
        BuiltInId.MathMinFraction =>
            Arithmetic(nameof(ProfiCMath.Min), typeof(Fraction), typeof(Fraction)),
        BuiltInId.MathMaxFraction =>
            Arithmetic(nameof(ProfiCMath.Max), typeof(Fraction), typeof(Fraction)),

        _ => throw new InvalidOperationException($"No runtime method stands behind '{id}'."),
    };

    private static MethodInfo Arithmetic(string name, params Type[] taking) =>
        typeof(ProfiCMath).GetMethod(name, taking)
        ?? throw new InvalidOperationException($"The runtime has no '{name}' taking those.");

    /// <summary>
    /// <para>Pushes a <c>real</c>, which takes rather more than pushing any other number.</para>
    /// <para>A decimal is a library type rather than one the CLR knows, so there is no <c>ldc</c>
    /// that loads one. What there is is the constructor the C# compiler uses for the same job:
    /// four parts of the number itself, whether it is negative, and where the point sits.</para>
    /// </summary>
    /// <summary>
    /// <para>What a primitive knows about itself, which is a constant in every case.</para>
    /// <para>Emitted as the number rather than as a read of a field somewhere, so a bound costs
    /// nothing at run time and needs nothing of the runtime. The three only a float has are the
    /// values its own arithmetic produces, so <c>1.0f / 0.0f == Float.Infinity</c> is true
    /// because both sides really are the same bits.</para>
    /// </summary>
    private void EmitBound(BuiltInId id)
    {
        switch (id)
        {
            case BuiltInId.IntegerMaxValue: _il.Emit(OpCodes.Ldc_I8, long.MaxValue); return;
            case BuiltInId.IntegerMinValue: _il.Emit(OpCodes.Ldc_I8, long.MinValue); return;

            case BuiltInId.RealMaxValue: EmitReal(decimal.MaxValue); return;
            case BuiltInId.RealMinValue: EmitReal(decimal.MinValue); return;

            case BuiltInId.FloatMaxValue: _il.Emit(OpCodes.Ldc_R8, double.MaxValue); return;
            case BuiltInId.FloatMinValue: _il.Emit(OpCodes.Ldc_R8, double.MinValue); return;
            case BuiltInId.FloatInfinity:
                _il.Emit(OpCodes.Ldc_R8, double.PositiveInfinity);
                return;
            case BuiltInId.FloatNegativeInfinity:
                _il.Emit(OpCodes.Ldc_R8, double.NegativeInfinity);
                return;
            case BuiltInId.FloatNotANumber: _il.Emit(OpCodes.Ldc_R8, double.NaN); return;

            case BuiltInId.StringEmpty: _il.Emit(OpCodes.Ldstr, string.Empty); return;

            default:
                throw Unhandled($"the bound '{id}'");
        }
    }

    private void EmitReal(decimal value)
    {
        int[] parts = decimal.GetBits(value);

        _il.Emit(OpCodes.Ldc_I4, parts[0]);
        _il.Emit(OpCodes.Ldc_I4, parts[1]);
        _il.Emit(OpCodes.Ldc_I4, parts[2]);
        _il.Emit(OpCodes.Ldc_I4, (parts[3] & unchecked((int)0x80000000)) != 0 ? 1 : 0);
        _il.Emit(OpCodes.Ldc_I4, (parts[3] >> 16) & 0xFF);
        _il.Emit(OpCodes.Newobj, RealFromParts);
    }

    private static readonly System.Reflection.ConstructorInfo RealFromParts =
        typeof(decimal).GetConstructor(
            [typeof(int), typeof(int), typeof(int), typeof(bool), typeof(byte)])!;
}
