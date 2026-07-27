namespace ProfiC.Compiler.Lexing;

/// <summary>
/// Classification helpers over <see cref="TokenType"/>, used by the parser and by
/// diagnostic messages.
/// </summary>
public static class TokenTypeExtensions
{
    /// <summary>True if the token type is one of the 55 reserved words.</summary>
    public static bool IsKeyword(this TokenType type) =>
        type >= TokenType.Abstract && type <= TokenType.Yield;

    /// <summary>
    /// True if the token type is a literal value. Note that <c>true</c> and <c>false</c>
    /// are keywords rather than literal token types, so they are excluded here.
    /// </summary>
    public static bool IsLiteral(this TokenType type) =>
        type >= TokenType.IntegerLiteral && type <= TokenType.FractionLiteral;

    /// <summary>
    /// <para>The canonical source spelling of a token type whose text never varies, for
    /// diagnostics such as "expected 'end function'".</para>
    /// <para>Returns null for identifiers, literals, and end of file, whose text differs
    /// from one occurrence to the next.</para>
    /// </summary>
    public static string? Text(this TokenType type) => type switch
    {
        TokenType.Plus => "+",
        TokenType.Minus => "-",
        TokenType.Star => "*",
        TokenType.Slash => "/",
        TokenType.Percent => "%",
        TokenType.EqualEqual => "==",
        TokenType.NotEqual => "!=",
        TokenType.LessThan => "<",
        TokenType.GreaterThan => ">",
        TokenType.LessThanOrEqual => "<=",
        TokenType.GreaterThanOrEqual => ">=",
        TokenType.Equal => "=",
        TokenType.Pipe => "|",
        TokenType.Question => "?",
        TokenType.Colon => ":",
        TokenType.Arrow => "=>",
        TokenType.LeftParen => "(",
        TokenType.RightParen => ")",
        TokenType.LeftBrace => "{",
        TokenType.RightBrace => "}",
        TokenType.LeftBracket => "[",
        TokenType.RightBracket => "]",
        TokenType.Comma => ",",
        TokenType.Semicolon => ";",
        TokenType.Dot => ".",
        _ => type.IsKeyword() ? KeywordText(type) : null,
    };

    private static string? KeywordText(TokenType type)
    {
        foreach (KeyValuePair<string, TokenType> entry in ReservedWords.Keywords)
        {
            if (entry.Value == type)
            {
                return entry.Key;
            }
        }

        return null;
    }
}
