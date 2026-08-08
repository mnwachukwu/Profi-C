namespace ProfiC.Compiler.Semantics;

/// <summary>What a built-in member call yields, and what it accepts.</summary>
/// <param name="Name">The member's name.</param>
/// <param name="ReturnType">The type it yields, or null if it yields nothing.</param>
/// <param name="ParameterTypes">
/// The types it takes. A null entry accepts any type, which is how <c>Console.Write</c> takes
/// a value of any kind and how an optional's fallback matches its own underlying type.
/// </param>
/// <param name="Id">
/// Set for a member reached through a built-in model's name, which is what the back end
/// switches on. Null for a member of a value — a set's <c>Count</c>, an optional's
/// <c>HasValue</c> — which are found by the receiver's type rather than by a name and are not
/// yet part of the catalog.
/// </param>
/// <param name="IsValue">
/// True for a member that is a value rather than something to call — <c>Math.Pi</c>. Written
/// without parentheses, and writing them is reported, which is the mirror of the diagnostic
/// that reports a function named without them.
/// </param>
/// <param name="Reach">
/// <para>What may be written to the left of the dot: the type's own name, a value of it, or
/// either.</para>
/// <para>Only asked of a model that has instances, since one that has none has nothing but
/// its name to reach a member through and every member of it is reached that way by
/// construction. Without the distinction the two questions share one list, and a type's name
/// answers about a value that was never made — which the interpreter reads as a default and
/// the emitter cannot load at all.</para>
/// </param>
public sealed record BuiltInMember(
    string Name,
    TypeSymbol? ReturnType,
    IReadOnlyList<TypeSymbol?> ParameterTypes,
    BuiltInId? Id = null,
    bool IsValue = false,
    Reached Reach = Reached.ThroughAValue)
{
    /// <summary>Whether the type's own name may be written to the left of the dot.</summary>
    public bool IsShared => Reach is Reached.ThroughTheName or Reached.EitherWay;

    /// <summary>Whether a value of the type may be.</summary>
    public bool IsOnValues => Reach is Reached.ThroughAValue or Reached.EitherWay;
}

/// <summary>How a built-in member is reached.</summary>
public enum Reached
{
    /// <summary>Through a value: <c>moment.Year</c>, <c>span.Hours</c>, <c>word.ToUpper()</c>.</summary>
    ThroughAValue,

    /// <summary>Through the type's name: <c>DateTime.Now</c>, <c>TimeSpan.FromHours</c>.</summary>
    ThroughTheName,

    /// <summary>
    /// Both. <c>Random</c>'s <c>Next</c> is the case: through the name it asks the generator
    /// the language keeps, and through a value it asks one somebody made with <c>new</c>.
    /// </summary>
    EitherWay,
}

/// <summary>
/// <para>The members the language provides on types it owns.</para>
/// <para>These are compiler-known rather than declared anywhere, the same way an optional's
/// three members are. That is what lets a set of any element type answer <c>Count()</c>, and
/// what lets <c>Console.Write</c> accept a value of any type at all, in a version with no
/// generics to express either.</para>
/// </summary>
public static class BuiltInMembers
{
    /// <summary>
    /// <para>Finds the versions of a member on a receiver's type, empty when there are
    /// none.</para>
    /// <para>A list rather than a single result because a few of these genuinely have more
    /// than one form: <c>WriteLine</c> with and without a value, and an optional's <c>Or</c>
    /// taking either a plain value or another optional.</para>
    /// </summary>
    public static IReadOnlyList<BuiltInMember> FindAll(TypeSymbol receiver, string name)
    {
        IReadOnlyList<BuiltInMember> candidates = MembersOf(receiver);
        List<BuiltInMember> matches =
            [.. candidates.Where(m => string.Equals(m.Name, name, StringComparison.Ordinal))];

        // The two members with a second form. Console.WriteLine also takes nothing at all, and
        // an optional's Or takes either a plain value or another optional: given another the
        // chain stays optional, given a value it ends with a definite one, which is what makes
        // a.Or(b).Or(c) work.
        if (receiver is ModelSymbol { Name: "Console" } && name == "WriteLine")
        {
            matches.Add(new BuiltInMember(name, null, [], BuiltInId.ConsoleWriteLine));
        }

        if (receiver is OptionalType optional && name == "Or")
        {
            matches.Add(new BuiltInMember(name, optional, [optional], BuiltInId.OptionalOr));
        }

        return matches;
    }

