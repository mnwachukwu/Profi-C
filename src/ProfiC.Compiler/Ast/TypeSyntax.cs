using ProfiC.Compiler.Text;

namespace ProfiC.Compiler.Ast;

/// <summary>
/// <para>A type written by name: one of the built-in words, or an identifier naming a model,
/// structure, or enumeration.</para>
/// <para><c>Model</c> arrives here as an ordinary identifier. It is a reserved type name
/// rather than a keyword, and enforcing that it cannot be redeclared is the resolver's job.
/// </para>
/// </summary>
public sealed class NamedTypeSyntax(SourceSpan span, string name) : TypeSyntax(span)
{
    public string Name { get; } = name;

    public override IEnumerable<SyntaxNode> Children => [];

    public override void Accept(SyntaxVisitor visitor) => visitor.VisitNamedType(this);

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
        visitor.VisitNamedType(this);
}

/// <summary>
/// A set of some element type, written as a <c>[]</c> suffix.
/// </summary>
/// <remarks>
/// Suffixes nest left to right, and the two orderings mean different things:
/// <c>Node?[]</c> is a set of optionals, so it parses to a set wrapping an optional;
/// <c>Node[]?</c> is an optional set, so it parses to an optional wrapping a set. Building
/// them as distinct trees is what makes the distinction survive into the type checker.
/// </remarks>
public sealed class SetTypeSyntax(SourceSpan span, TypeSyntax elementType) : TypeSyntax(span)
{
    public TypeSyntax ElementType { get; } = elementType;

    public override IEnumerable<SyntaxNode> Children => [ElementType];

    public override void Accept(SyntaxVisitor visitor) => visitor.VisitSetType(this);

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
        visitor.VisitSetType(this);
}

/// <summary>
/// An optional of some underlying type, written as a <c>?</c> suffix. See
/// <see cref="SetTypeSyntax"/> for how the two suffixes nest.
/// </summary>
public sealed class OptionalTypeSyntax(SourceSpan span, TypeSyntax underlyingType) : TypeSyntax(span)
{
    public TypeSyntax UnderlyingType { get; } = underlyingType;

    public override IEnumerable<SyntaxNode> Children => [UnderlyingType];

    public override void Accept(SyntaxVisitor visitor) => visitor.VisitOptionalType(this);

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
        visitor.VisitOptionalType(this);
}

/// <summary>
/// <para>The type of a function value, as in <c>integer function(integer, integer)</c>.</para>
/// <para>A null return type is a function that yields nothing.</para>
/// </summary>
public sealed class FunctionTypeSyntax(
    SourceSpan span,
    TypeSyntax? returnType,
    IReadOnlyList<TypeSyntax> parameterTypes) : TypeSyntax(span)
{
    public TypeSyntax? ReturnType { get; } = returnType;

    public IReadOnlyList<TypeSyntax> ParameterTypes { get; } = parameterTypes;

    public override IEnumerable<SyntaxNode> Children =>
        NonNull(ReturnType).Concat(ParameterTypes);

    public override void Accept(SyntaxVisitor visitor) => visitor.VisitFunctionType(this);

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
        visitor.VisitFunctionType(this);
}
