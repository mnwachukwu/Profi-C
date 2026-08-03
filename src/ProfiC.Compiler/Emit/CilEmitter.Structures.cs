using System.Reflection;
using System.Reflection.Emit;
using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Semantics;

namespace ProfiC.Compiler.Emit;

/// <summary>
/// <para>Structures, which are values: assigning one copies it, and the copy has nothing to do
/// with the original afterwards.</para>
///
/// <para><b>A structure is emitted as a sealed class, and the copying is done by hand.</b> That
/// is the surprising choice in this file and the rest of it follows from it, so the reasoning is
/// written down here rather than left to be rediscovered.</para>
///
/// <para>The obvious emission is a CLR value type. It is what C# uses for the same idea, the
/// runtime does the copying, and <c>Point b = a</c> would need no instructions at all beyond the
/// store. <b>One thing the language does cannot be done that way</b>, and it is reachable from a
/// three-line program:</para>
///
/// <para><b>Mutating one through a set index.</b> <c>grid[0].X = 99</c> changes the point in the
/// set. A <see cref="Runtime.ProfiCSet{T}"/> keeps its elements in a list, and a list's indexer
/// hands back a <em>copy</em> of a value type — C# refuses <c>list[0].X = 99</c> outright for
/// exactly this reason, because the write would land on a temporary and be lost. There is no
/// address to write through and a list has none to give. Emitted as a value type the line
/// compiles, runs, and silently does nothing.</para>
///
/// <para>That was checked against the interpreter rather than reasoned about, and the interpreter
/// is what the language means. It cannot be fixed while the emission is a value type, since it
/// needs an address a list cannot produce.</para>
///
/// <para><b>Being a reference underneath is not otherwise observable</b>, and is deliberately kept
/// so. <c>Reference.Equals</c> would give it away — a class answers about a structure and its copy
/// where a value type has no identity to answer with — which is why asking that of a value is
/// <c>PC0347</c> rather than a question with an answer. Nothing else can see the difference:
/// assignment copies, a <c>Model</c>-typed slot still refuses one, and nothing boxes.</para>
///
/// <para><b>What it costs to do it this way</b> is that copying becomes the emitter's job rather
/// than the runtime's, and the places a copy is due have to be exhaustive — see
/// <see cref="EmitValueInto"/>, which is the whole of that list. Miss one and a program aliases
/// where it was promised a copy, which shows up as a value changing behind a reader's back rather
/// than as anything that fails. Against that: the interpreter already enumerates the same places,
/// the two are compared on every sample, and a structure is not allocated often enough for the
/// extra object to matter in a teaching language.</para>
///
/// <para>Two alternatives were weighed and are recorded so they are not re-derived. Giving
/// <c>ProfiCSet</c> a <c>ref</c> accessor fixes the index case and not identity. Rewriting
/// <c>set[i].f = v</c> into read, change, write back also fixes only the first — and gives up the
/// automatic copying that was the reason to want a value type at all.</para>
/// </summary>
public sealed partial class CilEmitter
{
    /// <summary>What the copy is called. Not a name a program can write, so nothing collides.</summary>
    private const string CopyName = "<copy>";

    /// <summary>The copy made for each structure, and the constructor it allocates through.</summary>
    private readonly Dictionary<DeclaredTypeSymbol, MethodBuilder> _copies = [];

    private readonly Dictionary<DeclaredTypeSymbol, ConstructorBuilder> _bareConstructors = [];

    /// <summary>
    /// <para>Declares a structure's copy, and the empty constructor it allocates through.</para>
    /// <para>A constructor of its own because the one a program wrote takes arguments, and a copy
    /// has no arguments to give it — every field is about to be overwritten anyway. Private, so
    /// that nothing but the copy can make a structure with none of its fields settled.</para>
    /// </summary>
    private void DefineCopy(Shaped declaration)
    {
        if (_model.GetSymbol(declaration.Node) is not DeclaredTypeSymbol owner
            || !_types.TryGetValue(owner, out TypeBuilder? type))
        {
            return;
        }

        _bareConstructors[owner] = type.DefineConstructor(
            MethodAttributes.Private | MethodAttributes.SpecialName
            | MethodAttributes.RTSpecialName,
            CallingConventions.Standard,
            Type.EmptyTypes);

        _copies[owner] = type.DefineMethod(
            CopyName,
            MethodAttributes.Public | MethodAttributes.HideBySig,
            type,
            Type.EmptyTypes);
    }

