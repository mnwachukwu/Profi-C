using System.Globalization;

namespace ProfiC.Runtime;

/// <summary>
/// <para>An exact rational number, always held in lowest terms.</para>
/// <para>Profi-C has fractions as a primitive so that <c>1|3 + 1|6</c> is exactly <c>1|2</c>
/// rather than an approximation. Normalizing on construction is what makes equality and
/// printing behave: two fractions denoting the same number are the same value.</para>
/// <para>Both parts are 64-bit and arithmetic is checked. Denominators multiply on every
/// unlike addition, so a loop that accumulates fractions overflows within a few iterations.
/// Overflow throws rather than wrapping to a wrong answer.</para>
/// </summary>
public readonly struct Fraction : IEquatable<Fraction>, IComparable<Fraction>
{
    /// <summary>Zero.</summary>
    public static readonly Fraction Zero = new(0, 1, alreadyNormalized: true);

    /// <summary>One.</summary>
    public static readonly Fraction One = new(1, 1, alreadyNormalized: true);

    /// <summary>The numerator, carrying the sign.</summary>
    public long Numerator { get; }

    /// <summary>The denominator, always positive.</summary>
    public long Denominator { get; }

    private Fraction(long numerator, long denominator, bool alreadyNormalized)
    {
        Numerator = numerator;
        Denominator = denominator;
        _ = alreadyNormalized;
    }

    /// <summary>
    /// Creates a fraction, reducing it to lowest terms and moving any sign to the numerator.
    /// </summary>
    /// <exception cref="DivideByZeroException">The denominator is zero.</exception>
    public Fraction(long numerator, long denominator)
    {
        if (denominator == 0)
        {
            throw new DivideByZeroException("A fraction cannot have a denominator of zero.");
        }

        // Keeping the sign in the numerator means "-1|2" and "1|-2" are the same value, and
        // that a negative fraction prints the way it is written.
        if (denominator < 0)
        {
            if (denominator == long.MinValue || numerator == long.MinValue)
            {
                throw new OverflowException("Fraction is too large to normalize.");
            }

            numerator = -numerator;
            denominator = -denominator;
        }

        long divisor = GreatestCommonDivisor(Math.Abs(numerator), denominator);

        Numerator = numerator / divisor;
        Denominator = denominator / divisor;
    }

    /// <summary>A whole number as a fraction. Widening an integer is always exact.</summary>
    public static Fraction FromInteger(long value) => new(value, 1, alreadyNormalized: true);

    /// <summary>True when this fraction denotes a whole number.</summary>
    public bool IsWhole => Denominator == 1;

    /// <summary>
    /// <para>Euclid's algorithm. Zero is handled by the identity that gcd(0, n) is n, which
    /// keeps a zero numerator normalizing to <c>0|1</c> rather than dividing by zero.</para>
    /// </summary>
    private static long GreatestCommonDivisor(long a, long b)
    {
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }

        return a == 0 ? 1 : a;
    }

    // ---- Arithmetic ---------------------------------------------------------------------

    public static Fraction operator +(Fraction left, Fraction right) => checked(new Fraction(
        left.Numerator * right.Denominator + right.Numerator * left.Denominator,
        left.Denominator * right.Denominator));

    public static Fraction operator -(Fraction left, Fraction right) => checked(new Fraction(
        left.Numerator * right.Denominator - right.Numerator * left.Denominator,
        left.Denominator * right.Denominator));

    public static Fraction operator *(Fraction left, Fraction right) => checked(new Fraction(
        left.Numerator * right.Numerator,
        left.Denominator * right.Denominator));

    /// <exception cref="DivideByZeroException">The right operand is zero.</exception>
    public static Fraction operator /(Fraction left, Fraction right)
    {
        if (right.Numerator == 0)
        {
            throw new DivideByZeroException("Cannot divide a fraction by zero.");
        }

        return checked(new Fraction(
            left.Numerator * right.Denominator,
            left.Denominator * right.Numerator));
    }

    /// <summary>
    /// <para>What is left after taking out whole copies of the right operand.</para>
    /// <para>The same question <c>%</c> asks of two integers, answered exactly: one third goes
    /// into one half once, and one sixth is left. A real would answer this with whatever the
    /// nearest double is, which is the difference the type exists for.</para>
    /// <para>Truncated rather than floored, matching <c>%</c> on integers and reals, so the
    /// result carries the sign of the left operand.</para>
    /// </summary>
    /// <exception cref="DivideByZeroException">The right operand is zero.</exception>
    public static Fraction operator %(Fraction left, Fraction right)
    {
        if (right.Numerator == 0)
        {
            throw new DivideByZeroException("Cannot take the remainder of a fraction by zero.");
        }

        // How many whole copies of the right fit, toward zero.
        long whole = checked(
            (left.Numerator * right.Denominator) / (left.Denominator * right.Numerator));

        return checked(left - (right * new Fraction(whole, 1)));
    }

    /// <summary>
    /// <para>This fraction turned upside down: two thirds becomes three halves.</para>
    /// <para>Exact, and its own undo — turning one over twice gives back what it was, which is
    /// not true of doing the same to a real. The sign stays on the numerator, since a
    /// normalized fraction keeps its denominator positive.</para>
    /// </summary>
    /// <exception cref="DivideByZeroException">This fraction is zero.</exception>
    public Fraction Reciprocal()
    {
        if (Numerator == 0)
        {
            throw new DivideByZeroException(
                "Zero has no reciprocal: nothing multiplied by zero gives one.");
        }

        return Numerator < 0
            ? new Fraction(checked(-Denominator), checked(-Numerator))
            : new Fraction(Denominator, Numerator);
    }

    public static Fraction operator -(Fraction value) =>
        new(checked(-value.Numerator), value.Denominator, alreadyNormalized: true);

    public static Fraction operator +(Fraction value) => value;

    /// <summary>Adds two fractions.</summary>
    public static Fraction Add(Fraction left, Fraction right) => left + right;

    /// <summary>Subtracts one fraction from another.</summary>
    public static Fraction Subtract(Fraction left, Fraction right) => left - right;

    /// <summary>Multiplies two fractions.</summary>
    public static Fraction Multiply(Fraction left, Fraction right) => left * right;

    /// <summary>
    /// <para>Raises a fraction to a whole power, exactly.</para>
    /// <para>The exponent is an integer rather than a fraction because a rational raised to a
    /// fractional power is generally irrational — the square root of one half cannot be
    /// written as a ratio at all — so there would be nothing exact to give back.</para>
    /// <para>A negative exponent inverts, which is where this earns its place: two to the
    /// minus three is exactly one eighth here, where a real would only approach it.</para>
    /// </summary>
    /// <exception cref="DivideByZeroException">Zero raised to a negative power.</exception>
    /// <exception cref="OverflowException">The result does not fit in 64 bits.</exception>
    public static Fraction Pow(Fraction value, long exponent)
    {
        if (exponent == 0)
        {
            return One;
        }

        if (exponent < 0)
        {
            if (value.Numerator == 0)
            {
                throw new DivideByZeroException("Cannot raise zero to a negative power.");
            }

            return One / Pow(value, -exponent);
        }

        // Exponentiation by squaring: the same answer as multiplying it out, in far fewer
        // steps, which matters because each step can overflow and every one is checked.
        Fraction result = One;
        Fraction factor = value;

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

    /// <summary>Divides one fraction by another.</summary>
    public static Fraction Divide(Fraction left, Fraction right) => left / right;

    /// <summary>Negates a fraction.</summary>
    public static Fraction Negate(Fraction value) => -value;

    // ---- Measuring and rounding ------------------------------------------------------------

    /// <summary>
    /// The distance from zero. Normalization keeps the sign on the numerator, so this is the
    /// numerator's own magnitude and nothing has to be reduced again.
    /// </summary>
    public static Fraction Abs(Fraction value) =>
        value.Numerator < 0 ? -value : value;

    /// <summary>
    /// <para>The greatest whole number no larger than this one.</para>
    /// <para>Division in C# truncates toward zero, which is not what flooring means below
    /// zero: -7|2 truncates to -3 and floors to -4. The negative case is worked out from the
    /// remainder rather than by converting to a real, so the answer stays exact.</para>
    /// </summary>
    public static long Floor(Fraction value)
    {
        long quotient = value.Numerator / value.Denominator;

        return value.Numerator < 0 && quotient * value.Denominator != value.Numerator
            ? quotient - 1
            : quotient;
    }

    /// <summary>The least whole number no smaller than this one.</summary>
    public static long Ceiling(Fraction value) => -Floor(-value);

    /// <summary>
    /// <para>The nearest whole number, with a half going away from zero.</para>
    /// <para>That is the rule taught in school: 2.5 rounds to 3 and -2.5 to -3. .NET rounds a
    /// half to the even neighbor by default, which is better for statistics and worse for
    /// everyone learning, so this says which it wants rather than taking the default.</para>
    /// </summary>
    public static long Round(Fraction value)
    {
        Fraction half = new(1, 2);

        return value.Numerator < 0
            ? Ceiling(value - half)
            : Floor(value + half);
    }

    // ---- Conversions --------------------------------------------------------------------

    /// <summary>
    /// <para>Converts to a real, which is explicit in the language because it loses
    /// information: one third has no exact binary representation.</para>
    /// </summary>
    public double ToReal() => (double)Numerator / Denominator;

    /// <summary>
    /// <para>Converts a real to a fraction exactly, by reading the binary representation
    /// rather than approximating it.</para>
    /// <para>Every finite double <em>is</em> a rational, so this direction loses nothing —
    /// though the result is often startling, since 0.1 is really
    /// 3602879701896397|36028797018963968.</para>
    /// </summary>
    /// <exception cref="OverflowException">The value is infinite or not a number.</exception>
    public static Fraction FromReal(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new OverflowException("Only a finite real can be written as a fraction.");
        }

        if (value == 0)
        {
            return Zero;
        }

        long bits = BitConverter.DoubleToInt64Bits(value);
        bool negative = bits < 0;
        int exponent = (int)((bits >> 52) & 0x7FF);
        long mantissa = bits & 0xFFFFFFFFFFFFFL;

        // A normal double carries an implicit leading one; a subnormal does not.
        if (exponent == 0)
        {
            exponent++;
        }
        else
        {
            mantissa |= 1L << 52;
        }

        exponent -= 1075;

        if (negative)
        {
            mantissa = -mantissa;
        }

        if (exponent > 0)
        {
            // Large enough that the shift would overflow a long.
            if (exponent > 62)
            {
                throw new OverflowException("Real is too large to write as a fraction.");
            }

            return new Fraction(checked(mantissa * (1L << exponent)), 1);
        }

        if (exponent < -62)
        {
            throw new OverflowException("Real is too precise to write as a fraction.");
        }

        return new Fraction(mantissa, 1L << -exponent);
    }

    // ---- Equality and ordering ------------------------------------------------------------

    /// <summary>
    /// Both parts are compared directly, which is sound only because every fraction is held
    /// in lowest terms. That is the reason normalization happens on construction.
    /// </summary>
    public bool Equals(Fraction other) =>
        Numerator == other.Numerator && Denominator == other.Denominator;

    public override bool Equals(object? obj) => obj is Fraction other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Numerator, Denominator);

    public static bool operator ==(Fraction left, Fraction right) => left.Equals(right);

    public static bool operator !=(Fraction left, Fraction right) => !left.Equals(right);

    /// <summary>
    /// Compares by cross-multiplying, which avoids the rounding a conversion to double would
    /// introduce.
    /// </summary>
    public int CompareTo(Fraction other) =>
        checked(Numerator * other.Denominator).CompareTo(checked(other.Numerator * Denominator));

    public static bool operator <(Fraction left, Fraction right) => left.CompareTo(right) < 0;

    public static bool operator >(Fraction left, Fraction right) => left.CompareTo(right) > 0;

    public static bool operator <=(Fraction left, Fraction right) => left.CompareTo(right) <= 0;

    public static bool operator >=(Fraction left, Fraction right) => left.CompareTo(right) >= 0;

    /// <summary>Renders the fraction the way it is written in source.</summary>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Numerator}|{Denominator}");
}
