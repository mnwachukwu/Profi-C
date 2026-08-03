using System.Reflection;
using System.Reflection.Emit;
using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Semantics;

namespace ProfiC.Compiler.Emit;

/// <summary>
/// <para>Functions as values, which become CLR delegates.</para>
/// <para><b>Nothing here knows what a lambda is.</b> Closure conversion has already turned every
/// function value into a function on a model, named through whatever it is bound to — a frame
/// where it captured something, and the file's shared model where it captured nothing. So what
/// reaches the emitter is a member being read rather than called, and what it has to build is the
/// pair the CLR represents that with: the receiver, and the address of the method.</para>
/// <para><b>A delegate type per shape, not per name.</b> Two Profi-C types that take and yield
/// the same things are the same type — <c>integer delegate(integer)</c> written in two files is
/// one type — so the delegate is keyed by the CLR signature and shared. Writing one per mention
/// would make two values of the same Profi-C type refuse to be assigned to each other.</para>
/// <para>The two members are declared and left empty on purpose. A delegate's constructor and its
/// <c>Invoke</c> are supplied by the runtime rather than written down, which is what
/// <see cref="MethodImplAttributes.Runtime"/> says; a body on either is what the CLR would
/// refuse.</para>
/// </summary>
public sealed partial class CilEmitter
{
    /// <summary>The delegate built for each CLR signature, so one serves every mention of it.</summary>
    private readonly Dictionary<string, TypeBuilder> _delegates = [];

    /// <summary>
    /// The delegate type a Profi-C function type becomes, made the first time that shape is
    /// wanted.
    /// </summary>
    private Type DelegateFor(FunctionType type)
    {
        Type answer = type.ReturnType is null
            ? typeof(void)
            : TypeOf(type.ReturnType, "what a delegate yields");

        Type[] taking = [.. type.ParameterTypes.Select(p => TypeOf(p, "what a delegate takes"))];

        string shape = string.Join(
            "|", new[] { answer.FullName ?? answer.Name }
                .Concat(taking.Select(t => t.FullName ?? t.Name)));

        if (_delegates.TryGetValue(shape, out TypeBuilder? already))
        {
            return already;
        }

        TypeBuilder built = _module.DefineType(
            NameFor(type),
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class,
            typeof(MulticastDelegate));

        ConstructorBuilder constructor = built.DefineConstructor(
            MethodAttributes.Public | MethodAttributes.HideBySig
            | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            CallingConventions.Standard,
            [typeof(object), typeof(IntPtr)]);

        constructor.SetImplementationFlags(
            MethodImplAttributes.Runtime | MethodImplAttributes.Managed);

        MethodBuilder invoke = built.DefineMethod(
            "Invoke",
            MethodAttributes.Public | MethodAttributes.HideBySig
            | MethodAttributes.NewSlot | MethodAttributes.Virtual,
            answer,
            taking);

        invoke.SetImplementationFlags(
            MethodImplAttributes.Runtime | MethodImplAttributes.Managed);

        _delegates[shape] = built;
        _delegateConstructors[built] = constructor;
        _delegateInvocations[built] = invoke;

        return built;
    }

    /// <summary>
    /// The two members of each delegate, held rather than looked up — a builder's members are not
    /// answerable by reflection until the type is created, and by then every body that needed one
    /// has been written.
    /// </summary>
    private readonly Dictionary<Type, ConstructorBuilder> _delegateConstructors = [];

    private readonly Dictionary<Type, MethodBuilder> _delegateInvocations = [];

    /// <summary>
    /// <para>What to call it, taken from how the type is written.</para>
    /// <para>Named for a reader of the assembly rather than for the emitter, which reaches it by
    /// signature and never by name. A number is added where two spellings meet in the same CLR
    /// shape, since a name has to be unique and being one short of it is a build that fails
    /// halfway.</para>
    /// </summary>
    private string NameFor(FunctionType type)
    {
        string wanted = "<delegate>" + new string(
            [.. type.Display.Where(c => char.IsLetterOrDigit(c) || c == '_')]);

        string name = wanted;

        for (int at = 2; _delegateNames.Contains(name); at++)
        {
            name = $"{wanted}${at}";
        }

        _delegateNames.Add(name);

        return name;
    }

    private readonly HashSet<string> _delegateNames = new(StringComparer.Ordinal);

    /// <summary>
    /// <para>Builds a function value: the thing it is bound to, and the method's address.</para>
    /// <para><c>ldvirtftn</c> where the method has a slot, so that a value made from
    /// <c>shape.Area</c> reaches the override rather than the version the parent wrote — the same
    /// question <c>callvirt</c> answers, asked at the moment the value is made instead of at the
    /// moment it is called. <c>dup</c> is what lets one receiver serve both, and the CLR requires
    /// the two instructions adjacent.</para>
    /// <para>A shared function is bound to nothing, which is what the null is: the delegate holds
    /// no receiver because a shared function has none to hold. This is the ordinary case after
    /// closure conversion, since a value that captured nothing is lifted onto a shared model.
    /// </para>
    /// </summary>
    private void EmitFunctionValue(Expression? receiver, FunctionSymbol function, TypeSymbol type)
    {
        if (type is not FunctionType shape)
        {
            throw Unhandled($"naming '{function.Name}' where no function type was expected");
        }

        if (!_functions.TryGetValue(function, out MethodBuilder? method))
        {
            throw Unhandled($"naming '{function.Name}', which was never defined");
        }

        Type built = DelegateFor(shape);

        if (method.IsStatic)
        {
            _il.Emit(OpCodes.Ldnull);
            _il.Emit(OpCodes.Ldftn, method);
        }
        else
        {
            EmitReceiver(receiver);

            if (method.IsVirtual)
            {
                _il.Emit(OpCodes.Dup);
                _il.Emit(OpCodes.Ldvirtftn, method);
            }
            else
            {
                _il.Emit(OpCodes.Ldftn, method);
            }
        }

        _il.Emit(OpCodes.Newobj, _delegateConstructors[built]);
    }

    /// <summary>
    /// <para>Calls a function value, which is <c>Invoke</c> on the delegate holding it.</para>
    /// <para>The value goes on the stack before the arguments, being the receiver of that call —
    /// and it is a call rather than a jump because what the value is bound to has to travel with
    /// it. That is the whole of what a delegate is.</para>
    /// </summary>
    private void EmitCallThroughValue(CallExpr call, FunctionType shape)
    {
        Type built = DelegateFor(shape);

        EmitExpression(call.Callee);
        EmitArguments(call.Arguments);

        _il.Emit(OpCodes.Callvirt, _delegateInvocations[built]);
    }
}
