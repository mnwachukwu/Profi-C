using System.Globalization;

namespace ProfiC.Runtime;

/// <summary>
/// <para>Moments, days, times of day, and lengths of time — the four the language provides, and
/// every operation on them.</para>
/// <para><b>Written in the language's own types rather than the platform's.</b> A Profi-C
/// <c>integer</c> is 64 bits and a <c>real</c> counts in tens, while <c>DateTime.Year</c> answers
/// in <c>int</c> and <c>AddDays</c> asks for a <c>double</c>. Somebody has to bridge that, and
/// doing it here means it is bridged once: the interpreter and the emitter both call these, so
/// neither can round a fraction of a second differently from the other.</para>
/// <para><b>Everything reads and writes invariantly.</b> A moment printed on one machine reads
/// back on another, and a pattern is the only thing that says otherwise. Anything else would make
/// a program's output depend on where it ran, which is not a thing a teaching language should
/// spring on a reader.</para>
/// <para><b>Text that does not read is an absence, not a failure.</b> Every <c>Parse</c> answers
/// with an optional, because text arriving in a shape nobody promised is the ordinary case rather
/// than a fault — and the two forms of each are the same split every member here that can answer
/// with nothing takes: the typed one for an emitted program, and the untyped one for the
/// interpreter, which holds every absence as null.</para>
/// </summary>
public static class ProfiCMoments
{
    // ---- Making one -----------------------------------------------------------------------

    /// <summary>
    /// <para>A moment, or a refusal naming the numbers that were written.</para>
    /// <para>The platform raises an argument error for the thirty-first of February, which is the
    /// right answer badly worded — it names a parameter rather than the date. Caught and thrown
    /// again so the message shows what the program asked for.</para>
    /// </summary>
    public static DateTime MakeMoment(
        long year, long month, long day, long hour, long minute, long second)
    {
        try
        {
            return new DateTime(
                (int)year, (int)month, (int)day, (int)hour, (int)minute, (int)second);
        }
        catch (Exception failed) when (failed is ArgumentOutOfRangeException or OverflowException)
        {
            string written = hour == 0 && minute == 0 && second == 0
                ? $"{year}-{month}-{day}"
                : $"{year}-{month}-{day} {hour}:{minute}:{second}";

            throw new ArgumentException($"There is no such moment as {written}.");
        }
    }

    /// <summary>A moment at midnight, which is what a date with no time of day is.</summary>
    public static DateTime MakeDay(long year, long month, long day) =>
        MakeMoment(year, month, day, 0, 0, 0);

    public static DateTime Now() => DateTime.Now;

    public static DateTime Today() => DateTime.Today;

    /// <summary>A day, reporting one that is not a day.</summary>
    public static DateOnly MakeDate(long year, long month, long day)
    {
        try
        {
            return new DateOnly((int)year, (int)month, (int)day);
        }
        catch (Exception failed) when (failed is ArgumentOutOfRangeException or OverflowException)
        {
            throw new ArgumentException($"There is no such date as {year}-{month}-{day}.");
        }
    }

    /// <summary>A time of day, reporting one that no clock reads.</summary>
    public static TimeOnly MakeTime(long hour, long minute, long second)
    {
        try
        {
            return new TimeOnly((int)hour, (int)minute, (int)second);
        }
        catch (Exception failed) when (failed is ArgumentOutOfRangeException or OverflowException)
        {
            throw new ArgumentException(
                $"There is no such time of day as {hour}:{minute}:{second}.");
        }
    }

    public static TimeOnly MakeTime(long hour, long minute) => MakeTime(hour, minute, 0);

    /// <summary>
    /// A length of time, reporting one too large to hold. Days are counted separately rather than
    /// folded in, so a span of hours beyond a day still reads as hours.
    /// </summary>
    public static TimeSpan MakeSpan(long days, long hours, long minutes, long seconds)
    {
        try
        {
            return new TimeSpan((int)days, (int)hours, (int)minutes, (int)seconds);
        }
        catch (Exception failed) when (failed is ArgumentOutOfRangeException or OverflowException)
        {
            throw new OverflowException(
                $"A span of {days} days, {hours} hours, {minutes} minutes and {seconds} "
                + "seconds is too long to hold.");
        }
    }

    public static TimeSpan MakeSpan(long hours, long minutes, long seconds) =>
        MakeSpan(0, hours, minutes, seconds);

    // ---- Reading a moment ------------------------------------------------------------------

    public static long Year(DateTime moment) => moment.Year;

    public static long Month(DateTime moment) => moment.Month;

    public static long Day(DateTime moment) => moment.Day;

    public static long Hour(DateTime moment) => moment.Hour;

    public static long Minute(DateTime moment) => moment.Minute;

    public static long Second(DateTime moment) => moment.Second;

    public static long DayOfWeek(DateTime moment) => (long)moment.DayOfWeek;

    public static long DayOfYear(DateTime moment) => moment.DayOfYear;

    // ---- Moving one ------------------------------------------------------------------------

