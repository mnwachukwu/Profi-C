using System.Reflection;
using System.Reflection.Emit;
using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Semantics;

namespace ProfiC.Compiler.Emit;

/// <summary>
/// <para>Enumerations, which become CLR enumerations — the same thing C# writes, so a Profi-C
/// enumeration is one every .NET tool already understands.</para>
/// <para><b>A member is its ordinal and nothing more.</b> The CLR represents an enumeration as
/// its underlying number at run time, so <c>Suit.Hearts</c> compiles to the constant zero and
/// <c>suit == Suit.Hearts</c> compiles to <c>ceq</c>. The type exists in the metadata rather
/// than in the instructions, which is what makes <c>ToInteger</c> free and comparison exact.
/// </para>
/// <para>What the type is for is everything that happens away from the instructions: printing a
/// member by the name that was written, and answering whether an ordinal names one at all.</para>
/// </summary>
public sealed partial class CilEmitter
{
    /// <summary>
    /// <para>Defines an enumeration: the field holding the ordinal, then one constant per member.
    /// </para>
    /// <para><c>value__</c> is not a name chosen here. ECMA-335 fixes it, and a type deriving from
    /// <c>System.Enum</c> without exactly that field is one the runtime will not load — which
    /// surfaces as a <c>TypeLoadException</c> at the first use rather than as anything the build
    /// noticed.</para>
    /// <para>Backed by a 64-bit integer, because a Profi-C ordinal is an <c>integer</c> and that is
    /// what an integer is. <c>ToInteger</c> then has nothing to do, and no member can be declared
    /// with a value the type cannot hold.</para>
    /// </summary>
    private void DefineEnumeration(EnumerationDecl declaration)
    {
        if (_model.GetSymbol(declaration) is not EnumerationSymbol owner
            || _types.ContainsKey(owner))
        {
            return;
        }

        TypeBuilder type = _module.DefineType(
            owner.Name,
            TypeAttributes.Public | TypeAttributes.Sealed,
            typeof(Enum));

        type.DefineField(
            "value__",
            typeof(long),
            FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName);

        foreach (EnumMemberDecl written in declaration.Members)
        {
            if (_model.GetSymbol(written) is not EnumMemberSymbol member)
            {
                continue;
            }

            FieldBuilder literal = type.DefineField(
                member.Name,
                type,
                FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal);

            literal.SetConstant(member.Value);
        }

        _types[owner] = type;
    }

    /// <summary>
    /// A member, which is its ordinal. Nothing names the type: the value on the stack is the
    /// number, and what type it counts as is the metadata's to say rather than the stack's.
    /// </summary>
    private void EmitEnumerationMember(EnumMemberSymbol member) =>
        _il.Emit(OpCodes.Ldc_I8, member.Value);

    /// <summary>
    /// <para><c>n as Suit</c>, where <c>n</c> is an integer.</para>
    /// <para>The one shape of <c>as</c> that is not a question about what a value already is. An
    /// integer is not an enumeration member and never was; what is being asked is whether the
    /// enumeration has a member with that ordinal, which is a question about the type rather than
    /// about the value — so <c>isinst</c> has nothing to test and the answer comes from the
    /// metadata.</para>
    /// <para>This is why the language makes <c>as</c> yield an optional. There is no member for
    /// <c>17 as Suit</c> to be, and inventing one would let a number nobody named travel through
    /// a program as though it had a name.</para>
    /// </summary>
    private void EmitIntegerAsEnumeration(TypeCastExpr cast, Type enumeration, Type built)
    {
        LocalBuilder ordinal = _il.DeclareLocal(typeof(long));

        EmitExpression(cast.Operand);
        _il.Emit(OpCodes.Stloc, ordinal);

        Label missed = _il.DefineLabel();
        Label done = _il.DefineLabel();

        _il.Emit(OpCodes.Ldtoken, enumeration);
        _il.Emit(OpCodes.Call, TypeFromHandle);
        _il.Emit(OpCodes.Ldloc, ordinal);
        _il.Emit(OpCodes.Box, typeof(long));
        _il.Emit(OpCodes.Call, EnumIsDefined);
        _il.Emit(OpCodes.Brfalse, missed);

        // The ordinal itself, which is already what a member of this enumeration is.
        _il.Emit(OpCodes.Ldloc, ordinal);
        _il.Emit(OpCodes.Call, OptionalMethod(built, "Of"));
        _il.Emit(OpCodes.Br, done);

        _il.MarkLabel(missed);
        EmitEmptyOptional(built);

        _il.MarkLabel(done);
    }

    /// <summary>Whether a number names a member, which is what the metadata is asked.</summary>
    private static readonly MethodInfo EnumIsDefined =
        typeof(Enum).GetMethod(nameof(Enum.IsDefined), [typeof(Type), typeof(object)])!;
}
