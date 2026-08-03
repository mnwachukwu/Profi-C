using ProfiC.Runtime;

namespace ProfiC.Tests.Runtime;

/// <summary>
/// <para>What a Profi-C <c>string</c> does, tested where it lives rather than through either
/// engine.</para>
/// <para>Both engines call this class, so a rule broken here breaks in both at once and no
/// comparison between them would notice. That is the whole reason to test it directly: the corpus
/// proves the two agree, and this proves what they agree <em>on</em>.</para>
/// </summary>
[TestFixture]
public sealed class ProfiCTextTests
{
    /// <summary>
    /// <para>The rule that separates this class from <c>System.String</c>: an empty argument
    /// matches trivially and takes nothing away.</para>
    /// <para>The framework raises for the first two of these, and the message names
    /// <c>oldValue</c> — a parameter of a method the reader did not call. The rest of the family
    /// already reads the way this one does, so the rule is what makes the family consistent rather
    /// than an exception carved out of it.</para>
    /// </summary>
    [Test]
    public void AnEmptyArgumentChangesNothing()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ProfiCText.Replace("abc", "", "-"), Is.EqualTo("abc"));
            Assert.That(ProfiCText.Remove("abc", ""), Is.EqualTo("abc"));
            Assert.That(ProfiCText.Trim("abc", ""), Is.EqualTo("abc"));
            Assert.That(ProfiCText.TrimStart("abc", ""), Is.EqualTo("abc"));
            Assert.That(ProfiCText.TrimEnd("abc", ""), Is.EqualTo("abc"));
        });
    }

    /// <summary>The other half of the same rule: an empty argument is found everywhere.</summary>
    [Test]
    public void AnEmptyArgumentIsFoundWhereverItIsLookedFor()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ProfiCText.Contains("abc", ""), Is.True);
            Assert.That(ProfiCText.IndexOf("abc", ""), Is.Zero);
        });
    }

    /// <summary>
    /// Separating on nothing leaves the whole string, which is the same rule again: the separator
    /// is found everywhere, and cutting at every position of nothing cuts nowhere.
    /// </summary>
    [Test]
    public void SeparatingOnNothingLeavesOnePiece()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ProfiCText.Split("a,b", "").Count, Is.EqualTo(1));
            Assert.That(ProfiCText.Split("a,b", "")[0], Is.EqualTo("a,b"));
            Assert.That(ProfiCText.SplitUntyped("a,b", "").Count, Is.EqualTo(1));
        });
    }

    /// <summary>
    /// The two forms of each member that answers with a set hold the same things in the same
    /// order, differing only in the element type each engine can name.
    /// </summary>
    [Test]
    public void TheTypedAndUntypedFormsAgree()
    {
        ProfiCSet<char> typed = ProfiCText.ToCharacters("abc");
        ProfiCSet<object?> untyped = ProfiCText.ToCharactersUntyped("abc");

        Assert.Multiple(() =>
        {
            Assert.That(typed.Count, Is.EqualTo(untyped.Count));
            Assert.That(typed[0], Is.EqualTo('a'));
            Assert.That(untyped[0], Is.EqualTo('a'));

            Assert.That(
                ProfiCText.Split("a,b,c", ","),
                Is.EqualTo(new[] { "a", "b", "c" }).AsCollection);
        });
    }

    /// <summary>
    /// Trimming by a set reads the characters out of whichever set it was handed, since the two
    /// engines hand it different ones and neither is named in the signature.
    /// </summary>
    [Test]
    public void TrimmingByASetTakesEitherEnginesSet()
    {
        ProfiCSet<char> typed = new(['x', 'y']);
        ProfiCSet<object?> untyped = new(['x', 'y']);

        Assert.Multiple(() =>
        {
            Assert.That(ProfiCText.Trim("xyabcyx", typed), Is.EqualTo("abc"));
            Assert.That(ProfiCText.Trim("xyabcyx", untyped), Is.EqualTo("abc"));
        });
    }

    /// <summary>
    /// The first letter raised and the rest left exactly as it was — which is not what .NET's
    /// title-casing does, since that also lowers everything it did not raise.
    /// </summary>
    [Test]
    public void CapitalizingLeavesTheRestAlone()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ProfiCText.Capitalize("mcDonald"), Is.EqualTo("McDonald"));
            Assert.That(ProfiCText.Capitalize(""), Is.Empty);
        });
    }

    /// <summary>
    /// A position outside the string is refused in the language's own words, naming the position
    /// asked for and the length there was — not in the framework's, which would name a parameter
    /// of a method the reader did not call.
    /// </summary>
    [Test]
    public void APositionOutsideTheStringIsRefusedInTheLanguagesWords()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                Assert.Throws<IndexOutOfRangeException>(() => ProfiCText.RemoveAt("abc", 3))!.Message,
                Is.EqualTo("There is no position 3 in a string of 3."));

            // Insertion takes the end as well, since putting something after the last character
            // is a thing to mean.
            Assert.That(ProfiCText.InsertAt("abc", 3, "d"), Is.EqualTo("abcd"));
        });
    }

    /// <summary>
    /// <para>Reading a number out of text, which yields an optional rather than raising.</para>
    /// <para>Read without regard to where the program is running, so a decimal point is a point
    /// wherever the machine is set to.</para>
    /// </summary>
    [Test]
    public void TextReadsBackAsANumberOrAsNothing()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ProfiCText.ToInteger("42").Value, Is.EqualTo(42L));
            Assert.That(ProfiCText.ToInteger("four").HasValue, Is.False);
            Assert.That(ProfiCText.ToReal("3.5").Value, Is.EqualTo(3.5));
            Assert.That(ProfiCText.ToBoolean(" true ").Value, Is.True);

            // Only the two words the language writes, so these are not truths.
            Assert.That(ProfiCText.ToBoolean("yes").HasValue, Is.False);
            Assert.That(ProfiCText.ToBoolean("1").HasValue, Is.False);
        });
    }

    /// <summary>Writing one back out, by a pattern, and just as indifferent to where it runs.</summary>
    [Test]
    public void ANumberWritesByItsPatternWhereverItRuns()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ProfiCText.Format(1234L, "N0"), Is.EqualTo("1,234"));
            Assert.That(ProfiCText.Format(3.14159, "F2"), Is.EqualTo("3.14"));
        });
    }
}
