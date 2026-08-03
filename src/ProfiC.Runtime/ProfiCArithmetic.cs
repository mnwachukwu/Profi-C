namespace ProfiC.Runtime;

/// <summary>
/// <para>What the language's arithmetic operators mean, in one place both engines call.</para>
/// <para><b>Where a CLR instruction already means it, it is not here.</b> Comparison and the
/// bitwise three say nothing in this file, and every <c>float</c> operation is the instruction
/// itself. What is written out is the places the language and the machine disagree —</para>
/// <list type="bullet">
/// <item><description><b>Integer arithmetic is checked, not wrapped.</b> <c>add</c> lets a sum
/// past the end of an integer come back as a negative number; the language stops instead, because
/// a result that looks plausible and is wrong is the worse of the two. This is the rule
/// <see cref="ArithmeticFailures.TooLargeForAnInteger"/> is written to explain.</description></item>
/// <item><description><b>Dividing by zero is refused in the language's own words</b>, on an
/// integer and on a real alike. The CLR raises the same type with its own message, which says
/// nothing about <c>PC0324</c> or about why the zero was only found now.</description></item>
/// <item><description><b>A shift past the width is refused, not folded.</b> <c>shl</c> masks the
/// amount to the low six bits, so <c>x shiftleft 64</c> would quietly mean <c>x shiftleft 0</c> —
/// which is what C# does and what this language refuses to.</description></item>
/// </list>
/// <para><b>Three number types, three characters.</b> An <c>integer</c> and a <c>real</c> both
/// stop at their bounds; a <c>float</c> passes into infinity and keeps going. That difference is
/// the reason both types exist, so it is left visible rather than smoothed over.</para>
/// </summary>
public static class ProfiCArithmetic
{
    /// <summary>
    /// <para>The three that can outgrow an integer.</para>
    /// <para>Each is wrapped once rather than testing its own bounds, so one wording covers all of
    /// them and none can be added later without it.</para>
    /// </summary>
    public static long Add(long a, long b)
    {
        try
        {
            return checked(a + b);
        }
        catch (OverflowException)
        {
            throw ArithmeticFailures.TooLargeForAnInteger();
        }
    }

    /// <inheritdoc cref="Add(long, long)"/>
    public static long Subtract(long a, long b)
    {
        try
        {
            return checked(a - b);
        }
        catch (OverflowException)
        {
            throw ArithmeticFailures.TooLargeForAnInteger();
        }
    }

    /// <inheritdoc cref="Add(long, long)"/>
    public static long Multiply(long a, long b)
    {
        try
        {
            return checked(a * b);
        }
        catch (OverflowException)
        {
            throw ArithmeticFailures.TooLargeForAnInteger();
        }
    }

    /// <summary>
    /// Division, which truncates — one divided by three is zero, not a third. The one case that
    /// overflows is the smallest integer divided by minus one, whose answer has no room.
    /// </summary>
    public static long Divide(long a, long b)
    {
        if (b == 0)
        {
            throw ArithmeticFailures.DivideByZero();
        }

        if (a == long.MinValue && b == -1)
        {
            throw ArithmeticFailures.TooLargeForAnInteger();
        }

        return a / b;
    }

    /// <summary>What a division leaves behind, so it needs a division that can happen.</summary>
    public static long Remainder(long a, long b)
    {
        if (b == 0)
        {
            throw ArithmeticFailures.RemainderByZero();
        }

        // The same pair the division refuses, which the CLR raises on rather than answering zero.
        if (a == long.MinValue && b == -1)
        {
            return 0;
        }

        return a % b;
    }

    /// <summary>
    /// Moving the bits, by an amount that has to be one an integer has. An amount outside the
    /// width is refused rather than folded into range.
    /// </summary>
    public static long ShiftLeft(long value, long amount) =>
        Shiftable(amount) ? value << (int)amount : throw OutsideTheWidth(amount);

    /// <inheritdoc cref="ShiftLeft"/>
    public static long ShiftRight(long value, long amount) =>
        Shiftable(amount) ? value >> (int)amount : throw OutsideTheWidth(amount);

    private static bool Shiftable(long amount) => amount is >= 0 and < 64;

    private static ArgumentException OutsideTheWidth(long amount) => new(
        $"A shift of {amount} places is outside an integer, which holds 64 bits. An amount "
        + "from 0 to 63 is what there is to move.");

    // ---- real ---------------------------------------------------------------------------

    /// <summary>
    /// <para>The four on a <c>real</c>, which counts in tens.</para>
    /// <para>Each is the CLR's own decimal operator, wrapped only to say what went wrong in the
    /// language's words. A decimal outgrows its type at about 7.9 followed by 28 zeros, which is
    /// far sooner than binary floating point does — and unlike binary it stops rather than
    /// answering with an infinity, which is the same choice <c>integer</c> makes.</para>
    /// </summary>
    public static decimal Add(decimal a, decimal b) => Guarded(() => a + b);

