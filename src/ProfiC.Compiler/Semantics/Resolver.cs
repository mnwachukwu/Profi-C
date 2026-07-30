using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Text;

namespace ProfiC.Compiler.Semantics;

/// <summary>
/// <para>Works out what every name in a syntax tree refers to.</para>
/// <para>Two passes. The first collects declared types and their members; the second binds
/// the names inside function bodies. Two are needed because types are not ordered — a model
/// may name one declared below it — while the statements inside a body are.</para>
/// <para>The split works because a type cannot be declared inside a function. If one could,
/// the first pass would have to descend into bodies, the symbol table would become a tree, and
/// type lookup would become order-dependent in a way forward references contradict.</para>
/// </summary>
public sealed partial class Resolver
{
    /// <summary>
    /// <para>Every type the language provides. A name here resolves without being declared,
    /// and no program may declare one.</para>
    /// <para>Read from <see cref="BuiltIns"/> rather than listed here, so that the set the
    /// resolver protects and the set the type checker can find members on cannot drift apart.
    /// An exception subtype may still be <em>extended</em>; extending is not redeclaring.</para>
    /// </summary>
    private static IReadOnlySet<string> BuiltInTypeNames => BuiltIns.AllTypeNames;

    /// <summary>The exceptions the language throws itself, all of which extend Exception.</summary>
    private static IReadOnlySet<string> BuiltInExceptionNames => BuiltIns.ExceptionNames;

    private readonly DiagnosticBag _diagnostics;
    private readonly SemanticModel _model = new();

    /// <summary>
    /// The file being collected from, so that a type records where it was declared. Null once
    /// collection is over, since every later pass works on the whole compilation at once.
    /// </summary>
    private SourceText? _currentSource;

    /// <summary>Types declared anywhere, by name, for lookup during the second pass.</summary>
    private readonly Dictionary<string, DeclaredTypeSymbol> _typesByName =
        new(StringComparer.Ordinal);

    /// <summary>The model whose member is being resolved, for <c>this</c> and <c>base</c>.</summary>
    private ModelSymbol? _currentModel;

    /// <summary>The type whose members are being resolved, which may be a structure.</summary>
    private DeclaredTypeSymbol? _currentType;

    /// <summary>True while resolving a member that has no instance, so <c>this</c> is absent.</summary>
    private bool _inGlobalMember;

    /// <summary>The innermost run of locals and parameters.</summary>
    private Scope _scope = new(parent: null);

    private Resolver(DiagnosticBag diagnostics) => _diagnostics = diagnostics;

    /// <summary>
    /// <para>Resolves a compilation unit, reporting into the given bag.</para>
    /// <para><paramref name="requireEntryPoint"/> is off by default, because a file that
    /// declares no <c>Program</c> is perfectly well-formed — it simply is not a whole program.
    /// Demanding an entry point belongs to building an executable, not to checking a file.
    /// The rules <em>about</em> a <c>Program</c> that is present are checked either way.</para>
    /// </summary>
    public static SemanticModel Resolve(
        IReadOnlyList<CompilationUnit> units,
        DiagnosticBag diagnostics,
        bool requireEntryPoint = false)
    {
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(diagnostics);

        Resolver resolver = new(diagnostics);

        // Every file's declarations are collected before any body is bound, which is what lets
        // a file name a type another file declares without regard to the order they arrive in.
        foreach (CompilationUnit unit in units)
        {
            using DiagnosticBag.FileScope reporting = diagnostics.InFile(unit.Source);

            resolver._currentSource = unit.Source;
            resolver.CollectDeclarations(unit);
        }

        resolver._currentSource = null;

        resolver.LinkInheritance();
        resolver.SettleMemberSignatures();

        if (units.Count > 0)
        {
            using DiagnosticBag.FileScope reporting = diagnostics.InFile(units[0].Source);
            resolver.CheckEntryPoint(units[0], requireEntryPoint);
        }

        foreach (CompilationUnit unit in units)
        {
            using DiagnosticBag.FileScope reporting = diagnostics.InFile(unit.Source);
            resolver.BindBodies(unit);
        }

        return resolver._model;
    }

    /// <summary>Resolves one file, which is a compilation of one.</summary>
    public static SemanticModel Resolve(
        CompilationUnit unit,
        DiagnosticBag diagnostics,
        bool requireEntryPoint = false)
    {
        ArgumentNullException.ThrowIfNull(unit);

        return Resolve([unit], diagnostics, requireEntryPoint);
    }

