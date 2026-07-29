using ProfiC.Compiler.Text;

namespace ProfiC.Compiler.Lexing;

/// <summary>
/// <para>A single scanned token: what it is, the text it came from, and where it sat.</para>
/// <para><see cref="Lexeme"/> is always the exact source text the span covers, never a
/// reconstruction. Decoding a literal into a value, which for a string means resolving its
/// escape sequences, belongs to the parser rather than here.</para>
/// </summary>
public sealed record Token(TokenType Type, string Lexeme, SourceSpan Span)
{
    /// <summary>The one-based line the token begins on.</summary>
    public int Line => Span.Start.Line;

    /// <summary>The one-based column the token begins at.</summary>
    public int Column => Span.Start.Column;

    /// <summary>True for the token that terminates every token stream.</summary>
    public bool IsEndOfFile => Type == TokenType.EndOfFile;

    /// <summary>
    /// <para>The name an identifier stands for, without the <c>@</c> that may mark it.</para>
    /// <para>The mark says "this reserved word is a name here" and is no part of the name
    /// itself, so <c>@end</c> and a name spelled <c>end</c> are the same name. The lexeme keeps
    /// the mark because a lexeme is always the exact source slice.</para>
    /// </summary>
    public string Name => Lexeme.StartsWith('@') ? Lexeme[1..] : Lexeme;

    public override string ToString() => $"({Type}, '{Lexeme}')";
}
