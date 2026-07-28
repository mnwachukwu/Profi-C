using ProfiC.Compiler.Text;

namespace ProfiC.Compiler.Ast;

/// <summary>What a conversion actually does at run time.</summary>
public enum ConversionOperation
{
    /// <summary>Widen an integer to a real.</summary>
    IntegerToReal,

    /// <summary>Widen an integer to a fraction. Always exact.</summary>
    IntegerToFraction,

    /// <summary>
    /// <para>Approximate a fraction as a real.</para>
    /// <para>Never implicit in ordinary arithmetic, where mixing the two must be written out,
    /// because there an exact answer was available and choosing to lose it is a decision. It
    /// is implicit in one place only: the exponent of <c>^</c>, where the result is a root and
    /// so has no exact form to preserve in the first place.</para>
    /// </summary>
    FractionToReal,

    /// <summary>Wrap a present value into an optional.</summary>
    WrapOptional,

    /// <summary>Copy a string into a set of characters.</summary>
    StringToCharacters,

    /// <summary>Copy a set of characters into a string.</summary>
    CharactersToString,

    /// <summary>Render a value as a string, for joining one to a string with '+'.</summary>
    ToStringValue,

    /// <summary>Treat a model as one of its ancestors. Nothing happens at run time.</summary>
    Upcast,
}

/// <summary>
/// <para>A conversion the program did not write but the language performs.</para>
/// <para>These are made explicit while lowering rather than left for later phases to work
/// out again. A tree-walking interpreter could rediscover most of them from the values it
/// holds, but CIL cannot: a widening needs a specific instruction in a specific place, and
/// the type checker is the only pass that ever knew enough to say which.</para>
/// </summary>
public sealed class ConversionExpr(
    SourceSpan span,
    Expression operand,
    ConversionOperation operation) : Expression(span)
{
    public Expression Operand { get; } = operand;

    public ConversionOperation Operation { get; } = operation;

    public override string NodeKind => $"Conversion({Operation})";

    public override IEnumerable<SyntaxNode> Children => [Operand];

    public override void Accept(SyntaxVisitor visitor) => visitor.VisitConversionExpr(this);

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
        visitor.VisitConversionExpr(this);
}
