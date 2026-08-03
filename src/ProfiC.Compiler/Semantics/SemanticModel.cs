using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Text;

namespace ProfiC.Compiler.Semantics;

/// <summary>
/// <para>What the resolver worked out about a syntax tree.</para>
/// <para>Held beside the tree rather than on it. Keeping the tree immutable means it can be
/// shared, reparsed independently, and cached — which is what an editor needs — and it costs
/// only a lookup where a field would have been a dereference.</para>
/// </summary>
public sealed class SemanticModel
{
    private readonly Dictionary<SyntaxNode, Symbol> _symbols = [];
    private readonly Dictionary<SyntaxNode, TypeSymbol> _types = [];
    private readonly Dictionary<
        SyntaxNode,
        IReadOnlyList<(ConversionOperation Operation, TypeSymbol Target)>> _conversions = [];

    private readonly Dictionary<SyntaxNode, BuiltInId> _builtIns = [];

    private readonly Dictionary<SyntaxNode, bool> _settledTests = [];

    /// <summary>
    /// <para>Which names were in force over which stretch of which file.</para>
    /// <para>Kept by span rather than by node because scopes and nodes do not line up: one
    /// <c>if</c> opens two of them, and neither is the statement. Held per file because a span
    /// carries an offset and no notion of where it came from.</para>
    /// </summary>
    private readonly Dictionary<SourceText, List<(SourceSpan Covering, NameScope Names)>> _scopes =
        [];

    /// <summary>The global namespace, holding everything declared at the top level.</summary>
    public NamespaceSymbol GlobalNamespace { get; } = new(string.Empty, parent: null);

    /// <summary>The entry point, once one has been found.</summary>
    public FunctionSymbol? EntryPoint { get; internal set; }

    /// <summary>Records what a node refers to.</summary>
    internal void Bind(SyntaxNode node, Symbol symbol) => _symbols[node] = symbol;

    /// <summary>Records the names in force over a stretch of a file.</summary>
    internal void Opened(SourceText file, SourceSpan covering, NameScope names)
    {
        if (!_scopes.TryGetValue(file, out List<(SourceSpan, NameScope)>? opened))
        {
            _scopes[file] = opened = [];
        }

        opened.Add((covering, names));
    }

    /// <summary>
    /// <para>The names in force at a place in a file, or null where nothing was recorded — a
    /// point outside every function body, which is nowhere a bare name can be written.</para>
    /// <para>The narrowest stretch covering the offset, since scopes nest and the innermost is
    /// the one whose names shadow the rest. Scanned rather than indexed: this is asked once per
    /// question about one cursor, and a file has as many of these as it has blocks.</para>
    /// </summary>
    public NameScope? NamesAt(SourceText file, int offset)
    {
        if (file is null || !_scopes.TryGetValue(file, out List<(SourceSpan Covering, NameScope Names)>? opened))
        {
            return null;
        }

        NameScope? narrowest = null;
        int width = int.MaxValue;

        foreach ((SourceSpan covering, NameScope names) in opened)
        {
            if (offset < covering.Start.Offset || offset > covering.EndOffset)
            {
                continue;
            }

            if (covering.Length < width)
            {
                width = covering.Length;
                narrowest = names;
            }
        }

        return narrowest;
    }

    /// <summary>Records the type a node denotes.</summary>
    internal void BindType(SyntaxNode node, TypeSymbol type) => _types[node] = type;

    /// <summary>What a node refers to, or null if nothing was resolved for it.</summary>
    public Symbol? GetSymbol(SyntaxNode node) =>
        _symbols.TryGetValue(node, out Symbol? symbol) ? symbol : null;

    /// <summary>The type a node denotes, or null if none was recorded.</summary>
    public TypeSymbol? GetType(SyntaxNode node) =>
        _types.TryGetValue(node, out TypeSymbol? type) ? type : null;

