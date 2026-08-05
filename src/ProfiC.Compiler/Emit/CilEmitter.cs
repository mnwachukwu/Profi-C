using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Semantics;

namespace ProfiC.Compiler.Emit;

/// <summary>
/// <para>Turns the lowered tree into a .NET assembly on disk.</para>
/// <para><b>Two passes, and not by choice.</b> <see cref="PersistedAssemblyBuilder"/> writes
/// metadata only when the assembly is saved, so a token for a generated type is invalid until
/// then and the emitter cannot reflect over what it has just built. It therefore keeps its own
/// symbol-to-builder maps, and defines every type and every member <em>signature</em> before
/// emitting a single body — which is also what lets two functions call each other, since the
/// second is already defined by the time the first needs it.</para>
/// <para><b>What it emits from is the lowered tree</b>, the same one the interpreter walks.
/// Everything hard about capture, iteration, and implicit conversion has already happened by
/// then, so this file is a translation from a simple tree to a stack machine and nothing more.
/// </para>
/// <para><b>It emits every program the checker accepts.</b> There is nothing it declines, so
/// meeting a shape it has no sequence for is a fault in the compiler rather than a program to
/// report, and it throws — a reader can do nothing with such a message, and the alternative is an
/// assembly quietly missing a piece.</para>
/// </summary>
public sealed partial class CilEmitter
{
    /// <summary>
    /// <para>Emits an assembly.</para>
    /// <para><b>Every program the checker accepts is one this writes</b>, so there is nothing to
    /// report and nothing to decline. What keeps that true is a test: every built-in the language
    /// offers has to be one <see cref="CilBuiltIns"/> knows a sequence for, so a member added
    /// without an emission fails a build here rather than a reader's build later.</para>
    /// <para>Meeting a shape it has no sequence for is therefore a fault in the compiler rather
    /// than anything a program did, and is thrown rather than reported. A reader can do nothing
    /// with such a message, and the alternative is an assembly quietly missing a piece.</para>
    /// </summary>
    /// <param name="units">The lowered, closure-converted units.</param>
    /// <param name="model">What names and types were resolved to.</param>
    /// <param name="assemblyName">The name the assembly carries and its file is called.</param>
    /// <param name="path">Where to write the assembly.</param>
    public static void Emit(
        IReadOnlyList<CompilationUnit> units,
        SemanticModel model,
        string assemblyName,
        string path)
    {
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(assemblyName);
        ArgumentNullException.ThrowIfNull(path);

        new CilEmitter(model, assemblyName).Write(units, path);
    }

    private readonly SemanticModel _model;
    private readonly PersistedAssemblyBuilder _assembly;
    private readonly ModuleBuilder _module;

    /// <summary>
    /// The builders standing in for declared types and functions, which is the bookkeeping the
    /// two passes exist for: nothing can be looked up by reflection until the file is saved.
    /// </summary>
    private readonly Dictionary<DeclaredTypeSymbol, TypeBuilder> _types = [];
    private readonly Dictionary<FunctionSymbol, MethodBuilder> _functions = [];
    private readonly Dictionary<FunctionSymbol, ConstructorBuilder> _constructors = [];
    private readonly Dictionary<FieldSymbol, FieldBuilder> _fields = [];

    /// <summary>
    /// The constructor made for a model that declared none. Held rather than looked up, because
    /// asking a type for its constructors is not answerable until the type is created — and by
    /// then every body that needed one has already been written.
    /// </summary>
    private readonly Dictionary<DeclaredTypeSymbol, ConstructorBuilder> _defaultConstructors = [];

    /// <summary>
    /// The field initializers each model declares, in the order they were written. Run at the
    /// top of every constructor rather than where they appear, because a field must hold its
    /// initial value before any constructor body can read it.
    /// </summary>
    private readonly Dictionary<DeclaredTypeSymbol, List<FieldDecl>> _initializers = [];

    private CilEmitter(SemanticModel model, string assemblyName)
    {
        _model = model;
        _assembly = new PersistedAssemblyBuilder(
            new AssemblyName(assemblyName), typeof(object).Assembly);

        _module = _assembly.DefineDynamicModule(assemblyName);
    }

