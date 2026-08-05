using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Text;

namespace ProfiC.Compiler.Semantics;

public sealed partial class Resolver
{
    /// <summary>
    /// <para>Every type the compilation declares, in the order they were collected.</para>
    /// <para>A list rather than a map by name, because two namespaces may each declare a
    /// <c>Circle</c> and a map by name could hold only one of them. Which name reaches which
    /// type is <see cref="LookupType"/>'s question; this is only the set of all of them, for
    /// the passes that visit every type regardless of what it is called.</para>
    /// </summary>
    private readonly List<DeclaredTypeSymbol> _allTypes = [];

    /// <summary>The namespace a name is currently being read from.</summary>
    private NamespaceSymbol? _lookupNamespace;

    /// <summary>The file a name is currently being read from, which decides its usings.</summary>
    private SourceText? _lookupFile;

    /// <summary>The namespaces each file's <c>using</c> directives name.</summary>
    private readonly Dictionary<SourceText, List<NamespaceSymbol>> _fileUsings = [];

    /// <summary>Where a name is read from when nothing narrower has been entered.</summary>
    private NamespaceSymbol Here => _lookupNamespace ?? _model.GlobalNamespace;

    /// <summary>
    /// <para>Finds the type a bare name refers to, from where the name was written.</para>
    /// <para>Three places, in order. The namespace the name sits in and every namespace around
    /// it, ending at the global one — so a type reaches its neighbors without qualifying them,
    /// and reaches anything declared outside without a <c>using</c>. Then the namespaces this
    /// file said <c>using</c> of. Then types nested inside another type.</para>
    /// <para>Nearest wins, which is why the walk is outward rather than a single flat search: a
    /// <c>Circle</c> beside you is the one you meant, whatever else in the program shares the
    /// name. Only a tie among usings is ambiguous, because those name no order between them.
    /// </para>
    /// </summary>
    private DeclaredTypeSymbol? LookupType(string name)
    {
        for (NamespaceSymbol? scope = Here; scope is not null; scope = scope.Parent)
        {
            if (scope.Types.TryGetValue(name, out TypeSymbol? found)
                && found is DeclaredTypeSymbol declared)
            {
                return declared;
            }
        }

        if (LookupThroughUsings(name) is { } imported)
        {
            return imported;
        }

        return NestedInScope(name);
    }

