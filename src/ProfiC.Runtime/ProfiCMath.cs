namespace ProfiC.Runtime;

/// <summary>
/// <para>What the language's <c>Math</c> does, in one place both engines call.</para>
/// <para>Much of it is the framework's under another name, and that is the point: a member with
/// nothing to decide still belongs here, so that no part of <c>Math</c> is reached one way by the
/// interpreter and another by an emitted program. The four that genuinely differ are worth naming
/// —</para>
/// <list type="bullet">
/// <item><description><b>A half rounds away from zero</b>, the rule taught in school. .NET rounds
/// a half to its even neighbor, so 2.5 comes back as 2 there and as 3 here.</description></item>
/// <item><description><b>Floor, Ceiling and Round yield an integer</b>, since each lands on a
/// whole number and giving back a real with nothing after the point would only invite a
/// conversion.</description></item>
/// <item><description><b>A root is corrected where it is exact</b>, so that the cube root of 27
/// prints as 3 on every machine.</description></item>
/// <item><description><b>Factorial overflows loudly</b> rather than wrapping into a smaller wrong
/// answer.</description></item>
/// </list>
/// <para><b>Each member comes in two, one for <c>real</c> and one for <c>float</c>.</b> The ones a
/// decimal can do exactly — its sign, its two roundings, its bounds — are written twice and mean
/// the same thing. The rest cannot be exact in any base: there is no decimal square root of two
/// any more than there is a binary one, so the real form works in binary and converts back, which
/// .NET rounds to fifteen significant digits. That is fewer digits than a float shows and every
/// one of them is true, which is the better half of the trade for somebody learning.</para>
/// </summary>
public static class ProfiCMath
{
    /// <summary>
    /// <para>The ratio of a circle to its diameter, to as many digits as a real holds.</para>
    /// <para>More of them than a float can carry, which is worth having: a reader who prints it
    /// sees digits that are all correct, rather than a tail of noise.</para>
    /// </summary>
    public const decimal Pi = 3.1415926535897932384626433833m;

    /// <summary>The base of the natural logarithm, to the same depth.</summary>
    public const decimal E = 2.7182818284590452353602874714m;

    // ---- What a decimal does exactly ----------------------------------------------------

    public static long Abs(long value) => Math.Abs(value);

    public static decimal Abs(decimal value) => Math.Abs(value);

    public static double Abs(double value) => Math.Abs(value);

    public static Fraction Abs(Fraction value) => Fraction.Abs(value);

    /// <summary>
    /// <para>Refuses a float that names no number, before it is rounded to one.</para>
    /// <para>Only a float reaches here. A decimal and a fraction have no infinity and no
    /// not-a-number between them, so the whole question belongs to the one type that does.</para>
    /// <para><b>The conversion would otherwise succeed and be wrong.</b> Narrowing a floating
    /// point value to a whole one saturates rather than failing, so an infinity arrives as the
    /// largest integer and a not-a-number as zero — answers indistinguishable from a real count
    /// that happens to be large, and reached without anything being reported. Crossing the same
    /// gap by another route already stops: <c>ToReal</c> and <c>ToFraction</c> both refuse an
    /// infinity, on the grounds that the type being asked for has nothing to hold it. An integer
    /// has no more to hold it with, so rounding refuses it too.</para>
    /// </summary>
    private static double Roundable(double value, string what)
    {
        if (double.IsNaN(value))
        {
            throw new ArgumentException(
                $"A value that is not a number has no whole number to be {what} to. Only a "
                + "float has one at all, so there is nothing here to round.");
        }

        if (double.IsInfinity(value))
        {
            throw new ArgumentException(
                $"An infinity has no whole number to be {what} to. A float carries on past "
                + "every integer, so there is no whole number here for one to hold.");
        }

        return value;
    }

    /// <summary>The whole number at or below this one.</summary>
    public static long Floor(decimal value) => (long)Math.Floor(value);

    /// <inheritdoc cref="Floor(decimal)"/>
    public static long Floor(double value) => (long)Math.Floor(Roundable(value, "rounded down"));

    /// <inheritdoc cref="Floor(decimal)"/>
    public static long Floor(Fraction value) => Fraction.Floor(value);

    /// <summary>The whole number at or above this one.</summary>
    public static long Ceiling(decimal value) => (long)Math.Ceiling(value);

    /// <inheritdoc cref="Ceiling(decimal)"/>
    public static long Ceiling(double value) => (long)Math.Ceiling(Roundable(value, "rounded up"));

    /// <inheritdoc cref="Ceiling(decimal)"/>
    public static long Ceiling(Fraction value) => Fraction.Ceiling(value);

