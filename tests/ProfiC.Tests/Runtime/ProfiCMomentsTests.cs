using System.Globalization;
using ProfiC.Runtime;

namespace ProfiC.Tests.Runtime;

/// <summary>
/// <para>The time types, tested directly rather than through either engine.</para>
/// <para><b>Both engines call these, so neither can catch a bug in them.</b> The corpus runs
/// every sample twice and compares, which finds anything the two do differently — and finds
/// nothing at all once a rule lives in one place they share. What is left is to state the rules
/// here and check them.</para>
/// <para>Only what the language decides. That a January has 31 days is the platform's business
/// and testing it would be testing .NET; what is ours is the width of a year, the culture a
/// pattern is read in, and how an impossible date is refused.</para>
/// </summary>
[TestFixture]
public sealed class ProfiCMomentsTests
{
    /// <summary>
    /// Every count comes back as an <c>integer</c>, which is 64 bits. The platform answers these
    /// in 32, so each crossing is a place the two could have been written differently.
    /// </summary>
    [Test]
    public void EveryPartOfAMomentIsAnInteger()
    {
        DateTime moment = ProfiCMoments.MakeMoment(1969, 7, 20, 20, 17, 40);

        Assert.Multiple(() =>
        {
            Assert.That(ProfiCMoments.Year(moment), Is.EqualTo(1969L));
            Assert.That(ProfiCMoments.Month(moment), Is.EqualTo(7L));
            Assert.That(ProfiCMoments.Day(moment), Is.EqualTo(20L));
            Assert.That(ProfiCMoments.Hour(moment), Is.EqualTo(20L));
            Assert.That(ProfiCMoments.Minute(moment), Is.EqualTo(17L));
            Assert.That(ProfiCMoments.Second(moment), Is.EqualTo(40L));

            // Sunday is zero, which the sample says out loud because nothing else would.
            Assert.That(ProfiCMoments.DayOfWeek(moment), Is.EqualTo(0L));
        });
    }

