using ProfiC.Compiler.Text;

namespace ProfiC.Compiler.Ast;

/// <summary>
/// <para>The base of every syntax tree node.</para>
/// <para>Nodes are ordinary sealed classes rather than records. Records would be shorter to
/// declare across a hierarchy this size, but they synthesize a <c>ToString</c> that prints an
/// entire subtree, which is unusable in a debugger, and a structural equality that is quietly
/// wrong for any node holding a list of children.</para>
/// <para>Every node carries the span of source it was parsed from. Diagnostics from every
/// later phase are anchored on these, which is why source positions had to exist before any
/// of this could be built.</para>
/// </summary>
public abstract class SyntaxNode(SourceSpan span)
{
    /// <summary>The source this node was parsed from.</summary>
    public SourceSpan Span { get; } = span;

    private readonly SourceSpan? _nameSpan;

    /// <summary>
    /// <para>Where the name this node introduces or refers to is written, or the whole node
    /// where there is no name in it.</para>
    /// <para><b>Held apart from <see cref="Span"/> because the two are different questions, and
    /// only one of them can be answered by arithmetic.</b> A function's span runs from its first
    /// modifier to its <c>end function</c>; its name is one identifier somewhere inside. Anything
    /// that writes over a name — renaming one, highlighting it — needs exactly that identifier,
    /// and working out where it sits by counting from either end of the declaration is how an
    /// editor corrupts somebody's file.</para>
    /// <para>The whole node by default, which is right for most of them — a literal introduces no
    /// name — and is what everything answered before this was recorded, so a node built without
    /// it behaves as it always did. The parser sets it from the identifier token it already
    /// consumed; the token carries the span and nothing has to be computed.</para>
    /// </summary>
    public SourceSpan NameSpan
    {
        get => _nameSpan ?? Span;
        init => _nameSpan = value;
    }

    /// <summary>
    /// <para>Whether there is a name written here at all.</para>
    /// <para>The difference between a node whose <see cref="NameSpan"/> is a name and one whose
    /// <see cref="NameSpan"/> is only the default. Both answer with a span, and the span alone
    /// cannot tell them apart — which matters where the answer is an edit: a call is bound to the
    /// same symbol as the name inside it, and writing a new name over a call's span replaces the
    /// arguments with it.</para>
    /// </summary>
    public bool HasName => _nameSpan is not null;

    /// <summary>The one-based line this node begins on.</summary>
    public int Line => Span.Start.Line;

    /// <summary>The one-based column this node begins at.</summary>
    public int Column => Span.Start.Column;

    /// <summary>
    /// A short name for this kind of node, used by the printer and in diagnostics. Defaults
    /// to the class name, which is the right answer for nearly every node.
    /// </summary>
    public virtual string NodeKind => GetType().Name;

    /// <summary>
    /// <para>This node's children, in source order.</para>
    /// <para>Implemented by every node so that a walk, a printer, or a search can traverse
    /// the tree without knowing what it is looking at.</para>
    /// </summary>
    public abstract IEnumerable<SyntaxNode> Children { get; }

    /// <summary>Accepts a visitor that returns nothing.</summary>
    public abstract void Accept(SyntaxVisitor visitor);

    /// <summary>Accepts a visitor that returns a result.</summary>
    public abstract TResult Accept<TResult>(SyntaxVisitor<TResult> visitor);

    /// <summary>
    /// Every descendant of this node, depth first, excluding the node itself. Iterative
    /// rather than recursive, so a deeply nested expression cannot exhaust the stack.
    /// </summary>
    public IEnumerable<SyntaxNode> Descendants()
    {
        Stack<SyntaxNode> pending = new(Children.Reverse());

        while (pending.Count > 0)
        {
            SyntaxNode node = pending.Pop();
            yield return node;

            foreach (SyntaxNode child in node.Children.Reverse())
            {
                pending.Push(child);
            }
        }
    }

    /// <summary>Short identification only. The printer renders whole trees.</summary>
    public override string ToString() => $"{NodeKind} at {Span.Start}";

    /// <summary>Convenience for nodes whose children are a single optional node.</summary>
    private protected static IEnumerable<SyntaxNode> NonNull(params SyntaxNode?[] nodes)
    {
        foreach (SyntaxNode? node in nodes)
        {
            if (node is not null)
            {
                yield return node;
            }
        }
    }
}

/// <summary>The base of every declaration.</summary>
public abstract class Declaration(SourceSpan span) : SyntaxNode(span);

/// <summary>The base of every statement.</summary>
public abstract class Statement(SourceSpan span) : SyntaxNode(span);

/// <summary>The base of every expression.</summary>
public abstract class Expression(SourceSpan span) : SyntaxNode(span);

/// <summary>
/// The base of every type as it appears in source. This is syntax, not a resolved type: it
/// records what was written, and binding it to an actual type is the resolver's work.
/// </summary>
public abstract class TypeSyntax(SourceSpan span) : SyntaxNode(span);
