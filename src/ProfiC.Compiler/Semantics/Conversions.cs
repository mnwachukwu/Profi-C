namespace ProfiC.Compiler.Semantics;

/// <summary>How one type may become another.</summary>
public enum ConversionKind
{
    /// <summary>The types are the same.</summary>
    Identity,

    /// <summary>Happens on its own, wherever the target type is expected.</summary>
    Implicit,

    /// <summary>Possible, but must be written out.</summary>
    Explicit,

    /// <summary>Not possible at all.</summary>
    None,
}

/// <summary>
/// <para>Which types become which, and how.</para>
/// <para>Several pairs are <em>not</em> implicit. A fraction and a real are both numbers, but
/// neither converts to the other on its own: one direction loses information, and the other
/// produces exact results startling enough that the program states which it wants.</para>
/// </summary>
public static class Conversions
{
    /// <summary>Classifies a conversion from one type to another.</summary>
    public static ConversionKind Classify(TypeSymbol from, TypeSymbol to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        // An error type converts to and from anything, silently. That is what stops one
        // mistake from producing a diagnostic in every expression that touches it.
        if (from.IsError || to.IsError)
        {
            return ConversionKind.Identity;
        }

        if (ReferenceEquals(from, to) || SameType(from, to))
        {
            return ConversionKind.Identity;
        }

        // Any value may be wrapped into an optional of its own type. This is how a present
        // value reaches a slot that permits absence.
        if (to is OptionalType optionalTarget
            && Classify(from, optionalTarget.UnderlyingType) is ConversionKind.Identity
                or ConversionKind.Implicit)
        {
            return ConversionKind.Implicit;
        }

        // An optional reaches another optional wherever the values it holds would reach each
        // other. Absence is carried across rather than looked inside, so a string? fits a
        // character[]? and a Square? fits a Shape?, each staying absent if that is what it was.
        //
        // This does not soften the rule below. Nothing is unwrapped here: what comes out is
        // still an optional, and getting a plain value out of one still means proving it holds
        // something.
        if (from is OptionalType fromOptional && to is OptionalType toOptional)
        {
            return Classify(fromOptional.UnderlyingType, toOptional.UnderlyingType)
                       is ConversionKind.Identity or ConversionKind.Implicit
                ? ConversionKind.Implicit
                : ConversionKind.None;
        }

        // Reading an optional as a plain value is never automatic; that is the whole point
        // of optionals being strict.
        if (from is OptionalType)
        {
            return ConversionKind.None;
        }

        if (from is PrimitiveType fromPrimitive && to is PrimitiveType toPrimitive)
        {
            return ClassifyNumeric(fromPrimitive, toPrimitive);
        }

        // A string and a set of characters are interchangeable, each direction copying.
        if (IsCharacterSet(from) && ReferenceEquals(to, PrimitiveType.String))
        {
            return ConversionKind.Implicit;
        }

        if (ReferenceEquals(from, PrimitiveType.String) && IsCharacterSet(to))
        {
            return ConversionKind.Implicit;
        }

        // Every type inherits Model's members, but a value type never converts to Model: that
        // conversion is boxing, which the language does not have.
        //
        // Asked before the walk below rather than after it, because reaching Model is not a
        // question about a model's ancestors. Every model reaches it, whether or not it wrote
        // "extends" — so a walk that stops where the writing stops answers the wrong question.
        if (to is ModelSymbol { Name: "Model" })
        {
            return from.IsValueType ? ConversionKind.None : ConversionKind.Implicit;
        }

        // A model reaches any of its ancestors.
        if (from is ModelSymbol fromModel && to is ModelSymbol toModel)
        {
            return fromModel.SelfAndAncestors().Contains(toModel)
                ? ConversionKind.Implicit
                : ConversionKind.None;
        }

        // Every function is a Function, whatever it takes and yields. This is what lets one be
        // held without its signature being named, and it is the only thing Function is for:
        // nothing else reaches it, and a Function reaches no signature back without a cast.
        if (to is ModelSymbol { Name: "Function" })
        {
            return from is FunctionType ? ConversionKind.Implicit : ConversionKind.None;
        }

        // A set converts only to a set of the very same element type. Allowing a set of
        // squares where a set of shapes is expected would let a shape be inserted into it.
        if (from is SetType fromSet && to is SetType toSet)
        {
            return SameType(fromSet.ElementType, toSet.ElementType)
                ? ConversionKind.Identity
                : ConversionKind.None;
        }

        return ConversionKind.None;
    }

    /// <summary>True when a value of one type may be used where the other is expected.</summary>
    public static bool IsAssignable(TypeSymbol from, TypeSymbol to) =>
        Classify(from, to) is ConversionKind.Identity or ConversionKind.Implicit;

