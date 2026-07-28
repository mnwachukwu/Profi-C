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

                foreach (Declaration member in namespaceDecl.Declarations)
                {
                    CollectDeclaration(member, target);
                }

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

    private NamespaceSymbol DeclareNamespace(QualifiedName name, NamespaceSymbol enclosing)
    {
        NamespaceSymbol current = enclosing;

        foreach (string part in name.Parts)
        {
            if (!current.Namespaces.TryGetValue(part, out NamespaceSymbol? child))
            {
                child = new NamespaceSymbol(part, current);
                current.Namespaces[part] = child;
            }

            current = child;
        }

        return current;
    }

    /// <summary>Records a model or structure, then its members.</summary>
    private void DeclareType(
        DeclaredTypeSymbol symbol,
        string name,
        Declaration declaration,
        NamespaceSymbol enclosing,
        IReadOnlyList<Declaration> members)
    {
        if (ReservedTypeNames.Contains(name))
        {
            Report(DiagnosticDescriptors.ReservedTypeName, declaration, name);
            return;
        }

        if (!enclosing.Types.TryAdd(name, symbol))
        {
            Report(DiagnosticDescriptors.DuplicateDeclaration, declaration, name);
            return;
        }

        symbol.Container = enclosing;
        _typesByName[name] = symbol;
        _model.Bind(declaration, symbol);

        CheckTypeModifiers(symbol, declaration);
        CollectMembers(symbol, members, enclosing);
    }

    /// <summary>Rejects modifier combinations that cannot mean anything.</summary>
    private void CheckTypeModifiers(DeclaredTypeSymbol symbol, Declaration declaration)
    {
        if (symbol is not ModelSymbol model)
        {
            return;
        }

        if (model.IsSealed && model.IsAbstract)
        {
            Report(DiagnosticDescriptors.SealedAndAbstract, declaration, model.Name);
        }
    }

    private void CollectMembers(
        DeclaredTypeSymbol owner,
        IReadOnlyList<Declaration> members,
        NamespaceSymbol enclosing)
    {
        bool ownerIsGlobal = owner is ModelSymbol { IsGlobal: true };

        foreach (Declaration member in members)
        {
            switch (member)
            {
                case FieldDecl field:
                {
                    FieldSymbol symbol = new(
                        field.Name,
                        ResolveTypePlaceholder(field.Type),
                        EffectiveModifiers(field.Modifiers, ownerIsGlobal))
                    {
                        Declaration = field,
                    };

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
                            new ParameterSymbol(p.Name, ResolveTypePlaceholder(p.Type))
                            {
                                Declaration = p,
                            })],
                        EffectiveModifiers(function.Modifiers, ownerIsGlobal))
                    {
                        Declaration = function,

                        // A constructor is a function named for its type that yields nothing.
                        // Nothing in the syntax marks one, so this is where they part company.
                        IsConstructor = function.ReturnType is null
                                        && string.Equals(function.Name, owner.Name, StringComparison.Ordinal),
                    };

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
        if (ReservedTypeNames.Contains(symbol.Name))
        {
            Report(DiagnosticDescriptors.ReservedTypeName, declaration, symbol.Name);
            return;
        }

        symbol.Container = owner;
        owner.AddMember(symbol);
        _typesByName[symbol.Name] = symbol;
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

        if (ReservedTypeNames.Contains(symbol.Name))
        {
            Report(DiagnosticDescriptors.ReservedTypeName, declaration, symbol.Name);
            return;
        }

        if (owner is null)
        {
            if (!enclosing.Types.TryAdd(symbol.Name, symbol))
            {
                Report(DiagnosticDescriptors.DuplicateDeclaration, declaration, symbol.Name);
                return;
            }

            symbol.Container = enclosing;
        }
        else
        {
            symbol.Container = owner;
            owner.AddMember(symbol);
        }

        _typesByName[symbol.Name] = symbol;
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

            symbol.AddMember(memberSymbol);
            _model.Bind(member, memberSymbol);
            next++;
        }
    }

    /// <summary>
    /// Members of a global model are implicitly global, because a global model has no
    /// instances for an instance member to belong to.
    /// </summary>
    private static DeclarationModifiers EffectiveModifiers(
        DeclarationModifiers written,
        bool ownerIsGlobal) =>
        ownerIsGlobal ? written | DeclarationModifiers.Global : written;

    /// <summary>
    /// Reads a type during the first pass, when not every type has been recorded yet. Unknown
    /// names become the error type here and are resolved properly in the second pass.
    /// </summary>
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
        foreach (DeclaredTypeSymbol type in _typesByName.Values)
        {
            if (type is not ModelSymbol model
                || model.Declaration is not ModelDecl { BaseTypeName: { } baseName } declaration)
            {
                continue;
            }

            if (!_typesByName.TryGetValue(baseName, out DeclaredTypeSymbol? baseType))
            {
                if (!BuiltInTypeNames.Contains(baseName))
                {
                    Report(DiagnosticDescriptors.TypeNotFound, declaration, baseName);
                }
                else if (baseName == "Exception" || BuiltInExceptionNames.Contains(baseName))
                {
                    // Recorded rather than merely permitted, so that a model extending
                    // Exception inherits Message and one catch clause takes it.
                    model.BaseType = BuiltInModel(baseName);
                }
                else if (baseName != "Model")
                {
                    // Console and the rest are models only so that their members resolve.
                    // None of them has anything to inherit, and saying so is far better than
                    // accepting the line and quietly producing a model with no base at all.
                    Report(DiagnosticDescriptors.CannotExtendBuiltInType, declaration, baseName);
                }

                // What remains is "extends Model": legal and redundant, exactly as ": object"
                // is in C#, and it leaves the base where it already was.
                continue;
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
        foreach (DeclaredTypeSymbol type in _typesByName.Values)
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
    /// a global model.
    /// </summary>
    private void CheckEntryPoint(CompilationUnit unit, bool required)
    {
        List<DeclaredTypeSymbol> programs =
            [.. _typesByName.Values.Where(t => string.Equals(t.Name, "Program", StringComparison.Ordinal))];

        if (programs.Count == 0)
        {
            if (required)
            {
                Report(DiagnosticDescriptors.EntryPointMissing, unit);
            }

            return;
        }

        if (programs.Count > 1)
        {
            foreach (DeclaredTypeSymbol extra in programs.Skip(1))
            {
                if (extra.Declaration is not null)
                {
                    Report(DiagnosticDescriptors.ProgramDeclaredMoreThanOnce, extra.Declaration);
                }
            }
        }

        DeclaredTypeSymbol program = programs[0];

        if (program is not ModelSymbol { IsGlobal: true } model)
        {
            if (program.Declaration is not null)
            {
                Report(DiagnosticDescriptors.EntryPointNotGlobalModel, program.Declaration);
            }

            return;
        }

        if (model.Lookup("Main").OfType<FunctionSymbol>().FirstOrDefault() is { } main)
        {
            _model.EntryPoint = main;
            return;
        }

        // A Program without a Main is always wrong, whether or not an entry point was asked
        // for: the name is reserved for exactly this.
        Report(DiagnosticDescriptors.EntryPointMissing, model.Declaration ?? unit);
    }
}
