using System.Globalization;

namespace ProfiC.Runtime;

/// <summary>
/// <para>An exact rational number, always held in lowest terms.</para>
/// <para>Profi-C has fractions as a primitive so that <c>1|3 + 1|6</c> is exactly <c>1|2</c>
/// rather than an approximation. Normalizing on construction is what makes equality and
/// printing behave: two fractions denoting the same number are the same value.</para>
/// <para>Both parts are 64-bit and arithmetic is checked. Denominators multiply on every
/// unlike addition, so a loop that accumulates fractions overflows within a few iterations;
/// arbitrary precision was rejected on cost, which makes a loud failure the only honest
/// alternative to a silently wrong answer.</para>
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

    public static Fraction operator -(Fraction value) =>
        new(checked(-value.Numerator), value.Denominator, alreadyNormalized: true);

    public static Fraction operator +(Fraction value) => value;

    /// <summary>Adds two fractions.</summary>
    public static Fraction Add(Fraction left, Fraction right) => left + right;

    /// <summary>Subtracts one fraction from another.</summary>
    public static Fraction Subtract(Fraction left, Fraction right) => left - right;

    /// <summary>Multiplies two fractions.</summary>
    public static Fraction Multiply(Fraction left, Fraction right) => left * right;

    /// <summary>Divides one fraction by another.</summary>
    public static Fraction Divide(Fraction left, Fraction right) => left / right;

    /// <summary>Negates a fraction.</summary>
    public static Fraction Negate(Fraction value) => -value;

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
