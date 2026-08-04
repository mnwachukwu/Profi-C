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
    /// <para>Widen a real to a fraction.</para>
    /// <para>Exact, which is why it needs no asking: a real counts in tens, so it already is a
    /// fraction over a power of ten and a tenth converts to <c>1|10</c>. Only size can go wrong,
    /// since a fraction's parts are integers — and a value written down too wide to hold is
    /// refused while compiling rather than left to fail.</para>
    /// </summary>
    RealToFraction,

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
