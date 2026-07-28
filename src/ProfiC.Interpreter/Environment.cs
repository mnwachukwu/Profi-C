using ProfiC.Compiler.Semantics;

namespace ProfiC.Interpreter;

/// <summary>
/// <para>The variables in scope at a point during execution, and their storage.</para>
/// <para>Keyed by the symbol the resolver produced rather than by name, so shadowing needs no
/// handling here: two variables that share a name are two symbols, and cannot collide.</para>
/// </summary>
public sealed class Environment(Environment? parent)
{
    private readonly Dictionary<Symbol, Cell> _cells = [];

    /// <summary>The enclosing environment, or null at the outermost one.</summary>
    public Environment? Parent { get; } = parent;

    /// <summary>Opens a nested scope.</summary>
    public Environment Push() => new(this);

    /// <summary>Creates storage for a variable here.</summary>
    public Cell Declare(Symbol symbol, object? value)
    {
        Cell cell = new(value);
        _cells[symbol] = cell;
        return cell;
    }

    /// <summary>
    /// Finds a variable's storage, looking outward. Returning the cell rather than the value
    /// is what makes capture by reference work: a lambda holding this cell sees assignments
    /// made through it elsewhere.
    /// </summary>
    public Cell? Lookup(Symbol symbol)
    {
        for (Environment? scope = this; scope is not null; scope = scope.Parent)
        {
            if (scope._cells.TryGetValue(symbol, out Cell? cell))
            {
                return cell;
            }
        }

        return null;
    }
}
