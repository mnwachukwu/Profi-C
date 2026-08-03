using System.Globalization;
using System.Text;
using ProfiC.Compiler.Ast;
using ProfiC.Runtime;

namespace ProfiC.Compiler.Semantics;

/// <summary>
/// <para>Turns a literal's source text into the value it denotes.</para>
/// <para>The scanner deliberately kept every lexeme as an exact slice of the source, so this
/// is where escapes are resolved, digits become numbers, and a fraction is reduced. Doing it
/// here rather than while scanning means the scanner has one job and a literal's original
/// spelling survives for anything that wants it.</para>
/// </summary>
public static class LiteralDecoder
{
    /// <summary>
    /// Decodes a literal, or returns null if it names no value. Callers stay quiet about a
    /// null: either the scanner reported the text, or <see cref="FaultIn"/> did.
    /// </summary>
    public static object? Decode(LiteralExpr literal)
    {
        ArgumentNullException.ThrowIfNull(literal);

        return literal.Kind switch
        {
            LiteralKind.Integer => DecodeInteger(literal.Text),
            LiteralKind.Real => DecodeReal(literal.Text),
            LiteralKind.Float => DecodeFloat(literal.Text),
            LiteralKind.Fraction => DecodeFraction(literal.Text),
            LiteralKind.Character => DecodeCharacter(literal.Text),
            LiteralKind.String => DecodeString(literal.Text),
            LiteralKind.BlockString => DecodeBlockString(literal.Text),
            LiteralKind.Boolean => string.Equals(literal.Text, "true", StringComparison.Ordinal),
            _ => null,
        };
    }

    /// <summary>
    /// <para>What is wrong with a number that will not read, where the scanner had no way to
    /// see it.</para>
    /// <para>A malformed string, character or escape is the scanner's to report, since the fault
    /// is in the shape and the shape is what it reads. A number's shape is fine in every case
    /// here — the digits are digits — and only turning them into a value discovers that there
    /// is no value. So this is asked once by <see cref="LiteralChecker"/>, and everything else
    /// that decodes a literal stays quiet.</para>
    /// <para>A float is deliberately never at fault. It has a value for a number too large,
    /// which is what <c>Float.Infinity</c> names, and its own arithmetic already produces that
    /// value.</para>
    /// </summary>
    public static LiteralFault FaultIn(LiteralExpr literal)
    {
        ArgumentNullException.ThrowIfNull(literal);

        return literal.Kind switch
        {
            LiteralKind.Integer => DecodeInteger(literal.Text) is null
                ? LiteralFault.TooLarge
                : LiteralFault.None,

            LiteralKind.Real => DecodeReal(literal.Text) is null
                ? LiteralFault.TooLarge
                : LiteralFault.None,

            LiteralKind.Fraction => FaultInFraction(literal.Text),

            _ => LiteralFault.None,
        };
    }