    /// <summary>
    /// <para>A moment never changes, so each of these yields another one.</para>
    /// <para>The count is a real, which is what lets half an hour be written as one — and it
    /// crosses to the binary floating point the platform asks for, which is the one place in this
    /// file a value loses anything. The loss is below a tick and no program can see it.</para>
    /// </summary>
    public static DateTime AddDays(DateTime moment, decimal days) =>
        moment.AddDays((double)days);

    public static DateTime AddHours(DateTime moment, decimal hours) =>
        moment.AddHours((double)hours);

    public static DateTime AddMinutes(DateTime moment, decimal minutes) =>
        moment.AddMinutes((double)minutes);

    public static DateTime AddSeconds(DateTime moment, decimal seconds) =>
        moment.AddSeconds((double)seconds);

    public static DateTime AddYears(DateTime moment, long years) => moment.AddYears((int)years);

    public static DateTime AddMonths(DateTime moment, long months) => moment.AddMonths((int)months);

    public static long CompareMoments(DateTime moment, DateTime other) => moment.CompareTo(other);

    public static DateTime Add(DateTime moment, TimeSpan length) => moment + length;

    public static TimeSpan Subtract(DateTime moment, DateTime other) => moment - other;

    public static DateTime SubtractSpan(DateTime moment, TimeSpan length) => moment - length;

    // ---- The halves of a moment ------------------------------------------------------------

    public static DateOnly DatePart(DateTime moment) => DateOnly.FromDateTime(moment);

    public static TimeOnly TimePart(DateTime moment) => TimeOnly.FromDateTime(moment);

    public static DateTime FromDate(DateOnly day) => day.ToDateTime(TimeOnly.MinValue);

    public static DateTime FromDateAndTime(DateOnly day, TimeOnly clock) => day.ToDateTime(clock);

    // ---- A length of time ------------------------------------------------------------------

    public static TimeSpan Zero() => TimeSpan.Zero;

    public static TimeSpan FromDays(decimal days) => TimeSpan.FromDays((double)days);

    public static TimeSpan FromHours(decimal hours) => TimeSpan.FromHours((double)hours);

    public static TimeSpan FromMinutes(decimal minutes) => TimeSpan.FromMinutes((double)minutes);

    public static TimeSpan FromSeconds(decimal seconds) => TimeSpan.FromSeconds((double)seconds);

    /// <summary>
    /// The part of the span that is whole days, hours, minutes, seconds — as against the
    /// <c>Total</c> family below, which is the whole span counted in one unit. Two hours and a
    /// half is <c>Hours</c> 2 and <c>TotalHours</c> 2.5.
    /// </summary>
    public static long Days(TimeSpan length) => length.Days;

    public static long Hours(TimeSpan length) => length.Hours;

    public static long Minutes(TimeSpan length) => length.Minutes;

    public static long Seconds(TimeSpan length) => length.Seconds;

    public static decimal TotalDays(TimeSpan length) => (decimal)length.TotalDays;

    public static decimal TotalHours(TimeSpan length) => (decimal)length.TotalHours;

    public static decimal TotalMinutes(TimeSpan length) => (decimal)length.TotalMinutes;

    public static decimal TotalSeconds(TimeSpan length) => (decimal)length.TotalSeconds;

    public static TimeSpan Negate(TimeSpan length) => length.Negate();

    public static TimeSpan Duration(TimeSpan length) => length.Duration();

    public static TimeSpan AddSpan(TimeSpan length, TimeSpan other) => length + other;

    public static TimeSpan SubtractSpans(TimeSpan length, TimeSpan other) => length - other;

    public static long CompareSpans(TimeSpan length, TimeSpan other) => length.CompareTo(other);

    // ---- A day -----------------------------------------------------------------------------

    public static DateOnly TodayOnly() => DateOnly.FromDateTime(DateTime.Now);

    public static DateOnly DateFromMoment(DateTime moment) => DateOnly.FromDateTime(moment);

    public static long DateYear(DateOnly day) => day.Year;

    public static long DateMonth(DateOnly day) => day.Month;

    public static long DateDay(DateOnly day) => day.Day;

    public static long DateDayOfWeek(DateOnly day) => (long)day.DayOfWeek;

    public static long DateDayOfYear(DateOnly day) => day.DayOfYear;

    /// <summary>
    /// Moved by whole days, months, or years. A day has no smaller part to move by, which is why
    /// these count in integers where the moment family counts in reals.
    /// </summary>
    public static DateOnly DateAddDays(DateOnly day, long days) => day.AddDays((int)days);

    public static DateOnly DateAddMonths(DateOnly day, long months) => day.AddMonths((int)months);

    public static DateOnly DateAddYears(DateOnly day, long years) => day.AddYears((int)years);

    public static DateTime DateAtTime(DateOnly day, TimeOnly clock) => day.ToDateTime(clock);

    public static long CompareDates(DateOnly day, DateOnly other) => day.CompareTo(other);

    // ---- A time of day ---------------------------------------------------------------------

    public static TimeOnly TimeNow() => TimeOnly.FromDateTime(DateTime.Now);

    public static TimeOnly TimeFromMoment(DateTime moment) => TimeOnly.FromDateTime(moment);