    /// <summary>
    /// <para>Fills in the copy: a fresh instance, then every field.</para>
    /// <para><b>A field holding a structure is copied in turn; one holding a model is not.</b>
    /// That single line is the whole of what makes a pin copy its point and share its marker, and
    /// it is the same rule the interpreter follows — a value is copied all the way down, and a
    /// reference is copied as a reference.</para>
    /// <para>Shared fields are left out for the reason they are left out of equality: they belong
    /// to the type rather than to either instance, so there is nothing about them to copy.</para>
    /// </summary>
    private void EmitCopy(Shaped declaration)
    {
        if (_model.GetSymbol(declaration.Node) is not DeclaredTypeSymbol owner
            || !_copies.TryGetValue(owner, out MethodBuilder? copy))
        {
            return;
        }

        ILGenerator il = copy.GetILGenerator();

        // The empty constructor still has to reach System.Object's, which the CLR requires of
        // every constructor and will not verify one without.
        ILGenerator bare = _bareConstructors[owner].GetILGenerator();
        bare.Emit(OpCodes.Ldarg_0);
        bare.Emit(OpCodes.Call, ObjectConstructor);
        bare.Emit(OpCodes.Ret);

        il.Emit(OpCodes.Newobj, _bareConstructors[owner]);

        foreach (FieldSymbol field in CopiedMembersOf(owner))
        {
            FieldBuilder slot = _fields[field];

            // The new instance is left on the stack throughout and duplicated per field, which
            // is what makes the whole method one expression with no local at all.
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, slot);

            if (IsAStructure(field.Type) && _copies.TryGetValue((DeclaredTypeSymbol)field.Type,
                                                               out MethodBuilder? nested))
            {
                il.Emit(OpCodes.Callvirt, nested);
            }

            il.Emit(OpCodes.Stfld, slot);
        }

        il.Emit(OpCodes.Ret);
    }

    /// <summary>Every field a copy carries over, which is every one that is not shared.</summary>
    private static IEnumerable<FieldSymbol> CopiedMembersOf(DeclaredTypeSymbol owner) =>
        owner.Members.Values
             .SelectMany(group => group)
             .OfType<FieldSymbol>()
             .Where(field => !field.IsShared)
             .OrderBy(field => field.Declaration?.Span.Start.Offset ?? 0)
             .ThenBy(field => field.Name, StringComparer.Ordinal);

    private static bool IsAStructure(TypeSymbol? type) => type is StructureSymbol;

    /// <summary>
    /// <para>Pushes a value into a place that keeps it, copying it first where it is a
    /// structure.</para>
    /// <para><b>This is the list.</b> A structure is copied where it is stored and where it is
    /// passed — a declaration's initializer, an assignment, an argument, an element of a set, a
    /// field, and a yield. Everywhere else it is read rather than kept, and reading must
    /// <em>not</em> copy: <c>grid[0].X = 99</c> and <c>copy.Where.X = 5</c> both reach through a
    /// read to change the thing that was read, which is what the language means by them.</para>
    /// <para>So the rule is not "copy a structure whenever it is met". It is "copy it when
    /// something is about to hold on to it", and the difference between those two is every
    /// behavior in <c>samples/structures.pc</c>.</para>
    /// </summary>
    private void EmitValueInto(Expression source)
    {
        EmitExpression(source);

        if (IsAStructure(_model.GetType(source))
            && _copies.TryGetValue((DeclaredTypeSymbol)_model.GetType(source)!,
                                   out MethodBuilder? copy))
        {
            _il.Emit(OpCodes.Callvirt, copy);
        }
    }
}
