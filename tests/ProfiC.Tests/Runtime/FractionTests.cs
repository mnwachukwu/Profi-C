using ProfiC.Runtime;

namespace ProfiC.Tests.Runtime;

/// <summary>Exact rational arithmetic, and the edges where exactness is the whole point.</summary>
[TestFixture]
public sealed class FractionTests
{
    private static Fraction F(long numerator, long denominator) => new(numerator, denominator);

    [Test]
    public void ConstructionReducesToLowestTerms()
    {
        Assert.Multiple(() =>
        {
            Assert.That(F(2, 4).ToString(), Is.EqualTo("1|2"));
            Assert.That(F(100, 10).ToString(), Is.EqualTo("10|1"));
            Assert.That(F(6, 3).ToString(), Is.EqualTo("2|1"));
            Assert.That(F(-2, 4).ToString(), Is.EqualTo("-1|2"));
        });
    }

    [Test]
    public void SignIsAlwaysCarriedByTheNumerator()
    {
        // So that "-1|2" and "1|-2" are the same value, and a negative prints as written.
        Assert.Multiple(() =>
        {
            Assert.That(F(1, -2), Is.EqualTo(F(-1, 2)));
            Assert.That(F(1, -2).Denominator, Is.Positive);
            Assert.That(F(-1, -2), Is.EqualTo(F(1, 2)));
        });
    }

    [Test]
    public void ZeroNormalizesToZeroOverOne()
    {
        Assert.That(F(0, 5).ToString(), Is.EqualTo("0|1"));
        Assert.That(F(0, 5), Is.EqualTo(Fraction.Zero));
    }

    [Test]
    public void DenominatorOfZeroIsRejected()
    {
        Assert.Throws<DivideByZeroException>(() => _ = F(1, 0));
    }

    /// <summary>
    /// The example from the language documentation, and the reason fractions exist at all:
    /// in floating point this is 0.5000000000000001.
    /// </summary>
    [Test]
    public void OneThirdPlusOneSixthIsExactlyOneHalf()
    {
        Assert.That((F(1, 3) + F(1, 6)).ToString(), Is.EqualTo("1|2"));
    }

    [TestCase(1, 2, 1, 3, "5|6")]
    [TestCase(1, 2, 1, 2, "1|1")]
    [TestCase(3, 4, -1, 4, "1|2")]
    public void AdditionIsExact(long an, long ad, long bn, long bd, string expected)
    {
        Assert.That((F(an, ad) + F(bn, bd)).ToString(), Is.EqualTo(expected));
    }

    [TestCase(1, 2, 1, 3, "1|6")]
    [TestCase(1, 2, 1, 2, "0|1")]
    public void SubtractionIsExact(long an, long ad, long bn, long bd, string expected)
    {
        Assert.That((F(an, ad) - F(bn, bd)).ToString(), Is.EqualTo(expected));
    }

    [TestCase(2, 3, 3, 4, "1|2")]
    [TestCase(1, 2, 0, 5, "0|1")]
    public void MultiplicationReducesItsResult(long an, long ad, long bn, long bd, string expected)
    {
        Assert.That((F(an, ad) * F(bn, bd)).ToString(), Is.EqualTo(expected));
    }

    [Test]
    public void DivisionIsExact()
    {
        Assert.That((F(1, 2) / F(3, 4)).ToString(), Is.EqualTo("2|3"));
    }

    [Test]
    public void DividingByZeroIsRejected()
    {
        Assert.Throws<DivideByZeroException>(() => _ = F(1, 2) / Fraction.Zero);
    }

    [Test]
    public void NegationFlipsOnlyTheNumerator()
    {
        Fraction negated = -F(1, 2);

        Assert.Multiple(() =>
        {
            Assert.That(negated.Numerator, Is.EqualTo(-1));
            Assert.That(negated.Denominator, Is.EqualTo(2));
        });
    }

    /// <summary>
    /// Denominators multiply on every unlike addition, so a loop overflows within a few
    /// iterations. Overflow throws rather than wrapping to a wrong answer.
    /// </summary>
    [Test]
    public void OverflowThrowsRatherThanWrapping()
    {
        Fraction large = F(1, long.MaxValue / 2);

        Assert.Throws<OverflowException>(() =>
        {
            Fraction running = large;

            for (int i = 0; i < 10; i++)
            {
                running += F(1, long.MaxValue / 2 - i - 1);
            }
        });
    }

