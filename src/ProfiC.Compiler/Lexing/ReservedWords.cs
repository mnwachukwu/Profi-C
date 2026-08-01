using System.Collections.Frozen;

namespace ProfiC.Compiler.Lexing;

/// <summary>
/// <para>Profi-C's reserved words.</para>
/// <para>Held here rather than inside the scanner so that tests, tooling, and any future
/// editor-grammar generator can all read one list.</para>
/// </summary>
public static class ReservedWords
{
    /// <summary>The 56 words that scan as keywords rather than identifiers.</summary>
    public static readonly FrozenDictionary<string, TokenType> Keywords =
        new Dictionary<string, TokenType>(StringComparer.Ordinal)
        {
            ["abstract"] = TokenType.Abstract,
            ["and"] = TokenType.And,
            ["as"] = TokenType.As,
            ["base"] = TokenType.Base,
            ["begin"] = TokenType.Begin,
            ["bitwise"] = TokenType.Bitwise,
            ["boolean"] = TokenType.Boolean,
            ["break"] = TokenType.Break,
            ["case"] = TokenType.Case,
            ["catch"] = TokenType.Catch,
            ["character"] = TokenType.Character,
            ["constant"] = TokenType.Constant,
            ["continue"] = TokenType.Continue,
            ["default"] = TokenType.Default,
            ["delegate"] = TokenType.Delegate,
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
            ["if"] = TokenType.If,
            ["import"] = TokenType.Import,
            ["in"] = TokenType.In,
            ["integer"] = TokenType.Integer,
            ["internal"] = TokenType.Internal,
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
            ["shared"] = TokenType.Shared,
            ["shiftleft"] = TokenType.ShiftLeft,
            ["shiftright"] = TokenType.ShiftRight,
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
            ["xor"] = TokenType.Xor,
            ["yield"] = TokenType.Yield,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>The number of reserved words.</summary>
    public static int Count => Keywords.Count;

    /// <summary>
    /// <para>True if the word cannot be used as an identifier.</para>
    /// <para>Every reserved word is in the table above, and none is reserved outside it. A
    /// comment is marked rather than named, so it takes no word away from a program.</para>
    /// </summary>
    public static bool IsReserved(string word) => Keywords.ContainsKey(word);
}
