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
    /// Decodes a literal, or returns null if its text is malformed. A malformed literal has
    /// already been reported by the scanner, so callers stay quiet about a null.
    /// </summary>
    public static object? Decode(LiteralExpr literal)
    {
        ArgumentNullException.ThrowIfNull(literal);

        return literal.Kind switch
        {
            LiteralKind.Integer => DecodeInteger(literal.Text),
            LiteralKind.Real => DecodeReal(literal.Text),
            LiteralKind.Fraction => DecodeFraction(literal.Text),
            LiteralKind.Character => DecodeCharacter(literal.Text),
            LiteralKind.String => DecodeString(literal.Text),
            LiteralKind.BlockString => DecodeBlockString(literal.Text),
            LiteralKind.Boolean => string.Equals(literal.Text, "true", StringComparison.Ordinal),
            _ => null,
        };
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

    private static object? DecodeReal(string written) =>
        double.TryParse(
            written.Replace("_", string.Empty, StringComparison.Ordinal),
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
