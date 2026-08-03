using System.Reflection;
using System.Reflection.Emit;
using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Semantics;
using ProfiC.Runtime;

namespace ProfiC.Compiler.Emit;

/// <summary>
/// <para>Making an emitted model answer the runtime's questions about its own parts.</para>
/// <para><c>==</c> in Profi-C is deep and structural: two models holding equal fields are equal
/// without either of them having written anything to say so. The runtime does that walk, and it
/// reaches a value's parts through <see cref="IProfiCModel"/> — three members, because
/// <c>Equals(object)</c> has nowhere to carry the set of pairs already being compared and a
/// cycle-safe comparison cannot work without threading that through.</para>
/// <para>A set and an optional answer already, being the runtime's own types. A model is the
/// one shape the compiler makes, so it is the one the compiler has to implement.</para>
/// </summary>
public sealed partial class CilEmitter
{
    private static readonly MethodInfo TypeFromHandle =
        typeof(Type).GetMethod(nameof(Type.GetTypeFromHandle), [typeof(RuntimeTypeHandle)])!;

    private static readonly MethodInfo TypeIdentityGetter =
        typeof(IProfiCModel).GetProperty(nameof(IProfiCModel.DeepTypeIdentity))!.GetMethod!;

    private static readonly MethodInfo MemberCountGetter =
        typeof(IProfiCModel).GetProperty(nameof(IProfiCModel.DeepMemberCount))!.GetMethod!;

    private static readonly MethodInfo GetMemberMethod =
        typeof(IProfiCModel).GetMethod(nameof(IProfiCModel.GetDeepMember))!;

    /// <summary>
    /// <para>Written as explicit implementations, so that none of the three is reachable by name
    /// from a Profi-C program and a model declaring a member called <c>DeepMemberCount</c> takes
    /// nothing away.</para>
    /// </summary>
    private const MethodAttributes AnsweringTheRuntime =
        MethodAttributes.Private | MethodAttributes.Final | MethodAttributes.Virtual
        | MethodAttributes.NewSlot | MethodAttributes.HideBySig;

    /// <summary>
    /// <para>Gives one model the three members deep equality reads it through.</para>
    /// <para>A shared model is skipped: it is never instantiated, so there are never two of them
    /// to compare.</para>
    /// </summary>
    private void ImplementDeepEquality(Shaped declaration)
    {
        if (_model.GetSymbol(declaration.Node) is not DeclaredTypeSymbol owner
            || !_types.TryGetValue(owner, out TypeBuilder? type)
            || declaration.Modifiers.HasFlag(DeclarationModifiers.Shared))
        {
            return;
        }

        FieldBuilder[] parts = [.. DeepMembersOf(owner)];

        type.AddInterfaceImplementation(typeof(IProfiCModel));

        EmitTypeIdentity(type);
        EmitMemberCount(type, parts.Length);
        EmitGetMember(type, parts);
    }

    /// <summary>
    /// <para>Every field that takes part in equality, inherited ones first.</para>
    /// <para>Ordered by where each was declared, which totally orders them because a type is
    /// declared in exactly one place. What equality needs is only that both sides of a
    /// comparison agree, and two values of one type walk the same list by construction.</para>
    /// <para>A shared field is left out: it belongs to the type rather than to either value, so
    /// comparing it would compare a thing to itself.</para>
    /// </summary>
    private IEnumerable<FieldBuilder> DeepMembersOf(DeclaredTypeSymbol owner)
    {
        if (owner is ModelSymbol { BaseType: { } parent })
        {
            foreach (FieldBuilder inherited in DeepMembersOf(parent))
            {
                yield return inherited;
            }
        }

        IEnumerable<FieldSymbol> declared = owner.Members.Values
            .SelectMany(group => group)
            .OfType<FieldSymbol>()
            .Where(field => !field.IsShared)
            .OrderBy(field => field.Declaration?.Span.Start.Offset ?? 0)
            .ThenBy(field => field.Name, StringComparer.Ordinal);

        foreach (FieldSymbol field in declared)
        {
            // A field of a model this build did not write — the parent of a model extending a
            // built-in — has no builder, and nothing here can reach it.
            if (_fields.TryGetValue(field, out FieldBuilder? built))
            {
                yield return built;
            }
        }
    }

    /// <summary>
    /// <para>Which Profi-C type this is, as the emitted type itself.</para>
    /// <para>Belt and braces here rather than the whole answer: the runtime has already found
    /// the two host types equal by the time it asks, and every Profi-C type is emitted as a host
    /// type of its own. It is the interpreter that needs this, running every model as one class
    /// — and one question serving both is what keeps the two engines answering alike.</para>
    /// </summary>
    private static void EmitTypeIdentity(TypeBuilder type)
    {
        MethodBuilder getter = type.DefineMethod(
            "ProfiC.Runtime.IProfiCModel.get_DeepTypeIdentity",
            AnsweringTheRuntime | MethodAttributes.SpecialName,
            typeof(object),
            Type.EmptyTypes);

        ILGenerator il = getter.GetILGenerator();

        il.Emit(OpCodes.Ldtoken, type);
        il.Emit(OpCodes.Call, TypeFromHandle);
        il.Emit(OpCodes.Ret);

        type.DefineMethodOverride(getter, TypeIdentityGetter);
    }

    private static void EmitMemberCount(TypeBuilder type, int count)
    {
        MethodBuilder getter = type.DefineMethod(
            "ProfiC.Runtime.IProfiCModel.get_DeepMemberCount",
            AnsweringTheRuntime | MethodAttributes.SpecialName,
            typeof(int),
            Type.EmptyTypes);

        ILGenerator il = getter.GetILGenerator();

        il.Emit(OpCodes.Ldc_I4, count);
        il.Emit(OpCodes.Ret);

        type.DefineMethodOverride(getter, MemberCountGetter);
    }

    /// <summary>
    /// <para>One field by position, boxed, since the runtime reads them as objects.</para>
    /// <para>A jump table rather than a chain of comparisons, and an index outside the range
    /// answers nothing rather than throwing — which is what the interpreter does, and what lets
    /// the runtime walk without bounds-checking every read.</para>
    /// </summary>
    private static void EmitGetMember(TypeBuilder type, FieldBuilder[] parts)
    {
        MethodBuilder method = type.DefineMethod(
            "ProfiC.Runtime.IProfiCModel.GetDeepMember",
            AnsweringTheRuntime,
            typeof(object),
            [typeof(int)]);

        ILGenerator il = method.GetILGenerator();
        Label nothing = il.DefineLabel();
        Label[] arms = [.. parts.Select(_ => il.DefineLabel())];

        if (parts.Length > 0)
        {
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Switch, arms);
        }

        il.Emit(OpCodes.Br, nothing);

        for (int at = 0; at < parts.Length; at++)
        {
            il.MarkLabel(arms[at]);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, parts[at]);

            if (parts[at].FieldType.IsValueType)
            {
                il.Emit(OpCodes.Box, parts[at].FieldType);
            }

            il.Emit(OpCodes.Ret);
        }

        il.MarkLabel(nothing);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        type.DefineMethodOverride(method, GetMemberMethod);
    }
}
