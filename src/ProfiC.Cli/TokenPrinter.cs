using System.Text;
using ProfiC.Compiler.Lexing;

namespace ProfiC.Cli;

/// <summary>
/// <para>Renders a token stream in the format <c>profic tokens</c> prints.</para>
/// <para>The test suite pins lexer behavior against this same format, so the golden files
/// double as documentation of a shipped command rather than being a second format that has
/// to be maintained alongside it.</para>
/// </summary>
public static class TokenPrinter
{
    /// <summary>Formats a whole token stream, one token per line.</summary>
    public static string Print(IEnumerable<Token> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        StringBuilder builder = new();

        foreach (Token token in tokens)
        {
            builder.AppendLine(Print(token));
        }

        return builder.ToString();
    }

    /// <summary>
    /// <para>Formats one token as position, type, then lexeme.</para>
    /// <para>Only the line and column appear, not the raw offset: the offset is pinned
    /// separately by a round-trip property test, and leaving it out keeps the golden files
    /// from churning on incidental whitespace edits.</para>
    /// </summary>
    public static string Print(Token token)
    {
        ArgumentNullException.ThrowIfNull(token);

        string position = $"{token.Line,4}:{token.Column,-4}";
        return $"{position} {token.Type,-20} '{token.Lexeme}'";
    }
}
