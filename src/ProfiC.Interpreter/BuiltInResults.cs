using ProfiC.Compiler.Semantics;
using ProfiC.Runtime;

namespace ProfiC.Interpreter;

/// <summary>
/// <para>Checks that a built-in gave back the kind of value the catalog said it would.</para>
/// <para>The catalog is what the type checker believed, and every later pass took it at its
/// word. Nothing until now compared it against what the back end actually produced, and the
/// disagreement is invisible from inside a program: a member declared to yield an integer that
/// hands back a real prints the same characters and passes every recorded output. It only goes
/// wrong later, somewhere else, where the value is used as a count or an index. Rounding is
/// the case to picture: an answer meant to index a set is useless as a real.</para>
/// <para>Held apart from the switch that produces the values so that adding a member means
/// adding one arm rather than two, and so that this reads as one rule rather than as a hundred
/// assertions.</para>
/// </summary>
internal static class BuiltInResults
{
    /// <summary>
    /// What is wrong with the value a built-in produced, or null if nothing is. Every member
    /// is looked up in the catalog by identifier, so a member added there and implemented here
    /// is checked without anything being written in a third place.
    /// </summary>
    public static string? Disagrees(BuiltInId id, object? produced)
    {
        if (Expected(id) is not { } declared)
        {
            return null;
        }

        // Nothing at all is what a member yielding nothing produces, and is also how an
        // absent optional travels, so it satisfies any declared type.
        if (produced is null)
        {
            return null;
        }

        return Matches(declared, produced)
            ? null
            : $"'{id}' is declared to yield {declared.Display}, but produced "
              + $"{Describe(produced)}. The catalog and the back end disagree.";
    }

    /// <summary>The type the catalog says this member yields, or null where it yields nothing.</summary>
    private static TypeSymbol? Expected(BuiltInId id) =>
        Declared.TryGetValue(id, out TypeSymbol? type) ? type : null;

    /// <summary>
    /// Every member of every surface the catalog describes, by identifier. Built once, from
    /// the catalog itself, so a member cannot be listed here and nowhere else.
    /// </summary>
    private static readonly Dictionary<BuiltInId, TypeSymbol> Declared = Gather();

    private static Dictionary<BuiltInId, TypeSymbol> Gather()
    {
        // Only the surfaces whose signatures are the same whatever they are asked of.
        //
        // A set's and an optional's members are built from the receiver — Value on an
        // integer? yields an integer and on a character[]? yields a set — so no single type
        // describes one, and checking against any particular one would report the others as
        // wrong. Those are the two the catalog builds per receiver, which is what tells them
        // apart from the rest without a list anyone has to keep.
        IEnumerable<BuiltInMember> fixedSignatures =
        [
            .. BuiltIns.Models.SelectMany(m => m.Members),
            .. BuiltIns.Models.SelectMany(m => m.Constructors),
            .. BuiltIns.OnString(),
            .. BuiltIns.OnFraction(),
            .. BuiltIns.OnReal(),
            .. BuiltIns.OnEnumeration(),
            .. BuiltIns.OnException(),
            .. BuiltIns.OnEveryType(),
        ];

        Dictionary<BuiltInId, TypeSymbol> declared = [];

        foreach (BuiltInMember member in fixedSignatures)
        {
            if (member.Id is { } id && member.ReturnType is { } type)
            {
                declared[id] = type;
            }
        }

        return declared;
    }

    /// <summary>
    /// <para>Whether a value is one of the kind a type describes.</para>
    /// <para>Checked by what the value <em>is</em> rather than by what it could convert to,
    /// since the point is to catch a back end that produced the wrong thing. A model type is
    /// let through where the value is an instance or one of the things the language holds
    /// directly, because which model it is belongs to the type checker and was settled there.
    /// </para>
    /// </summary>
    private static bool Matches(TypeSymbol declared, object produced) => declared switch
    {
        PrimitiveType primitive when ReferenceEquals(primitive, PrimitiveType.Integer) =>
            produced is long,
        PrimitiveType primitive when ReferenceEquals(primitive, PrimitiveType.Real) =>
            produced is double,
        PrimitiveType primitive when ReferenceEquals(primitive, PrimitiveType.Boolean) =>
            produced is bool,
        PrimitiveType primitive when ReferenceEquals(primitive, PrimitiveType.Character) =>
            produced is char,
        PrimitiveType primitive when ReferenceEquals(primitive, PrimitiveType.String) =>
            produced is string,
        PrimitiveType primitive when ReferenceEquals(primitive, PrimitiveType.Fraction) =>
            produced is Fraction,

        SetType => produced is ProfiCSet<object?>,

        // An optional is the value itself or nothing at all, and nothing was let through
        // above, so what arrives here has to be the underlying kind.
        OptionalType inner => Matches(inner.UnderlyingType, produced),

        ModelSymbol { Name: "DateTime" } => produced is DateTime,
        ModelSymbol { Name: "TimeSpan" } => produced is TimeSpan,
        ModelSymbol { Name: "Date" } => produced is DateOnly,
        ModelSymbol { Name: "Time" } => produced is TimeOnly,
        ModelSymbol { Name: "Random" } => produced is ProfiCRandom,

        // Every other model, including Model and Function themselves.
        ModelSymbol => true,

        _ => true,
    };

    private static string Describe(object produced) => produced switch
    {
        long => "an integer",
        double => "a real",
        bool => "a boolean",
        char => "a character",
        string => "text",
        Fraction => "a fraction",
        ProfiCSet<object?> => "a set",
        DateTime => "a moment",
        TimeSpan => "a span",
        DateOnly => "a date",
        TimeOnly => "a time of day",
        ProfiCRandom => "a generator",
        _ => produced.GetType().Name,
    };
}
