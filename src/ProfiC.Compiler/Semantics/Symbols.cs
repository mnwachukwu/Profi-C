using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Text;

namespace ProfiC.Compiler.Semantics;

/// <summary>Something a program declares and can refer to by name.</summary>
public abstract class Symbol(string name)
{
    /// <summary>The name as written.</summary>
    public string Name { get; } = name;

    /// <summary>Where it was declared. Null for a built-in, which no source declares.</summary>
    public SyntaxNode? Declaration { get; init; }

    /// <summary>
    /// The type this is a member of, or null when it is not one. Set as a member is recorded,
    /// because whether a member can be reached is a question about where it was declared, and
    /// a lookup that walked up a chain of models no longer knows where it stopped.
    /// </summary>
    public DeclaredTypeSymbol? DeclaringType { get; internal set; }

    /// <summary>A short description used in diagnostics, such as "model" or "local".</summary>
    public abstract string Kind { get; }

    public override string ToString() => $"{Kind} {Name}";
}

/// <summary>A namespace, which holds types and other namespaces.</summary>
public sealed class NamespaceSymbol(string name, NamespaceSymbol? parent) : Symbol(name)
{
    public NamespaceSymbol? Parent { get; } = parent;

    public Dictionary<string, TypeSymbol> Types { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, NamespaceSymbol> Namespaces { get; } = new(StringComparer.Ordinal);

    public override string Kind => "namespace";

    /// <summary>The dotted path to this namespace.</summary>
    public string FullName =>
        Parent is null or { Name: "" } ? Name : $"{Parent.FullName}.{Name}";
}

/// <summary>
/// Shared behavior of the three declared type kinds: models, structures, and enumerations.
/// </summary>
public abstract class DeclaredTypeSymbol(string name, DeclarationModifiers modifiers)
    : TypeSymbol(name)
{
    public DeclarationModifiers Modifiers { get; } = modifiers;

    /// <summary>The namespace or model this was declared in.</summary>
    public Symbol? Container { get; internal set; }

    /// <summary>
    /// The file this was declared in. A compilation spans several, so where a type came from
    /// is not answered by its declaration alone.
    /// </summary>
    public SourceText? DeclaredIn { get; internal set; }

    /// <summary>
    /// <para>The project this was declared in, which is how far <c>internal</c> reaches.</para>
    /// <para>Empty when nothing said otherwise, which is a compilation that no project file
    /// described. Such a compilation is one project, so every <c>internal</c> in it reaches
    /// every other — the rule does not change, only how many projects there are to have.</para>
    /// </summary>
    public string Project { get; internal set; } = string.Empty;

    /// <summary>
    /// How far this type can be seen. A type says nothing far more often than it says
    /// <c>public</c>, so silence means the project rather than the world.
    /// </summary>
    public Visibility Visibility => Modifiers.OfType();

    /// <summary>Members declared directly on this type, keyed by name.</summary>
    public Dictionary<string, List<Symbol>> Members { get; } = new(StringComparer.Ordinal);

    public override string Display => Name;

    /// <summary>Records a member, allowing several to share a name so that overloads work.</summary>
    internal void AddMember(Symbol member)
    {
        member.DeclaringType = this;

        if (!Members.TryGetValue(member.Name, out List<Symbol>? existing))
        {
            existing = [];
            Members[member.Name] = existing;
        }

        existing.Add(member);
    }

    /// <summary>Every member with a given name declared directly on this type.</summary>
    public IReadOnlyList<Symbol> Lookup(string name) =>
        Members.TryGetValue(name, out List<Symbol>? found) ? found : [];
}

/// <summary>A model: a reference type with single inheritance and virtual dispatch.</summary>
public sealed class ModelSymbol(string name, DeclarationModifiers modifiers)
    : DeclaredTypeSymbol(name, modifiers)
{
    /// <summary>
    /// The model this extends. Null only for the root, since every model extends
    /// <c>Model</c> implicitly.
    /// </summary>
    public ModelSymbol? BaseType { get; internal set; }

    /// <summary>True when this cannot be extended.</summary>
    public bool IsSealed => Modifiers.Has(DeclarationModifiers.Sealed);

    /// <summary>True when this cannot be instantiated.</summary>
    public bool IsAbstract => Modifiers.Has(DeclarationModifiers.Abstract);

    /// <summary>True for a <c>global model</c>, which has no instances and only global members.</summary>
    public bool IsGlobal => Modifiers.Has(DeclarationModifiers.Global);

    public override bool IsValueType => false;

    public override string Kind => "model";

    /// <summary>This model and each of its ancestors, nearest first.</summary>
    public IEnumerable<ModelSymbol> SelfAndAncestors()
    {
        // Bounded rather than while-true: an inheritance cycle is reported separately, and
        // walking one here would hang instead of producing a diagnostic.
        HashSet<ModelSymbol> seen = [];

        for (ModelSymbol? current = this; current is not null; current = current.BaseType)
        {
            if (!seen.Add(current))
            {
                yield break;
            }

            yield return current;
        }
    }

    /// <summary>Finds a member on this model or any ancestor.</summary>
    public IReadOnlyList<Symbol> LookupIncludingBase(string name)
    {
        foreach (ModelSymbol model in SelfAndAncestors())
        {
            IReadOnlyList<Symbol> found = model.Lookup(name);

            if (found.Count > 0)
            {
                return found;
            }
        }

        return [];
    }
}

/// <summary>
/// A structure: a value type. It inherits <c>Model</c>'s members but never converts to
/// <c>Model</c>, since that conversion would be boxing.
/// </summary>
public sealed class StructureSymbol(string name, DeclarationModifiers modifiers)
    : DeclaredTypeSymbol(name, modifiers)
{
    public override bool IsValueType => true;

    public override string Kind => "structure";
}

/// <summary>An enumeration: an integer-backed value type with named members.</summary>
public sealed class EnumerationSymbol(string name, DeclarationModifiers modifiers)
    : DeclaredTypeSymbol(name, modifiers)
{
    public override bool IsValueType => true;

    public override string Kind => "enumeration";
}

/// <summary>One member of an enumeration.</summary>
public sealed class EnumMemberSymbol(string name, EnumerationSymbol owner, long value)
    : Symbol(name)
{
    public EnumerationSymbol Owner { get; } = owner;

    public long Value { get; } = value;

    public override string Kind => "enumeration member";
}

/// <summary>A field on a model or structure.</summary>
public sealed class FieldSymbol(
    string name,
    TypeSymbol type,
    DeclarationModifiers modifiers) : Symbol(name)
{
    /// <summary>
    /// The declared type. Collected as a placeholder and settled once every type is known,
    /// because a field may name a type declared after it, or in another file.
    /// </summary>
    public TypeSymbol Type { get; internal set; } = type;

    public DeclarationModifiers Modifiers { get; } = modifiers;

    public bool IsGlobal => Modifiers.Has(DeclarationModifiers.Global);

    public bool IsConstant => Modifiers.Has(DeclarationModifiers.Constant);

    /// <summary>How far this field can be seen. Silence means the type that declares it.</summary>
    public Visibility Visibility => Modifiers.OfMember();

    public override string Kind => IsConstant ? "constant" : "field";
}

/// <summary>A function, whether a member, a local function, or a constructor.</summary>
public sealed class FunctionSymbol(
    string name,
    TypeSymbol? returnType,
    IReadOnlyList<ParameterSymbol> parameters,
    DeclarationModifiers modifiers) : Symbol(name)
{
    /// <summary>
    /// Null when the function yields nothing. Settled once every type is known, for the same
    /// reason a field's type is.
    /// </summary>
    public TypeSymbol? ReturnType { get; internal set; } = returnType;

    public IReadOnlyList<ParameterSymbol> Parameters { get; } = parameters;

    public DeclarationModifiers Modifiers { get; } = modifiers;

    public bool IsGlobal => Modifiers.Has(DeclarationModifiers.Global);

    public bool IsVirtual => Modifiers.Has(DeclarationModifiers.Virtual);

    public bool IsOverride => Modifiers.Has(DeclarationModifiers.Override);

    /// <summary>
    /// Declared without a body, for a descendant to supply. An abstract function is offered for
    /// overriding by being abstract, so it needs no <c>virtual</c> beside it.
    /// </summary>
    public bool IsAbstract => Modifiers.Has(DeclarationModifiers.Abstract);

    /// <summary>Whether a descendant may override this. Three words say so, and any one is enough.</summary>
    public bool IsOverridable => IsVirtual || IsOverride || IsAbstract;

    /// <summary>
    /// <para>How far this function can be seen. Silence means the type that declares it — or,
    /// where the function is abstract, that type and everything extending it.</para>
    /// <para>A declaration with no visibility on it belongs to the smallest thing that could
    /// own it. For an abstract function the declaring type is not that thing: nothing there
    /// writes the function, so a descendant has to, and one that cannot see it could never
    /// oblige. Protected is the narrowest reach the word admits, which makes it what the word
    /// means rather than something to write beside it.</para>
    /// </summary>
    public Visibility Visibility =>
        IsAbstract && Modifiers.OfMember() == Visibility.Private
            ? Visibility.Protected
            : Modifiers.OfMember();

    /// <summary>
    /// True when this is a constructor: its name matches its type and it declares no return
    /// type. Nothing in the syntax marks one, so this is where the two are told apart.
    /// </summary>
    public bool IsConstructor { get; internal set; }

    public override string Kind => IsConstructor ? "constructor" : "function";

    /// <summary>This function's type, for when it is used as a value.</summary>
    public FunctionType AsType() =>
        new(ReturnType, [.. Parameters.Select(p => p.Type)]);
}

/// <summary>One parameter of a function or lambda.</summary>
public sealed class ParameterSymbol(string name, TypeSymbol type) : Symbol(name)
{
    /// <summary>
    /// The declared type. Settled once every type is known, for the same reason a field's
    /// type is.
    /// </summary>
    public TypeSymbol Type { get; internal set; } = type;

    public override string Kind => "parameter";
}

/// <summary>A variable declared in a function body.</summary>
public sealed class LocalSymbol(string name, TypeSymbol type, bool isConstant) : Symbol(name)
{
    /// <summary>The declared or inferred type. Inference happens while type checking.</summary>
    public TypeSymbol Type { get; internal set; } = type;

    public bool IsConstant { get; } = isConstant;

    /// <summary>
    /// True for a loop variable, which is read-only inside the body. Assigning to one would
    /// change only that iteration's copy, since each iteration binds a fresh variable, and a
    /// silent no-op is the worst outcome for someone learning.
    /// </summary>
    public bool IsLoopVariable { get; init; }

    public override string Kind => IsLoopVariable ? "loop variable" : IsConstant ? "constant" : "local";
}
