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
        PrimitiveType primitive when ReferenceEquals(primitive, PrimitiveType.Real) => typeof(decimal),
        PrimitiveType primitive when ReferenceEquals(primitive, PrimitiveType.Float) => typeof(double),
        PrimitiveType primitive when ReferenceEquals(primitive, PrimitiveType.Fraction) =>
            typeof(Runtime.Fraction),
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
        || type is EnumerationSymbol
        || OfRoot(type) is not null
        || OfProvided(type) is not null
        || OfBuiltInModel(type) is not null
        || (type is SetType set && IsSupported(set.ElementType))
        || (type is OptionalType optional && IsSupported(optional.UnderlyingType))
        || (type is FunctionType shape
            && (shape.ReturnType is null || IsSupported(shape.ReturnType))
            && shape.ParameterTypes.All(IsSupported));

    /// <summary>
    /// <para>The CLR type a model the <em>language</em> provides denotes, or null where the
    /// emitter has none for it.</para>
    /// <para>Only the exceptions so far, and they are the family that matters: a program writes
    /// <c>model MyFailure extends Exception</c> to name its own failures, and every name it can
    /// catch is a .NET exception type already. So the mapping is a lookup rather than work —
    /// <c>Exception</c> is <c>System.Exception</c>, <c>IOException</c> is
    /// <c>System.IO.IOException</c>, and a Profi-C <c>catch</c> becomes a CIL one.</para>
    /// <para>The others — <c>Random</c>, <c>DateTime</c> — are not here because constructing and
    /// calling them is work the emitter does not do yet, not because they have no type.</para>
    /// </summary>
    public static Type? OfBuiltInModel(TypeSymbol type) =>
        type is ModelSymbol model && ReferenceEquals(model.Container, BuiltInTypes.Standard)
            ? Runtime.BuiltInExceptions.Resolve(model.Name)
            : null;

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
    /// <para>The CLR type one of the two roots denotes, or null for anything else.</para>
    /// <para><c>Model</c> is what every value is one of, and <c>Function</c> what every function
    /// value is one of. Neither is written for its own sake — what they are for is holding
    /// something whose shape is not being named, which is why both map to the type the framework
    /// already has for exactly that.</para>
    /// <para>Kept apart from <see cref="OfBuiltInModel"/> rather than folded into it, because
    /// that answer is also what decides whether a <c>new</c> can build one. Neither of these is
    /// a thing to construct.</para>
    /// </summary>
    /// <summary>
    /// <para>The CLR type one of the provided value types denotes, or null for anything else.
    /// </para>
    /// <para>Each is the platform's own — a moment is a <c>System.DateTime</c>, a day a
    /// <c>DateOnly</c>, a time of day a <c>TimeOnly</c> — so a Profi-C moment handed to .NET is a
    /// .NET moment and nothing has to be unwrapped at the boundary. A generator is the runtime's,
    /// because <c>Next(low, high)</c> is the language's rule about an excluded upper bound rather
    /// than the framework's.</para>
    /// <para>What none of them is, is constructible by a plain <c>newobj</c>: the factories in
    /// <see cref="Runtime.ProfiCMoments"/> refuse an impossible date in the language's words, and
    /// the emitter calls those instead.</para>
    /// </summary>
    public static Type? OfProvided(TypeSymbol type) =>
        type is ModelSymbol model && ReferenceEquals(model.Container, BuiltInTypes.Standard)
            ? model.Name switch
            {
                "DateTime" => typeof(DateTime),
                "Date" => typeof(DateOnly),
                "Time" => typeof(TimeOnly),
                "TimeSpan" => typeof(TimeSpan),
                "Random" => typeof(Runtime.ProfiCRandom),
                _ => null,
            }
            : null;

    public static Type? OfRoot(TypeSymbol type) =>
        type is ModelSymbol model && ReferenceEquals(model.Container, BuiltInTypes.Standard)
            ? model.Name switch
            {
                "Model" => typeof(object),
                "Function" => typeof(Delegate),
                _ => null,
            }
            : null;

    /// <summary>
    /// <para>Whether this is a model the program itself declares, as against one the language
    /// provides.</para>
    /// <para>The difference matters because a declared model becomes a type in the assembly
    /// being written, while <c>Random</c> or <c>DateTime</c> is a type in the runtime that the
    /// emitter would have to know how to construct and call. Both are models and both are
    /// <see cref="ModelSymbol"/>; what tells them apart is which namespace holds them.</para>
    /// </summary>
    /// <para>A structure is one of these too. It is not a model, but the emitter builds it the
    /// same way — a type in the assembly being written — and every question this answers is
    /// about that rather than about how it is copied.</para>
    public static bool IsDeclaredModel(TypeSymbol type) =>
        type is StructureSymbol
        || (type is ModelSymbol model && !ReferenceEquals(model.Container, BuiltInTypes.Standard));

    /// <summary>
    /// The CLR type a function gives back. Null in Profi-C means it yields nothing, which is
    /// <c>void</c> here — the two spellings for the same absence.
    /// </summary>
    public static Type? Returning(TypeSymbol? returnType) =>
        returnType is null ? typeof(void) : Of(returnType);
}
