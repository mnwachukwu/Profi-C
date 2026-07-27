namespace ProfiC.Compiler.Semantics;

/// <summary>What a built-in member call yields, and what it accepts.</summary>
/// <param name="Name">The member's name.</param>
/// <param name="ReturnType">The type it yields, or null if it yields nothing.</param>
/// <param name="ParameterTypes">
/// The types it takes. A null entry accepts any type, which is how <c>Console.Write</c> takes
/// a value of any kind and how an optional's fallback matches its own underlying type.
/// </param>
public sealed record BuiltInMember(
    string Name,
    TypeSymbol? ReturnType,
    IReadOnlyList<TypeSymbol?> ParameterTypes);

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
        BuiltInMember? single = receiver switch
        {
            SetType set => FindOnSet(set, name),
            OptionalType optional => FindOnOptional(optional, name),
            PrimitiveType primitive => FindOnPrimitive(primitive, name),
            EnumerationSymbol => FindOnEnumeration(name),
            ModelSymbol model => FindOnBuiltInModel(model, name),
            _ => FindOnEveryType(name),
        };

        // The handful of members with a second form.
        if (receiver is ModelSymbol { Name: "Console" } && name is "WriteLine")
        {
            return single is null
                ? [new BuiltInMember(name, null, [])]
                : [single, new BuiltInMember(name, null, [])];
        }

        if (receiver is OptionalType optionalReceiver && name == "Or")
        {
            // Given another optional the chain stays optional; given a plain value it ends
            // with a definite one. That is what makes a.Or(b).Or(c) work.
            return
            [
                new BuiltInMember(name, optionalReceiver.UnderlyingType, [optionalReceiver.UnderlyingType]),
                new BuiltInMember(name, optionalReceiver, [optionalReceiver]),
            ];
        }

        return single is null ? [] : [single];
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

        // The two conversions the language deliberately refuses to make on its own.
        if (ReferenceEquals(type, PrimitiveType.Fraction) && name == "ToReal")
        {
            return new BuiltInMember(name, PrimitiveType.Real, []);
        }

        if (ReferenceEquals(type, PrimitiveType.Real) && name == "ToFraction")
        {
            return new BuiltInMember(name, PrimitiveType.Fraction, []);
        }

        return FindOnEveryType(name);
    }

    private static BuiltInMember? FindOnEnumeration(string name) => name switch
    {
        "ToInteger" => new BuiltInMember(name, PrimitiveType.Integer, []),
        _ => FindOnEveryType(name),
    };

    /// <summary>Members of the built-in models a program can name.</summary>
    private static BuiltInMember? FindOnBuiltInModel(ModelSymbol model, string name)
    {
        switch (model.Name)
        {
            case "Console":
                return name switch
                {
                    // Both take a value of any type; the compiler renders it from its static
                    // type, so no overload per primitive is needed.
                    "Write" => new BuiltInMember(name, null, [null]),
                    "WriteLine" => new BuiltInMember(name, null, [null]),
                    "Read" => new BuiltInMember(
                        name, new OptionalType(PrimitiveType.String), []),
                    _ => null,
                };

            case "Reference":
                return name == "Equals"
                    ? new BuiltInMember(name, PrimitiveType.Boolean, [null, null])
                    : null;

            case "Math":
                return name switch
                {
                    "Sqrt" or "Abs" or "Floor" or "Ceiling" =>
                        new BuiltInMember(name, PrimitiveType.Real, [PrimitiveType.Real]),
                    "Pow" => new BuiltInMember(
                        name, PrimitiveType.Real, [PrimitiveType.Real, PrimitiveType.Real]),
                    "Min" or "Max" => new BuiltInMember(
                        name, PrimitiveType.Integer, [PrimitiveType.Integer, PrimitiveType.Integer]),
                    _ => FindOnEveryType(name),
                };

            default:
                return FindOnEveryType(name);
        }
    }
}