    /// <summary>
    /// <para>Conversions among the built-in numbers, and which of them a program has to ask
    /// for.</para>
    /// <para><b>Exactness decides.</b> A conversion that loses nothing happens on its own; one
    /// that discards information is a decision, and the program says which it wants.</para>
    /// <list type="bullet">
    /// <item><description>An <b>integer</b> widens automatically to either — every whole number
    /// is a real and is a fraction over one.</description></item>
    /// <item><description>A <b>real</b> widens automatically to a fraction. A real counts in tens,
    /// so it already <em>is</em> a fraction over a power of ten: a tenth is <c>1|10</c>. What can
    /// go wrong is size rather than accuracy — a fraction's parts are integers — and that is
    /// caught where the value is rather than by refusing the whole conversion.</description></item>
    /// <item><description>A <b>fraction</b> converts to a real only when asked. Thirds have no
    /// exact decimal form any more than they have a binary one, so one third is 0.3333… and does
    /// not multiply back to one.</description></item>
    /// <item><description>A <b>float</b> converts to neither, and neither converts to it. Binary
    /// floating point is a different question from decimal arithmetic, and the whole reason both
    /// types exist is that a reader should notice which one they are in.</description></item>
    /// </list>
    /// </summary>
    private static ConversionKind ClassifyNumeric(PrimitiveType from, PrimitiveType to)
    {
        if (ReferenceEquals(from, PrimitiveType.Integer))
        {
            if (ReferenceEquals(to, PrimitiveType.Fraction))
            {
                return ConversionKind.Implicit;
            }

            if (ReferenceEquals(to, PrimitiveType.Real))
            {
                return ConversionKind.Implicit;
            }
        }

        if (ReferenceEquals(from, PrimitiveType.Real) && ReferenceEquals(to, PrimitiveType.Fraction))
        {
            return ConversionKind.Implicit;
        }

        if (ReferenceEquals(from, PrimitiveType.Fraction) && ReferenceEquals(to, PrimitiveType.Real))
        {
            return ConversionKind.Explicit;
        }

        // A character is not a small integer here. Treating it as one is a C habit that
        // teaches the wrong thing about what a character is.
        return ConversionKind.None;
    }

    /// <summary>Structural identity, since set and optional types are built on demand.</summary>
    public static bool SameType(TypeSymbol left, TypeSymbol right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        return (left, right) switch
        {
            (SetType a, SetType b) => SameType(a.ElementType, b.ElementType),
            (OptionalType a, OptionalType b) => SameType(a.UnderlyingType, b.UnderlyingType),
            (FunctionType a, FunctionType b) => SameFunction(a, b),
            _ => false,
        };
    }

    /// <summary>
    /// <para>Whether two functions take the same things, which is what makes them one function
    /// rather than two versions of a name.</para>
    /// <para>Asked by the resolver, to refuse a second declaration of a signature already
    /// written, and by the type checker, to know that a function further up a chain of models
    /// has been replaced by the one below it. The same question either way.</para>
    /// </summary>
    public static bool SameParameters(FunctionSymbol left, FunctionSymbol right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return left.Parameters.Count == right.Parameters.Count
            && left.Parameters
                .Zip(right.Parameters)
                .All(both => SameType(both.First.Type, both.Second.Type));
    }

    private static bool SameFunction(FunctionType left, FunctionType right)
    {
        if (left.ParameterTypes.Count != right.ParameterTypes.Count)
        {
            return false;
        }

        if ((left.ReturnType is null) != (right.ReturnType is null))
        {
            return false;
        }

        if (left.ReturnType is not null
            && !SameType(left.ReturnType, right.ReturnType!))
        {
            return false;
        }

        return !left.ParameterTypes.Where((t, i) => !SameType(t, right.ParameterTypes[i])).Any();
    }

    private static bool IsCharacterSet(TypeSymbol type) =>
        type is SetType set && ReferenceEquals(set.ElementType, PrimitiveType.Character);

    /// <summary>
    /// The types a <c>switch</c> may examine. Reals and fractions are absent because
    /// equality on them is a trap, which is exactly what a case label tests.
    /// </summary>
    public static bool IsSwitchable(TypeSymbol type) =>
        type.IsError
        || ReferenceEquals(type, PrimitiveType.Integer)
        || ReferenceEquals(type, PrimitiveType.Character)
        || ReferenceEquals(type, PrimitiveType.String)
        || ReferenceEquals(type, PrimitiveType.Boolean)
        || type is EnumerationSymbol;

    /// <summary>
    /// <para>Whether a type may be declared <c>constant</c>.</para>
    /// <para>Only types where an immutable binding really means an unchanging value. On a
    /// model or a set the binding could not change while the thing it names did, which is
    /// what "constant" would then fail to mean. Widening this is a v2 matter.</para>
    /// </summary>
    public static bool CanBeConstant(TypeSymbol type)
    {
        if (type.IsError)
        {
            return true;
        }

        return type switch
        {
            PrimitiveType => true,
            EnumerationSymbol => true,
            StructureSymbol structure => IsValueOnlyStructure(structure, []),
            _ => false,
        };
    }

    /// <summary>
    /// True when a structure holds nothing that could change behind an immutable binding.
    /// A structure cannot contain itself, so this walk terminates.
    /// </summary>
    private static bool IsValueOnlyStructure(StructureSymbol structure, HashSet<TypeSymbol> seen)
    {
        if (!seen.Add(structure))
        {
            return true;
        }

        foreach (List<Symbol> group in structure.Members.Values)
        {
            foreach (Symbol member in group)
            {
                if (member is not FieldSymbol field)
                {
                    continue;
                }

                bool ok = field.Type switch
                {
                    PrimitiveType => true,
                    EnumerationSymbol => true,
                    StructureSymbol nested => IsValueOnlyStructure(nested, seen),
                    _ => field.Type.IsError,
                };

                if (!ok)
                {
                    return false;
                }
            }
        }

        return true;
    }
}
