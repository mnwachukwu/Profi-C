using ProfiC.Runtime;

namespace ProfiC.Tests.Runtime;

/// <summary>Optionals, which are how Profi-C manages without null.</summary>
[TestFixture]
public sealed class OptionalTests
{
    [Test]
    public void APresentOptionalHoldsItsValue()
    {
        Optional<int> present = Optional.Of(5);

        Assert.Multiple(() =>
        {
            Assert.That(present.HasValue, Is.True);
            Assert.That(present.Value, Is.EqualTo(5));
        });
    }

    [Test]
    public void AnEmptyOptionalHasNoValue()
    {
        Assert.That(Optional<int>.Empty.HasValue, Is.False);
    }

    [Test]
    public void TheDefaultOptionalIsEmpty()
    {
        // A struct's default must be the absent case, or a field would start out holding a
        // value nobody assigned.
        Optional<string> uninitialized = default;
        Assert.That(uninitialized.HasValue, Is.False);
    }

    [Test]
    public void ReadingAnEmptyOptionalThrowsTheDedicatedException()
    {
        Assert.Throws<EmptyOptionalException>(() => _ = Optional<int>.Empty.Value);
    }

    [Test]
    public void OrSuppliesAFallbackWhenEmpty()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Optional<int>.Empty.Or(9), Is.EqualTo(9));
            Assert.That(Optional.Of(5).Or(9), Is.EqualTo(5));
        });
    }

    /// <summary>
    /// Written as a call, so a reader might expect the argument to be evaluated first. It is
    /// not: the compiler passes a thunk, which is what makes the fallback short-circuit.
    /// </summary>
    [Test]
    public void OrDoesNotEvaluateItsFallbackWhenAValueIsPresent()
    {
        int evaluations = 0;

        int result = Optional.Of(5).Or(() =>
        {
            evaluations++;
            return 9;
        });

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(5));
            Assert.That(evaluations, Is.Zero, "the fallback should not have run");
        });
    }

    [Test]
    public void OrEvaluatesItsFallbackWhenEmpty()
    {
        int evaluations = 0;

        int result = Optional<int>.Empty.Or(() =>
        {
            evaluations++;
            return 9;
        });

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(9));
            Assert.That(evaluations, Is.EqualTo(1));
        });
    }

    /// <summary>
    /// Chaining keeps the result optional, since every candidate may be absent. Only a
    /// non-optional fallback ends a chain with a definite value.
    /// </summary>
    [Test]
    public void OrChainsAcrossSeveralOptionals()
    {
        Optional<int> a = Optional<int>.Empty;
        Optional<int> b = Optional<int>.Empty;
        Optional<int> c = Optional.Of(3);

        Assert.Multiple(() =>
        {
            Assert.That(a.Or(b).Or(c).Value, Is.EqualTo(3));
            Assert.That(a.Or(b).Or(c).Or(4), Is.EqualTo(3));
            Assert.That(a.Or(b).HasValue, Is.False);
            Assert.That(a.Or(b).Or(4), Is.EqualTo(4));
        });
    }

    [Test]
    public void EqualityComparesPresenceThenValue()
    {
        Optional<int> one = Optional.Of(1);
        Optional<int> separatelyOne = Optional.Of(1);
        Optional<int> two = Optional.Of(2);
        Optional<int> empty = Optional<int>.Empty;
        Optional<int> separatelyEmpty = default;

        Assert.Multiple(() =>
        {
            Assert.That(one, Is.EqualTo(separatelyOne));
            Assert.That(one, Is.Not.EqualTo(two));
            Assert.That(empty, Is.EqualTo(separatelyEmpty));
            Assert.That(one, Is.Not.EqualTo(empty));
        });
    }

    /// <summary>
    /// The boundary where null stops. Every .NET reference that may be absent becomes an
    /// optional here, so null never enters the language's own type system.
    /// </summary>
    [Test]
    public void NullFromDotNetBecomesAnEmptyOptional()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Optional.FromNullable<string>(null).HasValue, Is.False);
            Assert.That(Optional.FromNullable("text").Value, Is.EqualTo("text"));
        });
    }

    [Test]
    public void TryGetValueReportsPresenceWithoutThrowing()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Optional.Of(5).TryGetValue(out int present), Is.True);
            Assert.That(present, Is.EqualTo(5));
            Assert.That(Optional<int>.Empty.TryGetValue(out _), Is.False);
        });
    }
}
