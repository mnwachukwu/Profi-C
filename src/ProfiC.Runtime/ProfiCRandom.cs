namespace ProfiC.Runtime;

/// <summary>
/// <para>A source of random numbers, and everything Profi-C asks one for.</para>
/// <para>Wraps the platform's generator rather than being one, since writing a generator is a
/// study in its own right and a worse one than the platform ships. What is here is the shape
/// of the questions: a whole number between two bounds, a real below one, and a coin.</para>
/// <para>A program may hold its own — two of these disturb nothing of each other's — and the
/// language also keeps one that everything shares, for the far more common case where nobody
/// cares which sequence they are drawing from. The shared one cannot be seeded, as .NET's
/// shared one cannot: a program that needs the same sequence twice holds its own, and holding
/// its own is the thing that makes it reproducible.</para>
/// </summary>
public sealed class ProfiCRandom
{
    private readonly Random _source;

    /// <summary>A generator seeded from the clock, so two runs differ.</summary>
    public ProfiCRandom() => _source = new Random();

    /// <summary>
    /// A generator seeded by hand, so two runs agree. This is what makes a program that uses
    /// chance testable, and what lets one person reproduce what another one saw.
    /// </summary>
    public ProfiCRandom(long seed) => _source = new Random(unchecked((int)seed));

    /// <summary>A whole number that is not negative.</summary>
    public long Next() => _source.NextInt64();

    /// <summary>A whole number from zero up to but never reaching <paramref name="below"/>.</summary>
    public long Next(long below) => Next(0, below);

    /// <summary>
    /// <para>A whole number from <paramref name="low"/> up to but never reaching
    /// <paramref name="high"/>.</para>
    /// <para>The upper bound is excluded, as .NET's is, so a die is Next(1, 7). That surprises
    /// everyone exactly once; reading it the other way here would surprise them a second time,
    /// in the language they moved to afterwards.</para>
    /// </summary>
    /// <exception cref="ArgumentException">The bounds are the wrong way round.</exception>
    public long Next(long low, long high)
    {
        if (low > high)
        {
            throw new ArgumentException(
                $"A random number needs a low bound no greater than the high one, but {low} "
                + $"is greater than {high}.");
        }

        return _source.NextInt64(low, high);
    }

    /// <summary>A real from zero up to but never reaching one.</summary>
    public double NextDouble() => _source.NextDouble();
}
