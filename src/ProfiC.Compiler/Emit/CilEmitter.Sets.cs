using System.Reflection;
using System.Reflection.Emit;
using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Semantics;

namespace ProfiC.Compiler.Emit;

/// <summary>
/// <para>Sets, which become <see cref="Runtime.ProfiCSet{T}"/> — the very type the interpreter
/// holds.</para>
/// <para>Sharing it rather than emitting a CLR array is what keeps the two engines agreeing
/// about the awkward parts: what <c>Remove</c> does to the order, what happens when a walk is
/// interrupted, and what a set prints as. An array could not serve in any case, since inserting
/// and removing are part of a Profi-C set's surface and an array's length is fixed.</para>
/// <para><b>Positions are converted at this boundary.</b> Profi-C counts with <c>integer</c>,
/// which is 64 bits; <c>List{T}</c> indexes with 32. Every index narrows on the way in and every
/// count widens on the way out, in one place, so no other part of the emitter has to remember
/// which side of the boundary it is on.</para>
/// </summary>
public sealed partial class CilEmitter
{
    /// <summary>
    /// <para>Builds a set from a literal: an empty one, then each element added in order.</para>
    /// <para>Written as a run of <c>Insert</c> calls rather than through the constructor taking a
    /// sequence, because building the sequence would mean emitting an array first — the same work
    /// plus one allocation nobody reads.</para>
    /// </summary>
    private void EmitCollection(CollectionExpr collection)
    {
        if (_model.GetType(collection) is not SetType set)
        {
            throw Unhandled("a set with no type");
        }

        Type built = TypeOf(set, "a set");

        _il.Emit(OpCodes.Newobj, SetConstructor(built));

        foreach (Expression element in collection.Elements)
        {
            // The set stays on the stack under each element, so one literal is one value at the
            // end however many elements it held.
            _il.Emit(OpCodes.Dup);
            EmitValueInto(element);
            _il.Emit(OpCodes.Callvirt, SetMethod(built, "Insert"));
        }
    }

    /// <summary>Reads one element: the set, the index narrowed, then the indexer.</summary>
    private void EmitIndexRead(IndexExpr index)
    {
        // A string is indexed too, and is not the runtime's set: it answers a position by its
        // own route, and out of range in the language's words rather than the platform's. The
        // position stays 64 bits on the way in, since the runtime takes it as an integer is.
        if (IsString(index.Receiver))
        {
            EmitExpression(index.Receiver);
            EmitExpression(index.Index);

            _il.Emit(OpCodes.Call, TextAt);
            return;
        }

        Type built = SetTypeOf(index.Receiver);

        EmitExpression(index.Receiver);
        EmitIndexValue(index.Index);

        _il.Emit(OpCodes.Callvirt, SetMethod(built, "get_Item"));
    }

    /// <summary>Writes one element. The value is emitted last, as the indexer's setter wants it.</summary>
    private void EmitAssignToIndex(IndexExpr index, Expression value)
    {
        Type built = SetTypeOf(index.Receiver);

        EmitExpression(index.Receiver);
        EmitIndexValue(index.Index);
        EmitValueInto(value);

        _il.Emit(OpCodes.Callvirt, SetMethod(built, "set_Item"));
    }

    /// <summary>
    /// <para>A <c>loop each</c>, which by this point is an index loop with a mark around it.</para>
    /// <para>The mark is what lets the set refuse to be changed mid-walk. It is paired in a
    /// <c>finally</c> so that leaving the loop by <c>break</c>, by a <c>yield</c>, or by an
    /// exception still unmarks it — a set left marked would refuse every later change, and the
    /// program would fail somewhere with no walk in sight.</para>
    /// </summary>
    private void EmitWalk(WalkStmt walk)
    {
        Type built = SetTypeOf(walk.Sequence);

        // The sequence is a name by now, so evaluating it twice is a read, not work repeated.
        EmitExpression(walk.Sequence);
        _il.Emit(OpCodes.Callvirt, SetMethod(built, "BeginWalk"));

        _il.BeginExceptionBlock();
        _protection++;

        EmitStatements([walk.Body]);

        _il.BeginFinallyBlock();

        EmitExpression(walk.Sequence);
        _il.Emit(OpCodes.Callvirt, SetMethod(built, "EndWalk"));

        _protection--;
        _il.EndExceptionBlock();
    }

