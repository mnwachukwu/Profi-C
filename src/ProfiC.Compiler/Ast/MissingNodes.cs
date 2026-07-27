using ProfiC.Compiler.Text;

namespace ProfiC.Compiler.Ast;

/// <summary>
/// <para>Stands in for an expression that was required and absent.</para>
/// <para>Its span is empty and sits where the expression should have started, so a later
/// phase reporting near it points at the gap rather than at whatever followed.</para>
/// <para>A phase must not report on a node whose subtree contains one of these. A single
/// syntax error should produce a single diagnostic, not a type error and an assignment error
/// stacked on top of it.</para>
/// </summary>
public sealed class MissingExpr(SourceSpan span) : Expression(span)
{
    public override IEnumerable<SyntaxNode> Children => [];

    public override void Accept(SyntaxVisitor visitor) => visitor.VisitMissingExpr(this);

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
        visitor.VisitMissingExpr(this);
}

/// <summary>
/// Stands in for a type that was required and absent. See <see cref="MissingExpr"/> for why
/// these exist and how later phases must treat them.
/// </summary>
public sealed class MissingType(SourceSpan span) : TypeSyntax(span)
{
    public override IEnumerable<SyntaxNode> Children => [];

    public override void Accept(SyntaxVisitor visitor) => visitor.VisitMissingType(this);

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
        visitor.VisitMissingType(this);
}

/// <summary>Tests for the absence of any parse failure within a subtree.</summary>
public static class SyntaxNodeExtensions
{
    /// <summary>
    /// True if this node or any descendant stands in for something the parser could not
    /// read. Later phases use this to stay quiet about code that never parsed.
    /// </summary>
    public static bool ContainsMissing(this SyntaxNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node is MissingExpr or MissingType
            || node.Descendants().Any(n => n is MissingExpr or MissingType);
    }
}