    private void Write(IReadOnlyList<CompilationUnit> units, string path)
    {
        Shaped[] models = [.. BasesFirst([.. units.SelectMany(Models)])];
        Declared[] functions = [.. models.SelectMany(Functions)];

        // Pass one: every type, then every field and signature. All of it before any body, so
        // that a body may name a method or a field whose own definition comes later — which is
        // what two models referring to each other requires.

        // Enumerations first, and they need no ordering among themselves: an enumeration extends
        // nothing and holds nothing but numbers, so none of them can name another.
        foreach (EnumerationDecl enumeration in units.SelectMany(Enumerations))
        {
            DefineEnumeration(enumeration);
        }

        foreach (Shaped model in models)
        {
            DefineType(model);
        }

        foreach (Shaped model in models)
        {
            DefineFields(model);
        }

        // After the fields, since what a model answers about its own parts is the list of them.
        foreach (Shaped model in models)
        {
            ImplementDeepEquality(model);
        }

        foreach (Declared function in functions)
        {
            DefineSignature(function);
        }

        // A model with no constructor still needs one, or nothing can make an instance and its
        // field initializers have nowhere to run. Defined here with everything else, because a
        // body written below may construct it — and the builder cannot be found by asking the
        // type for its constructors, which is not answerable until the type is created.
        foreach (Shaped model in models)
        {
            DefineDefaultConstructor(model);
        }

        // The copy a structure needs, defined with everything else because a body written below
        // calls it — every assignment of one does.
        foreach (Shaped model in models.Where(m => m.IsStructure))
        {
            DefineCopy(model);
        }

        // Pass two: the bodies, which may now refer to anything defined above.
        foreach (Declared function in functions)
        {
            EmitBody(function);
        }

        foreach (Shaped model in models)
        {
            EmitDefaultConstructor(model);
            EmitSharedFieldInitializers(model);

            if (model.IsStructure)
            {
                EmitCopy(model);
            }
        }

        DefineStart(units);

        foreach (TypeBuilder type in _types.Values)
        {
            type.CreateType();
        }

        // After the models, since a delegate is made while a body is being written and the loop
        // above is over a collection that is no longer growing by then.
        foreach (TypeBuilder built in _delegates.Values)
        {
            built.CreateType();
        }

        Save(path);
    }

    /// <summary>
    /// A function, and the model it was declared in. Carried together because a function symbol
    /// does not name its owner and the emitter needs one to hang a method on — the walk knows
    /// it, so the walk is what says it.
    /// </summary>
    private readonly record struct Declared(Shaped Owner, FunctionDecl Function);

    /// <summary>
    /// <para>A model or a structure, which the emitter builds the same way.</para>
    /// <para>The two declarations carry the same things — modifiers, a name, members — and share
    /// no base beyond <see cref="Declaration"/>, so this is what lets one pass define both.
    /// <c>BaseTypeName</c> is the whole of what a model has and a structure does not, and a
    /// structure extends nothing.</para>
    /// <para>What <see cref="IsStructure"/> decides is copying, and nothing else. See
    /// <see cref="DefineCopy"/> for why that is a method rather than a kind of type.</para>
    /// </summary>
    private readonly record struct Shaped(
        Declaration Node,
        DeclarationModifiers Modifiers,
        string Name,
        IReadOnlyList<Declaration> Members,
        bool IsStructure)
    {
        public static Shaped Of(ModelDecl model) =>
            new(model, model.Modifiers, model.Name, model.Members, IsStructure: false);

        public static Shaped Of(StructureDecl structure) =>
            new(structure, structure.Modifiers, structure.Name, structure.Members,
                IsStructure: true);
    }

    /// <summary>
    /// Every model and structure a unit declares. Namespaces are walked through, since a
    /// declaration inside one is still a declaration the unit makes.
    /// </summary>
    private static IEnumerable<Shaped> Models(CompilationUnit unit) => Models(unit.Declarations);

    private static IEnumerable<Shaped> Models(IEnumerable<Declaration> declarations)
    {
        foreach (Declaration declaration in declarations)
        {
            switch (declaration)
            {
                // A type's own members are walked as well as a namespace's, since a model or a
                // structure may be declared inside another and needs a CLR type of its own.
                case ModelDecl model:
                    yield return Shaped.Of(model);

                    foreach (Shaped found in Models(model.Members))
                    {
                        yield return found;
                    }

                    break;

                case StructureDecl structure:
                    yield return Shaped.Of(structure);

                    foreach (Shaped found in Models(structure.Members))
                    {
                        yield return found;
                    }

                    break;

                case NamespaceDecl inner:
                    foreach (Shaped found in Models(inner.Declarations))
                    {
                        yield return found;
                    }

                    break;
            }
        }
    }

