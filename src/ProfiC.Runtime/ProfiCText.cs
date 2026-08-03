using System.Globalization;

namespace ProfiC.Runtime;

/// <summary>
/// <para>What the language's <c>string</c> does, in one place both engines call.</para>
/// <para>Several of these are not what the framework's method of the same name does, and that is
/// the reason they are here rather than emitted as a call to it. The differences are small, and
/// each one is a place the interpreter and an emitted program could have quietly disagreed.</para>
/// <para><b>Text that is empty changes nothing.</b> Asked to replace nothing, or to remove
/// nothing, or to trim nothing from an end, a string comes back as it was. The framework raises
/// for the first two, naming a parameter this language does not have — and the rest of the family
/// already reads that way, <c>Contains("")</c> being true and <c>IndexOf("")</c> being zero, so
/// one rule covers all of it: an empty argument matches trivially and takes nothing away.</para>
/// </summary>
public static class ProfiCText
{
    /// <summary>Whether one string holds another. Ordinal, as every comparison here is.</summary>
    public static bool Contains(string subject, string what) =>
        subject.Contains(what, StringComparison.Ordinal);

    /// <summary>Where one string first holds another, or -1.</summary>
    public static long IndexOf(string subject, string what) =>
        subject.IndexOf(what, StringComparison.Ordinal);

    /// <summary>
    /// <para>Every place one string appears, written differently.</para>
    /// <para>Replacing nothing is nothing to do. The framework raises instead, and the message
    /// names <c>oldValue</c> — a parameter of a method the reader did not call.</para>
    /// </summary>
    public static string Replace(string subject, string what, string with) =>
        what.Length == 0 ? subject : subject.Replace(what, with, StringComparison.Ordinal);

    /// <summary>Every place one string appears, taken out. <see cref="Replace"/> with nothing.</summary>
    public static string Remove(string subject, string what) => Replace(subject, what, string.Empty);

    /// <summary>One character taken out, by position.</summary>
    public static string RemoveAt(string subject, long at) => subject.Remove(Within(subject, at), 1);

    /// <summary>Another string on the end.</summary>
    public static string Insert(string subject, string what) => subject + what;

    /// <summary>Another string put in at a position.</summary>
    public static string InsertAt(string subject, long at, string what) =>
        subject.Insert(Within(subject, at, alsoTheEnd: true), what);

    /// <summary>
    /// A run of a string, by where it starts and how many. The one member named for what C#
    /// calls it, since a reader arriving with the habit finds it where they reach.
    /// </summary>
    public static string Substring(string subject, long start, long length)
    {
        if (start < 0 || start > subject.Length || length < 0 || start + length > subject.Length)
        {
            throw new IndexOutOfRangeException(
                $"Cannot take {length} characters from position {start} of a string of "
                + $"{subject.Length}.");
        }

        return subject.Substring((int)start, (int)length);
    }

    /// <summary>A run to the end, the way a set is asked for one.</summary>
    public static string Subset(string subject, long start) => Subset(subject, start, subject.Length);

    /// <summary>
    /// A run from one position up to but not including another — the reading <c>until</c> has in
    /// a loop, and what makes two runs put the whole string back together.
    /// </summary>
    public static string Subset(string subject, long start, long end)
    {
        if (start < 0 || start > subject.Length || end < start || end > subject.Length)
        {
            throw new IndexOutOfRangeException(
                $"Cannot take the run from {start} to {end} of a string of {subject.Length}.");
        }

        return subject[(int)start..(int)end];
    }

    /// <summary>
    /// <para>Its characters, as a set of them.</para>
    /// <para><b>Two forms, one for each engine</b>, which is the shape every member here that
    /// answers with a set takes. An emitted program holds a set that names what it holds, so it
    /// wants the <c>char</c> one; the interpreter holds every set as one of objects, having no
    /// element type to name. Same characters in the same order — the element type is the whole of
    /// the difference.</para>
    /// </summary>
    public static ProfiCSet<char> ToCharacters(string subject) => new(subject);

    /// <inheritdoc cref="ToCharacters"/>
    public static ProfiCSet<object?> ToCharactersUntyped(string subject) =>
        new(subject.Select(c => (object?)c));

    /// <summary>
    /// <para>The pieces between each appearance of a separator.</para>
    /// <para>Separating on nothing leaves one piece, which is the whole string — the same answer
    /// as every other member gives for an empty argument.</para>
    /// </summary>
    public static ProfiCSet<string> Split(string subject, string separator) =>
        new(Pieces(subject, separator));

    /// <inheritdoc cref="Split"/>
    public static ProfiCSet<object?> SplitUntyped(string subject, string separator) =>
        new(Pieces(subject, separator).Select(piece => (object?)piece));

    public static string Trim(string subject) => subject.Trim();

