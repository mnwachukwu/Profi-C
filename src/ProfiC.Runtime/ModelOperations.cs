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
    /// <para>Renders any value the way Profi-C prints it.</para>
    /// <para>Defaults differ by kind, and the difference is forced rather than chosen. A
    /// structure prints field by field, because a structure cannot contain itself and the
    /// walk therefore ends. A model prints only its type name, because a model can take part
    /// in a cycle and there is no printing equivalent of the trick that makes equality
    /// cycle-safe.</para>
    /// </summary>
    public static string ToDisplayString(object? value) => value switch
    {
        null => "empty",
        string text => text,
        bool flag => flag ? "true" : "false",
        char character => character.ToString(),
        Fraction fraction => fraction.ToString(),
        double real => real.ToString("R", CultureInfo.InvariantCulture),
        float real => real.ToString("R", CultureInfo.InvariantCulture),
        Enum member => member.ToString(),

        // An enumeration member shows the name that was written, not the number behind it.
        EnumValue member => member.MemberName,
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
