using ProfiC.Compiler.Ast;

namespace ProfiC.Services;

/// <summary>
/// <para>Finds the syntax the cursor is in.</para>
/// <para>Every question an editor asks about a place — what is this, where is it declared, what
/// does it take — starts here, and each of them wants the <b>innermost</b> node covering the
/// offset. Asked about the <c>Words</c> in <c>Greeting.Words()</c>, the enclosing call and the
/// function and the model all contain it; the answer is the name, and everything above it is
/// context nobody asked for.</para>
/// <para>Walked from the tree rather than from an index built beforehand. An index would be the
/// faster answer and the wrong shape for now: it would have to be invalidated on every keystroke,
/// which is the caching this deliberately does not have yet.</para>
/// </summary>
public static class NodeAt
{
    /// <summary>
    /// <para>The innermost node covering an offset, or null where the tree does not reach it.
    /// </para>
    /// <para>Null is ordinary rather than a failure: an offset in whitespace, in a comment, or
    /// past the end of what parsed belongs to no node, and a caller has nothing to say about it.
    /// </para>
    /// </summary>
    public static SyntaxNode? Innermost(SyntaxNode root, int offset)
    {
        ArgumentNullException.ThrowIfNull(root);

        if (!Covers(root, offset))
        {
            return null;
        }

        SyntaxNode found = root;

        // Descends as far as anything covers the offset. A node's children are inside it, so the
        // last one still covering is the innermost.
        while (Deeper(found, offset) is { } child)
        {
            found = child;
        }

        return found;
    }

    /// <summary>
    /// <para>The innermost node of a given kind covering an offset, or null where there is
    /// none.</para>
    /// <para>What a caller usually wants, since the innermost node at all may be a piece of
    /// syntax with nothing recorded about it — a type name inside a declaration, say — while the
    /// declaration around it is the thing being asked about.</para>
    /// </summary>
    public static T? Innermost<T>(SyntaxNode root, int offset)
        where T : SyntaxNode
    {
        ArgumentNullException.ThrowIfNull(root);

        if (!Covers(root, offset))
        {
            return null;
        }

        T? found = root as T;
        SyntaxNode at = root;

        while (Deeper(at, offset) is { } child)
        {
            at = child;

            if (child is T wanted)
            {
                found = wanted;
            }
        }

        return found;
    }

    /// <summary>Every node covering an offset, innermost first.</summary>
    public static IReadOnlyList<SyntaxNode> Enclosing(SyntaxNode root, int offset)
    {
        ArgumentNullException.ThrowIfNull(root);

        List<SyntaxNode> spine = [];

        if (!Covers(root, offset))
        {
            return spine;
        }

        SyntaxNode at = root;
        spine.Add(at);

        while (Deeper(at, offset) is { } child)
        {
            at = child;
            spine.Insert(0, at);
        }

        return spine;
    }

    /// <summary>
    /// <para>Every node the cursor is inside <em>a name of</em>, innermost first.</para>
    /// <para>Walked outward from the innermost, because the innermost is often syntax nothing was
    /// recorded for while what encloses it is the thing being asked about — a name inside a member
    /// access, a type inside a declaration.</para>
    /// <para><b>The name is what stops the walk, and without it the walk does not stop anywhere
    /// useful.</b> A declaration encloses every line of its body, so a cursor on something the
    /// compiler recorded nothing about would walk outward until it reached the function the line
    /// sits in, and then answer about that: asking where <c>Count</c> is declared would take a
    /// reader to <c>Length</c>. Every question of the form "the name here" narrows this, so the
    /// rule lives once and each caller says only what it wants to find.</para>
    /// </summary>
    public static IEnumerable<SyntaxNode> NamesAt(SyntaxNode root, int offset)
    {
        foreach (SyntaxNode node in Enclosing(root, offset))
        {
            if (offset >= node.NameSpan.Start.Offset && offset <= node.NameSpan.EndOffset)
            {
                yield return node;
            }
        }
    }

    /// <summary>
    /// <para>Which argument of a list the cursor is in, counted by the arguments that end before
    /// it.</para>
    /// <para>Counted from the arguments rather than by looking for commas, because a comma inside
    /// a nested call or a string belongs to something else, and counting those would answer about
    /// the wrong parameter exactly where the code is hardest to read.</para>
    /// <para>An argument the parser stood in for is one that ends where the next token begins,
    /// which is past the cursor rather than at it — so a cursor sitting in an argument nobody has
    /// typed yet counts that argument as the one it is in, which it is.</para>
    /// </summary>
    public static int ArgumentAt(IReadOnlyList<Expression> arguments, int offset)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        int at = 0;

        foreach (Expression argument in arguments)
        {
            if (offset > argument.Span.EndOffset)
            {
                at++;
            }
        }

        return at;
    }

    /// <summary>
    /// The child covering the offset, or null where none does. The first is taken where several
    /// would do, which happens only for a node of no width.
    /// </summary>
    private static SyntaxNode? Deeper(SyntaxNode node, int offset) =>
        node.Children.FirstOrDefault(child => Covers(child, offset));

    /// <summary>
    /// <para>Whether a node covers an offset, counting the position just past its end.</para>
    /// <para>The cursor sits <em>between</em> characters, so asking about a name with the caret
    /// tucked against its last letter is asking about the name — which is exactly where somebody
    /// leaves it after typing one.</para>
    /// </summary>
    private static bool Covers(SyntaxNode node, int offset) =>
        offset >= node.Span.Start.Offset && offset <= node.Span.EndOffset;
}
