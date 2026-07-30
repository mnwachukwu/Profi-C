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

    MathPi,
    MathE,

    MathSqrt,
    MathCbrt,
    MathRoot,
    MathPow,
    MathFactorial,

    // .NET's names, and .NET's meanings with them: Log of one number is the natural
    // logarithm. Spelling it any other way here would give a reader who moves to C# the same
    // program and a different answer, which is the one outcome worth more than a nicer name.
    MathLog,
    MathLogInBase,
    MathLog10,
    MathLog2,

    MathSin,
    MathCos,
    MathTan,
    MathAsin,
    MathAcos,
    MathAtan,
    MathAtan2,

    // One version per number the language has, since a fraction is a number like any other
    // and an answer that arrives as a real cannot be counted with. The name is shared; which
    // version runs is settled by the argument, and the identifier says which was chosen.
    MathAbsInteger,
    MathAbsReal,
    MathAbsFraction,

    // Rounding lands on a whole number, which is the point of asking. Each is the honest
    // spelling of a conversion to integer, and naming three of them is what saves the
    // language from a "ToInteger" that has to pick one silently.
    MathFloorReal,
    MathFloorFraction,
    MathCeilingReal,
    MathCeilingFraction,
    MathRoundReal,
    MathRoundFraction,

    MathMinInteger,
    MathMinReal,
    MathMinFraction,
    MathMaxInteger,
    MathMaxReal,
    MathMaxFraction,

    FractionCreate,
    FractionCreateWhole,

    // ---- Members of a value, found by the receiver's type ----------------------------------

    SetCount,
    SetInsert,
    SetInsertAt,
    SetRemove,
    SetRemoveAt,
    SetContains,
    SetIndexOf,
    SetClear,
    SetSubsetFrom,
    SetSubsetBetween,

    // Only on a set of optionals, since only there is there anything empty to drop.
    SetTrim,
    SetTrimStart,
    SetTrimEnd,
    SetTrimAll,

    StringCount,
    StringContains,
    StringIndexOf,
    StringSubstring,
    StringInsert,
    StringInsertAt,
    StringRemove,
    StringRemoveAt,
    StringToCharacters,
    StringSubsetFrom,
    StringSubsetBetween,

    // Three forms each: whitespace, the characters of a string, or the characters of a set.
    StringTrim,
    StringTrimText,
    StringTrimSet,
    StringTrimStart,
    StringTrimStartText,
    StringTrimStartSet,
    StringTrimEnd,
    StringTrimEndText,
    StringTrimEndSet,

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
/// <para>The catalog of models the language provides.</para>
/// <para>One place to read to learn what exists, and one place to edit to add something. The
/// resolver takes the names it protects from here, and the type checker takes the signatures,
/// so neither can disagree with this or with the other.</para>
/// </summary>
public static class BuiltIns
{
    private static BuiltInMember Member(
        BuiltInId id, string name, TypeSymbol? returns, params TypeSymbol?[] parameters) =>
        new(name, returns, parameters, id);

    /// <summary>A member that is a value rather than something to call, such as Math.Pi.</summary>
    private static BuiltInMember Value(BuiltInId id, string name, TypeSymbol type) =>
        new(name, type, [], id, IsValue: true);

