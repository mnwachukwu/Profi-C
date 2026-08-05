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

    /// <summary>
    /// The project the file being collected from belongs to, which becomes the reach of every
    /// <c>internal</c> declared in it. Empty when the driver named no projects, since a
    /// compilation nobody divided is one project.
    /// </summary>
    private string _currentProject = string.Empty;

    /// <summary>Which project each file belongs to, as the driver worked it out.</summary>
    private IReadOnlyDictionary<SourceText, string> _projects =
        new Dictionary<SourceText, string>();

    /// <summary>
    /// Which <c>Program</c> the build begins at, as the project file named it, or null where
    /// nothing said. Only an answer where the sources declare exactly one.
    /// </summary>
    private string? _entryPoint;


    /// <summary>The model whose member is being resolved, for <c>this</c> and <c>base</c>.</summary>
    private ModelSymbol? _currentModel;

    /// <summary>The type whose members are being resolved, which may be a structure.</summary>
    private DeclaredTypeSymbol? _currentType;

    /// <summary>True while resolving a member that has no instance, so <c>this</c> is absent.</summary>
    private bool _inSharedMember;

    /// <summary>
    /// The field whose starting value is being resolved, or null anywhere else. Named rather
    /// than a flag because the field is what a reader is told to move into a constructor.
    /// </summary>
    private string? _initializingField;

    /// <summary>The innermost run of locals and parameters.</summary>
    private Scope _scope = new(parent: null);

    /// <summary>
    /// The file whose bodies are being bound, which is what a recorded scope is filed under. Not
    /// the same question as <c>_lookupFile</c>, which follows the type a name is read from and so
    /// may be a different file than the one being walked.
    /// </summary>
    private SourceText? _currentFile;

    /// <summary>
    /// <para>What stops this partway through, and never signalled when a build asked.</para>
    /// <para>Checked once per declaration and once per statement rather than at every node. That
    /// is fine enough to stop within a fraction of a millisecond on anything a reader is typing,
    /// and coarse enough that the check does not appear on every line of the walk.</para>
    /// </summary>
    private readonly CancellationToken _cancellation;

    private Resolver(DiagnosticBag diagnostics, CancellationToken cancellation)
    {
        _diagnostics = diagnostics;
        _cancellation = cancellation;
    }

    /// <summary>
    /// <para>Resolves a compilation unit, reporting into the given bag.</para>
    /// <para><paramref name="requireEntryPoint"/> is off by default, because a file that
    /// declares no <c>Program</c> is perfectly well-formed — it simply is not a whole program.
    /// Demanding an entry point belongs to building an executable, not to checking a file.
    /// The rules <em>about</em> a <c>Program</c> that is present are checked either way.</para>
    /// <para><paramref name="entryPoint"/> is the <c>Program</c> the build begins at, written
    /// as the project file wrote it. Null when nothing said, which is an answer only where the
    /// sources declare exactly one — with several, the compiler must be told rather than
    /// choose, since choosing would make the result depend on the order files were listed.
    /// </para>
    /// <para><paramref name="cancellation"/> stops the walk where the answer is no longer
    /// wanted, which is what a language server needs when the reader has typed again before this
    /// finished. A build never signals it, and leaves an <see cref="OperationCanceledException"/>
    /// that cannot be thrown.</para>
    /// </summary>
    public static SemanticModel Resolve(
        IReadOnlyList<CompilationUnit> units,
        DiagnosticBag diagnostics,
        bool requireEntryPoint = false,
        IReadOnlyDictionary<SourceText, string>? projects = null,
        string? entryPoint = null,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(diagnostics);

        Resolver resolver = new(diagnostics, cancellation);

        if (projects is not null)
        {
            resolver._projects = projects;
        }

        resolver._entryPoint = entryPoint;

        // Every file's declarations are collected before any body is bound, which is what lets
        // a file name a type another file declares without regard to the order they arrive in.
        foreach (CompilationUnit unit in units)
        {
            using DiagnosticBag.FileScope reporting = diagnostics.InFile(unit.Source);

            resolver._currentSource = unit.Source;

            resolver._currentProject = resolver._projects.TryGetValue(unit.Source, out string? named)
                ? named
                : string.Empty;

            resolver.CollectDeclarations(unit);
        }

        resolver._currentSource = null;
        resolver._currentProject = string.Empty;

        // After every file, because a using may name a namespace that a file read later
        // declares: the directives are unordered with respect to what they reach.
        resolver.ResolveUsings(units);

        resolver.LinkInheritance();
        resolver.SettleMemberSignatures();
        resolver.CheckOverrides();
        resolver.CheckAbstractFunctions();

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
        bool requireEntryPoint = false,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(unit);

        return Resolve([unit], diagnostics, requireEntryPoint, cancellation: cancellation);
    }

    // ---- Reporting ------------------------------------------------------------------------

    private void Report(DiagnosticDescriptor descriptor, SyntaxNode node, params object?[] args) =>
        _diagnostics.Report(descriptor, node.Span, args);

    private void Report(DiagnosticDescriptor descriptor, SourceSpan span, params object?[] args) =>
        _diagnostics.Report(descriptor, span, args);

    // ---- Scopes ---------------------------------------------------------------------------

    /// <summary>
    /// <para>Runs an action inside a nested scope, restoring the previous one after.</para>
    /// <para>The span is the stretch of source the new scope governs, written down so that
    /// something asking later — an editor, about a cursor — can find which names were in force
    /// where. Null where the stretch cannot be named, which is only an empty body: one declares
    /// nothing, so the scope around it holds exactly the same names.</para>
    /// </summary>
    private void InScope(SourceSpan? covering, Action body)
    {
        Scope saved = _scope;
        _scope = _scope.Push();

        if (covering is { } stretch && _currentFile is { } file)
        {
            _model.Opened(
                file,
                stretch,
                new NameScope(
                    _scope,
                    Here,
                    _lookupFile is not null && _fileUsings.TryGetValue(_lookupFile, out List<NamespaceSymbol>? used)
                        ? used
                        : [],
                    _nestedTypes,
                    _currentType,
                    _inSharedMember));
        }

        try
        {
            body();
        }
        finally
        {
            _scope = saved;
        }
    }

    /// <summary>
    /// The stretch a run of statements covers, or null where there are none. Taken from the
    /// statements themselves rather than from what encloses them, because one <c>if</c> holds
    /// two runs and a span covering the whole statement could not tell them apart.
    /// </summary>
    private static SourceSpan? SpanOver(IReadOnlyList<Statement>? statements)
    {
        if (statements is null or { Count: 0 })
        {
            return null;
        }

        SourcePosition start = statements[0].Span.Start;

        return new SourceSpan(start, statements[^1].Span.EndOffset - start.Offset);
    }

    /// <summary>
    /// <para>Declares a local or parameter, reporting a name already taken.</para>
    /// <para>Two ways it can be taken, and they are told apart because the fixes differ. A
    /// second declaration in the same scope is a duplicate. One that hides a name from a scope
    /// around it is a shadow, which the language forbids so that a bare name means one thing
    /// throughout a function body.</para>
    /// <para>Only locals and parameters live in a scope, so a local named after a field is
    /// neither: that is what <c>this.</c> distinguishes.</para>
    /// </summary>
    private void Declare(Symbol symbol, SyntaxNode node)
    {
        // A throwaway is left out of the scope, which is what makes several of them ordinary
        // and reading one impossible. The symbol is still made and still bound to the
        // declaration, so what it was given is still typed and still evaluated; it simply
        // answers to no name.
        if (Throwaway.Is(symbol.Name))
        {
            return;
        }

        // A name that failed to parse is empty, and two of those are not a clash worth
        // reporting — whatever went wrong has already been said once each.
        bool named = symbol.Name.Length > 0;

        if (!_scope.TryDeclare(symbol))
        {
            if (named)
            {
                Report(DiagnosticDescriptors.DuplicateDeclaration, node, symbol.Name);
            }

            return;
        }

        // The name is declared either way, so the body binds to what the reader wrote rather
        // than to whatever it would have hidden.
        if (named && _scope.Parent?.Lookup(symbol.Name) is not null)
        {
            Report(DiagnosticDescriptors.NameShadowsEnclosing, node, symbol.Name);
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

        // A written type is a use of a name, and is bound like every other use of one. Recording
        // only the type it came to would answer what the thing is and not which declaration it
        // reached — so the type on the left of a declaration, and the one after 'as', could not
        // be followed, renamed, marked, or read the documentation of.
        //
        // Only where a name was written. A set, an optional and a delegate are built out of types
        // rather than being one that was named, and their parts are bound as they are resolved.
        if (syntax is NamedTypeSyntax && resolved is DeclaredTypeSymbol declared)
        {
            _model.Bind(syntax, declared);
        }

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
            {
                TypeSymbol underlying = ResolveType(optional.UnderlyingType);

                // An optional already says a value may be absent. Saying it twice describes
                // nothing a program can act on — and the two ways of being empty it would create
                // cannot be told apart by any member an optional has.
                if (underlying is OptionalType)
                {
                    Report(DiagnosticDescriptors.OptionalOfAnOptional, optional);
                    return underlying;
                }

                return new OptionalType(underlying);
            }

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

                if (LookupQualifiedType(named.Parts) is { } declared)
                {
                    RequireVisibleType(named, declared);
                    return RequireInhabitable(named, declared)
                        ? declared
                        : ErrorType.Instance;
                }

                if (ReportIfAmbiguous(named, named.Text))
                {
                    return ErrorType.Instance;
                }

                Report(DiagnosticDescriptors.TypeNotFound, named, named.Text);
                return ErrorType.Instance;
            }

            default:
                return ErrorType.Instance;
        }
    }

    /// <summary>
    /// <para>Rejects a type nothing can be, written where a value's type belongs.</para>
    /// <para>Two kinds reach here. A <c>shared model</c> has no instances by definition, which
    /// is what the word means. And four the language provides are names to reach members
    /// through — <c>Console</c>, <c>Math</c>, <c>Reference</c>, and <c>Fraction</c>, which is
    /// the model beside the <c>fraction</c> type rather than that type.</para>
    /// <para>Without this each of them is a declaration every rule accepts and no value can
    /// ever fill, which runs.</para>
    /// <para>Answers whether the type may be written here, so that one that may not reads as
    /// the error type. The declaration is then wrong in exactly one way rather than two: the
    /// message already names the type to write instead, and "a fraction does not fit a
    /// Fraction" on the line below only obscures it.</para>
    /// </summary>
    private bool RequireInhabitable(NamedTypeSyntax named, DeclaredTypeSymbol declared)
    {
        bool empty = declared is ModelSymbol { IsShared: true }
                     || (ReferenceEquals(declared.Container, BuiltInTypes.Standard)
                         && BuiltIns.HasNoInstances(declared.Name));

        if (!empty)
        {
            return true;
        }

        // A capital letter away from the type that was almost certainly meant.
        string fix = PrimitiveType.ByName.ContainsKey(declared.Name.ToLowerInvariant())
            ? $"Write '{declared.Name.ToLowerInvariant()}' for the type of that name."
            : $"Its members are reached through the name '{declared.Name}' instead.";

        Report(DiagnosticDescriptors.TypeHasNoValues, named, declared.Name, fix);
        return false;
    }

    /// <summary>
    /// <para>Rejects a type named from outside the project that declares it.</para>
    /// <para>Only <c>internal</c> can fail: a type is internal or public, and public is
    /// everywhere. The project doing the naming is the one the enclosing type belongs to,
    /// falling back to the file being collected from, which covers a name written before any
    /// type has been entered.</para>
    /// <para>Called from every place a type name is read — in a signature, as the receiver of
    /// a shared member, and after <c>new</c> — because a boundary that holds in one of the
    /// three and not the others is not a boundary.</para>
    /// </summary>
    private void RequireVisibleType(SyntaxNode where, DeclaredTypeSymbol declared)
    {
        if (declared.Visibility == Visibility.Public)
        {
            return;
        }

        string here = _currentType?.Project ?? _currentProject;

        if (string.Equals(here, declared.Project, StringComparison.Ordinal))
        {
            return;
        }

        Report(
            DiagnosticDescriptors.TypeIsNotVisible,
            where,
            declared.Name,
            TypeChecker.ProjectName(declared.Project),
            TypeChecker.ProjectName(here));
    }

    /// <summary>
    /// <para>The symbol for a built-in type.</para>
    /// <para>Taken from the registry every compilation shares rather than made here, because
    /// two types are the same type when they are the same object: the catalog's signatures
    /// name these symbols too, and a DateTime made here would not be the DateTime a member
    /// says it yields.</para>
    /// </summary>
    private static ModelSymbol BuiltInModel(string name) => BuiltInTypes.Of(name);
}