    /// <summary>Every enumeration a unit declares, namespaces walked through as models are.</summary>
    private static IEnumerable<EnumerationDecl> Enumerations(CompilationUnit unit) =>
        Enumerations(unit.Declarations);

    private static IEnumerable<EnumerationDecl> Enumerations(IEnumerable<Declaration> declarations)
    {
        foreach (Declaration declaration in declarations)
        {
            switch (declaration)
            {
                case EnumerationDecl enumeration:
                    yield return enumeration;
                    break;

                case ModelDecl model:
                    foreach (EnumerationDecl found in Enumerations(model.Members))
                    {
                        yield return found;
                    }

                    break;

                case StructureDecl structure:
                    foreach (EnumerationDecl found in Enumerations(structure.Members))
                    {
                        yield return found;
                    }

                    break;

                case NamespaceDecl inner:
                    foreach (EnumerationDecl found in Enumerations(inner.Declarations))
                    {
                        yield return found;
                    }

                    break;
            }
        }
    }

    private static IEnumerable<Declared> Functions(Shaped model) =>
        model.Members.OfType<FunctionDecl>().Select(f => new Declared(model, f));

    /// <summary>
    /// <para>The models ordered so that a parent always comes before what extends it.</para>
    /// <para>A type is defined with its base named, and a base has to exist to be named — so
    /// this ordering is what lets the rest of the emitter treat the list as flat. Declaration
    /// order will not do: a file may declare a child above its parent, and two files have no
    /// order between them at all.</para>
    /// </summary>
    private IEnumerable<Shaped> BasesFirst(IReadOnlyList<Shaped> models)
    {
        Dictionary<DeclaredTypeSymbol, Shaped> byType = [];

        foreach (Shaped model in models)
        {
            if (_model.GetSymbol(model.Node) is DeclaredTypeSymbol owner)
            {
                byType.TryAdd(owner, model);
            }
        }

        List<Shaped> ordered = new(models.Count);
        HashSet<Shaped> placed = [];
        HashSet<Shaped> walking = [];

        foreach (Shaped model in models)
        {
            Place(model);
        }

        return ordered;

        void Place(Shaped model)
        {
            // 'walking' guards against an inheritance cycle, which the resolver reports and
            // which would otherwise be followed forever here.
            if (!placed.Add(model) || !walking.Add(model))
            {
                return;
            }

            // A nested type is defined on its container's builder, so the container has to be
            // there first — the same constraint a base is, and settled the same way.
            if (_model.GetSymbol(model.Node) is DeclaredTypeSymbol
                { Container: DeclaredTypeSymbol outer }
                && byType.TryGetValue(outer, out Shaped holding))
            {
                Place(holding);
            }

            if (_model.GetSymbol(model.Node) is ModelSymbol { BaseType: { } parent }
                && byType.TryGetValue(parent, out Shaped above))
            {
                Place(above);
            }

            walking.Remove(model);
            ordered.Add(model);
        }
    }

    /// <summary>
    /// <para>Defines a model's type, with whatever it extends.</para>
    /// <para>A <c>shared</c> model becomes a sealed abstract class, which is what a C# static
    /// class is: it has no instances by definition, and saying so in the metadata means the
    /// runtime enforces it rather than the convention holding by luck.</para>
    /// <para>Anything else derives from the model it extends, or from <c>System.Object</c> where
    /// it extends nothing — which is what Profi-C's <c>Model</c> is, so the root of every chain
    /// needs no adapter. <c>sealed</c> and <c>abstract</c> are written into the metadata for the
    /// same reason <c>shared</c> is: the runtime then enforces what the language promised.</para>
    /// </summary>
    private void DefineType(Shaped declaration)
    {
        if (_model.GetSymbol(declaration.Node) is not DeclaredTypeSymbol owner
            || _types.ContainsKey(owner))
        {
            return;
        }

        TypeAttributes shape = declaration.Modifiers.HasFlag(DeclarationModifiers.Shared)
            ? TypeAttributes.Sealed | TypeAttributes.Abstract
            : TypeAttributes.BeforeFieldInit;

        if (declaration.Modifiers.HasFlag(DeclarationModifiers.Abstract))
        {
            shape |= TypeAttributes.Abstract;
        }

        if (declaration.Modifiers.HasFlag(DeclarationModifiers.Sealed))
        {
            shape |= TypeAttributes.Sealed;
        }

        // A structure is sealed because nothing extends one — the language has no way to say it,
        // and saying so in the metadata means the runtime enforces what the language promised.
        if (declaration.IsStructure)
        {
            shape |= TypeAttributes.Sealed;
        }

        // A type declared inside another becomes a CLR nested type, which is what C# does with
        // one and what keeps two containers free to hold a Node each: the names never meet.
        // The reach written into the metadata is assembly-wide unless the language said public,
        // since everything a program emits lands in one assembly and a narrower one would have
        // the runtime refuse what the compiler allowed.
        if (owner.Container is DeclaredTypeSymbol outer && _types.TryGetValue(outer, out TypeBuilder? around))
        {
            _types[owner] = around.DefineNestedType(
                owner.Name,
                (owner.Visibility == Visibility.Public
                    ? TypeAttributes.NestedPublic
                    : TypeAttributes.NestedAssembly) | TypeAttributes.Class | shape,
                BaseOf(owner));

            return;
        }

        _types[owner] = _module.DefineType(
            owner.Name,
            TypeAttributes.Public | TypeAttributes.Class | shape,
            BaseOf(owner));
    }