    /// <summary>
    /// <para>Models a program may name but never declare.</para>
    /// <para><c>Model</c> and <c>Exception</c> carry no members of their own here: what they
    /// contribute is inherited by every type and by every exception respectively, and is
    /// answered on the value rather than through the model's name.</para>
    /// </summary>
    public static readonly IReadOnlyList<BuiltInModelInfo> Models =
    [
        new("Model", "Standard", MayBeExtended: true, []),

        // Every function type descends from this one, so a function may be held without its
        // signature being named. It may not be extended: a program adding a child to it would
        // be declaring something that is a function without being any particular function.
        new("Function", "Standard", MayBeExtended: false, []),

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

        // Every version taking a number is written for each number the language has. A
        // fraction is a number like any other, and an answer that arrives as a real cannot be
        // counted with, so one version per type is what keeps either from being a dead end.
        //
        // The exact-match rule picks among them: an integer widens to both real and fraction,
        // so without it the order these are written in would decide what "Abs(-3)" means.
        new("Math", "Standard", MayBeExtended: false,
        [
            // Written without parentheses, since neither is something to do.
            Value(BuiltInId.MathPi, "Pi", PrimitiveType.Real),
            Value(BuiltInId.MathE, "E", PrimitiveType.Real),

            // Roots and powers are the ones that genuinely leave the rationals: the square
            // root of a fraction is usually irrational, so all of these answer in reals
            // whatever they were given. The "^" operator is the exact form, and keeps a
            // fraction base exact where the exponent is whole.
            Member(BuiltInId.MathSqrt, "Sqrt", PrimitiveType.Real, PrimitiveType.Real),
            Member(BuiltInId.MathCbrt, "Cbrt", PrimitiveType.Real, PrimitiveType.Real),
            Member(BuiltInId.MathRoot, "Root", PrimitiveType.Real,
                   PrimitiveType.Real, PrimitiveType.Real),
            Member(BuiltInId.MathPow, "Pow", PrimitiveType.Real,
                   PrimitiveType.Real, PrimitiveType.Real),

            // Counting arrangements, so it counts: whole in, whole out. Past 20 the answer
            // outgrows an integer and it throws, as any other overflow does.
            Member(BuiltInId.MathFactorial, "Factorial", PrimitiveType.Integer,
                   PrimitiveType.Integer),

            // Log of one number is the NATURAL logarithm, which is what .NET, Java and C mean
            // by the name. Log10 and Log2 are the other two .NET spells out.
            Member(BuiltInId.MathLog, "Log", PrimitiveType.Real, PrimitiveType.Real),
            Member(BuiltInId.MathLogInBase, "Log", PrimitiveType.Real,
                   PrimitiveType.Real, PrimitiveType.Real),
            Member(BuiltInId.MathLog10, "Log10", PrimitiveType.Real, PrimitiveType.Real),
            Member(BuiltInId.MathLog2, "Log2", PrimitiveType.Real, PrimitiveType.Real),

            // Angles are in radians, as everywhere else that has these. Abbreviated because
            // they are borrowed rather than invented here, and every one of these names is
            // what the mathematics is written as on paper.
            Member(BuiltInId.MathSin, "Sin", PrimitiveType.Real, PrimitiveType.Real),
            Member(BuiltInId.MathCos, "Cos", PrimitiveType.Real, PrimitiveType.Real),
            Member(BuiltInId.MathTan, "Tan", PrimitiveType.Real, PrimitiveType.Real),
            Member(BuiltInId.MathAsin, "Asin", PrimitiveType.Real, PrimitiveType.Real),
            Member(BuiltInId.MathAcos, "Acos", PrimitiveType.Real, PrimitiveType.Real),
            Member(BuiltInId.MathAtan, "Atan", PrimitiveType.Real, PrimitiveType.Real),
            Member(BuiltInId.MathAtan2, "Atan2", PrimitiveType.Real,
                   PrimitiveType.Real, PrimitiveType.Real),

            // Measuring keeps the type it measured, so a distance between integers is one.
            Member(BuiltInId.MathAbsInteger, "Abs", PrimitiveType.Integer, PrimitiveType.Integer),
            Member(BuiltInId.MathAbsReal, "Abs", PrimitiveType.Real, PrimitiveType.Real),
            Member(BuiltInId.MathAbsFraction, "Abs", PrimitiveType.Fraction, PrimitiveType.Fraction),

            // Rounding lands on a whole number. Returning a real would leave the answer
            // unusable as a count, an index, or a bound — which is most of what it is for.
            Member(BuiltInId.MathFloorReal, "Floor", PrimitiveType.Integer, PrimitiveType.Real),
            Member(BuiltInId.MathFloorFraction, "Floor", PrimitiveType.Integer, PrimitiveType.Fraction),
            Member(BuiltInId.MathCeilingReal, "Ceiling", PrimitiveType.Integer, PrimitiveType.Real),
            Member(BuiltInId.MathCeilingFraction, "Ceiling", PrimitiveType.Integer, PrimitiveType.Fraction),
            Member(BuiltInId.MathRoundReal, "Round", PrimitiveType.Integer, PrimitiveType.Real),
            Member(BuiltInId.MathRoundFraction, "Round", PrimitiveType.Integer, PrimitiveType.Fraction),

            // Choosing between two values gives back one of them, so the answer is whatever
            // they both were.
            Member(BuiltInId.MathMinInteger, "Min", PrimitiveType.Integer,
                   PrimitiveType.Integer, PrimitiveType.Integer),
            Member(BuiltInId.MathMinReal, "Min", PrimitiveType.Real,
                   PrimitiveType.Real, PrimitiveType.Real),
            Member(BuiltInId.MathMinFraction, "Min", PrimitiveType.Fraction,
                   PrimitiveType.Fraction, PrimitiveType.Fraction),
            Member(BuiltInId.MathMaxInteger, "Max", PrimitiveType.Integer,
                   PrimitiveType.Integer, PrimitiveType.Integer),
            Member(BuiltInId.MathMaxReal, "Max", PrimitiveType.Real,
                   PrimitiveType.Real, PrimitiveType.Real),
            Member(BuiltInId.MathMaxFraction, "Max", PrimitiveType.Fraction,
                   PrimitiveType.Fraction, PrimitiveType.Fraction),
        ]),

        // "fraction" is the type and a reserved word; "Fraction" is the model beside it,
        // holding what a fraction needs that is not a member of one.
        new("Fraction", "Standard", MayBeExtended: false,
        [
            Member(BuiltInId.FractionCreate, "Create", PrimitiveType.Fraction,
                   PrimitiveType.Integer, PrimitiveType.Integer),

            // A whole number over one. An integer already widens to a fraction wherever one is
            // wanted, so this earns its place only where nothing says a fraction is wanted:
            // "let f = 3;" holds an integer, and "let f = Fraction.Create(3);" holds 3|1.
            Member(BuiltInId.FractionCreateWhole, "Create", PrimitiveType.Fraction,
                   PrimitiveType.Integer),
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
    /// <para>Read from the runtime's catalog rather than listed again, so that a name the
    /// language can raise is a name a program can catch. <c>Exception</c> itself is the root
    /// and is cataloged above as a model.</para>
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

        // A run of the set, copied out. The end is exclusive, so Subset(1, 3) is two
        // elements — the same reading "until" has, and the one that makes
        // Subset(0, n) + Subset(n, count) put the whole set back together.
        Member(BuiltInId.SetSubsetFrom, "Subset", set, PrimitiveType.Integer),
        Member(BuiltInId.SetSubsetBetween, "Subset", set,
               PrimitiveType.Integer, PrimitiveType.Integer),

        .. TrimmingEmpties(set),
        .. OnEveryType(),
    ];

    /// <summary>
    /// <para>The four ways to drop the empties out of a set of optionals, and nothing at all
    /// for a set of anything else.</para>
    /// <para><c>TrimAll</c> is the one that changes the type. Removing every empty leaves a
    /// set where nothing can be absent, so it yields the underlying type and the caller stops
    /// having to unwrap. The other three take from the ends only, so an empty in the middle
    /// survives and the type has to keep saying so.</para>
    /// </summary>
    private static IReadOnlyList<BuiltInMember> TrimmingEmpties(SetType set) =>
        set.ElementType is OptionalType present
            ?
            [
                Member(BuiltInId.SetTrim, "Trim", set),
                Member(BuiltInId.SetTrimStart, "TrimStart", set),
                Member(BuiltInId.SetTrimEnd, "TrimEnd", set),
                Member(BuiltInId.SetTrimAll, "TrimAll", new SetType(present.UnderlyingType)),
            ]
            : [];

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

        // A string answers Subset as a set does, since it is one when read that way. The two
        // differ in their second argument rather than in what they do: Substring takes how
        // many, Subset takes where to stop. Whichever number you have is the one to write.
        //
        // Both give back a string, because a run of a string is a string — the same rule
        // Subset follows on a set, where a run of one is a set.
        Member(BuiltInId.StringSubsetFrom, "Subset", PrimitiveType.String, PrimitiveType.Integer),
        Member(BuiltInId.StringSubsetBetween, "Subset", PrimitiveType.String,
               PrimitiveType.Integer, PrimitiveType.Integer),
        Member(BuiltInId.StringInsert, "Insert", PrimitiveType.String, PrimitiveType.String),
        Member(BuiltInId.StringInsertAt, "InsertAt", PrimitiveType.String,
               PrimitiveType.Integer, PrimitiveType.String),
        Member(BuiltInId.StringRemove, "Remove", PrimitiveType.String, PrimitiveType.String),
        Member(BuiltInId.StringRemoveAt, "RemoveAt", PrimitiveType.String, PrimitiveType.Integer),
        Member(BuiltInId.StringToCharacters, "ToCharacters", new SetType(PrimitiveType.Character)),

        // Three forms each. Written with nothing, whitespace goes; written with a string,
        // any of its characters go; written with a set, any in the set goes. The middle form
        // is the one people reach for, and the set form is there because a set of characters
        // is what you already have when the characters were worked out rather than typed.
        Member(BuiltInId.StringTrim, "Trim", PrimitiveType.String),
        Member(BuiltInId.StringTrimText, "Trim", PrimitiveType.String, PrimitiveType.String),
        Member(BuiltInId.StringTrimSet, "Trim", PrimitiveType.String,
               new SetType(PrimitiveType.Character)),

        Member(BuiltInId.StringTrimStart, "TrimStart", PrimitiveType.String),
        Member(BuiltInId.StringTrimStartText, "TrimStart", PrimitiveType.String, PrimitiveType.String),
        Member(BuiltInId.StringTrimStartSet, "TrimStart", PrimitiveType.String,
               new SetType(PrimitiveType.Character)),

        Member(BuiltInId.StringTrimEnd, "TrimEnd", PrimitiveType.String),
        Member(BuiltInId.StringTrimEndText, "TrimEnd", PrimitiveType.String, PrimitiveType.String),
        Member(BuiltInId.StringTrimEndSet, "TrimEnd", PrimitiveType.String,
               new SetType(PrimitiveType.Character)),

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
