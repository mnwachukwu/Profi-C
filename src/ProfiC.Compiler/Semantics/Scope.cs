namespace ProfiC.Compiler.Semantics;

/// <summary>
/// <para>A nested run of local names: a function body, a loop, a block.</para>
/// <para>Only locals and parameters live here. Fields and shared members are reached through
/// <c>this.</c> or a type name, never by a bare identifier, which is what collapses name
/// lookup from the usual five-level search to two: this chain, then nothing.</para>
/// </summary>
public sealed class Scope(Scope? parent)
{
    private readonly Dictionary<string, Symbol> _symbols = new(StringComparer.Ordinal);

    /// <summary>The enclosing scope, or null for a function's outermost one.</summary>
    public Scope? Parent { get; } = parent;

    /// <summary>Opens a nested scope.</summary>
    public Scope Push() => new(this);

    /// <summary>
    /// Declares a name here. Returns false if this scope already has one. A name taken by a
    /// scope further out still returns true, since the two are different mistakes with
    /// different fixes; the caller looks outward itself and reports accordingly.
    /// </summary>
    public bool TryDeclare(Symbol symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        return _symbols.TryAdd(symbol.Name, symbol);
    }

    /// <summary>Looks a name up here, then outward.</summary>
    public Symbol? Lookup(string name)
    {
        for (Scope? scope = this; scope is not null; scope = scope.Parent)
        {
            if (scope._symbols.TryGetValue(name, out Symbol? found))
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>Names declared directly in this scope.</summary>
    public IReadOnlyCollection<Symbol> Declared => _symbols.Values;
}
