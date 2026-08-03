using ProfiC.Runtime;

namespace ProfiC.Tests.Runtime;

/// <summary>
/// <para>What the language's integer operators mean, tested where they live rather than through
/// either engine.</para>
/// <para><b>A test that compares the two engines cannot see any of this.</b> Both call this class,
/// so a rule broken here is broken identically on both sides and they go on agreeing — which is the
/// limit of differential testing stated plainly: it finds divergence, and shared code has none to
/// find. Every rule below is one the CLR instruction of the same name does <em>not</em> follow, so
/// each is worth an assertion of its own.</para>
/// </summary>
[TestFixture]
public sealed class ProfiCArithmeticTests
{
    /// <summary>
    /// <para>The rule the language states out loud: arithmetic is checked, not wrapped.</para>
    /// <para>Emitted as <c>add</c>, the first of these comes back as the smallest integer and the
    /// program carries on — printing a number that looks plausible and is wrong, which is the
    /// outcome <see cref="ArithmeticFailures.TooLargeForAnInteger"/> exists to promise against.
    /// </para>
    /// </summary>
    [Test]
    public void ArithmeticPastTheEndOfAnIntegerStops()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<OverflowException>(() => ProfiCArithmetic.Add(long.MaxValue, 1));
            Assert.Throws<OverflowException>(() => ProfiCArithmetic.Subtract(long.MinValue, 1));
            Assert.Throws<OverflowException>(() => ProfiCArithmetic.Multiply(long.MaxValue, 2));

            // The one division with no room for its answer: the smallest integer has no positive
            // twin, so negating it is asking for a number that is not there.
            Assert.Throws<OverflowException>(() => ProfiCArithmetic.Divide(long.MinValue, -1));
        });
    }

    /// <summary>The same three, well inside the bounds, still answer.</summary>
    [Test]
    public void OrdinaryArithmeticIsOrdinary()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ProfiCArithmetic.Add(2, 3), Is.EqualTo(5));
            Assert.That(ProfiCArithmetic.Subtract(2, 3), Is.EqualTo(-1));
            Assert.That(ProfiCArithmetic.Multiply(6, 7), Is.EqualTo(42));

            // Division truncates, which is worth knowing: one divided by three is zero.
            Assert.That(ProfiCArithmetic.Divide(7, 2), Is.EqualTo(3));
            Assert.That(ProfiCArithmetic.Divide(1, 3), Is.Zero);
            Assert.That(ProfiCArithmetic.Divide(-7, 2), Is.EqualTo(-3));
            Assert.That(ProfiCArithmetic.Remainder(7, 3), Is.EqualTo(1));

            // The pair the division refuses leaves nothing behind, which is an answer rather
            // than a failure — and is the pair the CLR's own instruction raises on.
            Assert.That(ProfiCArithmetic.Remainder(long.MinValue, -1), Is.Zero);
        });
    }

    /// <summary>
    /// Dividing by zero is refused in the language's own words, which name <c>PC0324</c> and say
    /// why a zero could only be found now. The framework raises the same type saying none of that.
    /// </summary>
    [Test]
    public void DividingByZeroIsRefusedInTheLanguagesWords()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                Assert.Throws<DivideByZeroException>(() => ProfiCArithmetic.Divide(1, 0))!.Message,
                Does.Contain("PC0324"));

            Assert.That(
                Assert.Throws<DivideByZeroException>(() => ProfiCArithmetic.Remainder(1, 0))!.Message,
                Does.Contain("a division that can happen"));
        });
    }

    /// <summary>
    /// <para>A shift past the width is refused rather than folded into range.</para>
    /// <para><c>shl</c> masks the amount to its low six bits, so <c>x shiftleft 64</c> would
    /// quietly mean <c>x shiftleft 0</c> and hand back <c>x</c> unchanged. That is what C# does,
    /// and it is the thing this language declines to do.</para>
    /// </summary>
    [Test]
    public void AShiftOutsideTheWidthIsRefusedRatherThanFolded()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() => ProfiCArithmetic.ShiftLeft(1, 64));
            Assert.Throws<ArgumentException>(() => ProfiCArithmetic.ShiftRight(1, 64));
            Assert.Throws<ArgumentException>(() => ProfiCArithmetic.ShiftLeft(1, -1));

            // Both ends of what an integer does have.
            Assert.That(ProfiCArithmetic.ShiftLeft(1, 0), Is.EqualTo(1));
            Assert.That(ProfiCArithmetic.ShiftLeft(1, 10), Is.EqualTo(1024));
            Assert.That(ProfiCArithmetic.ShiftLeft(1, 63), Is.EqualTo(long.MinValue));
            Assert.That(ProfiCArithmetic.ShiftRight(1024, 3), Is.EqualTo(128));
        });
    }
}