    /// <summary>
    /// The set type an expression has. Asked of the semantic model rather than worked out from
    /// what was emitted, since only the model knows what a name was declared as.
    /// </summary>
    private Type SetTypeOf(Expression expression) =>
        _model.GetType(expression) is SetType set
            ? TypeOf(set, "a set")
            : throw Unhandled("indexing something that is not a set");

    /// <summary>
    /// An index, narrowed to what the CLR indexes with. Profi-C counts in 64 bits and a list
    /// addresses in 32, and this is the only place that difference is spelled out.
    /// </summary>
    private void EmitIndexValue(Expression index)
    {
        EmitExpression(index);
        _il.Emit(OpCodes.Conv_I4);
    }

    /// <summary>
    /// <para>The members of a set the emitter has a sequence for.</para>
    /// <para>Everything here is one call on the runtime's own set. What is missing —
    /// <c>Subset</c>, <c>Union</c>, <c>Distinct</c> and the rest — is missing because the
    /// runtime does not have it either: those live in the interpreter today, and emitting them
    /// would mean writing a second implementation for the two engines to disagree about. Moving
    /// them into the runtime is what closes that, and it closes it for both at once.</para>
    /// </summary>
    /// <param name="member">The member as written, whose receiver is the set.</param>
    /// <param name="arguments">
    /// What the call passed, empty for a member that is read rather than called — <c>Count</c> is
    /// a value and arrives with none.
    /// </param>
    /// <param name="id">Which member the checker settled on.</param>
    private void EmitSetMember(MemberExpr member, IReadOnlyList<Expression> arguments, BuiltInId id)
    {
        Type built = SetTypeOf(member.Receiver);

        EmitExpression(member.Receiver);

        switch (id)
        {
            case BuiltInId.SetCount:
                _il.Emit(OpCodes.Callvirt, SetMethod(built, "get_Count"));
                _il.Emit(OpCodes.Conv_I8);
                return;

            case BuiltInId.SetInsert:
                EmitExpression(arguments[0]);
                _il.Emit(OpCodes.Callvirt, SetMethod(built, "Insert"));
                return;

            case BuiltInId.SetInsertAt:
                EmitIndexValue(arguments[0]);
                EmitExpression(arguments[1]);
                _il.Emit(OpCodes.Callvirt, SetMethod(built, "InsertAt"));
                return;

            case BuiltInId.SetRemove:
                EmitExpression(arguments[0]);
                _il.Emit(OpCodes.Callvirt, SetMethod(built, "Remove"));
                return;

            case BuiltInId.SetRemoveAt:
                EmitIndexValue(arguments[0]);
                _il.Emit(OpCodes.Callvirt, SetMethod(built, "RemoveAt"));
                return;

            case BuiltInId.SetContains:
                EmitExpression(arguments[0]);
                _il.Emit(OpCodes.Callvirt, SetMethod(built, "Contains"));
                return;

            case BuiltInId.SetIndexOf:
                EmitExpression(arguments[0]);
                _il.Emit(OpCodes.Callvirt, SetMethod(built, "IndexOf"));
                _il.Emit(OpCodes.Conv_I8);
                return;

            case BuiltInId.SetClear:
                _il.Emit(OpCodes.Callvirt, SetMethod(built, "Clear"));
                return;

            // Two overloads of one name, so each is reached by how many it takes rather than by
            // the name alone.
            case BuiltInId.SetSubsetFrom:
                EmitIndexValue(arguments[0]);
                _il.Emit(OpCodes.Callvirt, SetMethod(built, "Subset", taking: 1));
                return;

            case BuiltInId.SetSubsetBetween:
                EmitIndexValue(arguments[0]);
                EmitIndexValue(arguments[1]);
                _il.Emit(OpCodes.Callvirt, SetMethod(built, "Subset", taking: 2));
                return;

            case BuiltInId.SetUnion:
                EmitExpression(arguments[0]);
                _il.Emit(OpCodes.Callvirt, SetMethod(built, "Union"));
                return;

            case BuiltInId.SetIntersect:
                EmitExpression(arguments[0]);
                _il.Emit(OpCodes.Callvirt, SetMethod(built, "Intersect"));
                return;

            case BuiltInId.SetExcept:
                EmitExpression(arguments[0]);
                _il.Emit(OpCodes.Callvirt, SetMethod(built, "Except"));
                return;

            case BuiltInId.SetDistinct:
                _il.Emit(OpCodes.Callvirt, SetMethod(built, "Distinct"));
                return;

            case BuiltInId.SetJoin:
                EmitExpression(arguments[0]);
                _il.Emit(OpCodes.Callvirt, SetMethod(built, "Join"));
                return;

            case BuiltInId.SetTrim:
                _il.Emit(OpCodes.Callvirt, SetMethod(built, "Trim"));
                return;

            case BuiltInId.SetTrimStart:
                _il.Emit(OpCodes.Callvirt, SetMethod(built, "TrimStart"));
                return;

            case BuiltInId.SetTrimEnd:
                _il.Emit(OpCodes.Callvirt, SetMethod(built, "TrimEnd"));
                return;

            // The one that answers with a different kind of set than it was asked of, so it is a
            // call to a method of its own rather than one on the set.
            case BuiltInId.SetTrimAll:
                _il.Emit(OpCodes.Call, WithoutEmpties(built));
                return;

            default:
                throw Unhandled($"the set member '{id}'");
        }
    }

