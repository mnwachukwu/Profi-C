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
public sealed record BuiltInMember(
    string Name,
    TypeSymbol? ReturnType,
    IReadOnlyList<TypeSymbol?> ParameterTypes,
    BuiltInId? Id = null,
    bool IsValue = false);

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
    public static BuiltInMember? Find(TypeSymbol receiver, string name) =>
        FindAll(receiver, name).FirstOrDefault();

    /// <summary>
    /// Members every type inherits from <c>Model</c>. Value types included: calling
    /// <c>ToString</c> on one compiles to a direct call and allocates nothing.
    /// </summary>
    private static BuiltInMember? FindOnEveryType(string name) => name switch
    {
        "ToString" => new BuiltInMember("ToString", PrimitiveType.String, []),
        "Equals" => new BuiltInMember("Equals", PrimitiveType.Boolean, [null]),
        _ => null,
    };

    private static BuiltInMember? FindOnSet(SetType set, string name) => name switch
    {
        "Insert" => new BuiltInMember(name, null, [set.ElementType]),
        "InsertAt" => new BuiltInMember(name, null, [PrimitiveType.Integer, set.ElementType]),

        // The only mutator that yields anything, matching the list it is built on.
        "Remove" => new BuiltInMember(name, PrimitiveType.Boolean, [set.ElementType]),

        "RemoveAt" => new BuiltInMember(name, null, [PrimitiveType.Integer]),
        "Count" => new BuiltInMember(name, PrimitiveType.Integer, []),
        "Contains" => new BuiltInMember(name, PrimitiveType.Boolean, [set.ElementType]),
        "IndexOf" => new BuiltInMember(name, PrimitiveType.Integer, [set.ElementType]),
        "Clear" => new BuiltInMember(name, null, []),
        _ => FindOnEveryType(name),
    };

    /// <summary>
    /// A string's members mirror a set's, so that the two read alike. It reports its length
    /// with <c>Count()</c> rather than a differently named member for the same idea.
    /// </summary>
    private static BuiltInMember? FindOnString(string name)
    {
        SetType characters = new(PrimitiveType.Character);

        return name switch
        {
            "Count" => new BuiltInMember(name, PrimitiveType.Integer, []),
            "Contains" => new BuiltInMember(name, PrimitiveType.Boolean, [PrimitiveType.String]),
            "IndexOf" => new BuiltInMember(name, PrimitiveType.Integer, [PrimitiveType.String]),

            // Every one of these yields a new string; none changes the original.
            "Insert" => new BuiltInMember(name, PrimitiveType.String, [PrimitiveType.String]),
            "InsertAt" => new BuiltInMember(
                name, PrimitiveType.String, [PrimitiveType.Integer, PrimitiveType.String]),
            "Remove" => new BuiltInMember(name, PrimitiveType.String, [PrimitiveType.String]),
            "RemoveAt" => new BuiltInMember(name, PrimitiveType.String, [PrimitiveType.Integer]),
            "Substring" => new BuiltInMember(
                name, PrimitiveType.String, [PrimitiveType.Integer, PrimitiveType.Integer]),
            "ToCharacters" => new BuiltInMember(name, characters, []),
            _ => FindOnEveryType(name),
        };
    }

    /// <summary>
    /// <para>The three members of an optional, and there are only three.</para>
    /// <para><c>Or</c> appears twice with different results: given a plain value it ends the
    /// chain with a definite one, and given another optional it keeps the chain going. That is
    /// what makes <c>a.Or(b).Or(c).Or(d)</c> work.</para>
    /// </summary>
    private static BuiltInMember? FindOnOptional(OptionalType optional, string name) => name switch
    {
        "HasValue" => new BuiltInMember(name, PrimitiveType.Boolean, []),

        // The argument type is decided by what is passed, which the checker settles.
        "Or" => new BuiltInMember(name, optional.UnderlyingType, [null]),

        "Value" => new BuiltInMember(name, optional.UnderlyingType, []),
        _ => null,
    };

    private static BuiltInMember? FindOnPrimitive(PrimitiveType type, string name)
    {
        if (ReferenceEquals(type, PrimitiveType.String))
        {
            return FindOnString(name);
        }

        // The two conversions the language deliberately refuses to make on its own — not because
        // either loses anything, but because each answer is surprising enough to be worth asking
        // for. A third has no exact decimal form; a float's tenth is not a tenth.
        if (ReferenceEquals(type, PrimitiveType.Fraction) && name == "ToReal")
        {
            return new BuiltInMember(name, PrimitiveType.Real, []);
        }

        if (ReferenceEquals(type, PrimitiveType.Float) && name == "ToFraction")
        {
            return new BuiltInMember(name, PrimitiveType.Fraction, []);
        }

        // No ToFraction on a real: it converts on its own, and exactly. A real counts in tens,
        // so it already is a fraction over a power of ten.
        return FindOnEveryType(name);
    }

    private static BuiltInMember? FindOnEnumeration(string name) => name switch
    {
        "ToInteger" => new BuiltInMember(name, PrimitiveType.Integer, []),
        _ => FindOnEveryType(name),
    };

    /// <summary>
    /// Members of the built-in models a program can name, read from the catalog rather than
    /// listed again here. See <see cref="BuiltIns"/>.
    /// </summary>
    private static BuiltInMember? FindOnBuiltInModel(ModelSymbol model, string name)
    {
        if (BuiltIns.Find(model.Name, name) is { } fromCatalog)
        {
            return fromCatalog;
        }

        // Every exception carries the message it was constructed with, including one a
        // program declares by extending Exception.
        if (IsException(model) && name == "Message")
        {
            return new BuiltInMember(name, PrimitiveType.String, []);
        }

        return FindOnEveryType(name);
    }

    /// <summary>Whether a type is, or descends from, the built-in <c>Exception</c>.</summary>
    public static bool IsException(TypeSymbol type) =>
        type is ModelSymbol model && model.SelfAndAncestors().Any(m => m.Name == "Exception");
}