    /// <summary>
    /// <para>A type nested inside the one being read from, or inside a type around that.</para>
    /// <para>Walked outward from where the name was written, so a type declared inside
    /// <c>Shape</c> is a bare name to <c>Shape</c>'s own code and to anything nested deeper
    /// inside it — and to nothing else. From outside, the container has to be named:
    /// <c>Shape.Corner</c>.</para>
    /// <para>That is what nesting is for. A flat map of every nested type by bare name, which
    /// is what stood here, made nesting a way of writing a top-level type indented: the name
    /// reached across the whole program, two containers could not each hold a <c>Node</c>, and
    /// writing the container's name did not work at all.</para>
    /// </summary>
    private DeclaredTypeSymbol? NestedInScope(string name)
    {
        for (Symbol? scope = _currentType; scope is DeclaredTypeSymbol holding;
             scope = holding.Container)
        {
            if (NestedTypeIn(holding, name) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// <para>Finds a type among the namespaces this file wrote <c>using</c> of.</para>
    /// <para>Two usings offering the same name is a tie, and nothing breaks it: neither is
    /// nearer than the other, so choosing either would make which one a program meant depend
    /// on the order they were written. That is reported at the name rather than at the usings,
    /// since two namespaces sharing a name is only a problem once something needs it.</para>
    /// </summary>
    private DeclaredTypeSymbol? LookupThroughUsings(string name)
    {
        DeclaredTypeSymbol? single = null;
        List<DeclaredTypeSymbol>? several = null;

        // Standard sits here rather than below, so that a using offering one of its names is a
        // tie to be reported instead of a silent replacement. Nothing collides with it today,
        // which is exactly why the rule is worth fixing now: it costs nothing until .NET types
        // can be named, and by then a bare name would already have changed meaning quietly.
        foreach (NamespaceSymbol scope in InScopeNamespaces())
        {
            if (!scope.Types.TryGetValue(name, out TypeSymbol? candidate)
                || candidate is not DeclaredTypeSymbol found)
            {
                continue;
            }

            if (single is null)
            {
                single = found;
                continue;
            }

            // Two names for the same type is one answer, which a namespace reached by more
            // than one using produces.
            if (ReferenceEquals(single, found))
            {
                continue;
            }

            several ??= [single];
            several.Add(found);
        }

        if (several is not null)
        {
            _ambiguous = several;
            return null;
        }

        return single;
    }

    /// <summary>
    /// <para>Finds the type a dotted name refers to, from where the name was written.</para>
    /// <para>Everything before the last part names where to look, and is found the way a bare
    /// name is — beside this declaration, then outward, then among what is in scope. So
    /// <c>Shapes.Circle</c> written inside <c>Tour</c> reaches <c>Tour.Shapes</c> if there is
    /// one and the top-level <c>Shapes</c> otherwise. Nearest wins, as everywhere else.</para>
    /// </summary>
    private DeclaredTypeSymbol? LookupQualifiedType(IReadOnlyList<string> parts)
    {
        if (parts.Count == 1)
        {
            return LookupType(parts[0]);
        }

        string name = parts[^1];

        foreach (NamespaceSymbol scope in QualifyingNamespaces([.. parts.Take(parts.Count - 1)]))
        {
            if (scope.Types.TryGetValue(name, out TypeSymbol? found)
                && found is DeclaredTypeSymbol declared)
            {
                return declared;
            }
        }

        return LookupThroughTypes(parts);
    }

    /// <summary>
    /// <para>A dotted name whose prefix is a type rather than a namespace, which is how a type
    /// declared inside another is reached from outside it.</para>
    /// <para>The split is looked for from the right, so the longest prefix that names a type
    /// wins and <c>Tools.Shapes.Square.Corner</c> reads as far into namespaces as it can before
    /// it starts reading into types. The prefix goes back through the whole of qualified lookup
    /// rather than through namespaces alone, which is what lets types nest more than one deep.
    /// </para>
    /// </summary>
    private DeclaredTypeSymbol? LookupThroughTypes(IReadOnlyList<string> parts)
    {
        for (int split = parts.Count - 1; split >= 1; split--)
        {
            if (LookupQualifiedType([.. parts.Take(split)]) is not { } holding)
            {
                continue;
            }

            DeclaredTypeSymbol? reached = holding;

            for (int index = split; index < parts.Count && reached is not null; index++)
            {
                reached = NestedTypeIn(reached, parts[index]);
            }

            if (reached is not null)
            {
                return reached;
            }
        }

        return null;
    }

    /// <summary>
    /// <para>A type declared directly inside another, by name.</para>
    /// <para>Only what the type itself declares. A nested type is not inherited: extending a
    /// model says what its instances can do, and the types written inside it are not part of
    /// that — the same answer C# gives.</para>
    /// </summary>
    private static DeclaredTypeSymbol? NestedTypeIn(DeclaredTypeSymbol owner, string name) =>
        owner.Lookup(name).OfType<DeclaredTypeSymbol>().FirstOrDefault();

    /// <summary>
    /// <para>Every namespace a written prefix could name, nearest first.</para>
    /// <para>The first part is looked for as any name is; the rest is walked down from
    /// whichever place answered. A prefix naming one of the namespaces in scope answers too,
    /// which is what lets <c>Standard.Math</c> be written with nothing imported.</para>
    /// </summary>
    private IEnumerable<NamespaceSymbol> QualifyingNamespaces(IReadOnlyList<string> prefix)
    {
        string head = prefix[0];
        List<string> rest = [.. prefix.Skip(1)];

        for (NamespaceSymbol? scope = Here; scope is not null; scope = scope.Parent)
        {
            if (scope.Namespaces.TryGetValue(head, out NamespaceSymbol? nested)
                && WalkFrom(nested, rest) is { } reached)
            {
                yield return reached;
            }
        }

        foreach (NamespaceSymbol scope in InScopeNamespaces())
        {
            if (string.Equals(scope.Name, head, StringComparison.Ordinal)
                && WalkFrom(scope, rest) is { } named)
            {
                yield return named;
            }

            if (scope.Namespaces.TryGetValue(head, out NamespaceSymbol? nested)
                && WalkFrom(nested, rest) is { } under)
            {
                yield return under;
            }
        }
    }

    /// <summary>
    /// <para>Every namespace a bare name is looked for in after the walk outward fails: the
    /// ones this file wrote <c>using</c> of, and <c>Standard</c>.</para>
    /// <para>Standard needs no <c>using</c> and is never absent, which is what makes the
    /// library reachable from a file that says nothing. It ranks with the others rather than
    /// beneath them, so nothing can quietly take one of its names.</para>
    /// </summary>
    private IEnumerable<NamespaceSymbol> InScopeNamespaces()
    {
        if (_lookupFile is not null
            && _fileUsings.TryGetValue(_lookupFile, out List<NamespaceSymbol>? used))
        {
            foreach (NamespaceSymbol scope in used)
            {
                yield return scope;
            }
        }

        yield return BuiltInTypes.Standard;
    }

    /// <summary>
    /// The types a name matched in more than one used namespace, set by the lookup that found
    /// them so the caller can say which they were. Cleared by whoever reports it.
    /// </summary>
    private List<DeclaredTypeSymbol>? _ambiguous;

    /// <summary>
    /// <para>Reports a name that more than one <c>using</c> offered, if the last lookup found
    /// one, and gives back whether it did.</para>
    /// <para>Kept apart from the lookup so that every place a type name is read reports it the
    /// same way, against whatever node that place has to point at.</para>
    /// </summary>
    private bool ReportIfAmbiguous(SyntaxNode where, string name)
    {
        if (_ambiguous is not { } candidates)
        {
            return false;
        }

        _ambiguous = null;

        Report(
            DiagnosticDescriptors.AmbiguousTypeName,
            where,
            name,
            string.Join(" and ", candidates.Select(c => NameOf(c))));

        return true;
    }

    /// <summary>A type's full name, for a message that has to tell two of them apart.</summary>
    private static string NameOf(DeclaredTypeSymbol type) =>
        NamespaceOf(type) is { } scope && scope.FullName.Length > 0
            ? $"{scope.FullName}.{type.Name}"
            : type.Name;

    /// <summary>
    /// The namespace a type sits in, looking past any types it is nested inside. A nested type
    /// belongs to the namespace of whatever it is nested in, however deep that goes.
    /// </summary>
    private static NamespaceSymbol? NamespaceOf(DeclaredTypeSymbol? type)
    {
        for (Symbol? container = type?.Container; container is not null;)
        {
            if (container is NamespaceSymbol scope)
            {
                return scope;
            }

            container = container is DeclaredTypeSymbol outer ? outer.Container : null;
        }

        return null;
    }

    /// <summary>
    /// <para>Enters a type, so that names read from inside it are read from where it sits.</para>
    /// <para>Returns what was entered before, for the caller to restore.</para>
    /// </summary>
    private (NamespaceSymbol? Scope, SourceText? File) EnterTypeContext(DeclaredTypeSymbol type)
    {
        (NamespaceSymbol? Scope, SourceText? File) saved = (_lookupNamespace, _lookupFile);

        _lookupNamespace = NamespaceOf(type);
        _lookupFile = type.DeclaredIn ?? saved.File;

        return saved;
    }

    private void RestoreContext((NamespaceSymbol? Scope, SourceText? File) saved) =>
        (_lookupNamespace, _lookupFile) = saved;

    /// <summary>
    /// <para>Reads every file's <c>using</c> directives into the namespaces they name.</para>
    /// <para>Runs after every file has been collected, because a using may name a namespace
    /// that a file read later declares — the directives are unordered with respect to the
    /// declarations they reach, which is the whole point of them.</para>
    /// </summary>
    private void ResolveUsings(IReadOnlyList<CompilationUnit> units)
    {
        foreach (CompilationUnit unit in units)
        {
            if (unit.Usings.Count == 0)
            {
                continue;
            }

            using DiagnosticBag.FileScope reporting = _diagnostics.InFile(unit.Source);

            List<NamespaceSymbol> used = [];

            foreach (UsingDirective directive in unit.Usings)
            {
                // Standard is in scope already, and at the very rank a using would give it, so
                // this line changes no name in the file. Said rather than refused: it is not
                // wrong, only empty, and a reader who wrote it meant something reasonable.
                if (directive.Name.Parts is [BuiltInTypes.StandardName])
                {
                    Report(DiagnosticDescriptors.StandardNeedsNoUsing, directive);
                    continue;
                }

                if (FindNamespace(directive.Name.Parts) is not { } scope)
                {
                    Report(
                        DiagnosticDescriptors.NamespaceNotFound,
                        directive,
                        directive.Name.Text);

                    continue;
                }

                if (used.Contains(scope))
                {
                    Report(
                        DiagnosticDescriptors.NamespaceUsedTwice,
                        directive,
                        directive.Name.Text);

                    continue;
                }

                used.Add(scope);
            }

            _fileUsings[unit.Source] = used;
        }
    }

    /// <summary>
    /// Walks a dotted name down from the global namespace, or gives back null where no such
    /// namespace was declared. Only a whole path counts: <c>Shapes.Flat</c> is not found by
    /// there being a <c>Shapes</c>.
    /// </summary>
    private NamespaceSymbol? FindNamespace(IReadOnlyList<string> parts)
    {
        if (parts is [BuiltInTypes.StandardName, ..])
        {
            return WalkFrom(BuiltInTypes.Standard, parts.Skip(1));
        }

        return WalkFrom(_model.GlobalNamespace, parts);
    }

    private static NamespaceSymbol? WalkFrom(NamespaceSymbol from, IEnumerable<string> parts)
    {
        NamespaceSymbol current = from;

        foreach (string part in parts)
        {
            if (!current.Namespaces.TryGetValue(part, out NamespaceSymbol? child))
            {
                return null;
            }

            current = child;
        }

        return current;
    }
}
