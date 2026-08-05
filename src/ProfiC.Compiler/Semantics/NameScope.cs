namespace ProfiC.Compiler.Semantics;

/// <summary>
/// <para>Everything a bare name could reach from one place in a program.</para>
/// <para><b>Recorded while resolving rather than worked out afterwards.</b> Which names are in
/// force somewhere depends on the chain of scopes the resolver was holding when it got there, the
/// namespace the enclosing type sits in, and what that file wrote <c>using</c> of — none of which
/// survives the pass. Reconstructing it later from the syntax would be a second account of scope,
/// and the two would agree until the day they did not.</para>
/// <para>This is the set of names, not what any one of them means. Two namespaces may each offer
/// a <c>Circle</c>: both make the name worth offering, and which one a program that writes it
/// gets is settled by the resolver when it binds it, exactly as it would be for a name typed by
/// hand.</para>
/// <para>Nothing here is copied. A scope holds references to the chain, the namespace and the
/// lists the resolver was already keeping, so recording one costs a handful of fields however
/// large the program is. Which is why every compilation records them rather than only the ones an
/// editor asked for: on a 48,000-line file with 28,000 scopes in it the difference does not rise
/// out of the noise, and a flag to turn it off would be two paths through the resolver to save
/// nothing measurable.</para>
/// </summary>
public sealed class NameScope
{
    private readonly Scope _locals;
    private readonly NamespaceSymbol _here;
    private readonly IReadOnlyList<NamespaceSymbol> _usings;

    internal NameScope(
        Scope locals,
        NamespaceSymbol here,
        IReadOnlyList<NamespaceSymbol> usings,
        DeclaredTypeSymbol? enclosingType,
        bool inSharedMember)
    {
        _locals = locals;
        _here = here;
        _usings = usings;
        EnclosingType = enclosingType;
        InSharedMember = inSharedMember;
    }

    /// <summary>The type whose body this sits in, or null at the top of a file.</summary>
    public DeclaredTypeSymbol? EnclosingType { get; }

    /// <summary>
    /// Whether the member this sits in is shared, which decides whether <c>this</c> means
    /// anything here.
    /// </summary>
    public bool InSharedMember { get; }

    /// <summary>
    /// <para>Every name reachable here, nearest first and each appearing once.</para>
    /// <para>In the order a lookup would try them: the chain of locals outward, then the
    /// namespace this sits in and every namespace around it, then the ones this file wrote
    /// <c>using</c> of, then <c>Standard</c>, then types nested inside another type, then the
    /// names the language provides. A name found near shadows the same name found far, which is
    /// why first wins rather than last.</para>
    /// </summary>
    public IEnumerable<Symbol> Visible()
    {
        HashSet<string> already = new(StringComparer.Ordinal);

        for (Scope? scope = _locals; scope is not null; scope = scope.Parent)
        {
            foreach (Symbol local in scope.Declared)
            {
                if (already.Add(local.Name))
                {
                    yield return local;
                }
            }
        }

        foreach (NamespaceSymbol scope in Reachable())
        {
            foreach (TypeSymbol type in scope.Types.Values)
            {
                if (type is DeclaredTypeSymbol declared && already.Add(declared.Name))
                {
                    yield return declared;
                }
            }
        }

        // A type nested inside another answers to a bare name only from inside its container,
        // so only those are offered — the same walk outward the resolver makes when it reads
        // one. Offering every nested type in the program would suggest names that do not
        // resolve, which is worse than suggesting nothing.
        for (Symbol? scope = EnclosingType; scope is DeclaredTypeSymbol holding;
             scope = holding.Container)
        {
            foreach (DeclaredTypeSymbol nested in
                     holding.Members.Values.SelectMany(members => members)
                            .OfType<DeclaredTypeSymbol>())
            {
                if (already.Add(nested.Name))
                {
                    yield return nested;
                }
            }
        }

        foreach (string name in BuiltIns.AllTypeNames)
        {
            if (already.Add(name))
            {
                yield return BuiltInTypes.Of(name);
            }
        }
    }

    /// <summary>
    /// Every namespace a bare name is looked for in, nearest first: outward from where this sits,
    /// then whatever the file imported, then <c>Standard</c>, which needs no import and is never
    /// absent.
    /// </summary>
    private IEnumerable<NamespaceSymbol> Reachable()
    {
        for (NamespaceSymbol? scope = _here; scope is not null; scope = scope.Parent)
        {
            yield return scope;
        }

        foreach (NamespaceSymbol scope in _usings)
        {
            yield return scope;
        }

        yield return BuiltInTypes.Standard;
    }
}