    // ---- Reaching a member of a type that does not exist yet ------------------------------

    /// <summary>
    /// <para>A method on a constructed set, reached in whichever way its element type allows.
    /// </para>
    /// <para>A set of something the CLR already has — <c>integer[]</c> — is an ordinary
    /// constructed type and answers to ordinary reflection. A set of a model this build is in
    /// the middle of writing is not: its element type is a builder for a type that does not
    /// exist, so nothing can be looked up on it and <see cref="TypeBuilder.GetMethod"/> is the
    /// only way to name the member. Which of the two it is decides which call is legal, and
    /// using the wrong one throws rather than producing a wrong answer.</para>
    /// </summary>
    /// <param name="built">The constructed set type.</param>
    /// <param name="name">The member's name.</param>
    /// <param name="taking">
    /// How many parameters, for a name with more than one form. <c>Subset</c> is the only one,
    /// and its two forms differ in nothing else.
    /// </param>
    private static MethodInfo SetMethod(Type built, string name, int? taking = null)
    {
        MethodInfo definition = typeof(Runtime.ProfiCSet<>)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(method => method.Name == name
                              && (taking is null || method.GetParameters().Length == taking));

        return HoldsATypeBeingBuilt(built)
            ? TypeBuilder.GetMethod(built, definition)
            : definition.DeclaringType == built.GetGenericTypeDefinition()
                ? built.GetMethod(name, [.. definition.GetParameters()
                                              .Select(p => Substituted(p.ParameterType, built))])!
                : built.GetMethod(name)!;
    }

    /// <summary>
    /// <para><c>TrimAll</c>, made for the value a set's optionals hold.</para>
    /// <para>The set is <c>ProfiCSet&lt;Optional&lt;V&gt;&gt;</c> and the answer is a
    /// <c>ProfiCSet&lt;V&gt;</c>, so what is needed is the <c>V</c> — one level in past the
    /// optional, rather than the set's own element type.</para>
    /// </summary>
    private static MethodInfo WithoutEmpties(Type built)
    {
        Type held = built.GetGenericArguments()[0].GetGenericArguments()[0];

        return typeof(Runtime.ProfiCSet)
            .GetMethod(nameof(Runtime.ProfiCSet.WithoutEmpties))!
            .MakeGenericMethod(held);
    }

    /// <summary>
    /// A parameter's type with the set's element type put in place of <c>T</c>. Needed to pick
    /// between two forms of one name on a closed type, where the parameters are what tell them
    /// apart and each is written in terms of the type argument.
    /// </summary>
    private static Type Substituted(Type parameter, Type built) =>
        parameter.IsGenericParameter
            ? built.GetGenericArguments()[parameter.GenericParameterPosition]
            : parameter.IsGenericType
                ? parameter.GetGenericTypeDefinition().MakeGenericType(
                    [.. parameter.GetGenericArguments().Select(a => Substituted(a, built))])
                : parameter;

    private static ConstructorInfo SetConstructor(Type built)
    {
        ConstructorInfo definition = typeof(Runtime.ProfiCSet<>)
            .GetConstructor(Type.EmptyTypes)!;

        return HoldsATypeBeingBuilt(built)
            ? TypeBuilder.GetConstructor(built, definition)
            : built.GetConstructor(Type.EmptyTypes)!;
    }

    /// <summary>
    /// Whether a constructed type reaches a type this build has not finished. Recursive, since a
    /// set of sets of a declared model hides one two levels down.
    /// </summary>
    private static bool HoldsATypeBeingBuilt(Type type) =>
        type is TypeBuilder
        || (type.IsGenericType && type.GetGenericArguments().Any(HoldsATypeBeingBuilt));
}