    public static string TrimStart(string subject) => subject.TrimStart();

    public static string TrimEnd(string subject) => subject.TrimEnd();

    /// <summary>Any of these characters, taken from both ends.</summary>
    public static string Trim(string subject, string these) => subject.Trim(these.ToCharArray());

    public static string TrimStart(string subject, string these) =>
        subject.TrimStart(these.ToCharArray());

    public static string TrimEnd(string subject, string these) =>
        subject.TrimEnd(these.ToCharArray());

    /// <summary>
    /// <para>Any of these characters, where they were given as a set rather than a string.</para>
    /// <para>Taken as the set every set is, rather than as one of characters, so that the two
    /// engines reach one method: an emitted program hands over a set of <c>char</c> and the
    /// interpreter one of objects, and neither shape is named here.</para>
    /// </summary>
    public static string Trim(string subject, IProfiCSet these) =>
        subject.Trim(Characters(these));

    public static string TrimStart(string subject, IProfiCSet these) =>
        subject.TrimStart(Characters(these));

    public static string TrimEnd(string subject, IProfiCSet these) =>
        subject.TrimEnd(Characters(these));

    public static string ToUpper(string subject) => subject.ToUpperInvariant();

    public static string ToLower(string subject) => subject.ToLowerInvariant();

    /// <summary>
    /// The first letter raised and the rest left alone. An empty string has no first letter and
    /// comes back as it went in, rather than as a position nobody asked about.
    /// </summary>
    public static string Capitalize(string subject) =>
        subject.Length == 0 ? subject : char.ToUpperInvariant(subject[0]) + subject[1..];

    /// <summary>A whole number, or nothing where the text is not one.</summary>
    public static Optional<long> ToInteger(string subject) =>
        long.TryParse(subject, NumberStyles.Integer, CultureInfo.InvariantCulture, out long whole)
            ? Optional<long>.Of(whole)
            : Optional<long>.Empty;

    /// <summary>A measured number, or nothing.</summary>
    public static Optional<decimal> ToReal(string subject) =>
        decimal.TryParse(subject, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal measured)
            ? Optional<decimal>.Of(measured)
            : Optional<decimal>.Empty;

    /// <summary>The same digits read as binary floating point, or nothing.</summary>
    public static Optional<double> ToFloat(string subject) =>
        double.TryParse(subject, NumberStyles.Float, CultureInfo.InvariantCulture, out double measured)
            ? Optional<double>.Of(measured)
            : Optional<double>.Empty;

    /// <summary>
    /// True or false, or nothing. Only the two words the language writes, so "yes" and "1" are
    /// not truths — and read without regard to case, since a person typing one is not thinking
    /// about that.
    /// </summary>
    public static Optional<bool> ToBoolean(string subject) =>
        bool.TryParse(subject.Trim(), out bool truth) ? Optional<bool>.Of(truth) : Optional<bool>.Empty;

    /// <summary>
    /// <para>A number written by a pattern, which is the way out that <see cref="ToInteger"/> and
    /// <see cref="ToReal"/> are the way in.</para>
    /// <para>Written without regard to where the program is running. A decimal point is a point
    /// wherever the machine is set to, because a program that printed <c>3.14</c> in one country
    /// and <c>3,14</c> in another would be one whose output nobody could check — and checking it
    /// against the other engine is exactly what this language does.</para>
    /// </summary>
    public static string Format(long value, string pattern) =>
        value.ToString(pattern, CultureInfo.InvariantCulture);

    /// <inheritdoc cref="Format(long, string)"/>
    public static string Format(decimal value, string pattern) =>
        value.ToString(pattern, CultureInfo.InvariantCulture);

    /// <inheritdoc cref="Format(long, string)"/>
    public static string Format(double value, string pattern) =>
        value.ToString(pattern, CultureInfo.InvariantCulture);

    private static char[] Characters(IProfiCSet these) =>
        [.. Enumerable.Range(0, these.Count).Select(these.GetElement).OfType<char>()];

    /// <summary>
    /// What a string separates into, before either engine decides what kind of set holds it.
    /// </summary>
    private static IEnumerable<string> Pieces(string subject, string separator) =>
        separator.Length == 0 ? [subject] : subject.Split(separator, StringSplitOptions.None);

    /// <summary>
    /// A position inside the string, or a refusal naming what was asked for. Taking the end as
    /// well is what an insertion wants, since putting something after the last character is a
    /// thing to mean.
    /// </summary>
    private static int Within(string subject, long at, bool alsoTheEnd = false)
    {
        if (at < 0 || at > subject.Length || (!alsoTheEnd && at == subject.Length))
        {
            throw new IndexOutOfRangeException(
                $"There is no position {at} in a string of {subject.Length}.");
        }

        return (int)at;
    }
}
