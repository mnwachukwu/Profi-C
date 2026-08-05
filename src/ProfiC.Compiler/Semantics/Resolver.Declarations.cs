using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;

namespace ProfiC.Compiler.Semantics;

public sealed partial class Resolver
{
    /// <summary>
    /// The first pass: record every declared type and its members, without looking inside any
    /// function body. Nothing in a body can declare a type, which is what makes this possible.
    /// </summary>
    private void CollectDeclarations(CompilationUnit unit)
    {
        foreach (Declaration declaration in unit.Declarations)
        {
            _cancellation.ThrowIfCancellationRequested();
            CollectDeclaration(declaration, _model.GlobalNamespace);
        }
    }

    private void CollectDeclaration(Declaration declaration, NamespaceSymbol enclosing)
    {
        switch (declaration)
        {
            case NamespaceDecl namespaceDecl:
            {
                NamespaceSymbol target = DeclareNamespace(namespaceDecl.Name, enclosing);
                NamespaceSymbol? saved = _lookupNamespace;
                _lookupNamespace = target;

                foreach (Declaration member in namespaceDecl.Declarations)
                {
                    CollectDeclaration(member, target);
                }

                _lookupNamespace = saved;
                break;
            }

            case ModelDecl model:
                DeclareType(new ModelSymbol(model.Name, model.Modifiers) { Declaration = model },
                            model.Name, model, enclosing, model.Members);
                break;

            case StructureDecl structure:
                DeclareType(
                    new StructureSymbol(structure.Name, structure.Modifiers) { Declaration = structure },
                    structure.Name, structure, enclosing, structure.Members);
                break;

            case EnumerationDecl enumeration:
                CollectEnumeration(enumeration, enclosing);
                break;
        }
    }