    /// <summary>
    /// <para>The CLR type a model derives from: the builder for the model it extends, the runtime
    /// type of one the language provides, or <c>System.Object</c> where it extends nothing.</para>
    /// <para>The middle case is how a program names its own failures. <c>model Overdrawn extends
    /// Exception</c> becomes a class deriving from <c>System.Exception</c>, which is what makes
    /// <c>throw</c> and <c>catch</c> ordinary CIL rather than something the emitter has to build a
    /// mechanism for.</para>
    /// </summary>
    private Type BaseOf(DeclaredTypeSymbol owner)
    {
        if (owner is not ModelSymbol { BaseType: { } parent })
        {
            return typeof(object);
        }

        return _types.TryGetValue(parent, out TypeBuilder? built)
            ? built
            : CilTypes.OfBuiltInModel(parent) ?? typeof(object);
    }

    /// <summary>
    /// Defines a model's fields, and remembers which of them were written with a value so that
    /// every constructor can start by running those.
    /// </summary>
    private void DefineFields(Shaped declaration)
    {
        if (_model.GetSymbol(declaration.Node) is not DeclaredTypeSymbol owner
            || !_types.TryGetValue(owner, out TypeBuilder? type))
        {
            return;
        }

        List<FieldDecl> initialized = [];

        foreach (FieldDecl declared in declaration.Members.OfType<FieldDecl>())
        {
            if (_model.GetSymbol(declared) is not FieldSymbol field)
            {
                continue;
            }

            FieldAttributes shape = field.IsShared
                ? FieldAttributes.Public | FieldAttributes.Static
                : FieldAttributes.Public;

            _fields[field] = type.DefineField(field.Name, TypeOf(field.Type, field.Name), shape);

            if (declared.Initializer is not null)
            {
                initialized.Add(declared);
            }
        }

        _initializers[owner] = initialized;
    }

    private void DefineSignature(Declared declared)
    {
        if (_model.GetSymbol(declared.Function) is not FunctionSymbol function
            || _model.GetSymbol(declared.Owner.Node) is not DeclaredTypeSymbol owner
            || !_types.TryGetValue(owner, out TypeBuilder? type))
        {
            return;
        }

        Type[] parameters =
        [
            .. function.Parameters.Select(p => TypeOf(p.Type, p.Name)),
        ];

        if (function.IsConstructor)
        {
            _constructors[function] = type.DefineConstructor(
                MethodAttributes.Public
                | MethodAttributes.SpecialName
                | MethodAttributes.RTSpecialName,
                CallingConventions.Standard,
                parameters);

            return;
        }

        MethodBuilder method = type.DefineMethod(
            function.Name,
            ShapeOf(function, declared.Owner),
            Returning(function),
            parameters);

        for (int i = 0; i < function.Parameters.Count; i++)
        {
            // One-based: zero names the return value, which is a thing the API can also do.
            method.DefineParameter(i + 1, ParameterAttributes.None, function.Parameters[i].Name);
        }

        _functions[function] = method;
    }

