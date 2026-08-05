using System.Text.Json.Nodes;
using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Semantics;

namespace ProfiC.Cli.LanguageServer;

/// <summary>
/// <para>Every place in this file that writes the name under the cursor.</para>
/// <para>Rename's question without the edit, and it can afford to be asked more often: this
/// changes nothing, so it runs whenever the caret moves rather than when somebody commits to
/// something. Which is why it answers for names rename refuses — putting the cursor on
/// <c>Count</c> marks every use of <c>Count</c>, and marking a name the language owns writes
/// nothing anywhere.</para>
/// <para>Two scales, one rule. <see cref="In"/> marks what is on screen, since a use in a file
/// nobody has open cannot be marked in it. <see cref="Across"/> lists every use in the whole
/// compilation, which is what somebody means when they ask where a name is used — the answer they
/// want is a list to walk, and it does not stop at the file they happen to be looking at.</para>
/// </summary>
public static class Occurrences
{
    /// <summary>Read, in the protocol's numbering: the name is being used.</summary>
    private const int Read = 2;

    /// <summary>Written: the name is being declared or assigned to.</summary>
    private const int Written = 3;

    /// <summary>
    /// <para>Where else this name is written, or null where the cursor is not in one.</para>
    /// <para>Null rather than an empty list, so that a cursor on a keyword or a number leaves the
    /// file alone instead of clearing whatever was marked.</para>
    /// </summary>
    public static JsonArray? In(CompilationUnit unit, SemanticModel model, int offset)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(model);

        if (Wanted(unit, model, offset) is not { } wanted)
        {
            return null;
        }

        // Where a value is put into a name, which is what tells one kind of mark from the other.
        // Gathered first because the question is asked of a node about its parent, and a node
        // does not carry one.
        HashSet<SyntaxNode> assigned = [];

        foreach (SyntaxNode node in Everything(unit))
        {
            if (node is AssignmentStmt assignment)
            {
                assigned.Add(assignment.Target);
            }
        }

        JsonArray marks = [];

        foreach (SyntaxNode node in Everything(unit))
        {
            // A name is written here, rather than a call or a statement that happens to be bound
            // to the same thing — the same guard renaming needs, for the same reason.
            if (!node.HasName || !wanted.Matches(node, model))
            {
                continue;
            }

            marks.Add(new JsonObject
            {
                ["range"] = Conversions.RangeOf(node.NameSpan, unit.Source),
                ["kind"] = wanted.IsWrittenAt(node, assigned) ? Written : Read,
            });
        }

        return marks;
    }

    /// <summary>
    /// <para>Every place the whole compilation writes this name, or null where the cursor is not
    /// in one.</para>
    /// <para><b>Rename's list without the rename</b>, and it reaches further than rename does for
    /// the same reason marking does: nothing is written, so a name the language owns can be
    /// answered for. Asking where <c>WriteLine</c> is called is a fair question with a useful
    /// answer, and renaming it is not a thing to offer.</para>
    /// <para>The declaration is one of them unless the editor said to leave it out. Editors ask
    /// both ways and mean different things by it — a list of callers is not the same list as
    /// everywhere the name appears — so this is the caller's choice rather than a rule here.</para>
    /// </summary>
    public static JsonArray? Across(
        IReadOnlyList<CompilationUnit> units,
        SemanticModel model,
        CompilationUnit asking,
        int offset,
        bool includingDeclaration)
    {
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(asking);

        if (Wanted(asking, model, offset) is not { } wanted)
        {
            return null;
        }

        JsonArray found = [];

        foreach (CompilationUnit unit in units)
        {
            foreach (SyntaxNode node in Everything(unit))
            {
                // A name is written here, rather than a call or a statement that happens to be
                // bound to the same thing — the same guard renaming needs, for the same reason.
                if (!node.HasName || !wanted.Matches(node, model))
                {
                    continue;
                }

                if (!includingDeclaration && wanted.IsDeclarationAt(node))
                {
                    continue;
                }

                found.Add(new JsonObject
                {
                    ["uri"] = Conversions.UriOf(unit.Source.FileName),
                    ["range"] = Conversions.RangeOf(node.NameSpan, unit.Source),
                });
            }
        }

        return found;
    }

    /// <summary>
    /// <para>The thing the cursor is on, which is one of two kinds.</para>
    /// <para>A name a program declared is a symbol, and two uses are the same name when the
    /// resolver bound them to the same one. A member the language provides is not a symbol at
    /// all — it is an entry in the compiler's catalog, recorded against the node by the type
    /// checker, which is the only pass that could have decided it. So sameness is asked
    /// differently for the two, and the difference is real rather than an accident of how they
    /// are stored: <c>Count</c> on a string and <c>Count</c> on a set share a spelling and are
    /// separate members, which the catalog says and a name does not.</para>
    /// </summary>
    private abstract record Match
    {
        public abstract bool Matches(SyntaxNode node, SemanticModel model);

        public abstract bool IsWrittenAt(SyntaxNode node, HashSet<SyntaxNode> assigned);

        /// <summary>Whether this is the place the name was declared, rather than a use of it.</summary>
        public abstract bool IsDeclarationAt(SyntaxNode node);
    }

    private sealed record Declared(Symbol Symbol) : Match
    {
        /// <summary>
        /// <para>A constructor counts as its type, because the two share one name and the
        /// language requires it: a constructor is the function named for its model. Marking the
        /// type and leaving the constructor unmarked would show a reader one of the two places
        /// the name is written and hide the other.</para>
        /// </summary>
        public override bool Matches(SyntaxNode node, SemanticModel model) =>
            model.GetSymbol(node) is { } found
            && (ReferenceEquals(found, Symbol)
                || (found is FunctionSymbol { IsConstructor: true }
                    && ReferenceEquals(found.DeclaringType, Symbol)));

        public override bool IsWrittenAt(SyntaxNode node, HashSet<SyntaxNode> assigned) =>
            assigned.Contains(node) || IsDeclarationAt(node);

        public override bool IsDeclarationAt(SyntaxNode node) =>
            Symbol.Declaration is { } declared && ReferenceEquals(declared, node);
    }

    private sealed record Provided(BuiltInId Id) : Match
    {
        public override bool Matches(SyntaxNode node, SemanticModel model) =>
            model.GetBuiltIn(node) == Id;

        // Nothing in a program declares one, and none of them can be assigned to, so every place
        // one appears is a use.
        public override bool IsWrittenAt(SyntaxNode node, HashSet<SyntaxNode> assigned) => false;

        public override bool IsDeclarationAt(SyntaxNode node) => false;
    }

    /// <summary>
    /// What the cursor is on, looked for as a declared name first. A node can carry both — a
    /// member access resolves to the catalog entry while the receiver beside it is a symbol — and
    /// the name the cursor is inside is the member.
    /// </summary>
    private static Match? Wanted(CompilationUnit unit, SemanticModel model, int offset)
    {
        foreach (SyntaxNode node in NodeAt.NamesAt(unit, offset))
        {
            if (model.GetBuiltIn(node) is { } provided)
            {
                return new Provided(provided);
            }

            if (model.GetSymbol(node) is { } symbol)
            {
                return new Declared(symbol);
            }
        }

        return null;
    }

    private static IEnumerable<SyntaxNode> Everything(SyntaxNode node)
    {
        yield return node;

        foreach (SyntaxNode child in node.Children)
        {
            foreach (SyntaxNode inside in Everything(child))
            {
                yield return inside;
            }
        }
    }
}