    /// <summary>
    /// <para>The same, restricted to what a <em>value</em> of the type answers.</para>
    /// <para>What the name reaches and what a value reaches are different questions, and the
    /// catalog holds both: a moment answers <c>Year</c>, while <c>Now</c> is a moment the type
    /// produces and belongs to the type. The path that starts at a value asks this one; the
    /// path that starts at a type name asks <see cref="FindAll"/> whole, so that a member
    /// belonging to each value is found and refused by name rather than reported missing.</para>
    /// </summary>
    public static IReadOnlyList<BuiltInMember> FindAllOnValues(TypeSymbol receiver, string name) =>
        [.. FindAll(receiver, name).Where(m => m.IsOnValues)];

    /// <summary>
    /// <para>Everything the language provides on a receiver of this type.</para>
    /// <para>Given whole rather than only by name, because something offering a reader what they
    /// could write next needs the list rather than an answer about one entry. Read from the same
    /// catalog <see cref="FindAll"/> reads, so what is offered is what will resolve.</para>
    /// </summary>
    public static IReadOnlyList<BuiltInMember> On(TypeSymbol receiver)
    {
        ArgumentNullException.ThrowIfNull(receiver);

        return MembersOf(receiver);
    }

    /// <summary>
    /// Everything the language provides on a receiver of this type, read from the catalog.
    /// See <see cref="BuiltIns"/>.
    /// </summary>
    private static IReadOnlyList<BuiltInMember> MembersOf(TypeSymbol receiver) => receiver switch
    {
        SetType set => BuiltIns.OnSet(set),
        OptionalType optional => BuiltIns.OnOptional(optional),
        EnumerationSymbol => BuiltIns.OnEnumeration(),
        PrimitiveType primitive => OnPrimitive(primitive),
        ModelSymbol model => OnModel(model),
        _ => BuiltIns.OnEveryType(),
    };

    private static IReadOnlyList<BuiltInMember> OnPrimitive(PrimitiveType type)
    {
        if (ReferenceEquals(type, PrimitiveType.String))
        {
            return BuiltIns.OnString();
        }

        if (ReferenceEquals(type, PrimitiveType.Fraction))
        {
            return BuiltIns.OnFraction();
        }

        if (ReferenceEquals(type, PrimitiveType.Real))
        {
            return BuiltIns.OnReal();
        }

        if (ReferenceEquals(type, PrimitiveType.Float))
        {
            return BuiltIns.OnFloat();
        }

        if (ReferenceEquals(type, PrimitiveType.Integer))
        {
            return BuiltIns.OnInteger();
        }

        return BuiltIns.OnEveryType();
    }

    private static IReadOnlyList<BuiltInMember> OnModel(ModelSymbol model)
    {
        List<BuiltInMember> members = [];

        if (BuiltIns.FindModel(model.Name) is { } builtIn)
        {
            members.AddRange(builtIn.Members);
        }

        // Exception's own contribution is answered on the value rather than through its name,
        // so it is added here for the built-in Exception, for the subtypes the language
        // raises, and for a model a program declared by extending it.
        members.AddRange(IsException(model) ? BuiltIns.OnException() : BuiltIns.OnEveryType());
        return members;
    }

    /// <summary>The single version of a member, where a caller needs only its type.</summary>
    /// <summary>
    /// <para>Whether a type puts its own values in order, which is what lets <c>&lt;</c>,
    /// <c>&gt;</c>, <c>&lt;=</c> and <c>&gt;=</c> be written on it.</para>
    /// <para>Read out of the catalog rather than listed here. A type is ordered when it answers
    /// <c>CompareTo</c> against its own type, and that is the very member a comparison is
    /// lowered into — so the question the checker asks and the member the program ends up
    /// calling cannot come apart. Naming the types that qualify would let a later one arrive
    /// with a <c>CompareTo</c> and no operators, or the reverse.</para>
    /// <para>Ordering belongs to these rather than to every model. Two models in general have
    /// no order to be in, and deriving one from their fields would make <c>&lt;</c> mean
    /// whatever they happened to be declared in.</para>
    /// </summary>
    public static bool IsOrdered(TypeSymbol type) =>
        type is ModelSymbol
        && FindAll(type, "CompareTo").Any(
            member => ReferenceEquals(member.ReturnType, PrimitiveType.Integer)
                      && member.ParameterTypes is [{ } only]
                      && Conversions.SameType(only, type));

    public static BuiltInMember? Find(TypeSymbol receiver, string name) =>
        FindAll(receiver, name).FirstOrDefault();

    /// <summary>Whether a type is, or descends from, the built-in <c>Exception</c>.</summary>
    public static bool IsException(TypeSymbol type) =>
        type is ModelSymbol model && model.SelfAndAncestors().Any(m => m.Name == "Exception");
}