    /// <summary>
    /// Whether a namespace of this name already sits somewhere around this one. The global
    /// namespace is nameless and is skipped, since nothing can repeat an empty name.
    /// </summary>
    private static bool RepeatsAnEnclosingName(NamespaceSymbol enclosing, string part)
    {
        for (NamespaceSymbol? scope = enclosing; scope is not null; scope = scope.Parent)
        {
            if (scope.Name.Length > 0 && string.Equals(scope.Name, part, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Says when a declaration takes a name the language also uses. A warning rather than a
    /// refusal: the nearer name wins, which is the ordinary rule, and Standard.X still reaches
    /// what was shadowed.
    /// </summary>
    private void WarnIfShadowsStandard(string name, Declaration declaration)
    {
        if (BuiltInTypeNames.Contains(name))
        {
            Report(DiagnosticDescriptors.ShadowsStandardType, declaration, name);
        }
    }

    /// <summary>
    /// <para>Refuses a throwaway naming something that is reached by name.</para>
    /// <para>A throwaway is for a binding the program does not want to keep, which only makes
    /// sense where nothing needs to reach it again. A field, a function, a type or an
    /// enumeration member is reached by writing its name, so one called <c>_</c> could never
    /// be used at all.</para>
    /// </summary>
    private void RefuseThrowawayAsAName(string name, SyntaxNode declaration, string what)
    {
        if (Throwaway.Is(name))
        {
            Report(DiagnosticDescriptors.ThrowawayCannotName, declaration, what);
        }
    }

    /// <summary>
    /// <para>Refuses a throwaway written as a parameter.</para>
    /// <para>Called where the parameters are first read rather than where they are put into a
    /// scope, so that each is reported once and an abstract function — which has parameters and
    /// no body to bind them into — is covered like any other.</para>
    /// </summary>
    private void RefuseThrowawayParameters(IReadOnlyList<ParameterDecl> parameters)
    {
        foreach (ParameterDecl parameter in parameters)
        {
            if (Throwaway.Is(parameter.Name))
            {
                Report(DiagnosticDescriptors.ThrowawayCannotBeAParameter, parameter);
            }
        }
    }

    private NamespaceSymbol DeclareNamespace(QualifiedName name, NamespaceSymbol enclosing)
    {
        // Namespaces merge, so a program writing this one would be adding types that then read
        // as the language's own. Refused at the root only: a Standard nested inside something
        // else is an ordinary name that happens to be spelled the same.
        if (ReferenceEquals(enclosing, _model.GlobalNamespace)
            && name.Parts is [BuiltInTypes.StandardName, ..])
        {
            Report(DiagnosticDescriptors.StandardNamespaceIsReserved, name);
        }

        NamespaceSymbol current = enclosing;

        foreach (string part in name.Parts)
        {
            // Said before the namespace is made, so that a dotted name repeating a part of
            // itself is caught as readily as one repeating what it is written inside.
            if (RepeatsAnEnclosingName(current, part))
            {
                Report(DiagnosticDescriptors.NamespaceRepeatsEnclosingName, name, part);
            }

            RefuseThrowawayAsAName(part, name, "a namespace");

            if (!current.Namespaces.TryGetValue(part, out NamespaceSymbol? child))
            {
                child = new NamespaceSymbol(part, current);
                current.Namespaces[part] = child;
            }

            current = child;
        }

        return current;
    }

    /// <summary>
    /// Reports a second type of a name already taken, saying where the first one is. A reader
    /// looking at the second declaration cannot see the first, and in a compilation of several
    /// files it may not even be open.
    /// </summary>
    private void ReportDuplicateType(string name, Declaration declaration, TypeSymbol first)
    {
        // Named by file rather than by path: the diagnostic's own location already carries the
        // path of the file being read, and a second full path in the sentence buries the line
        // number that is the useful part.
        string where = first is DeclaredTypeSymbol { Declaration: { } written } declared
            ? declared.DeclaredIn is { } file && !ReferenceEquals(file, _currentSource)
                ? $"in {Path.GetFileName(file.FileName)}, on line {written.Span.Start.Line}"
                : $"on line {written.Span.Start.Line}"
            : "elsewhere in this compilation";

        Report(DiagnosticDescriptors.DuplicateTypeDeclaration, declaration, name, where);
    }

    /// <summary>Records a model or structure, then its members.</summary>
    private void DeclareType(
        DeclaredTypeSymbol symbol,
        string name,
        Declaration declaration,
        NamespaceSymbol enclosing,
        IReadOnlyList<Declaration> members)
    {
        WarnIfShadowsStandard(name, declaration);
        RefuseThrowawayAsAName(name, declaration, $"a {symbol.Kind}");

        if (!enclosing.Types.TryAdd(name, symbol))
        {
            ReportDuplicateType(name, declaration, enclosing.Types[name]);
            return;
        }

        symbol.Container = enclosing;
        symbol.DeclaredIn = _currentSource;
        symbol.Project = _currentProject;
        _allTypes.Add(symbol);
        _model.Bind(declaration, symbol);

        CheckTypeModifiers(symbol, declaration);
        CollectMembers(symbol, members, enclosing);
    }

    /// <summary>Rejects modifier combinations that cannot mean anything.</summary>
    private void CheckTypeModifiers(DeclaredTypeSymbol symbol, Declaration declaration)
    {
        CheckVisibilityWords(symbol.Modifiers, symbol.Name, declaration);

        // Protected names a line of descent from the type that declares the member. A type has
        // no declaring type, so there is no line for the word to name.
        if (symbol.Modifiers.Has(DeclarationModifiers.Protected))
        {
            Report(DiagnosticDescriptors.TypeCannotBeProtected, declaration, symbol.Name);
        }

        if (symbol is ModelSymbol { IsSealed: true, IsAbstract: true } model)
        {
            Report(DiagnosticDescriptors.SealedAndAbstract, declaration, model.Name);
        }
    }

    /// <summary>
    /// Rejects a declaration written with two visibilities. Each names a different reach, so
    /// two of them say two things about one declaration and neither can be the one meant.
    /// </summary>
    private void CheckVisibilityWords(
        DeclarationModifiers modifiers,
        string name,
        Declaration declaration)
    {
        DeclarationModifiers written = modifiers & VisibilityExtensions.Words;

        if (int.PopCount((int)written) > 1)
        {
            Report(
                DiagnosticDescriptors.ConflictingVisibility,
                declaration,
                name,
                written.ToDisplayString().Replace(" ", " and ", StringComparison.Ordinal));
        }
    }

    private void CollectMembers(
        DeclaredTypeSymbol owner,
        IReadOnlyList<Declaration> members,
        NamespaceSymbol enclosing)
    {
        bool ownerIsShared = owner is ModelSymbol { IsShared: true };

        foreach (Declaration member in members)
        {
            switch (member)
            {
                case FieldDecl field:
                {
                    FieldSymbol symbol = new(
                        field.Name,
                        ResolveTypePlaceholder(field.Type),
                        EffectiveModifiers(field.Modifiers, ownerIsShared))
                    {
                        Declaration = field,
                    };

                    CheckVisibilityWords(field.Modifiers, field.Name, field);
                    RefuseThrowawayAsAName(field.Name, field, $"a {symbol.Kind}");
                    owner.AddMember(symbol);
                    _model.Bind(field, symbol);
                    break;
                }

                case FunctionDecl function:
                {
                    FunctionSymbol symbol = new(
                        function.Name,
                        function.ReturnType is null ? null : ResolveTypePlaceholder(function.ReturnType),
                        [.. function.Parameters.Select(p =>
                            new ParameterSymbol(p.Name, ResolveWrittenTypePlaceholder(p))
                            {
                                Declaration = p,
                            })],
                        EffectiveModifiers(function.Modifiers, ownerIsShared))
                    {
                        Declaration = function,

                        // A constructor is a function named for its type that yields nothing.
                        // Nothing in the syntax marks one, so this is where they part company.
                        IsConstructor = function.ReturnType is null
                                        && string.Equals(function.Name, owner.Name, StringComparison.Ordinal),
                    };

                    CheckVisibilityWords(function.Modifiers, function.Name, function);
                    RefuseThrowawayAsAName(function.Name, function, "a function");
                    RefuseThrowawayParameters(function.Parameters);
                    owner.AddMember(symbol);
                    _model.Bind(function, symbol);
                    break;
                }

                case ModelDecl nestedModel:
                    CollectNestedType(
                        new ModelSymbol(nestedModel.Name, nestedModel.Modifiers) { Declaration = nestedModel },
                        nestedModel, owner, nestedModel.Members, enclosing);
                    break;

                case StructureDecl nestedStructure:
                    CollectNestedType(
                        new StructureSymbol(nestedStructure.Name, nestedStructure.Modifiers)
                        {
                            Declaration = nestedStructure,
                        },
                        nestedStructure, owner, nestedStructure.Members, enclosing);
                    break;

                case EnumerationDecl nestedEnumeration:
                    CollectEnumeration(nestedEnumeration, enclosing, owner);
                    break;
            }
        }
    }

    private void CollectNestedType(
        DeclaredTypeSymbol symbol,
        Declaration declaration,
        DeclaredTypeSymbol owner,
        IReadOnlyList<Declaration> members,
        NamespaceSymbol enclosing)
    {
        WarnIfShadowsStandard(symbol.Name, declaration);

        symbol.Container = owner;
        owner.AddMember(symbol);
        _allTypes.Add(symbol);
        _nestedTypes[symbol.Name] = symbol;
        _model.Bind(declaration, symbol);

        CheckTypeModifiers(symbol, declaration);
        CollectMembers(symbol, members, enclosing);
    }

    private void CollectEnumeration(
        EnumerationDecl declaration,
        NamespaceSymbol enclosing,
        DeclaredTypeSymbol? owner = null)
    {
        EnumerationSymbol symbol = new(declaration.Name, declaration.Modifiers)
        {
            Declaration = declaration,
        };

        WarnIfShadowsStandard(symbol.Name, declaration);
        RefuseThrowawayAsAName(symbol.Name, declaration, "an enumeration");

        if (owner is null)
        {
            if (!enclosing.Types.TryAdd(symbol.Name, symbol))
            {
                ReportDuplicateType(symbol.Name, declaration, enclosing.Types[symbol.Name]);
                return;
            }

            symbol.Container = enclosing;
            symbol.DeclaredIn = _currentSource;
        }
        else
        {
            symbol.Container = owner;
            owner.AddMember(symbol);
        }

        // A nested type is in the same project as the file that declared it, exactly as a
        // top-level one is. Nesting says where a type sits, not which build it belongs to.
        symbol.Project = _currentProject;

        _allTypes.Add(symbol);

        if (owner is not null)
        {
            _nestedTypes[symbol.Name] = symbol;
        }
        _model.Bind(declaration, symbol);

        // Ordinals continue from the last explicit value, so an unmarked member is one more
        // than whatever preceded it.
        long next = 0;

        foreach (EnumMemberDecl member in declaration.Members)
        {
            if (member.Value is LiteralExpr { Kind: LiteralKind.Integer } literal
                && long.TryParse(literal.Text, out long explicitValue))
            {
                next = explicitValue;
            }

            EnumMemberSymbol memberSymbol = new(member.Name, symbol, next)
            {
                Declaration = member,
            };

            RefuseThrowawayAsAName(member.Name, member, "an enumeration member");
            symbol.AddMember(memberSymbol);
            _model.Bind(member, memberSymbol);
            next++;
        }
    }

    /// <summary>
    /// Members of a shared model are implicitly shared, because a shared model has no
    /// instances for an instance member to belong to.
    /// </summary>
    private static DeclarationModifiers EffectiveModifiers(
        DeclarationModifiers written,
        bool ownerIsShared) =>
        ownerIsShared ? written | DeclarationModifiers.Shared : written;

    /// <summary>
    /// <para>Settles every member's declared types, once all of them are known.</para>
    /// <para>Collection reads a signature before the whole program has been seen, so a name it
    /// cannot yet place stands as the error type. This replaces those placeholders with what
    /// the names actually denote, which is what lets a field hold a type declared further down
    /// the file, or in another file entirely.</para>
    /// <para>This is where a signature's type names are reported unknown, so that a name
    /// written once is reported once rather than again when the body is bound.</para>
    /// </summary>
    private void SettleMemberSignatures()
    {
        foreach (DeclaredTypeSymbol type in _allTypes)
        {
            // Entered so that a type named in a signature is judged from where it was written:
            // which project may reach it, and which file to report against when it cannot.
            DeclaredTypeSymbol? saved = _currentType;
            (NamespaceSymbol? Scope, Text.SourceText? File) savedContext = EnterTypeContext(type);
            _currentType = type;

            if (type.DeclaredIn is { } file)
            {
                using DiagnosticBag.FileScope reporting = _diagnostics.InFile(file);
                SettleSignaturesOf(type);
            }
            else
            {
                SettleSignaturesOf(type);
            }

            _currentType = saved;
            RestoreContext(savedContext);
        }
    }

    /// <summary>Settles the declared types in one type's member signatures.</summary>
    private void SettleSignaturesOf(DeclaredTypeSymbol type)
    {
        foreach (Symbol member in type.Members.Values.SelectMany(overloads => overloads))
        {
            switch (member)
            {
                case FieldSymbol { Declaration: FieldDecl declaration } field:
                    field.Type = ResolveType(declaration.Type);
                    break;

                case FunctionSymbol { Declaration: FunctionDecl declaration } function:
                    function.ReturnType = declaration.ReturnType is null
                        ? null
                        : ResolveType(declaration.ReturnType);

                    foreach (ParameterSymbol parameter in function.Parameters)
                    {
                        if (parameter.Declaration is ParameterDecl written)
                        {
                            parameter.Type = ResolveWrittenType(written);
                        }
                    }

                    break;
            }
        }

        ReportMembersSharingAName(type);
    }

    /// <summary>
    /// <para>Reports a name that two members of one type both answer to.</para>
    /// <para>Two are allowed only where they are versions of one function, told apart by what
    /// they take. Everything else is one name meaning two things: two fields, a field beside a
    /// function, or two functions taking the same types. Left alone, the second declaration is
    /// unreachable and nothing says so — the first one answers every use of the name, and the
    /// reader who wrote the second watches their code do what the other one does.</para>
    /// <para>Asked here rather than as each member is collected, because what tells two functions
    /// apart is their parameter types, and those are not settled until every type is known.</para>
    /// </summary>
    private void ReportMembersSharingAName(DeclaredTypeSymbol type)
    {
        foreach (List<Symbol> sharing in type.Members.Values)
        {
            for (int at = 1; at < sharing.Count; at++)
            {
                Symbol member = sharing[at];

                // A name that failed to parse is empty, and two of those are not a clash worth
                // reporting — whatever went wrong has already been said once each.
                if (member.Name.Length == 0 || member.Declaration is not { } written)
                {
                    continue;
                }

                if (sharing.Take(at).FirstOrDefault(
                        before => !VersionsOfOneFunction(before, member))
                    is { Declaration: { } first })
                {
                    Report(
                        DiagnosticDescriptors.MemberNameTaken,
                        written,
                        type.WithArticleCapitalized(),
                        member.Name,
                        first.Span.Start.Line);
                }
            }
        }
    }

    /// <summary>
    /// <para>Whether two members of one name are versions of one function rather than a clash.
    /// </para>
    /// <para>A signature holding a type that did not resolve decides nothing, and is treated as
    /// distinct so that one unknown type name is reported once rather than again here as a
    /// clash it did not cause.</para>
    /// </summary>
    private static bool VersionsOfOneFunction(Symbol one, Symbol other) =>
        one is FunctionSymbol first
        && other is FunctionSymbol second
        && (Undecided(first) || Undecided(second) || !Conversions.SameParameters(first, second));

    private static bool Undecided(FunctionSymbol function) =>
        function.Parameters.Any(parameter => parameter.Type.IsError);

    /// <summary>
    /// Reads a type during the first pass, when not every type has been recorded yet. Unknown
    /// names become the error type here and are settled by
    /// <see cref="SettleMemberSignatures"/> once every type is known.
    /// </summary>
    private TypeSymbol ResolveWrittenTypePlaceholder(ParameterDecl parameter) =>
        parameter.Type is null ? ErrorType.Instance : ResolveTypePlaceholder(parameter.Type);

    private TypeSymbol ResolveTypePlaceholder(TypeSyntax syntax) => syntax switch
    {
        MissingType => ErrorType.Instance,
        SetTypeSyntax set => new SetType(ResolveTypePlaceholder(set.ElementType)),
        OptionalTypeSyntax optional => new OptionalType(ResolveTypePlaceholder(optional.UnderlyingType)),
        FunctionTypeSyntax function => new FunctionType(
            function.ReturnType is null ? null : ResolveTypePlaceholder(function.ReturnType),
            [.. function.ParameterTypes.Select(ResolveTypePlaceholder)]),
        NamedTypeSyntax named when PrimitiveType.ByName.TryGetValue(named.Name, out PrimitiveType? p) => p,
        _ => ErrorType.Instance,
    };

    // ---- Inheritance ------------------------------------------------------------------------

    /// <summary>
    /// Links each model to its parent, once every type is known, and rejects the arrangements
    /// that cannot work.
    /// </summary>
    private void LinkInheritance()
    {
        foreach (DeclaredTypeSymbol type in _allTypes)
        {
            if (type is not ModelSymbol model
                || model.Declaration is not ModelDecl { BaseTypeName: { } baseName } declaration)
            {
                continue;
            }

            // Read from where the model sits, so that a base named without qualifying it is
            // looked for beside the model first, exactly as any other name would be.
            (NamespaceSymbol? Scope, Text.SourceText? File) saved = EnterTypeContext(type);
            DeclaredTypeSymbol? baseType = LookupType(baseName);
            bool ambiguous = baseType is null && ReportIfAmbiguous(declaration, baseName);
            RestoreContext(saved);

            if (ambiguous)
            {
                continue;
            }

            if (baseType is null)
            {
                Report(DiagnosticDescriptors.TypeNotFound, declaration, baseName);
                continue;
            }

            // A type the language owns is reached through Standard like any other, so what it
            // permits has to be asked here rather than only where a name failed to resolve.
            if (BuiltInTypeNames.Contains(baseType.Name)
                && ReferenceEquals(baseType.Container, BuiltInTypes.Standard))
            {
                if (baseType.Name == "Model")
                {
                    // Legal and redundant, exactly as ": object" is in C#. Every model extends
                    // Model already, so writing it leaves the base where it was.
                    continue;
                }

                if (!BuiltIns.MayBeExtended(baseType.Name))
                {
                    // Console and the rest are models only so that their members resolve. None
                    // has anything to inherit, and saying so is far better than accepting the
                    // line and quietly producing a model with no base at all.
                    Report(DiagnosticDescriptors.CannotExtendBuiltInType, declaration, baseName);
                    continue;
                }
            }

            if (baseType is not ModelSymbol baseModel)
            {
                Report(DiagnosticDescriptors.CannotExtendNonModel, declaration, baseName, baseType.Kind);
                continue;
            }

            if (baseModel.IsSealed)
            {
                Report(DiagnosticDescriptors.CannotExtendSealed, declaration, baseName);
                continue;
            }

            model.BaseType = baseModel;
        }

        DetectInheritanceCycles();
    }

    /// <summary>
    /// Breaks any inheritance cycle, so that later walks up the chain terminate. A cycle is
    /// reported once per model taking part.
    /// </summary>
    private void DetectInheritanceCycles()
    {
        foreach (DeclaredTypeSymbol type in _allTypes)
        {
            if (type is not ModelSymbol model)
            {
                continue;
            }

            HashSet<ModelSymbol> seen = [];

            for (ModelSymbol? current = model; current is not null; current = current.BaseType)
            {
                if (seen.Add(current))
                {
                    continue;
                }

                if (model.Declaration is not null)
                {
                    Report(DiagnosticDescriptors.CircularInheritance, model.Declaration, model.Name);
                }

                model.BaseType = null;
                break;
            }
        }
    }

    // ---- Entry point --------------------------------------------------------------------------

    /// <summary>
    /// Finds <c>Program.Main</c>, and checks the rules around it: exactly one Program, always
    /// a shared model.
    /// </summary>
    private void CheckEntryPoint(CompilationUnit unit, bool required)
    {
        List<DeclaredTypeSymbol> programs =
            // Found wherever it sits. A namespace organizes names, and the entry point is
            // reached by the compiler rather than by a name any program has to write, so
            // putting Program inside one changes where it is written and nothing else.
            [.. _allTypes.Where(t => string.Equals(t.Name, "Program", StringComparison.Ordinal))];

        if (programs.Count == 0)
        {
            if (required)
            {
                Report(DiagnosticDescriptors.EntryPointMissing, unit);
            }

            return;
        }

        if (ChooseProgram(programs, unit) is not { } program)
        {
            return;
        }

        if (program is not ModelSymbol { IsShared: true } model)
        {
            if (program.Declaration is not null)
            {
                Report(DiagnosticDescriptors.EntryPointNotSharedModel, program.Declaration);
            }

            return;
        }

        if (model.Lookup("Main").OfType<FunctionSymbol>().FirstOrDefault() is { } main)
        {
            // Signatures were settled before this ran, so the declared result is the real one.
            if (main.ReturnType is { IsError: false } result
                && !ReferenceEquals(result, PrimitiveType.Integer)
                && main.Declaration is not null)
            {
                Report(DiagnosticDescriptors.EntryPointResultNotInteger, main.Declaration);
            }

            _model.EntryPoint = main;
            return;
        }

        // A Program without a Main is always wrong, whether or not an entry point was asked
        // for: the name is reserved for exactly this.
        Report(DiagnosticDescriptors.EntryPointMissing, model.Declaration ?? unit);
    }

    /// <summary>
    /// <para>Settles which <c>Program</c> a compilation begins at.</para>
    /// <para>One is no question. Several is a question the compiler must not answer for
    /// itself: the choice belongs to the build, because an assembly holds one entry point in
    /// its metadata and picking by source order would make the answer depend on the order a
    /// project happened to list its files.</para>
    /// <para>Null means the question went unanswered and was reported, so nothing downstream
    /// should go looking for an entry point.</para>
    /// </summary>
    private DeclaredTypeSymbol? ChooseProgram(
        List<DeclaredTypeSymbol> programs,
        CompilationUnit unit)
    {
        if (_entryPoint is null)
        {
            if (programs.Count == 1)
            {
                return programs[0];
            }

            // Listed and suggested in one settled order, so the name offered is the first of
            // the names shown rather than whichever file happened to be read first.
            List<string> choices =
                [.. programs.Select(FullNameOf).OrderBy(n => n, StringComparer.Ordinal)];

            Report(
                DiagnosticDescriptors.EntryPointAmbiguous, unit, Wording.List(choices), choices[0]);

            return null;
        }

        DeclaredTypeSymbol? named = programs.FirstOrDefault(
            p => string.Equals(FullNameOf(p), _entryPoint, StringComparison.Ordinal));

        if (named is null)
        {
            Report(
                DiagnosticDescriptors.EntryPointNotFound,
                unit,
                _entryPoint,
                programs.Count == 0
                    ? "These sources declare none."
                    : "Did you mean "
                      + Wording.Either([.. programs.Select(FullNameOf)
                                                   .OrderBy(n => n, StringComparer.Ordinal)])
                      + "?");

            return null;
        }

        if (programs.Count == 1)
        {
            Report(DiagnosticDescriptors.EntryPointUnnecessary, unit, FullNameOf(named));
        }

        return named;
    }

    /// <summary>
    /// A type's name with the namespaces it sits in written in front, which is what an
    /// <c>entry</c> line names it by.
    /// </summary>
    private static string FullNameOf(DeclaredTypeSymbol type) =>
        type.Container is NamespaceSymbol { FullName.Length: > 0 } place
            ? $"{place.FullName}.{type.Name}"
            : type.Name;
}