    /// <summary>
    /// The nearest whole number, a half going away from zero — so 2.5 rounds to 3 and -2.5 to -3.
    /// </summary>
    public static long Round(decimal value) => (long)Math.Round(value, MidpointRounding.AwayFromZero);

    /// <inheritdoc cref="Round(decimal)"/>
    public static long Round(double value) =>
        (long)Math.Round(Roundable(value, "rounded"), MidpointRounding.AwayFromZero);

    /// <inheritdoc cref="Round(decimal)"/>
    public static long Round(Fraction value) => Fraction.Round(value);

    /// <summary>
    /// The same rounding, kept to a number of places — which stays a real, since a number with
    /// places after the point is not a whole one.
    /// </summary>
    public static decimal Round(decimal value, long places) =>
        Math.Round(value, (int)places, MidpointRounding.AwayFromZero);

    /// <inheritdoc cref="Round(decimal, long)"/>
    public static double Round(double value, long places) =>
        Math.Round(value, (int)places, MidpointRounding.AwayFromZero);

    public static long Min(long first, long second) => Math.Min(first, second);

    public static decimal Min(decimal first, decimal second) => Math.Min(first, second);

    public static double Min(double first, double second) => Math.Min(first, second);

    public static Fraction Min(Fraction first, Fraction second) => first <= second ? first : second;

    public static long Max(long first, long second) => Math.Max(first, second);

    public static decimal Max(decimal first, decimal second) => Math.Max(first, second);

    public static double Max(double first, double second) => Math.Max(first, second);

    public static Fraction Max(Fraction first, Fraction second) => first >= second ? first : second;

    /// <summary>
    /// <para>Counts arrangements, so it counts in whole numbers.</para>
    /// <para>Twenty is the largest whose answer an integer holds; the twenty-first overflows,
    /// which is reported as an overflow rather than wrapping into a smaller wrong answer.</para>
    /// </summary>
    public static long Factorial(long n)
    {
        if (n < 0)
        {
            throw new ArgumentException(
                "A factorial counts arrangements, so it needs a whole number that is not negative.");
        }

        long result = 1;

        try
        {
            for (long i = 2; i <= n; i++)
            {
                result = checked(result * i);
            }
        }
        catch (OverflowException)
        {
            throw ArithmeticFailures.TooLargeForAnInteger();
        }

        return result;
    }

    // ---- What no base does exactly ------------------------------------------------------

    public static decimal Sqrt(decimal value) => AsReal(Math.Sqrt((double)value));

    public static double Sqrt(double value) => Math.Sqrt(value);

    /// <summary>The cube root, corrected where it is exact. <see cref="Root(double, double)"/>.</summary>
    public static decimal Cbrt(decimal value) => AsReal(Cbrt((double)value));

    /// <inheritdoc cref="Cbrt(decimal)"/>
    public static double Cbrt(double value) => Exactly(Math.Cbrt(value), value, 3);

    /// <inheritdoc cref="Root(double, double)"/>
    public static decimal Root(decimal value, decimal degree) =>
        AsReal(Root((double)value, (double)degree));

    /// <summary>
    /// <para>The nth root, which .NET spells only for n of 2 and 3.</para>
    /// <para>A negative number has a real root when n is odd — the cube root of -8 is -2 — and
    /// none at all when n is even, so the odd case is worked out from the magnitude and the sign
    /// put back. Left to <c>Pow</c> it would be NaN, since a fractional power of a negative is not
    /// a real number.</para>
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The degree is zero, which is a bad argument rather than an arithmetic failure — the same
    /// kind of mistake as a factorial of a negative number, and reported the same way.
    /// </exception>
    public static double Root(double value, double degree)
    {
        if (degree == 0)
        {
            throw new ArgumentException("A root of degree zero is not a number.");
        }

        if (value >= 0)
        {
            return Exactly(Math.Pow(value, 1.0 / degree), value, degree);
        }

        bool odd = degree == Math.Floor(degree) && Math.Abs(degree % 2) == 1;

        return odd
            ? Exactly(-Math.Pow(-value, 1.0 / degree), value, degree)
            : double.NaN;
    }

    public static decimal Pow(decimal value, decimal exponent) =>
        AsReal(Math.Pow((double)value, (double)exponent));

    public static double Pow(double value, double exponent) => Math.Pow(value, exponent);

    public static decimal Log(decimal value) => AsReal(Math.Log((double)value));

    public static double Log(double value) => Math.Log(value);

    public static decimal Log(decimal value, decimal numberBase) =>
        AsReal(Math.Log((double)value, (double)numberBase));

    public static double Log(double value, double numberBase) => Math.Log(value, numberBase);

    public static decimal Log10(decimal value) => AsReal(Math.Log10((double)value));

    public static double Log10(double value) => Math.Log10(value);

    public static decimal Log2(decimal value) => AsReal(Math.Log2((double)value));