    /// <summary>
    /// <para>A date nobody could write is refused in the language's words.</para>
    /// <para>The platform raises the right answer badly worded — it names a parameter, which
    /// tells a reader nothing about the date they typed. What they need to see is the date.</para>
    /// </summary>
    [Test]
    public void AnImpossibleDateNamesWhatWasWritten()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                Assert.Throws<ArgumentException>(
                    () => ProfiCMoments.MakeMoment(2026, 2, 31, 0, 0, 0))!.Message,
                Is.EqualTo("There is no such moment as 2026-2-31."));

            Assert.That(
                Assert.Throws<ArgumentException>(() => ProfiCMoments.MakeDate(2026, 13, 1))!.Message,
                Is.EqualTo("There is no such date as 2026-13-1."));

            Assert.That(
                Assert.Throws<ArgumentException>(() => ProfiCMoments.MakeTime(25, 0, 0))!.Message,
                Is.EqualTo("There is no such time of day as 25:0:0."));
        });
    }

    /// <summary>
    /// <para>A length of time counts its parts separately from its totals.</para>
    /// <para>Two hours and a half is two hours and thirty minutes, and it is also two and a half
    /// hours. Both are true and they are different questions, which is why the language offers
    /// both names.</para>
    /// </summary>
    [Test]
    public void PartsAreNotTotals()
    {
        TimeSpan length = ProfiCMoments.MakeSpan(1, 2, 30, 0);

        Assert.Multiple(() =>
        {
            Assert.That(ProfiCMoments.Days(length), Is.EqualTo(1L));
            Assert.That(ProfiCMoments.Hours(length), Is.EqualTo(2L));
            Assert.That(ProfiCMoments.Minutes(length), Is.EqualTo(30L));

            Assert.That(ProfiCMoments.TotalHours(length), Is.EqualTo(26.5m));

            // A real, but not one carrying a real's twenty-eight digits: the platform counts a
            // total in binary floating point and this crosses back, so what survives is the
            // fifteen or so a double held. Written out rather than rounded away, because a
            // reader comparing two totals should know which of them is exact.
            Assert.That(ProfiCMoments.TotalDays(length), Is.EqualTo(1.10416666666667m));
        });
    }

    /// <summary>
    /// Days counted separately rather than folded in, so a span of hours beyond a day still reads
    /// as hours — which is what makes <c>MakeSpan(0, 26, 0, 0)</c> a day and two hours rather than
    /// a refusal.
    /// </summary>
    [Test]
    public void ASpanOfHoursBeyondADayIsStillHours()
    {
        TimeSpan length = ProfiCMoments.MakeSpan(26, 0, 0);

        Assert.Multiple(() =>
        {
            Assert.That(ProfiCMoments.Days(length), Is.EqualTo(1L));
            Assert.That(ProfiCMoments.Hours(length), Is.EqualTo(2L));
            Assert.That(ProfiCMoments.TotalHours(length), Is.EqualTo(26m));
        });
    }

    /// <summary>
    /// <para>Everything reads and writes invariantly, whatever the machine says its culture is.
    /// </para>
    /// <para>So a moment written on one machine reads back on another, and a program prints the
    /// same thing wherever it runs. A pattern is the only thing that says otherwise.</para>
    /// </summary>
    [Test]
    public void ReadingAndWritingIgnoreTheMachinesCulture()
    {
        CultureInfo was = CultureInfo.CurrentCulture;

        try
        {
            // One that writes dates the other way round and uses a comma for a decimal point.
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            DateTime moment = ProfiCMoments.MakeMoment(2026, 12, 25, 9, 5, 0);

            Assert.Multiple(() =>
            {
                // Written with the separators that are placeholders rather than literals: '/'
                // is whatever the culture puts between the parts of a date, and de-DE puts a
                // full stop. A pattern of dashes and colons would pass under either culture and
                // assert nothing, which is how the first version of this test was useless.
                Assert.That(
                    ProfiCMoments.Format(moment, "yyyy/MM/dd HH:mm"),
                    Is.EqualTo("2026/12/25 09:05"));

                Assert.That(
                    ProfiCMoments.ParseMoment("2026-12-25 09:05").Value,
                    Is.EqualTo(moment));

                Assert.That(
                    ProfiCMoments.ParseDateExactly("25/12/2026", "dd/MM/yyyy").Value,
                    Is.EqualTo(ProfiCMoments.MakeDate(2026, 12, 25)));
            });
        }
        finally
        {
            CultureInfo.CurrentCulture = was;
        }
    }

    /// <summary>
    /// Text that does not read is an absence rather than a failure, which is why every one of
    /// these answers with an optional. Nothing is raised and nothing has to be caught.
    /// </summary>
    [Test]
    public void TextThatDoesNotReadIsAbsent()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ProfiCMoments.ParseMoment("the fifteenth").HasValue, Is.False);
            Assert.That(ProfiCMoments.ParseDate("nonsense").HasValue, Is.False);
            Assert.That(ProfiCMoments.ParseTime("half past").HasValue, Is.False);
            Assert.That(ProfiCMoments.ParseSpan("a while").HasValue, Is.False);

            // And the untyped forms the interpreter reads, which say the same with null.
            Assert.That(ProfiCMoments.ParseMomentUntyped("the fifteenth"), Is.Null);
            Assert.That(ProfiCMoments.ParseDateUntyped("2026-08-15"), Is.Not.Null);
        });
    }

    /// <summary>
    /// A clock wraps round midnight rather than running past it, which is the whole difference
    /// between a time of day and a moment — a moment has a date to carry the difference into.
    /// </summary>
    [Test]
    public void ATimeOfDayWrapsRoundMidnight()
    {
        TimeOnly late = ProfiCMoments.TimeAddHours(ProfiCMoments.MakeTime(17, 30), 8m);

        Assert.That(ProfiCMoments.TimeHour(late), Is.EqualTo(1L));
        Assert.That(ProfiCMoments.TimeMinute(late), Is.EqualTo(30L));
    }
}