    [Test]
    public void EqualityFollowsFromNormalization()
    {
        Assert.Multiple(() =>
        {
            Assert.That(F(1, 2), Is.EqualTo(F(2, 4)));
            Assert.That(F(1, 2), Is.EqualTo(F(50, 100)));
            Assert.That(F(1, 2).GetHashCode(), Is.EqualTo(F(2, 4).GetHashCode()));
            Assert.That(F(1, 2), Is.Not.EqualTo(F(1, 3)));
        });
    }

    [Test]
    public void ComparisonAvoidsFloatingPoint()
    {
        Assert.Multiple(() =>
        {
            Assert.That(F(1, 3) < F(1, 2), Is.True);
            Assert.That(F(2, 3) > F(1, 2), Is.True);
            Assert.That(F(1, 2) <= F(2, 4), Is.True);
            Assert.That(F(-1, 2) < F(1, 2), Is.True);
        });
    }

    [Test]
    public void WideningAnIntegerIsExact()
    {
        Assert.That(Fraction.FromInteger(7).ToString(), Is.EqualTo("7|1"));
        Assert.That(Fraction.FromInteger(7).IsWhole, Is.True);
    }

    [Test]
    public void ConvertingToRealIsLossyAndThereforeExplicit()
    {
        Assert.That(F(1, 3).ToReal(), Is.EqualTo(1.0 / 3.0).Within(1e-15));
        Assert.That(F(1, 2).ToReal(), Is.EqualTo(0.5));
    }

    /// <summary>
    /// Every finite double is a rational, so this direction loses nothing — even when the
    /// answer is startling.
    /// </summary>
    [Test]
    public void ConvertingFromRealIsExactRatherThanApproximate()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Fraction.FromReal(0.5).ToString(), Is.EqualTo("1|2"));
            Assert.That(Fraction.FromReal(0.25).ToString(), Is.EqualTo("1|4"));
            Assert.That(Fraction.FromReal(2.0).ToString(), Is.EqualTo("2|1"));
            Assert.That(Fraction.FromReal(0.0), Is.EqualTo(Fraction.Zero));

            // One tenth is not one tenth. This is the documented example.
            Assert.That(Fraction.FromReal(0.1).ToString(),
                        Is.EqualTo("3602879701896397|36028797018963968"));
        });
    }

    [Test]
    public void ConvertingFromRealRoundTripsBack()
    {
        foreach (double value in new[] { 0.5, 0.25, 0.1, 2.0, -0.75, 1.0 / 3.0 })
        {
            Assert.That(Fraction.FromReal(value).ToReal(), Is.EqualTo(value),
                        $"round trip failed for {value}");
        }
    }

    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    [TestCase(double.NegativeInfinity)]
    public void ConvertingANonFiniteRealIsRejected(double value)
    {
        Assert.Throws<OverflowException>(() => _ = Fraction.FromReal(value));
    }

    // ---- Raising to a power -------------------------------------------------------------

    [TestCase(1, 2, 3L, "1|8")]
    [TestCase(2, 3, 2L, "4|9")]
    [TestCase(3, 1, 4L, "81|1")]
    [TestCase(1, 2, 0L, "1|1")]
    [TestCase(-1, 2, 3L, "-1|8")]
    [TestCase(-1, 2, 2L, "1|4")]
    public void PowerIsExact(long n, long d, long exponent, string expected)
    {
        Assert.That(Fraction.Pow(F(n, d), exponent).ToString(), Is.EqualTo(expected));
    }

    /// <summary>
    /// The reason a fraction is worth raising at all: a negative power inverts exactly, where
    /// a real could only approach the answer.
    /// </summary>
    [TestCase(1, 2, -3L, "8|1")]
    [TestCase(2, 3, -2L, "9|4")]
    [TestCase(5, 1, -1L, "1|5")]
    public void ANegativePowerInvertsExactly(long n, long d, long exponent, string expected)
    {
        Assert.That(Fraction.Pow(F(n, d), exponent).ToString(), Is.EqualTo(expected));
    }

    [Test]
    public void ZeroToANegativePowerIsRejected()
    {
        Assert.Throws<DivideByZeroException>(() => _ = Fraction.Pow(Fraction.Zero, -1));
    }

    [Test]
    public void ZeroToTheZeroIsOne()
    {
        Assert.That(Fraction.Pow(Fraction.Zero, 0), Is.EqualTo(Fraction.One));
    }

    [Test]
    public void APowerTooLargeToHoldFailsRatherThanWrapping()
    {
        Assert.Throws<OverflowException>(() => _ = Fraction.Pow(F(3, 2), 200));
    }
}
