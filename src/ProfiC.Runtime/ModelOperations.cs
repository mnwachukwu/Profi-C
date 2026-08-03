using System.Globalization;
using System.Text;

namespace ProfiC.Runtime;

/// <summary>
/// <para>The members every Profi-C type inherits from <c>Model</c>.</para>
/// <para>These are operations rather than a base class, because <c>Model</c> is
/// <c>System.Object</c> in emitted code. It has to be: <c>string</c> and the set type derive
/// from <c>object</c> already, and nothing a runtime could define would sit above them.
/// </para>
/// </summary>
public static class ModelOperations
{
    /// <summary>
    /// <para>How a <c>real</c> is written: every digit it holds, and no trailing zeros.</para>
    /// <para><b>A decimal carries how precise it is, and the language does not.</b> Three point
    /// zero times three point zero really is <c>9.00</c> to a decimal — the scale is part of the
    /// value, and .NET prints it — but scale is a thing Profi-C never talks about anywhere else,
    /// so showing it only here would leak the backing type into what a reader sees.</para>
    /// <para>Written as a pattern rather than as <c>G29</c>, which normalizes the same way and
    /// then turns a small number into <c>1E-10</c>. A reader who wrote a tenth of a billionth
    /// should see one.</para>
    /// </summary>
    private const string WithoutTrailingZeros = "0.############################";

    /// <summary>
    /// <para>Renders any value the way Profi-C prints it.</para>
    /// <para>Defaults differ by kind, and the difference is forced rather than chosen. A
    /// structure prints field by field, because a structure cannot contain itself and the
    /// walk therefore ends. A model prints only its type name, because a model can take part
    /// in a cycle and there is no printing equivalent of the trick that makes equality
    /// cycle-safe.</para>
    /// </summary>
    public static string ToDisplayString(object? value) => value switch
    {
        // Two spellings of the same absence. The interpreter holds an empty optional as null,
        // being untyped; emitted code holds a typed Optional<T>, which cannot be null and says
        // so instead. Both read as 'empty', which is what a program sees either way.
        null => "empty",
        IProfiCOptional { HasValue: false } => "empty",
        IProfiCOptional present => ToDisplayString(present.GetValue()),

        string text => text,
        bool flag => flag ? "true" : "false",
        char character => character.ToString(),
        Fraction fraction => fraction.ToString(),
        // A real holds its digits as digits, so what it shows is what it holds and no shortest-
        // round-trip rule is needed to hide a binary approximation.
        decimal real => real.ToString(WithoutTrailingZeros, CultureInfo.InvariantCulture),

        // The one a float has that no other number does, written the way the language names it
        // rather than the way .NET abbreviates it. A reader who prints this and a reader who
        // writes 'Float.NotANumber' should be looking at the same word.
        double notANumber when double.IsNaN(notANumber) => "NotANumber",

        // Binary floating point, where the shortest form that reads back as the same value is the
        // only honest rendering: writing every digit would show noise the value never carried.
        double binary => binary.ToString("R", CultureInfo.InvariantCulture),
        float binary => binary.ToString("R", CultureInfo.InvariantCulture),
        Enum member => member.ToString(),

        // An enumeration member shows the name that was written, not the number behind it.
        EnumValue member => member.MemberName,

        // Year first, and the time only when there is one. The platform's own rendering is
        // "01/02/2000", which leaves a reader to guess whether that is January or February
        // depending on where they learned to write dates. This order is the same everywhere,
        // and it sorts the way it reads.
        DateTime moment => moment.TimeOfDay == TimeSpan.Zero
            ? moment.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : moment.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),

        // The "c" form, which .NET defines as culture-invariant rather than merely rendering
        // it that way. It shows days only when there are some, keeps a fraction of a second
        // where there is one, and keeps the sign — which a hand-written pattern does not, and
        // a span of minus half an hour that reads as half an hour is worse than a verbose one.
        TimeSpan length => length.ToString("c", CultureInfo.InvariantCulture),