    /// <summary>
    /// <para>Records which member the language provides a name resolved to.</para>
    /// <para>The type checker is the only pass that can decide this: it knows the receiver's
    /// type, what narrowing has proved about it, and whether the receiver's own type declares
    /// a member of the same name. Writing the answer down means the back end carries it out
    /// rather than deciding a second time from the value in hand, which is a different
    /// question with a different answer.</para>
    /// </summary>
    internal void BindBuiltIn(SyntaxNode node, BuiltInId id) => _builtIns[node] = id;

    /// <summary>
    /// The member the language provides that a name resolved to, or null when the name
    /// resolved to something a program declared.
    /// </summary>
    public BuiltInId? GetBuiltIn(SyntaxNode node) =>
        _builtIns.TryGetValue(node, out BuiltInId id) ? id : null;

    /// <summary>
    /// <para>Records that a type test's answer follows from the types alone.</para>
    /// <para>Some tests cannot be answered by looking at the value: a set does not carry its
    /// element type and a function does not carry its signature. They do not need to be, since
    /// the declared types settle them — but only this pass knows that, so it writes the answer
    /// down rather than leaving the back end to work out something it cannot see.</para>
    /// </summary>
    internal void SettleTest(SyntaxNode node, bool answer) => _settledTests[node] = answer;

    /// <summary>
    /// The answer a type test was settled with, or null when it is a real question about the
    /// value and has to be asked at run time.
    /// </summary>
    public bool? GetSettledTest(SyntaxNode node) =>
        _settledTests.TryGetValue(node, out bool answer) ? answer : null;

    /// <summary>
    /// <para>Records what a value has to do to reach where it sits, in order.</para>
    /// <para>Written down by the type checker, because it is the only pass that knows both
    /// what a value is and what is expected of it. Lowering then makes the conversion a real
    /// node rather than working the question out a second time.</para>
    /// <para><b>A sequence, because one value may have two things to do.</b> <c>real? tally =
    /// 3</c> widens the integer and then wraps the result, and either step alone leaves the slot
    /// holding something its type says it does not.</para>
    /// </summary>
    internal void RecordConversion(
        SyntaxNode node,
        IReadOnlyList<(ConversionOperation Operation, TypeSymbol Target)> steps) =>
        _conversions[node] = steps;

    /// <summary>
    /// What a node has to do to reach its place, in the order it has to do it — empty where it
    /// needs nothing. Each step carries the type it produces rather than leaving it to be
    /// derived, since the node's own type is what it was <em>before</em> converting.
    /// </summary>
    public IReadOnlyList<(ConversionOperation Operation, TypeSymbol Target)> GetConversion(
        SyntaxNode node) =>
        _conversions.TryGetValue(
            node, out IReadOnlyList<(ConversionOperation, TypeSymbol)>? found) ? found : [];

    /// <summary>Every type declared anywhere, for tooling and for tests.</summary>
    public IEnumerable<TypeSymbol> AllTypes()
    {
        Stack<NamespaceSymbol> pending = new();
        pending.Push(GlobalNamespace);

        while (pending.Count > 0)
        {
            NamespaceSymbol current = pending.Pop();

            foreach (TypeSymbol type in current.Types.Values)
            {
                yield return type;

                if (type is DeclaredTypeSymbol declared)
                {
                    foreach (TypeSymbol nested in NestedTypes(declared))
                    {
                        yield return nested;
                    }
                }
            }

            foreach (NamespaceSymbol child in current.Namespaces.Values)
            {
                pending.Push(child);
            }
        }

        static IEnumerable<TypeSymbol> NestedTypes(DeclaredTypeSymbol type)
        {
            foreach (List<Symbol> group in type.Members.Values)
            {
                foreach (Symbol member in group)
                {
                    if (member is DeclaredTypeSymbol nested)
                    {
                        yield return nested;

                        foreach (TypeSymbol deeper in NestedTypes(nested))
                        {
                            yield return deeper;
                        }
                    }
                }
            }
        }
    }
}
