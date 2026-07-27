namespace ProfiC.Compiler.Lexing;

/// <summary>
/// <para>Represents a single scanned token.</para>
/// <para>Pairs a lexeme string with its classified token type.</para>
/// </summary>
public class Token
{
    /// <summary>The raw lexeme text from the source.</summary>
    public string Lexeme { get; }

    /// <summary>The classified token type.</summary>
    public TokenType Type { get; }

    public Token(string lexeme, TokenType type)
    {
        Lexeme = lexeme;
        Type = type;
    }

    public override string ToString()
    {
        return $"({Type}, '{Lexeme}')";
    }
}
