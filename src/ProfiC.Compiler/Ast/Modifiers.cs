using ProfiC.Compiler.Lexing;

namespace ProfiC.Compiler.Ast;

/// <summary>
/// <para>The modifier words that may precede a declaration.</para>
/// <para>Held as flags because the parser accumulates them one word at a time and the
/// grammar allows several in sequence. Which combinations are legal is a semantic question,
/// not a syntactic one: <c>sealed abstract</c> parses and is then rejected, which produces a
/// better message than a parse failure would.</para>
/// </summary>
[Flags]
public enum DeclarationModifiers
{
    None = 0,
    Public = 1 << 0,
    Protected = 1 << 1,
    Internal = 1 << 8,
    Shared = 1 << 2,
    Virtual = 1 << 3,
    Override = 1 << 4,
    Sealed = 1 << 5,
    Abstract = 1 << 6,
    Constant = 1 << 7,
}

/// <summary>Helpers for reading and reporting modifier sets.</summary>
public static class DeclarationModifiersExtensions
{
    public static bool Has(this DeclarationModifiers modifiers, DeclarationModifiers flag) =>
        (modifiers & flag) == flag;

    /// <summary>The modifier a reserved word introduces, or null if it is not a modifier.</summary>
    public static DeclarationModifiers? FromToken(TokenType type) => type switch
    {
        TokenType.Public => DeclarationModifiers.Public,
        TokenType.Protected => DeclarationModifiers.Protected,
        TokenType.Internal => DeclarationModifiers.Internal,
        TokenType.Shared => DeclarationModifiers.Shared,
        TokenType.Virtual => DeclarationModifiers.Virtual,
        TokenType.Override => DeclarationModifiers.Override,
        TokenType.Sealed => DeclarationModifiers.Sealed,
        TokenType.Abstract => DeclarationModifiers.Abstract,
        TokenType.Constant => DeclarationModifiers.Constant,
        _ => null,
    };

    /// <summary>The words present, in the order the grammar writes them. Empty when none.</summary>
    public static string ToDisplayString(this DeclarationModifiers modifiers)
    {
        if (modifiers == DeclarationModifiers.None)
        {
            return string.Empty;
        }

        List<string> words = [];

        if (modifiers.Has(DeclarationModifiers.Public)) { words.Add("public"); }
        if (modifiers.Has(DeclarationModifiers.Protected)) { words.Add("protected"); }
        if (modifiers.Has(DeclarationModifiers.Internal)) { words.Add("internal"); }
        if (modifiers.Has(DeclarationModifiers.Shared)) { words.Add("shared"); }
        if (modifiers.Has(DeclarationModifiers.Constant)) { words.Add("constant"); }
        if (modifiers.Has(DeclarationModifiers.Virtual)) { words.Add("virtual"); }
        if (modifiers.Has(DeclarationModifiers.Override)) { words.Add("override"); }
        if (modifiers.Has(DeclarationModifiers.Sealed)) { words.Add("sealed"); }
        if (modifiers.Has(DeclarationModifiers.Abstract)) { words.Add("abstract"); }

        return string.Join(' ', words);
    }
}
