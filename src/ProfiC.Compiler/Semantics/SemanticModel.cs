using ProfiC.Compiler.Ast;

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
    private readonly Dictionary<SyntaxNode, (ConversionOperation Operation, TypeSymbol Target)>
        _conversions = [];

    /// <summary>The global namespace, holding everything declared at the top level.</summary>
    public NamespaceSymbol GlobalNamespace { get; } = new(string.Empty, parent: null);

    /// <summary>The entry point, once one has been found.</summary>
    public FunctionSymbol? EntryPoint { get; internal set; }

    /// <summary>Records what a node refers to.</summary>
    internal void Bind(SyntaxNode node, Symbol symbol) => _symbols[node] = symbol;

    /// <summary>Records the type a node denotes.</summary>
    internal void BindType(SyntaxNode node, TypeSymbol type) => _types[node] = type;

    /// <summary>What a node refers to, or null if nothing was resolved for it.</summary>
    public Symbol? GetSymbol(SyntaxNode node) =>
        _symbols.TryGetValue(node, out Symbol? symbol) ? symbol : null;

    /// <summary>The type a node denotes, or null if none was recorded.</summary>
    public TypeSymbol? GetType(SyntaxNode node) =>
        _types.TryGetValue(node, out TypeSymbol? type) ? type : null;

    /// <summary>
    /// <para>Records that a value needs converting where it sits.</para>
    /// <para>Written down by the type checker, because it is the only pass that knows both
    /// what a value is and what is expected of it. Lowering then makes the conversion a real
    /// node rather than working the question out a second time.</para>
    /// </summary>
    internal void RecordConversion(SyntaxNode node, ConversionOperation operation, TypeSymbol target) =>
        _conversions[node] = (operation, target);

    /// <summary>
    /// The conversion a node needs and the type it produces, or null when it needs none. The
    /// target is recorded rather than derived, since the node's own type is what it was
    /// <em>before</em> converting.
    /// </summary>
    public (ConversionOperation Operation, TypeSymbol Target)? GetConversion(SyntaxNode node) =>
        _conversions.TryGetValue(node, out (ConversionOperation, TypeSymbol) found) ? found : null;

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
