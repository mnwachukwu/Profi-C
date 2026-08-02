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

    /// <summary>Whether the emitter has a CLR type for this one.</summary>
    public static bool IsSupported(TypeSymbol type) => Of(type) is not null || IsDeclaredModel(type);

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
        operation == ConversionOperation.IntegerToReal;
}
