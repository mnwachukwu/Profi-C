using System.Collections.Frozen;

namespace ProfiC.Compiler.Lexing;

/// <summary>
/// <para>Profi-C's reserved words.</para>
/// <para>Held here rather than inside the scanner so that tests, tooling, and any future
/// editor-grammar generator can all read one list.</para>
/// </summary>
public static class ReservedWords
{
    /// <summary>
    /// <para>The word that begins a comment.</para>
    /// <para>It is absent from <see cref="Keywords"/> because it never produces a token: the
    /// scanner tests for it before tokenizing and skips what follows. It is nonetheless
    /// reserved in practice, since the word can never be read as an identifier.</para>
    /// </summary>
    public const string Comment = "comment";

    /// <summary>The 54 words that scan as keywords rather than identifiers.</summary>
    public static readonly FrozenDictionary<string, TokenType> Keywords =
        new Dictionary<string, TokenType>(StringComparer.Ordinal)
        {
            ["abstract"] = TokenType.Abstract,
            ["and"] = TokenType.And,
            ["as"] = TokenType.As,
            ["base"] = TokenType.Base,
            ["begin"] = TokenType.Begin,
            ["boolean"] = TokenType.Boolean,
            ["break"] = TokenType.Break,
            ["case"] = TokenType.Case,
            ["catch"] = TokenType.Catch,
            ["character"] = TokenType.Character,
            ["constant"] = TokenType.Constant,
            ["continue"] = TokenType.Continue,
            ["default"] = TokenType.Default,
            ["each"] = TokenType.Each,
            ["else"] = TokenType.Else,
            ["end"] = TokenType.End,
            ["enumeration"] = TokenType.Enumeration,
            ["extends"] = TokenType.Extends,
            ["false"] = TokenType.False,
            ["finally"] = TokenType.Finally,
            ["for"] = TokenType.For,
            ["fraction"] = TokenType.Fraction,
            ["function"] = TokenType.Function,
            ["global"] = TokenType.Global,
            ["if"] = TokenType.If,
            ["import"] = TokenType.Import,
            ["in"] = TokenType.In,
            ["integer"] = TokenType.Integer,
            ["is"] = TokenType.Is,
            ["let"] = TokenType.Let,
            ["model"] = TokenType.Model,
            ["namespace"] = TokenType.Namespace,
            ["new"] = TokenType.New,
            ["not"] = TokenType.Not,
            ["or"] = TokenType.Or,
            ["override"] = TokenType.Override,
            ["protected"] = TokenType.Protected,
            ["public"] = TokenType.Public,
            ["real"] = TokenType.Real,
            ["sealed"] = TokenType.Sealed,
            ["step"] = TokenType.Step,
            ["string"] = TokenType.String,
            ["structure"] = TokenType.Structure,
            ["switch"] = TokenType.Switch,
            ["then"] = TokenType.Then,
            ["this"] = TokenType.This,
            ["throw"] = TokenType.Throw,
            ["to"] = TokenType.To,
            ["true"] = TokenType.True,
            ["try"] = TokenType.Try,
            ["until"] = TokenType.Until,
            ["using"] = TokenType.Using,
            ["virtual"] = TokenType.Virtual,
            ["while"] = TokenType.While,
            ["yield"] = TokenType.Yield,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>The number of reserved words, excluding <see cref="Comment"/>.</summary>
    public static int Count => Keywords.Count;

    /// <summary>True if the word cannot be used as an identifier.</summary>
    public static bool IsReserved(string word) =>
        Keywords.ContainsKey(word) || string.Equals(word, Comment, StringComparison.Ordinal);
}