    /// <summary>
    /// <para>How a function is declared in the metadata.</para>
    /// <para>A shared function has no receiver; anything else is called on one, and the CLR puts
    /// that receiver in argument zero.</para>
    /// <para><b>The slot is what makes dispatch work.</b> A virtual method may either take a slot
    /// of its own or reuse the one its parent already has, and which of those it does is the
    /// whole difference between overriding and hiding. <c>virtual</c> starts a slot;
    /// <c>override</c> reuses the one above it, so a call written against the parent reaches the
    /// child. Getting that backwards costs nothing at build time and everything at run time — the
    /// program works, and every call through a parent reaches the parent's version.</para>
    /// <para><c>HideBySig</c> throughout, so a name is hidden by a matching signature rather than
    /// by the name alone. Without it a child declaring <c>Area(integer)</c> would hide the
    /// parent's <c>Area()</c> as well, which is not what either of them said.</para>
    /// </summary>
    private static MethodAttributes ShapeOf(FunctionSymbol function, Shaped owner)
    {
        MethodAttributes shape = MethodAttributes.Public | MethodAttributes.HideBySig;

        if (function.Modifiers.HasFlag(DeclarationModifiers.Shared)
            || owner.Modifiers.HasFlag(DeclarationModifiers.Shared))
        {
            return shape | MethodAttributes.Static;
        }

        if (function.Modifiers.HasFlag(DeclarationModifiers.Override))
        {
            shape |= MethodAttributes.Virtual;
        }
        else if (function.Modifiers.HasFlag(DeclarationModifiers.Virtual)
                 || function.Modifiers.HasFlag(DeclarationModifiers.Abstract))
        {
            shape |= MethodAttributes.Virtual | MethodAttributes.NewSlot;
        }

        if (function.Modifiers.HasFlag(DeclarationModifiers.Abstract))
        {
            shape |= MethodAttributes.Abstract;
        }

        // Final only alongside Virtual: sealing is a statement about a slot, and a method with
        // no slot has nothing to seal. The CLR rejects the pair written any other way.
        if (function.Modifiers.HasFlag(DeclarationModifiers.Sealed)
            && shape.HasFlag(MethodAttributes.Virtual))
        {
            shape |= MethodAttributes.Final;
        }

        return shape;
    }

    /// <summary>
    /// <para>Whether a member was reached through a type's name rather than through a value.</para>
    /// <para><b>The node shape is half the question, and not a formality.</b> A <c>new</c> binds
    /// to the type it makes, so asking the symbol alone calls <c>new Time(17, 30).AddHours(8.0)</c>
    /// a member reached through a name and emits the call with nothing underneath it — which the
    /// CLR refuses as an invalid program, a long way from the line that caused it.</para>
    /// </summary>
    private bool IsThroughATypeName(Expression receiver) =>
        receiver is IdentifierExpr or MemberExpr
        && _model.GetSymbol(receiver) is DeclaredTypeSymbol;

    private Type Returning(FunctionSymbol function) =>
        function.ReturnType is null ? typeof(void) : TypeOf(function.ReturnType, function.Name);

    /// <summary>
    /// <para>The CLR type a Profi-C type becomes, primitives and declared models alike.</para>
    /// <para>A declared model resolves to the builder standing in for it, which is a real
    /// <c>Type</c> even though the type does not exist yet — that is the whole reason the
    /// builders are held in a map rather than looked up by reflection.</para>
    /// </summary>
    private Type TypeOf(TypeSymbol type, string what)
    {
        if (CilTypes.Of(type) is { } primitive)
        {
            return primitive;
        }

        if (type is DeclaredTypeSymbol declared && _types.TryGetValue(declared, out TypeBuilder? built))
        {
            return built;
        }

        // One of the two roots — 'Model', or 'Function' — which is a place to hold something
        // whose shape is not being named.
        if (CilTypes.OfRoot(type) is { } root)
        {
            return root;
        }

        // A moment, a day, a time of day, a length of one, or a generator.
        if (CilTypes.OfProvided(type) is { } holding)
        {
            return holding;
        }

        // A model the language provides, which is a type in the runtime rather than one being
        // written here — an exception, so far.
        if (CilTypes.OfBuiltInModel(type) is { } provided)
        {
            return provided;
        }

        // A set is built from what it holds, which may itself be a set or a model this build is
        // still writing — so the element is resolved the same way rather than looked up.
        if (type is SetType set)
        {
            return CilTypes.SetOf(TypeOf(set.ElementType, what));
        }

        if (type is OptionalType optional)
        {
            return CilTypes.OptionalOf(TypeOf(optional.UnderlyingType, what));
        }

        // A function type becomes a delegate, made the first time that shape is wanted rather
        // than in the pass above: what shapes a program needs is not knowable from its
        // declarations, since one is written wherever a value is passed along.
        if (type is FunctionType shape)
        {
            return DelegateFor(shape);
        }

        return Required(null, what);
    }

