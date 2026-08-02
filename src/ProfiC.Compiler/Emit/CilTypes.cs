using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Semantics;

namespace ProfiC.Compiler.Emit;

/// <summary>
/// <para>What each Profi-C type is, once it reaches the CLR.</para>
/// <para>The mapping is the language's, not a choice made here: <c>integer</c> is 64 bits and
/// signed because the specification says so, <c>string</c> is <c>System.String</c> outright, and
/// <c>Model</c> is <c>System.Object</c> rather than a base class the runtime ships. Writing it
/// down once means the emitter and the survey cannot disagree about what is emittable.</para>
/// </summary>
internal static class CilTypes
{
    /// <summary>
    /// <para>The CLR type a primitive becomes, or null for anything else.</para>
    /// <para>Only the primitives, because they are the ones with an answer that does not depend
    /// on the program: a declared model becomes a type this build is in the middle of creating,
    /// which only the emitter holding the builders can say.</para>
    /// </summary>
    public static Type? Of(TypeSymbol type) => type switch
    {
        PrimitiveType primitive when ReferenceEquals(primitive, PrimitiveType.Integer) => typeof(long),
        PrimitiveType primitive when ReferenceEquals(primitive, PrimitiveType.Real) => typeof(double),
        PrimitiveType primitive when ReferenceEquals(primitive, PrimitiveType.Boolean) => typeof(bool),
        PrimitiveType primitive when ReferenceEquals(primitive, PrimitiveType.Character) => typeof(char),
        PrimitiveType primitive when ReferenceEquals(primitive, PrimitiveType.String) => typeof(string),
        PrimitiveType primitive when ReferenceEquals(primitive, PrimitiveType.Void) => typeof(void),
        _ => null,
    };

    /// <summary>
    /// <para>Whether the emitter has a CLR type for this one.</para>
    /// <para>A set is supported when what it holds is, which is what lets <c>integer[][]</c> work
    /// with nothing written for it: a set of sets is a set whose element type is a set, and the
    /// question recurses until it reaches something with an answer of its own.</para>
    /// </summary>
    public static bool IsSupported(TypeSymbol type) =>
        Of(type) is not null
        || IsDeclaredModel(type)
        || (type is SetType set && IsSupported(set.ElementType))
        || (type is OptionalType optional && IsSupported(optional.UnderlyingType));

    /// <summary>
    /// <para>The runtime type a set becomes, for an element type already resolved.</para>
    /// <para>The very type the interpreter uses, rather than a CLR array: inserting and removing
    /// are part of a Profi-C set's surface and an array's length is fixed. Sharing it is also
    /// what keeps the two engines agreeing about what <c>Remove</c> does to the order.</para>
    /// </summary>
    public static Type SetOf(Type element) =>
        typeof(Runtime.ProfiCSet<>).MakeGenericType(element);

    /// <summary>
    /// <para>The runtime type an optional becomes.</para>
    /// <para>One shape whatever it holds, unlike C#, where <c>int?</c> is a <c>Nullable</c> and
    /// <c>string?</c> is the reference itself. Profi-C has no null to reuse for the second case,
    /// and a single shape means the emitter never asks which kind it has — which is also why the
    /// language could forbid <c>T??</c> without leaving a hole.</para>
    /// <para>A struct, so an optional local allocates nothing and an absent one holds nothing.
    /// </para>
    /// </summary>
    public static Type OptionalOf(Type underlying) =>
        typeof(Runtime.Optional<>).MakeGenericType(underlying);

    /// <summary>
    /// <para>Whether this is a model the program itself declares, as against one the language
    /// provides.</para>
    /// <para>The difference matters because a declared model becomes a type in the assembly
    /// being written, while <c>Random</c> or <c>DateTime</c> is a type in the runtime that the
    /// emitter would have to know how to construct and call. Both are models and both are
    /// <see cref="ModelSymbol"/>; what tells them apart is which namespace holds them.</para>
    /// </summary>
    public static bool IsDeclaredModel(TypeSymbol type) =>
        type is ModelSymbol model && !ReferenceEquals(model.Container, BuiltInTypes.Standard);

    /// <summary>
    /// The CLR type a function gives back. Null in Profi-C means it yields nothing, which is
    /// <c>void</c> here — the two spellings for the same absence.
    /// </summary>
    public static Type? Returning(TypeSymbol? returnType) =>
        returnType is null ? typeof(void) : Of(returnType);
}

/// <summary>Which recorded conversions the emitter can perform.</summary>
internal static class CilConversions
{
    /// <summary>
    /// Only the numeric widening so far. The rest need a runtime type the emitter does not yet
    /// produce: an optional to wrap into, a set to fill, a fraction to construct.
    /// </summary>
    public static bool IsSupported(ConversionOperation operation) =>
        operation is ConversionOperation.IntegerToReal or ConversionOperation.WrapOptional;
}
