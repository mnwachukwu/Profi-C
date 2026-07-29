namespace ProfiC.Compiler.Semantics;

/// <summary>
/// <para>Every member reached through the name of a built-in model, one identifier each.</para>
/// <para>These join the two halves of a built-in — what the type checker knows about it, and
/// what happens when it runs — with something the C# compiler checks rather than with a name
/// written out twice. The back end switches on this enumeration without a fallback arm, so a
/// member declared here and implemented nowhere does not compile.</para>
/// </summary>
public enum BuiltInId
{
    ConsoleWrite,
    ConsoleWriteLine,
    ConsoleRead,

    ReferenceEquals,

    MathSqrt,
    MathAbs,
    MathFloor,
    MathCeiling,
    MathPow,
    MathMin,
    MathMax,

    FractionCreate,
}

/// <summary>A built-in model, and everything the language knows about it.</summary>
/// <param name="Name">The name a program writes.</param>
/// <param name="Namespace">The namespace the model belongs to.</param>
/// <param name="MayBeExtended">Whether a program may write <c>extends</c> against it.</param>
/// <param name="Members">Members reached through the model's name.</param>
public sealed record BuiltInModelInfo(
    string Name,
    string Namespace,
    bool MayBeExtended,
    IReadOnlyList<BuiltInMember> Members);

/// <summary>
/// <para>The catalogue of models the language provides.</para>
/// <para>One place to read to learn what exists, and one place to edit to add something. The
/// resolver takes the names it protects from here, and the type checker takes the signatures,
/// so neither can disagree with this or with the other.</para>
/// </summary>
public static class BuiltIns
{
    private static BuiltInMember Member(
        BuiltInId id, string name, TypeSymbol? returns, params TypeSymbol?[] parameters) =>
        new(name, returns, parameters, id);

    /// <summary>
    /// <para>Models a program may name but never declare.</para>
    /// <para><c>Model</c> and <c>Exception</c> carry no members of their own here: what they
    /// contribute is inherited by every type and by every exception respectively, and is
    /// answered on the value rather than through the model's name.</para>
    /// </summary>
    public static readonly IReadOnlyList<BuiltInModelInfo> Models =
    [
        new("Model", "Standard", MayBeExtended: true, []),

        new("Exception", "Standard", MayBeExtended: true, []),

        new("Console", "Standard", MayBeExtended: false,
        [
            // Both take a value of any type — a null parameter type means "anything" — so no
            // overload per primitive is needed in a version with no generics.
            Member(BuiltInId.ConsoleWrite, "Write", null, [null]),
            Member(BuiltInId.ConsoleWriteLine, "WriteLine", null, [null]),
            Member(BuiltInId.ConsoleRead, "Read", new OptionalType(PrimitiveType.String)),
        ]),

        new("Reference", "Standard", MayBeExtended: false,
        [
            Member(BuiltInId.ReferenceEquals, "Equals", PrimitiveType.Boolean, [null, null]),
        ]),

        new("Math", "Standard", MayBeExtended: false,
        [
            Member(BuiltInId.MathSqrt, "Sqrt", PrimitiveType.Real, PrimitiveType.Real),
            Member(BuiltInId.MathAbs, "Abs", PrimitiveType.Real, PrimitiveType.Real),
            Member(BuiltInId.MathFloor, "Floor", PrimitiveType.Real, PrimitiveType.Real),
            Member(BuiltInId.MathCeiling, "Ceiling", PrimitiveType.Real, PrimitiveType.Real),
            Member(BuiltInId.MathPow, "Pow", PrimitiveType.Real,
                   PrimitiveType.Real, PrimitiveType.Real),

            // Min and Max count rather than measure, so they work on integers.
            Member(BuiltInId.MathMin, "Min", PrimitiveType.Integer,
                   PrimitiveType.Integer, PrimitiveType.Integer),
            Member(BuiltInId.MathMax, "Max", PrimitiveType.Integer,
                   PrimitiveType.Integer, PrimitiveType.Integer),
        ]),

        // "fraction" is the type and a reserved word; "Fraction" is the model beside it,
        // holding what a fraction needs that is not a member of one.
        new("Fraction", "Standard", MayBeExtended: false,
        [
            Member(BuiltInId.FractionCreate, "Create", PrimitiveType.Fraction,
                   PrimitiveType.Integer, PrimitiveType.Integer),
        ]),

        // Named, so a program may not declare them, and carrying no members.
        new("Random", "Standard", MayBeExtended: false, []),
        new("DateTime", "Standard", MayBeExtended: false, []),
    ];

    /// <summary>Every built-in model name. No program may declare one of these.</summary>
    public static readonly IReadOnlySet<string> ModelNames =
        Models.Select(m => m.Name).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The built-in exceptions, which descend from <c>Exception</c> and may be extended.
    /// Kept beside the models because they share the rule that a program cannot declare them.
    /// </summary>
    public static readonly IReadOnlySet<string> ExceptionNames =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "DivideByZeroException",
            "IndexOutOfRangeException",
            "EmptyOptionalException",
            "InvalidCastException",
            "FormatException",
            "ArgumentException",
        };

    /// <summary>Every name the language owns, whether a model or an exception.</summary>
    public static readonly IReadOnlySet<string> AllTypeNames =
        ModelNames.Concat(ExceptionNames).ToHashSet(StringComparer.Ordinal);

    /// <summary>Whether a name belongs to the language rather than to a program.</summary>
    public static bool IsBuiltInType(string name) => AllTypeNames.Contains(name);

    /// <summary>Whether <c>extends</c> may name this type.</summary>
    public static bool MayBeExtended(string name) =>
        ExceptionNames.Contains(name)
        || Models.FirstOrDefault(m => m.Name == name)?.MayBeExtended == true;

    /// <summary>The model of that name, or null if the language does not own it.</summary>
    public static BuiltInModelInfo? FindModel(string name) =>
        Models.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.Ordinal));

    /// <summary>A member reached through a model's name, or null if there is none.</summary>
    public static BuiltInMember? Find(string modelName, string memberName) =>
        FindModel(modelName)?.Members
            .FirstOrDefault(m => string.Equals(m.Name, memberName, StringComparison.Ordinal));
}