    /// <summary>
    /// A CLR type the survey promised would be there. Null here means the survey and the
    /// emitter disagree about the subset, which is a fault in the compiler rather than anything
    /// a program did.
    /// </summary>
    private static Type Required(Type? type, string what) =>
        type ?? throw new InvalidOperationException(
            $"the emitter has no CLR type for '{what}', which it has no sequence for");

    /// <summary>
    /// <para>Writes the assembly, with the entry point recorded in the PE header.</para>
    /// <para>Built through the metadata rather than through <c>Save</c>, because an entry point
    /// is a token in the header and the simpler overload has nowhere to put one. A
    /// <c>runtimeconfig.json</c> goes beside it: without one the host does not know which
    /// framework to start, and the assembly will not run however correct its code is.</para>
    /// <para>A compilation declaring no <c>Program</c> is a library — types for something else to
    /// build on, with nowhere to begin. It says so in its header and takes no configuration
    /// beside it, since what a configuration names is the framework to start and nothing starts
    /// a library.</para>
    /// </summary>
    private void Save(string path)
    {
        // Metadata first, and the token only afterwards. A builder's token is assigned while
        // metadata is generated, so reading it before gives a number that names nothing — which
        // shows up not as an error here but as "entry point not found" when the assembly is
        // run, a long way from the line that caused it.
        MetadataBuilder metadata = _assembly.GenerateMetadata(
            out BlobBuilder il, out BlobBuilder fieldData);

        MethodDefinitionHandle entryPoint = default;

        // The wrapper rather than Main itself, so that a failure reaching the top is described
        // the way the interpreter describes it instead of by the CLR's own handler.
        if (_start is not null)
        {
            entryPoint = MetadataTokens.MethodDefinitionHandle(_start.MetadataToken);
        }

        ManagedPEBuilder image = new(
            header: new PEHeaderBuilder(
                imageCharacteristics: _start is null
                    ? Characteristics.ExecutableImage | Characteristics.Dll
                    : Characteristics.ExecutableImage,
                subsystem: Subsystem.WindowsCui),
            metadataRootBuilder: new MetadataRootBuilder(metadata),
            ilStream: il,
            mappedFieldData: fieldData,
            entryPoint: entryPoint);

        BlobBuilder assembly = new();
        image.Serialize(assembly);

        if (Path.GetDirectoryName(Path.GetFullPath(path)) is { Length: > 0 } folder)
        {
            Directory.CreateDirectory(folder);
        }

        using (FileStream file = new(path, FileMode.Create, FileAccess.Write))
        {
            assembly.WriteContentTo(file);
        }

        if (_start is not null)
        {
            File.WriteAllText(RuntimeConfigBeside(path), RuntimeConfig);
        }

        CopyRuntimeBeside(path);
    }

    /// <summary>
    /// <para>Puts the Profi-C runtime next to the assembly that needs it.</para>
    /// <para>Emitted code calls into it — printing a value goes through the runtime so that a
    /// boolean reads <c>true</c> and a fraction reads <c>1|2</c>, which is the language's
    /// decision rather than the framework's. An assembly without it beside it loads and then
    /// fails at the first line that prints, so copying it is part of writing the program rather
    /// than a step someone has to remember.</para>
    /// </summary>
    private static void CopyRuntimeBeside(string path)
    {
        string runtime = typeof(Runtime.ModelOperations).Assembly.Location;

        if (runtime.Length == 0 || Path.GetDirectoryName(Path.GetFullPath(path)) is not { } folder)
        {
            return;
        }

        string beside = Path.Combine(folder, Path.GetFileName(runtime));

        if (!string.Equals(Path.GetFullPath(runtime), beside, StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(runtime, beside, overwrite: true);
        }
    }

    /// <summary>Where the host looks for the framework to start: the assembly's name, plus the suffix.</summary>
    internal static string RuntimeConfigBeside(string assemblyPath) =>
        Path.ChangeExtension(assemblyPath, null) + ".runtimeconfig.json";

    private const string RuntimeConfig =
        """
        {
          "runtimeOptions": {
            "tfm": "net10.0",
            "framework": {
              "name": "Microsoft.NETCore.App",
              "version": "10.0.0"
            }
          }
        }
        """;
}
