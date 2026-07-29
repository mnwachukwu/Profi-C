namespace ProfiC.Compiler.Semantics;

/// <summary>
/// <para>Every member reached through the name of a built-in model, one identifier each.</para>
/// <para>These join the two halves of a built-in — what the type checker knows about it, and
/// what happens when it runs — with something the C# compiler checks. The back end switches on this 
/// enumeration without a fallback arm, so a member declared here and implemented nowhere does not 
/// compile.</para>
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

    // ---- Members of a value, found by the receiver's type ----------------------------------

    SetCount,
    SetInsert,
    SetInsertAt,
    SetRemove,
    SetRemoveAt,
    SetContains,
    SetIndexOf,
    SetClear,

    StringCount,
    StringContains,
    StringIndexOf,
    StringSubstring,
    StringInsert,
    StringInsertAt,
    StringRemove,
    StringRemoveAt,
    StringToCharacters,

    OptionalHasValue,
    OptionalOr,
    OptionalValue,

    FractionToReal,
    RealToFraction,
    EnumerationToInteger,
    ExceptionMessage,

    // Inherited by every type from Model.
    ModelToString,
    ModelEquals,
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
    /// <para>The built-in exceptions, which descend from <c>Exception</c> and may be extended.
    /// Kept beside the models because they share the rule that a program cannot declare
    /// them.</para>
    /// <para>Read from the runtime's catalogue rather than listed again, so that a name the
    /// language can raise is a name a program can catch. <c>Exception</c> itself is the root
    /// and is catalogued above as a model.</para>
    /// </summary>
    public static readonly IReadOnlySet<string> ExceptionNames =
        Runtime.BuiltInExceptions.Names
            .Where(name => !string.Equals(name, "Exception", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

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

    // ---- Members of a value --------------------------------------------------------------
    // Found by the receiver's type rather than by a name. Several depend on the receiver: a
    // set's Insert takes its element type, an optional's Value yields its underlying one, so
    // each list is built for the receiver it is asked about.

    /// <summary>Members every type inherits from <c>Model</c>.</summary>
    public static IReadOnlyList<BuiltInMember> OnEveryType() =>
    [
        Member(BuiltInId.ModelToString, "ToString", PrimitiveType.String),

        // Structural, the same question '==' asks. Takes a value of any type.
        Member(BuiltInId.ModelEquals, "Equals", PrimitiveType.Boolean, [null]),
    ];

    public static IReadOnlyList<BuiltInMember> OnSet(SetType set) =>
    [
        Member(BuiltInId.SetCount, "Count", PrimitiveType.Integer),
        Member(BuiltInId.SetInsert, "Insert", null, set.ElementType),
        Member(BuiltInId.SetInsertAt, "InsertAt", null, PrimitiveType.Integer, set.ElementType),

        // The only mutator that yields anything, matching the list it is built on.
        Member(BuiltInId.SetRemove, "Remove", PrimitiveType.Boolean, set.ElementType),

        Member(BuiltInId.SetRemoveAt, "RemoveAt", null, PrimitiveType.Integer),
        Member(BuiltInId.SetContains, "Contains", PrimitiveType.Boolean, set.ElementType),
        Member(BuiltInId.SetIndexOf, "IndexOf", PrimitiveType.Integer, set.ElementType),
        Member(BuiltInId.SetClear, "Clear", null),
        .. OnEveryType(),
    ];

    /// <summary>
    /// A string's members mirror a set's, so that the two read alike. It reports its length
    /// with <c>Count()</c> rather than a differently named member for the same idea, and every
    /// one of these yields a new string rather than changing the original.
    /// </summary>
    public static IReadOnlyList<BuiltInMember> OnString() =>
    [
        Member(BuiltInId.StringCount, "Count", PrimitiveType.Integer),
        Member(BuiltInId.StringContains, "Contains", PrimitiveType.Boolean, PrimitiveType.String),
        Member(BuiltInId.StringIndexOf, "IndexOf", PrimitiveType.Integer, PrimitiveType.String),
        Member(BuiltInId.StringSubstring, "Substring", PrimitiveType.String,
               PrimitiveType.Integer, PrimitiveType.Integer),
        Member(BuiltInId.StringInsert, "Insert", PrimitiveType.String, PrimitiveType.String),
        Member(BuiltInId.StringInsertAt, "InsertAt", PrimitiveType.String,
               PrimitiveType.Integer, PrimitiveType.String),
        Member(BuiltInId.StringRemove, "Remove", PrimitiveType.String, PrimitiveType.String),
        Member(BuiltInId.StringRemoveAt, "RemoveAt", PrimitiveType.String, PrimitiveType.Integer),
        Member(BuiltInId.StringToCharacters, "ToCharacters", new SetType(PrimitiveType.Character)),
        .. OnEveryType(),
    ];

    /// <summary>
    /// <para>The three members of an optional, and there are only three.</para>
    /// <para><c>Or</c> has two forms: given a plain value it ends the chain with a definite
    /// one, and given another optional it keeps the chain going, which is what makes
    /// <c>a.Or(b).Or(c)</c> work. The second form is added by the caller that knows the
    /// argument.</para>
    /// </summary>
    public static IReadOnlyList<BuiltInMember> OnOptional(OptionalType optional) =>
    [
        Member(BuiltInId.OptionalHasValue, "HasValue", PrimitiveType.Boolean),
        Member(BuiltInId.OptionalOr, "Or", optional.UnderlyingType, optional.UnderlyingType),
        Member(BuiltInId.OptionalValue, "Value", optional.UnderlyingType),
    ];

    /// <summary>The two conversions the language deliberately refuses to make on its own.</summary>
    public static IReadOnlyList<BuiltInMember> OnFraction() =>
    [
        Member(BuiltInId.FractionToReal, "ToReal", PrimitiveType.Real),
        .. OnEveryType(),
    ];

    public static IReadOnlyList<BuiltInMember> OnReal() =>
    [
        Member(BuiltInId.RealToFraction, "ToFraction", PrimitiveType.Fraction),
        .. OnEveryType(),
    ];

    public static IReadOnlyList<BuiltInMember> OnEnumeration() =>
    [
        Member(BuiltInId.EnumerationToInteger, "ToInteger", PrimitiveType.Integer),
        .. OnEveryType(),
    ];

    /// <summary>Carried by every exception, including one a program declares.</summary>
    public static IReadOnlyList<BuiltInMember> OnException() =>
    [
        Member(BuiltInId.ExceptionMessage, "Message", PrimitiveType.String),
        .. OnEveryType(),
    ];
}