    public static double Log2(double value) => Math.Log2(value);

    public static decimal Sin(decimal angle) => AsReal(Math.Sin((double)angle));

    public static double Sin(double angle) => Math.Sin(angle);

    public static decimal Cos(decimal angle) => AsReal(Math.Cos((double)angle));

    public static double Cos(double angle) => Math.Cos(angle);

    public static decimal Tan(decimal angle) => AsReal(Math.Tan((double)angle));

    public static double Tan(double angle) => Math.Tan(angle);

    public static decimal Asin(decimal ratio) => AsReal(Math.Asin((double)ratio));

    public static double Asin(double ratio) => Math.Asin(ratio);

    public static decimal Acos(decimal ratio) => AsReal(Math.Acos((double)ratio));

    public static double Acos(double ratio) => Math.Acos(ratio);

    public static decimal Atan(decimal ratio) => AsReal(Math.Atan((double)ratio));

    public static double Atan(double ratio) => Math.Atan(ratio);

    /// <summary>The angle to a point, which needs both sides to know which quarter it is in.</summary>
    public static decimal Atan2(decimal down, decimal across) =>
        AsReal(Math.Atan2((double)down, (double)across));

    /// <inheritdoc cref="Atan2(decimal, decimal)"/>
    public static double Atan2(double down, double across) => Math.Atan2(down, across);

    public static decimal Sinh(decimal value) => AsReal(Math.Sinh((double)value));

    public static double Sinh(double value) => Math.Sinh(value);

    public static decimal Cosh(decimal value) => AsReal(Math.Cosh((double)value));

    public static double Cosh(double value) => Math.Cosh(value);

    public static decimal Tanh(decimal value) => AsReal(Math.Tanh((double)value));

    public static double Tanh(double value) => Math.Tanh(value);

    public static decimal Asinh(decimal value) => AsReal(Math.Asinh((double)value));

    public static double Asinh(double value) => Math.Asinh(value);

    public static decimal Acosh(decimal value) => AsReal(Math.Acosh((double)value));

    public static double Acosh(double value) => Math.Acosh(value);

    public static decimal Atanh(decimal value) => AsReal(Math.Atanh((double)value));

    public static double Atanh(double value) => Math.Atanh(value);

    /// <summary>
    /// <para>Brings a binary answer back to a real.</para>
    /// <para>The conversion rounds to fifteen significant digits, which is exactly what is wanted:
    /// a double carries about that many that mean anything, and writing the rest out would show a
    /// reader digits the calculation never had.</para>
    /// <para><b>An infinity or a NaN has no real to become.</b> Those are a <c>float</c>'s answers
    /// and a real has none, so the square root of a negative real stops rather than producing a
    /// value that is not a number — which is the same choice dividing by zero makes, and one of
    /// the differences the two types exist to show.</para>
    /// </summary>
    private static decimal AsReal(double answer)
    {
        if (double.IsNaN(answer))
        {
            throw new ArgumentException(
                "That has no answer among the reals. A float would give back 'NaN' here and carry "
                + "on; a real has no such value, so the calculation stops instead.");
        }

        if (double.IsInfinity(answer))
        {
            throw ArithmeticFailures.TooLargeForAReal();
        }

        // A finite answer can still be past what a real holds, since binary floating point
        // reaches far further — ten to the three hundredth is a perfectly ordinary double and
        // nothing a decimal can carry. The conversion is what discovers it, and it says so in
        // the framework's words, so the failure is caught and said again in the language's.
        try
        {
            return (decimal)answer;
        }
        catch (OverflowException)
        {
            throw ArithmeticFailures.TooLargeForAReal();
        }
    }

    /// <summary>
    /// <para>Corrects a root to the whole number it should be, where there is one.</para>
    /// <para>Roots are not required to be correctly rounded and the platforms disagree: the cube
    /// root of 27 comes back as 3 from one C runtime and as 3.0000000000000004 from another. Where
    /// raising the nearest whole number by the degree gives the value back exactly, that whole
    /// number <em>is</em> a root of it, and the drift is simply a worse answer than the type can
    /// hold.</para>
    /// <para>So this is more accurate rather than a fudge, and it is what lets a program print the
    /// same thing wherever it is run. Only a whole degree within reach is considered, and the check
    /// is a multiplication rather than another call to <c>Pow</c>, so nothing here rests on the
    /// library that produced the drift.</para>
    /// </summary>
    private static double Exactly(double approximate, double value, double degree)
    {
        if (degree < 1 || degree > 64 || degree != Math.Floor(degree))
        {
            return approximate;
        }

        double whole = Math.Round(approximate);
        double raised = 1;

        for (int i = 0; i < degree; i++)
        {
            raised *= whole;
        }

        return raised == value ? whole : approximate;
    }
}
