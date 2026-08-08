using ProfiC.Runtime;

namespace ProfiC.Tests.Runtime;

/// <summary>
/// <para>The parts of <c>Math</c> that decide something rather than forwarding.</para>
/// <para>Written against the runtime rather than against a program, because both engines call
/// these same methods. A rule that lives here is one the corpus diff cannot see: the interpreter
/// and an emitted program would agree on a wrong answer as readily as on a right one, so the
/// rule has to be asserted where it is written.</para>
/// </summary>
[TestFixture]
public sealed class ProfiCMathTests
{
    /// <summary>
    /// <para>Rounding a float that names no number stops, rather than saturating.</para>
    /// <para>Narrowing a floating point value to a whole one saturates instead of failing, so
    /// every one of these once came back as a number: an infinity as the largest integer, a
    /// not-a-number as zero. Nothing was reported, and the answer was indistinguishable from a
    /// real count — which is the shape of wrong answer worth refusing outright.</para>
    /// </summary>
    [Test]
    public void RoundingAFloatThatNamesNoNumberStops()
    {
        Assert.Multiple(() =>
        {
            foreach (double value in new[]
                     { double.PositiveInfinity, double.NegativeInfinity, double.NaN })
            {
                Assert.Throws<ArgumentException>(() => ProfiCMath.Floor(value));
                Assert.Throws<ArgumentException>(() => ProfiCMath.Ceiling(value));
                Assert.Throws<ArgumentException>(() => ProfiCMath.Round(value));
            }

            // The refusal names which of the two it met, since the fixes differ.
            Assert.That(
                Assert.Throws<ArgumentException>(
                    () => ProfiCMath.Round(double.PositiveInfinity))!.Message,
                Does.Contain("infinity"));

            Assert.That(
                Assert.Throws<ArgumentException>(() => ProfiCMath.Round(double.NaN))!.Message,
                Does.Contain("not a number"));
        });
    }

    /// <summary>
    /// <para>An ordinary float still rounds, and the three still differ.</para>
    /// <para>Refusing the values above by refusing everything would pass the test beside this
    /// one.</para>
    /// </summary>
    [Test]
    public void AnOrdinaryFloatStillRounds()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ProfiCMath.Floor(2.9), Is.EqualTo(2));
            Assert.That(ProfiCMath.Ceiling(2.1), Is.EqualTo(3));
            Assert.That(ProfiCMath.Round(2.5), Is.EqualTo(3));
            Assert.That(ProfiCMath.Round(-2.5), Is.EqualTo(-3));

            // Kept to places, a float stays a float — so an infinity is a float it can hold and
            // there is nothing here to refuse.
            Assert.That(ProfiCMath.Round(double.PositiveInfinity, 2),
                        Is.EqualTo(double.PositiveInfinity));
        });
    }

    /// <summary>
    /// <para>A real and a fraction are unaffected.</para>
    /// <para>Neither has an infinity or a not-a-number to meet, so the guard belongs to the float
    /// forms only and the other two must not have picked it up.</para>
    /// </summary>
    [Test]
    public void ARealAndAFractionRoundAsTheyDid()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ProfiCMath.Floor(2.9m), Is.EqualTo(2));
            Assert.That(ProfiCMath.Ceiling(2.1m), Is.EqualTo(3));
            Assert.That(ProfiCMath.Round(2.5m), Is.EqualTo(3));

            Assert.That(ProfiCMath.Round(new Fraction(5, 2)), Is.EqualTo(3));
            Assert.That(ProfiCMath.Floor(new Fraction(5, 2)), Is.EqualTo(2));
            Assert.That(ProfiCMath.Ceiling(new Fraction(5, 2)), Is.EqualTo(3));
        });
    }
}
