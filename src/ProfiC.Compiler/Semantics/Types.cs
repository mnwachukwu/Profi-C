namespace ProfiC.Compiler.Semantics;

/// <summary>
/// <para>A type, as the compiler understands it after names have been resolved.</para>
/// <para>Declared types are symbols too — a model both declares something and names a type —
/// so this and <see cref="Symbol"/> share a hierarchy rather than mirroring each other.</para>
/// </summary>
public abstract class TypeSymbol : Symbol
{
    private protected TypeSymbol(string name)
        : base(name)
    {
    }

    /// <summary>True for types held by value: no aliasing, no identity.</summary>
    public abstract bool IsValueType { get; }

    /// <summary>
    /// True when this stands in for a type the compiler could not work out. Operations on
    /// one produce another, and nothing is reported about it, so a single mistake does not
    /// echo through every later phase.
    /// </summary>
    public virtual bool IsError => false;

    /// <summary>How the type is written in source, for diagnostics.</summary>
    public abstract string Display { get; }

    /// <summary>
    /// Types describe themselves as "type" unless they are something more specific. A model
    /// or an enumeration overrides this, since naming the kind is what makes a diagnostic
    /// about it readable.
    /// </summary>
    public override string Kind => "type";

    public override string ToString() => Display;

    /// <summary>
    /// <para>The type with an article in front, as a diagnostic would read it aloud: "an
    /// integer", "a real".</para>
    /// <para>The article follows the first letter of the type's spelling, so a diagnostic
    /// never reads "a integer".</para>
    /// </summary>
    public string WithArticle() => $"{ArticleFor(Display)} {Display}";

    /// <summary>The same, capitalized for the start of a sentence.</summary>
    public string WithArticleCapitalized()
    {
        string article = ArticleFor(Display);
        return $"{char.ToUpperInvariant(article[0])}{article[1..]} {Display}";
    }

    private static string ArticleFor(string display) =>
        display.Length > 0 && "aeiouAEIOU".Contains(display[0], StringComparison.Ordinal)
            ? "an"
            : "a";
}

/// <summary>The built-in types the language names with a keyword.</summary>
public sealed class PrimitiveType : TypeSymbol
{
    private PrimitiveType(string name, bool isValueType)
        : base(name) =>
        IsValueType = isValueType;

    public override bool IsValueType { get; }

    public override string Display => Name;

    public static readonly PrimitiveType Integer = new("integer", isValueType: true);
    public static readonly PrimitiveType Real = new("real", isValueType: true);
    public static readonly PrimitiveType Character = new("character", isValueType: true);
    public static readonly PrimitiveType Boolean = new("boolean", isValueType: true);
    public static readonly PrimitiveType Fraction = new("fraction", isValueType: true);

    /// <summary>
    /// A reference type, and immutable. It is <c>System.String</c> outright.
    /// </summary>
    public static readonly PrimitiveType String = new("string", isValueType: false);

    /// <summary>
    /// <para>The type of an expression that produces no value: the absence of a result, not a
    /// reference to nothing.</para>
    /// <para>No program can write it, and nothing else has this type — it arises only from
    /// calling a function that yields nothing. Its display therefore spells out what it is
    /// rather than naming a type nobody can declare, so that a diagnostic reads "A call that
    /// yields nothing cannot be indexed".</para>
    /// <para>"Call" rather than "function", because a function value is a real thing here —
    /// <c>integer function(integer) f</c> — and a set of them can be indexed. What sits in the
    /// offending position is always the call.</para>
    /// </summary>
    public static readonly PrimitiveType Void = new("call that yields nothing", isValueType: true);

    /// <summary>Every primitive, by the word that names it.</summary>
    public static readonly IReadOnlyDictionary<string, PrimitiveType> ByName =
        new Dictionary<string, PrimitiveType>(StringComparer.Ordinal)
        {
            ["integer"] = Integer,
            ["real"] = Real,
            ["character"] = Character,
            ["boolean"] = Boolean,
            ["fraction"] = Fraction,
            ["string"] = String,
        };
}

/// <summary>
/// <para>Stands in for a type the compiler could not determine.</para>
/// <para>Everything about it is deliberately permissive: it converts to and from anything, and
/// no diagnostic is reported against it. That is what keeps one mistake from producing a
/// second diagnostic in every phase that follows.</para>
/// </summary>
public sealed class ErrorType : TypeSymbol
{
    public static readonly ErrorType Instance = new();

    private ErrorType()
        : base("?")
    {
    }

    public override bool IsValueType => false;

    public override bool IsError => true;

    public override string Display => "?";
}

/// <summary>A set of some element type, written with a <c>[]</c> suffix.</summary>
public sealed class SetType(TypeSymbol elementType) : TypeSymbol("set")
{
    public TypeSymbol ElementType { get; } = elementType;

    /// <summary>Sets are references, so assigning one aliases rather than copying.</summary>
    public override bool IsValueType => false;

    public override bool IsError => ElementType.IsError;

    public override string Display => $"{ElementType.Display}[]";
}

/// <summary>
/// An optional, written with a <c>?</c> suffix. This is what the language has instead of null.
/// </summary>
public sealed class OptionalType(TypeSymbol underlyingType) : TypeSymbol("optional")
{
    public TypeSymbol UnderlyingType { get; } = underlyingType;

    public override bool IsValueType => UnderlyingType.IsValueType;

    public override bool IsError => UnderlyingType.IsError;

    public override string Display => $"{UnderlyingType.Display}?";
}

/// <summary>The type of a function value.</summary>
public sealed class FunctionType(
    TypeSymbol? returnType,
    IReadOnlyList<TypeSymbol> parameterTypes) : TypeSymbol("function")
{
    /// <summary>Null when the function yields nothing.</summary>
    public TypeSymbol? ReturnType { get; } = returnType;

    public IReadOnlyList<TypeSymbol> ParameterTypes { get; } = parameterTypes;

    public override bool IsValueType => false;

    public override bool IsError =>
        (ReturnType?.IsError ?? false) || ParameterTypes.Any(p => p.IsError);

    public override string Display
    {
        get
        {
            string parameters = string.Join(", ", ParameterTypes.Select(p => p.Display));
            string prefix = ReturnType is null ? string.Empty : ReturnType.Display + " ";
            return $"{prefix}function({parameters})";
        }
    }
}
