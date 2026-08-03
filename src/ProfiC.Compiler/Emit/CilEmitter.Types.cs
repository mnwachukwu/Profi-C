using System.Reflection.Emit;
using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Semantics;

namespace ProfiC.Compiler.Emit;

/// <summary>
/// <para>Asking what a value turned out to be: <c>is</c> and <c>as</c>.</para>
/// <para><b>Some of these are not questions at run time.</b> The checker settles a test it can
/// already answer — <c>shape is Shape</c> is always true, <c>square is Circle</c> never — and
/// records which. Both engines read that record rather than working it out again, which is what
/// keeps them agreeing about the tests a value could not answer for itself: a set carries no
/// element type and a function no signature, so <c>things is integer[]</c> has to be settled while
/// compiling or not asked at all.</para>
/// <para>The operand is evaluated either way. A test that was settled still runs whatever was
/// written on the left of it, and dropping the value is not the same as never producing it.</para>
/// </summary>
public sealed partial class CilEmitter
{
    /// <summary>
    /// <c>is</c>, which is <c>isinst</c> and a comparison against nothing — the instruction gives
    /// back the value seen as the type, or null where it is not one.
    /// </summary>
    private void EmitTypeTest(TypeTestExpr test)
    {
        if (_model.GetSettledTest(test) is { } settled)
        {
            EmitDiscarded(test.Operand);
            _il.Emit(settled ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            return;
        }

        EmitExpression(test.Operand);
        _il.Emit(OpCodes.Isinst, TestedAgainst(test.TargetType));
        _il.Emit(OpCodes.Ldnull);
        _il.Emit(OpCodes.Cgt_Un);
    }

    /// <summary>
    /// <para><c>as</c>, which yields <c>T?</c> rather than failing.</para>
    /// <para>There is no null in this language for a mismatch to produce, so an optional is the
    /// natural result and no machinery is needed beyond the one that already holds it.</para>
    /// </summary>
    private void EmitTypeCast(TypeCastExpr cast)
    {
        Type built = _model.GetType(cast) is OptionalType optional
            ? TypeOf(optional, "an optional")
            : throw Unhandled("an 'as' that does not yield an optional");

        switch (_model.GetSettledTest(cast))
        {
            // Always. The value is already what was asked for, so it is wrapped where it stands
            // and nothing is tested — which also covers a value type, where there is nothing for
            // 'isinst' to be asked of.
            case true:
                EmitExpression(cast.Operand);
                _il.Emit(OpCodes.Call, OptionalMethod(built, "Of"));
                return;

            // Never. The value is produced and let go, and the answer is nothing.
            case false:
                EmitDiscarded(cast.Operand);
                EmitEmptyOptional(built);
                return;
        }

        // An ordinal being asked whether it names a member, which is a different question from
        // every other 'as' and has its own sequence.
        if (_model.GetType(cast.TargetType) is EnumerationSymbol enumeration)
        {
            EmitIntegerAsEnumeration(cast, TypeOf(enumeration, enumeration.Name), built);
            return;
        }

        Label missed = _il.DefineLabel();
        Label done = _il.DefineLabel();

        EmitExpression(cast.Operand);
        _il.Emit(OpCodes.Isinst, TestedAgainst(cast.TargetType));

        // The tested value is kept rather than produced twice: the operand may be a call, and
        // asking it again would run it again.
        _il.Emit(OpCodes.Dup);
        _il.Emit(OpCodes.Brfalse, missed);

        _il.Emit(OpCodes.Call, OptionalMethod(built, "Of"));
        _il.Emit(OpCodes.Br, done);

        _il.MarkLabel(missed);
        _il.Emit(OpCodes.Pop);
        EmitEmptyOptional(built);

        _il.MarkLabel(done);
    }

    /// <summary>
    /// <para>An optional holding nothing.</para>
    /// <para>Written as a struct left at its default rather than as a call to <c>Empty</c>,
    /// because that is what <c>Empty</c> is — and it spares the emitter reaching for a property on
    /// a type that may not exist yet.</para>
    /// </summary>
    private void EmitEmptyOptional(Type built)
    {
        LocalBuilder nothing = _il.DeclareLocal(built);

        _il.Emit(OpCodes.Ldloca, nothing);
        _il.Emit(OpCodes.Initobj, built);
        _il.Emit(OpCodes.Ldloc, nothing);
    }

    /// <summary>The type a test names, as a CLR type to hand to <c>isinst</c>.</summary>
    private Type TestedAgainst(TypeSyntax written) =>
        _model.GetType(written) is { } target
            ? TypeOf(target, "the type of a test")
            : throw Unhandled("a test against a type that resolved to nothing");
}
