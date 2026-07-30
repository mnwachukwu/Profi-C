using ProfiC.Compiler.Ast;

namespace ProfiC.Compiler.Semantics;

/// <summary>
/// <para>How far a declaration can be seen, narrowest first.</para>
/// <para>The order is the whole point: one visibility is at least as wide as another exactly
/// when it compares greater or equal, so widening checks are comparisons rather than a table.
/// </para>
/// </summary>
public enum Visibility
{
    /// <summary>The declaring type, and nothing else.</summary>
    Private,

    /// <summary>The declaring type and anything extending it.</summary>
    Protected,

    /// <summary>Every type in the same project.</summary>
    Internal,

    /// <summary>Everywhere.</summary>
    Public,
}

/// <summary>
/// <para>Reading a visibility off the words that were written.</para>
/// <para>Two defaults, and they are the same rule applied twice: a declaration with no word
/// belongs to the smallest thing that could own it. A member's owner is its type, so a member
/// defaults to private. A type's owner is its project, so a type defaults to internal.</para>
/// </summary>
public static class VisibilityExtensions
{
    /// <summary>The words that name a visibility, for reporting one that is written twice.</summary>
    public const DeclarationModifiers Words =
        DeclarationModifiers.Public | DeclarationModifiers.Protected | DeclarationModifiers.Internal;

    /// <summary>What a member's modifiers say, defaulting to the declaring type alone.</summary>
    public static Visibility OfMember(this DeclarationModifiers modifiers) =>
        Read(modifiers, Visibility.Private);

    /// <summary>What a type's modifiers say, defaulting to the project it is declared in.</summary>
    public static Visibility OfType(this DeclarationModifiers modifiers) =>
        Read(modifiers, Visibility.Internal);

    /// <summary>How a visibility is written, for a message that has to name one.</summary>
    public static string Spell(this Visibility visibility) => visibility switch
    {
        Visibility.Private => "private",
        Visibility.Protected => "protected",
        Visibility.Internal => "internal",
        Visibility.Public => "public",
        _ => throw new ArgumentOutOfRangeException(nameof(visibility)),
    };

    /// <summary>
    /// The widest word written, so that a declaration saying two things is read as the one that
    /// grants most. Writing two is reported separately; reading the wider of them keeps the
    /// mistake to one message instead of every use of the member becoming a second.
    /// </summary>
    private static Visibility Read(DeclarationModifiers modifiers, Visibility whenSilent)
    {
        if (modifiers.Has(DeclarationModifiers.Public)) { return Visibility.Public; }
        if (modifiers.Has(DeclarationModifiers.Internal)) { return Visibility.Internal; }
        if (modifiers.Has(DeclarationModifiers.Protected)) { return Visibility.Protected; }

        return whenSilent;
    }
}
