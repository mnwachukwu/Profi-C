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
/// <para><b>What it can emit is <see cref="EmitSurvey"/>'s to say</b>, and that runs first. Any
/// construct reaching here that the survey should have refused is a fault in the compiler, and
/// is thrown rather than reported — a reader can do nothing with it, and the alternative is an
/// assembly quietly missing a piece.</para>
/// </summary>
public sealed partial class CilEmitter
{
    /// <summary>
    /// <para>Emits an assembly, or reports why it cannot and writes nothing.</para>
    /// <para>Nothing is written until the survey passes, so a refused build leaves no file
    /// behind — an assembly that is missing part of a program still loads, and fails only when
    /// a run reaches the gap.</para>
    /// </summary>
    /// <param name="units">The lowered, closure-converted units.</param>
    /// <param name="model">What names and types were resolved to.</param>
    /// <param name="assemblyName">The name the assembly carries and its file is called.</param>
    /// <param name="path">Where to write the assembly.</param>
    /// <param name="diagnostics">Where refusals are reported.</param>
    /// <returns>True where an assembly was written.</returns>
    public static bool Emit(
        IReadOnlyList<CompilationUnit> units,
        SemanticModel model,
        string assemblyName,
        string path,
        DiagnosticBag diagnostics)
    {
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(assemblyName);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (!EmitSurvey.CanEmit(units, model, diagnostics))
        {
            return false;
        }

        new CilEmitter(model, assemblyName).Write(units, path);

        return true;
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
        ModelDecl[] models = [.. BasesFirst([.. units.SelectMany(Models)])];
        Declared[] functions = [.. models.SelectMany(Functions)];

        // Pass one: every type, then every field and signature. All of it before any body, so
        // that a body may name a method or a field whose own definition comes later — which is
        // what two models referring to each other requires.
        foreach (ModelDecl model in models)
        {
            DefineType(model);
        }

        foreach (ModelDecl model in models)
        {
            DefineFields(model);
        }

        // After the fields, since what a model answers about its own parts is the list of them.
        foreach (ModelDecl model in models)
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
        foreach (ModelDecl model in models)
        {
            DefineDefaultConstructor(model);
        }

        // Pass two: the bodies, which may now refer to anything defined above.
        foreach (Declared function in functions)
        {
            EmitBody(function);
        }

        foreach (ModelDecl model in models)
        {
            EmitDefaultConstructor(model);
            EmitSharedFieldInitializers(model);
        }

        DefineStart(units);

        foreach (TypeBuilder type in _types.Values)
        {
            type.CreateType();
        }

        Save(path);
    }

    /// <summary>
    /// A function, and the model it was declared in. Carried together because a function symbol
    /// does not name its owner and the emitter needs one to hang a method on — the walk knows
    /// it, so the walk is what says it.
    /// </summary>
    private readonly record struct Declared(ModelDecl Owner, FunctionDecl Function);

    /// <summary>
    /// Every model a unit declares. Namespaces are walked through, since a declaration inside
    /// one is still a declaration the unit makes.
    /// </summary>
    private static IEnumerable<ModelDecl> Models(CompilationUnit unit) => Models(unit.Declarations);

    private static IEnumerable<ModelDecl> Models(IEnumerable<Declaration> declarations)
    {
        foreach (Declaration declaration in declarations)
        {
            switch (declaration)
            {
                case ModelDecl model:
                    yield return model;
                    break;

                case NamespaceDecl inner:
                    foreach (ModelDecl found in Models(inner.Declarations))
                    {
                        yield return found;
                    }

                    break;
            }
        }
    }

    private static IEnumerable<Declared> Functions(ModelDecl model) =>
        model.Members.OfType<FunctionDecl>().Select(f => new Declared(model, f));

    /// <summary>
    /// <para>The models ordered so that a parent always comes before what extends it.</para>
    /// <para>A type is defined with its base named, and a base has to exist to be named — so
    /// this ordering is what lets the rest of the emitter treat the list as flat. Declaration
    /// order will not do: a file may declare a child above its parent, and two files have no
    /// order between them at all.</para>
    /// </summary>
    private IEnumerable<ModelDecl> BasesFirst(IReadOnlyList<ModelDecl> models)
    {
        Dictionary<DeclaredTypeSymbol, ModelDecl> byType = [];

        foreach (ModelDecl model in models)
        {
            if (_model.GetSymbol(model) is DeclaredTypeSymbol owner)
            {
                byType.TryAdd(owner, model);
            }
        }

        List<ModelDecl> ordered = new(models.Count);
        HashSet<ModelDecl> placed = [];
        HashSet<ModelDecl> walking = [];

        foreach (ModelDecl model in models)
        {
            Place(model);
        }

        return ordered;

        void Place(ModelDecl model)
        {
            // 'walking' guards against an inheritance cycle, which the resolver reports and
            // which would otherwise be followed forever here.
            if (!placed.Add(model) || !walking.Add(model))
            {
                return;
            }

            if (_model.GetSymbol(model) is ModelSymbol { BaseType: { } parent }
                && byType.TryGetValue(parent, out ModelDecl? above))
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
    private void DefineType(ModelDecl declaration)
    {
        if (_model.GetSymbol(declaration) is not DeclaredTypeSymbol owner
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
    private void DefineFields(ModelDecl declaration)
    {
        if (_model.GetSymbol(declaration) is not DeclaredTypeSymbol owner
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
            || _model.GetSymbol(declared.Owner) is not DeclaredTypeSymbol owner
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
    private static MethodAttributes ShapeOf(FunctionSymbol function, ModelDecl owner)
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

        return Required(null, what);
    }

    /// <summary>
    /// A CLR type the survey promised would be there. Null here means the survey and the
    /// emitter disagree about the subset, which is a fault in the compiler rather than anything
    /// a program did.
    /// </summary>
    private static Type Required(Type? type, string what) =>
        type ?? throw new InvalidOperationException(
            $"the emitter has no CLR type for '{what}', which the survey should have refused");

    /// <summary>
    /// <para>Writes the assembly, with the entry point recorded in the PE header.</para>
    /// <para>Built through the metadata rather than through <c>Save</c>, because an entry point
    /// is a token in the header and the simpler overload has nowhere to put one. A
    /// <c>runtimeconfig.json</c> goes beside it: without one the host does not know which
    /// framework to start, and the assembly will not run however correct its code is.</para>
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
                imageCharacteristics: Characteristics.ExecutableImage,
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

        File.WriteAllText(RuntimeConfigBeside(path), RuntimeConfig);
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