        // Year first and hours in twenty-four, for the same reason a moment is: the order is
        // the same wherever the reader learned to write one, and it sorts as it reads.
        DateOnly day => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        TimeOnly clock => clock.ToString("HH:mm:ss", CultureInfo.InvariantCulture),

        IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    /// <summary>
    /// <para>How a value shows when it sits beside others, separated by a delimiter.</para>
    /// <para>A character and a string are quoted the way each is written in source. Without it
    /// the delimiter cannot be told from the same characters inside a value: a set of two
    /// strings shows as <c>{a, b}</c>, which is what one string holding a comma would show as
    /// too. Quoting also spares the reader guessing whether a gap is a space or nothing.</para>
    /// <para>A quote inside quotes is left as it is. It reads a little oddly and is much the
    /// smaller of the two problems.</para>
    /// <para>A value printed on its own is not quoted, since nothing sits beside it to be
    /// confused with.</para>
    /// </summary>
    public static string ToElementString(object? value) => value switch
    {
        char character => $"'{character}'",
        string text => $"\"{text}\"",

        // A present optional is written as what it holds, quoting included — so a set of
        // optional strings reads the same as a set of strings, which is what it looks like.
        IProfiCOptional { HasValue: true } present => ToElementString(present.GetValue()),

        _ => ToDisplayString(value),
    };

    /// <summary>
    /// <para>The default rendering of a structure: its type name, then each field.</para>
    /// <para>Emitted structures call this unless the author overrode <c>ToString</c>.</para>
    /// </summary>
    public static string StructureToString(string typeName, IProfiCModel value)
    {
        ArgumentNullException.ThrowIfNull(value);

        StringBuilder builder = new();
        builder.Append(typeName).Append(" { ");

        for (int i = 0; i < value.DeepMemberCount; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            builder.Append(ToElementString(value.GetDeepMember(i)));
        }

        return builder.Append(" }").ToString();
    }

    /// <summary>Structural equality, as <c>==</c> uses.</summary>
    public static bool DeepEquals(object? left, object? right) => DeepEquality.Equals(left, right);
}

/// <summary>
/// <para>The <c>Reference</c> built-in: identity comparison, spelled out.</para>
/// <para>Profi-C's <c>==</c> is structural, so this is how a program asks the question C#
/// answers by default. It takes models only — asking whether two values have the same
/// identity is meaningless, so the compiler rejects a structure here rather than answering
/// false.</para>
/// </summary>
public static class Reference
{
    /// <summary>True when both arguments are the same object.</summary>
    public static new bool Equals(object? left, object? right) => ReferenceEquals(left, right);
}

/// <summary>
/// <para>The <c>Console</c> built-in.</para>
/// <para><c>Write</c> leaves the cursor where it is and <c>WriteLine</c> ends the line,
/// exactly as in C#. Both accept a value of any type; the compiler renders it from its static
/// type, so nothing here needs an overload per primitive.</para>
/// </summary>
public static class ProfiCConsole
{
    /// <summary>Writes a value without ending the line.</summary>
    public static void Write(object? value) =>
        Console.Write(ModelOperations.ToDisplayString(value));

    /// <summary>Ends the line.</summary>
    public static void WriteLine() => Console.WriteLine();

    /// <summary>Writes a value and ends the line.</summary>
    public static void WriteLine(object? value) =>
        Console.WriteLine(ModelOperations.ToDisplayString(value));

    /// <summary>
    /// Writes a formatted value. Placeholders are zero-based, matching .NET, so lowering maps
    /// straight onto <c>String.Format</c> with no index translation.
    /// </summary>
    public static void Write(string format, ProfiCSet<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(arguments);

        Console.Write(string.Format(CultureInfo.InvariantCulture, format, [.. arguments]));
    }

    /// <summary>
    /// <para>Reads a line from input.</para>
    /// <para>Optional, because input can end. This is where null stops: a .NET reference that
    /// may be absent becomes an optional at the boundary and never enters the language.</para>
    /// </summary>
    public static Optional<string> Read() => Optional.FromNullable(Console.ReadLine());
}