    /// <summary>
    /// <para>Which of a fraction's two ways of naming no number this one is.</para>
    /// <para>Held apart from <see cref="DecodeFraction"/> rather than folded into it, because
    /// the two faults want different things said: a part that outgrows a whole number is a size
    /// problem, and a zero underneath is division by zero.</para>
    /// </summary>
    private static LiteralFault FaultInFraction(string written)
    {
        string text = written.Replace("_", string.Empty, StringComparison.Ordinal);
        int bar = text.IndexOf('|', StringComparison.Ordinal);

        if (bar < 0)
        {
            return LiteralFault.None;
        }

        if (!long.TryParse(text[..bar], NumberStyles.None, CultureInfo.InvariantCulture, out _)
            || !long.TryParse(
                text[(bar + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out long denominator))
        {
            return LiteralFault.TooLarge;
        }

        return denominator == 0 ? LiteralFault.OverZero : LiteralFault.None;
    }

    /// <summary>
    /// <para>Reads a block string: the text between its delimiters, taken as it stands.</para>
    /// <para>Where the block spans lines, the indentation of the closing quotes is removed
    /// from every line, and the line breaks next to each delimiter go. That is C#'s rule,
    /// and it is what lets a block sit at the indentation of the code around it without
    /// carrying that indentation into what it holds — which is the difference between the
    /// feature being usable inside a function and only at the left margin.</para>
    /// <para>Written on one line, it is simply what lies between the quotes.</para>
    /// <para>The delimiter is however many quotes opened the block, so the text says how to
    /// read itself and nothing has to be carried here from the scanner.</para>
    /// </summary>
    private static object DecodeBlockString(string text)
    {
        int delimiter = 0;

        while (delimiter < text.Length && text[delimiter] == '"')
        {
            delimiter++;
        }

        string inner = text.Length >= delimiter * 2
            ? text[delimiter..^delimiter]
            : string.Empty;

        int firstBreak = inner.IndexOf('\n');

        if (firstBreak < 0)
        {
            return inner;
        }

        // Everything before the first break is whitespace in a well-formed block; anything
        // else there is the author's and is kept by leaving the one-line reading alone above.
        string body = inner[(firstBreak + 1)..];
        int lastBreak = body.LastIndexOf('\n');

        string closing = lastBreak < 0 ? string.Empty : body[(lastBreak + 1)..];

        if (closing.Trim().Length > 0)
        {
            // The closing quotes share a line with text, so there is no margin to read and
            // nothing is removed.
            return body;
        }

        string[] lines = (lastBreak < 0 ? string.Empty : body[..lastBreak])
            .Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            lines[i] = lines[i].StartsWith(closing, StringComparison.Ordinal)
                ? lines[i][closing.Length..]
                : lines[i].TrimStart();
        }

        return string.Join("\n", lines).ReplaceLineEndings("\n");
    }

    /// <summary>
    /// Reads a whole number, in ten or in the base its prefix names. The prefix is dropped
    /// rather than parsed, since what it says is which digits follow rather than any of them.
    /// </summary>
    private static object? DecodeInteger(string written)
    {
        // Separators group digits for a reader and mean nothing to the value.
        string text = written.Replace("_", string.Empty, StringComparison.Ordinal);

        NumberStyles style = text.Length > 2 && text[0] == '0'
            ? text[1] switch
            {
                'x' or 'X' => NumberStyles.AllowHexSpecifier,
                'b' or 'B' => NumberStyles.AllowBinarySpecifier,
                _ => NumberStyles.None,
            }
            : NumberStyles.None;

        string digits = style == NumberStyles.None ? text : text[2..];

        return long.TryParse(digits, style, CultureInfo.InvariantCulture, out long value)
            ? value
            : null;
    }

    /// <summary>
    /// <para>Reads a number with a decimal point, as the decimal it is written as.</para>
    /// <para>The digits are kept as digits rather than converted to the nearest binary fraction,
    /// which is the whole of what makes <c>0.1 + 0.2</c> come to <c>0.3</c> here.</para>
    /// </summary>
    private static object? DecodeReal(string written) =>
        decimal.TryParse(
            written.Replace("_", string.Empty, StringComparison.Ordinal),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out decimal value)
            ? value
            : null;

    /// <summary>
    /// The same digits read as binary floating point, which is what the <c>f</c> against them
    /// asked for. The suffix is dropped rather than parsed, since what it says is which type to
    /// read into rather than any part of the number.
    /// </summary>
    private static object? DecodeFloat(string written) =>
        double.TryParse(
            written.Replace("_", string.Empty, StringComparison.Ordinal).TrimEnd('f', 'F'),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out double value)
            ? value
            : null;

    /// <summary>Decodes <c>numerator|denominator</c>, reducing it on the way.</summary>
    private static object? DecodeFraction(string written)
    {
        string text = written.Replace("_", string.Empty, StringComparison.Ordinal);
        int bar = text.IndexOf('|', StringComparison.Ordinal);

        if (bar < 0)
        {
            return null;
        }

        if (!long.TryParse(text[..bar], NumberStyles.None, CultureInfo.InvariantCulture, out long numerator)
            || !long.TryParse(text[(bar + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out long denominator)
            || denominator == 0)
        {
            return null;
        }

        return new Fraction(numerator, denominator);
    }

    private static object? DecodeCharacter(string text)
    {
        if (text.Length < 2 || text[0] != '\'' || text[^1] != '\'')
        {
            return null;
        }

        string decoded = Unescape(text[1..^1]);
        return decoded.Length == 1 ? decoded[0] : null;
    }

    private static object? DecodeString(string text)
    {
        if (text.Length < 2 || text[0] != '"')
        {
            return null;
        }

        // The closing quote is absent on an unterminated literal, which the scanner already
        // reported; decode what is there rather than failing a second time.
        int end = text[^1] == '"' && text.Length > 1 ? text.Length - 1 : text.Length;
        return Unescape(text[1..end]);
    }

    /// <summary>Resolves the escape sequences the language recognizes.</summary>
    private static string Unescape(string text)
    {
        if (!text.Contains('\\', StringComparison.Ordinal))
        {
            return text;
        }

        StringBuilder builder = new(text.Length);

        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '\\' || i + 1 >= text.Length)
            {
                builder.Append(text[i]);
                continue;
            }

            char escape = text[++i];

            switch (escape)
            {
                case 'n': builder.Append('\n'); break;
                case 't': builder.Append('\t'); break;
                case 'r': builder.Append('\r'); break;
                case '0': builder.Append('\0'); break;
                case '\\': builder.Append('\\'); break;
                case '"': builder.Append('"'); break;
                case '\'': builder.Append('\''); break;

                case 'u' when i + 4 < text.Length
                              && ushort.TryParse(
                                  text.AsSpan(i + 1, 4),
                                  NumberStyles.HexNumber,
                                  CultureInfo.InvariantCulture,
                                  out ushort code):
                    builder.Append((char)code);
                    i += 4;
                    break;

                default:
                    // Already reported while scanning; keep the character so the rest of the
                    // literal still decodes.
                    builder.Append(escape);
                    break;
            }
        }

        return builder.ToString();
    }
}