    // ---- Reporting ------------------------------------------------------------------------

    private void Report(DiagnosticDescriptor descriptor, SyntaxNode node, params object?[] args) =>
        _diagnostics.Report(descriptor, node.Span, args);

    private void Report(DiagnosticDescriptor descriptor, SourceSpan span, params object?[] args) =>
        _diagnostics.Report(descriptor, span, args);

    // ---- Scopes ---------------------------------------------------------------------------

    /// <summary>Runs an action inside a nested scope, restoring the previous one after.</summary>
    private void InScope(Action body)
    {
        Scope saved = _scope;
        _scope = _scope.Push();

        try
        {
            body();
        }
        finally
        {
            _scope = saved;
        }
    }

    /// <summary>Declares a local or parameter, reporting a clash within the same scope.</summary>
    private void Declare(Symbol symbol, SyntaxNode node)
    {
        if (_scope.TryDeclare(symbol))
        {
            return;
        }

        // A name that failed to parse is empty, and two of those are not a clash worth
        // reporting — whatever went wrong has already been said once each.
        if (symbol.Name.Length > 0)
        {
            Report(DiagnosticDescriptors.DuplicateDeclaration, node, symbol.Name);
        }
    }

    // ---- Type references --------------------------------------------------------------------

    /// <summary>
    /// Turns a written type into the type it denotes. An unknown name yields the error type,
    /// so a single bad name does not produce a second diagnostic everywhere it is used.
    /// </summary>
    private TypeSymbol ResolveType(TypeSyntax syntax)
    {
        TypeSymbol resolved = ResolveTypeCore(syntax);
        _model.BindType(syntax, resolved);
        return resolved;
    }

    /// <summary>
    /// The type written on a declared function's parameter. Only a lambda may leave one out
    /// for the surrounding code to settle, so a missing one here is a parse failure and reads
    /// as the error type rather than as something to work out.
    /// </summary>
    private TypeSymbol ResolveWrittenType(ParameterDecl parameter) =>
        parameter.Type is null ? ErrorType.Instance : ResolveType(parameter.Type);

    private TypeSymbol ResolveTypeCore(TypeSyntax syntax)
    {
        switch (syntax)
        {
            case MissingType:
                return ErrorType.Instance;

            case SetTypeSyntax set:
                return new SetType(ResolveType(set.ElementType));

            case OptionalTypeSyntax optional:
                return new OptionalType(ResolveType(optional.UnderlyingType));

            case FunctionTypeSyntax function:
                return new FunctionType(
                    function.ReturnType is null ? null : ResolveType(function.ReturnType),
                    [.. function.ParameterTypes.Select(ResolveType)]);

            case NamedTypeSyntax named:
            {
                if (PrimitiveType.ByName.TryGetValue(named.Name, out PrimitiveType? primitive))
                {
                    return primitive;
                }

                if (_typesByName.TryGetValue(named.Name, out DeclaredTypeSymbol? declared))
                {
                    return declared;
                }

                // "Model" is a reserved type name rather than a keyword, so it arrives here
                // as an ordinary identifier and is recognized at this point.
                if (BuiltInTypeNames.Contains(named.Name))
                {
                    return BuiltInModel(named.Name);
                }

                Report(DiagnosticDescriptors.TypeNotFound, named, named.Name);
                return ErrorType.Instance;
            }

            default:
                return ErrorType.Instance;
        }
    }

    /// <summary>The built-in models, created on demand and shared.</summary>
    private readonly Dictionary<string, ModelSymbol> _builtIns = new(StringComparer.Ordinal);

    private ModelSymbol BuiltInModel(string name)
    {
        if (_builtIns.TryGetValue(name, out ModelSymbol? model))
        {
            return model;
        }

        model = new ModelSymbol(name, DeclarationModifiers.Public);
        _builtIns[name] = model;

        // The built-in exceptions really do descend from Exception, so one catch clause takes
        // them all and Message is inherited rather than repeated on each. Recorded before
        // anything asks, and the entry above is already in place, so this cannot recur.
        if (BuiltInExceptionNames.Contains(name))
        {
            model.BaseType = BuiltInModel("Exception");
        }

        // Everything else the language names is a Model, which is what the word means. Said
        // here as well as in Conversions, because a model reaching an ancestor is answered by
        // walking the chain and a built-in with no chain would never reach anything.
        else if (name != "Model")
        {
            model.BaseType = BuiltInModel("Model");
        }

        return model;
    }
}