    /// <inheritdoc cref="Add(decimal, decimal)"/>
    public static decimal Subtract(decimal a, decimal b) => Guarded(() => a - b);

    /// <inheritdoc cref="Add(decimal, decimal)"/>
    public static decimal Multiply(decimal a, decimal b) => Guarded(() => a * b);

    /// <summary>
    /// Division, which is exact where the answer has an exact decimal form and rounded to
    /// twenty-eight or so digits where it has not — a third is not a thing decimal can hold any
    /// more than binary can.
    /// </summary>
    public static decimal Divide(decimal a, decimal b) =>
        b == 0 ? throw ArithmeticFailures.DivideByZero() : Guarded(() => a / b);

    /// <inheritdoc cref="Divide(decimal, decimal)"/>
    public static decimal Remainder(decimal a, decimal b) =>
        b == 0 ? throw ArithmeticFailures.RemainderByZero() : Guarded(() => a % b);

    /// <summary>
    /// A real too large for its type, said the way an integer says it. The CLR's own wording
    /// names neither the type nor the bound, which is most of what a reader needs.
    /// </summary>
    private static decimal Guarded(Func<decimal> operation)
    {
        try
        {
            return operation();
        }
        catch (OverflowException)
        {
            throw ArithmeticFailures.TooLargeForAReal();
        }
    }

    // ---- float --------------------------------------------------------------------------

    /// <summary>
    /// <para>The four on a <c>float</c>, which are the instructions themselves.</para>
    /// <para><b>Nothing is guarded, and that is the type's whole character.</b> Binary floating
    /// point does not overflow — it reaches infinity and carries on — and dividing by zero
    /// produces an infinity rather than stopping. A reader meeting <c>float</c> is meeting those
    /// rules on purpose, so wrapping them would hide the thing being taught.</para>
    /// </summary>
    public static double Add(double a, double b) => a + b;

    /// <inheritdoc cref="Add(double, double)"/>
    public static double Subtract(double a, double b) => a - b;

    /// <inheritdoc cref="Add(double, double)"/>
    public static double Multiply(double a, double b) => a * b;

    /// <inheritdoc cref="Add(double, double)"/>
    public static double Divide(double a, double b) => a / b;

    /// <inheritdoc cref="Add(double, double)"/>
    public static double Remainder(double a, double b) => a % b;

    /// <summary>
    /// <para>A whole power of a whole number, by squaring.</para>
    /// <para>Every step is checked, so a result too large to hold stops the program rather than
    /// wrapping silently into a wrong answer — the same rule the four above follow.</para>
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The exponent is negative, which has no whole answer. Refused while compiling wherever it
    /// can be seen; one arriving in a variable reaches here, and is an argument that was wrong
    /// rather than a fault, so a program can catch it exactly as it catches a variable that
    /// turned out to be zero.
    /// </exception>
    public static long Power(long value, long exponent)
    {
        if (exponent < 0)
        {
            throw new ArgumentException(
                $"An integer raised to the power {exponent} is not a whole number. Raise a "
                + "fraction instead, or use Math.Pow for a real result.");
        }

        long result = 1;
        long factor = value;

        try
        {
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
        }
        catch (OverflowException)
        {
            throw ArithmeticFailures.TooLargeForAnInteger();
        }

        return result;
    }

    // ---- Crossing between the two ---------------------------------------------------------

    /// <summary>
    /// <para>A real as a float, which always answers.</para>
    /// <para>A real holds about twenty-eight digits and a float sixteen, so the tail is lost —
    /// but every real fits well inside a float's range, so nothing here can fail.</para>
    /// </summary>
    public static double ToFloat(decimal value) => (double)value;

    /// <summary>
    /// <para>A float as a real, which is the direction that can fail.</para>
    /// <para>Three ways. A float reaches far past what a real holds; an infinity has no real to
    /// become; and neither has a value that is not a number. Each is said in the language's own
    /// words rather than the framework's, which reports all three as one overflow.</para>
    /// <para><b>What succeeds is tidied, and that is worth knowing.</b> The float holding a tenth
    /// is really 3602879701896397 over 36028797018963968, and this gives back exactly
    /// <c>0.1</c> — the conversion reads the shortest decimal the float rounds to, so the mess
    /// disappears rather than being carried across. It is a truer number and not the same one.
    /// </para>
    /// </summary>
    public static decimal ToReal(double value)
    {
        if (double.IsNaN(value))
        {
            throw new ArgumentException(
                "A value that is not a number has no real to become. Only a float has one at "
                + "all, so there is nothing here for a real to hold.");
        }

        if (double.IsInfinity(value))
        {
            throw new ArgumentException(
                "An infinity has no real to become. A float carries on past its bounds and a "
                + "real stops at them, so there is no number here for it to hold.");
        }

        try
        {
            return (decimal)value;
        }
        catch (OverflowException)
        {
            throw ArithmeticFailures.TooLargeForAReal();
        }
    }
}