    public static long TimeHour(TimeOnly clock) => clock.Hour;

    public static long TimeMinute(TimeOnly clock) => clock.Minute;

    public static long TimeSecond(TimeOnly clock) => clock.Second;

    /// <summary>
    /// Moved around the clock, which wraps: an hour after eleven at night is midnight. That is
    /// what a time of day is, as against a moment, which has a date to carry the difference into.
    /// </summary>
    public static TimeOnly TimeAddHours(TimeOnly clock, decimal hours) =>
        clock.AddHours((double)hours);

    public static TimeOnly TimeAddMinutes(TimeOnly clock, decimal minutes) =>
        clock.AddMinutes((double)minutes);

    public static TimeSpan TimeToSpan(TimeOnly clock) => clock.ToTimeSpan();

    public static long CompareTimes(TimeOnly clock, TimeOnly other) => clock.CompareTo(other);

    // ---- Writing by a pattern --------------------------------------------------------------

    /// <summary>
    /// <para>Written by a pattern, invariantly.</para>
    /// <para>A pattern the platform cannot read raises a <c>FormatException</c>, which is already
    /// a name a Profi-C program can catch — the two are the same type — so nothing here has to
    /// translate it.</para>
    /// </summary>
    public static string Format(DateTime moment, string pattern) =>
        moment.ToString(pattern, CultureInfo.InvariantCulture);

    public static string Format(TimeSpan length, string pattern) =>
        length.ToString(pattern, CultureInfo.InvariantCulture);

    public static string Format(DateOnly day, string pattern) =>
        day.ToString(pattern, CultureInfo.InvariantCulture);

    public static string Format(TimeOnly clock, string pattern) =>
        clock.ToString(pattern, CultureInfo.InvariantCulture);

    // ---- Reading back from text ------------------------------------------------------------

    public static Optional<DateTime> ParseMoment(string text) =>
        DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime it)
            ? Optional<DateTime>.Of(it)
            : default;

    public static Optional<DateTime> ParseMomentExactly(string text, string pattern) =>
        DateTime.TryParseExact(
            text, pattern, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime it)
            ? Optional<DateTime>.Of(it)
            : default;

    public static Optional<TimeSpan> ParseSpan(string text) =>
        TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out TimeSpan it)
            ? Optional<TimeSpan>.Of(it)
            : default;

    public static Optional<TimeSpan> ParseSpanExactly(string text, string pattern) =>
        TimeSpan.TryParseExact(text, pattern, CultureInfo.InvariantCulture, out TimeSpan it)
            ? Optional<TimeSpan>.Of(it)
            : default;

    public static Optional<DateOnly> ParseDate(string text) =>
        DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly it)
            ? Optional<DateOnly>.Of(it)
            : default;

    public static Optional<DateOnly> ParseDateExactly(string text, string pattern) =>
        DateOnly.TryParseExact(
            text, pattern, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly it)
            ? Optional<DateOnly>.Of(it)
            : default;

    public static Optional<TimeOnly> ParseTime(string text) =>
        TimeOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly it)
            ? Optional<TimeOnly>.Of(it)
            : default;

    public static Optional<TimeOnly> ParseTimeExactly(string text, string pattern) =>
        TimeOnly.TryParseExact(
            text, pattern, CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly it)
            ? Optional<TimeOnly>.Of(it)
            : default;

    /// <summary>
    /// <para>The same eight, for the interpreter, which holds an absence as null.</para>
    /// <para><inheritdoc cref="ProfiCText.ToCharactersUntyped" path="/summary/para[2]"/></para>
    /// </summary>
    public static object? ParseMomentUntyped(string text) => Untyped(ParseMoment(text));

    /// <inheritdoc cref="ParseMomentUntyped"/>
    public static object? ParseMomentExactlyUntyped(string text, string pattern) =>
        Untyped(ParseMomentExactly(text, pattern));

    /// <inheritdoc cref="ParseMomentUntyped"/>
    public static object? ParseSpanUntyped(string text) => Untyped(ParseSpan(text));

    /// <inheritdoc cref="ParseMomentUntyped"/>
    public static object? ParseSpanExactlyUntyped(string text, string pattern) =>
        Untyped(ParseSpanExactly(text, pattern));

    /// <inheritdoc cref="ParseMomentUntyped"/>
    public static object? ParseDateUntyped(string text) => Untyped(ParseDate(text));

    /// <inheritdoc cref="ParseMomentUntyped"/>
    public static object? ParseDateExactlyUntyped(string text, string pattern) =>
        Untyped(ParseDateExactly(text, pattern));

    /// <inheritdoc cref="ParseMomentUntyped"/>
    public static object? ParseTimeUntyped(string text) => Untyped(ParseTime(text));

    /// <inheritdoc cref="ParseMomentUntyped"/>
    public static object? ParseTimeExactlyUntyped(string text, string pattern) =>
        Untyped(ParseTimeExactly(text, pattern));

    private static object? Untyped<T>(Optional<T> held) where T : struct =>
        held.HasValue ? held.Value : null;
}
