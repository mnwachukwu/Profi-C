using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Semantics;
using ProfiC.Runtime;

namespace ProfiC.Interpreter;

public sealed partial class Interpreter
{
    /// <summary>
    /// <para>Runs a call.</para>
    /// <para>Three shapes reach here: a member call, which is the common one; a call on a
    /// function held in a variable; and <c>base(...)</c>, which runs a parent's constructor
    /// rather than calling anything.</para>
    /// </summary>
    private object? EvaluateCall(CallExpr call, Environment scope, Instance? receiver)
    {
        if (call.Callee is ReceiverExpr { Receiver: ReceiverKind.Base })
        {
            return RunBaseConstructor(call, scope, receiver);
        }

        if (call.Callee is MemberExpr member)
        {
            return EvaluateMemberCall(call, member, scope, receiver);
        }

        // A function held in a variable, including a local function or a lambda.
        object? callee = Evaluate(call.Callee, scope, receiver);
        List<object?> arguments = [.. call.Arguments.Select(a => Evaluate(a, scope, receiver))];

        return callee is FunctionValue function
            ? Invoke(function, call.Arguments, arguments)
            : throw new ProfiCRuntimeException("This is not something that can be called.");
    }

    private object? EvaluateMemberCall(
        CallExpr call,
        MemberExpr member,
        Environment scope,
        Instance? receiver)
    {
        List<object?> arguments = [.. call.Arguments.Select(a => Evaluate(a, scope, receiver))];

        // A type name on the left: either a built-in like Console, or a global function.
        if (member.Receiver is IdentifierExpr name
            && _model.GetSymbol(name) is DeclaredTypeSymbol declaringType)
        {
            if (BuiltInCall(declaringType.Name, member.MemberName, arguments) is { } handled)
            {
                return handled.Value;
            }

            if (_model.GetSymbol(member) is FunctionSymbol callee
                && BodyOf(callee) is { } global)
            {
                return Invoke(
                    new FunctionValue(global.Parameters, global.Body, null, _globals, null),
                    call.Arguments,
                    arguments);
            }

            return null;
        }

        object? target = Evaluate(member.Receiver, scope, receiver);

        // "base.Method()" runs the parent's version rather than the overriding one.
        if (member.Receiver is ReceiverExpr { Receiver: ReceiverKind.Base }
            && receiver is not null
            && _model.GetSymbol(member) is FunctionSymbol parent
            && BodyOf(parent) is { } parentMethod)
        {
            return Invoke(
                new FunctionValue(parentMethod.Parameters, parentMethod.Body, null, _globals, receiver),
                call.Arguments,
                arguments);
        }

        if (ValueMemberCall(target, member.MemberName, arguments) is { } result)
        {
            return result.Value;
        }

        if (target is Instance instance)
        {
            // Dispatch on the runtime type, so an override wins over the version the
            // declaring type wrote.
            if (FindMethod(instance.Type, member.MemberName, arguments.Count) is { } found
                && BodyOf(found) is { } method)
            {
                return Invoke(
                    new FunctionValue(method.Parameters, method.Body, null, _globals, instance),
                    call.Arguments,
                    arguments);
            }

            // A field holding a function value is called through, rather than dispatched to.
            if (_model.GetSymbol(member) is FieldSymbol field
                && instance.Fields.TryGetValue(field, out object? stored)
                && stored is FunctionValue held)
            {
                return Invoke(held, call.Arguments, arguments);
            }

            // Inherited from the built-in Exception, and reached only when the model did not
            // declare its own, so a program is free to say something better.
            if (member.MemberName == "Message" && BuiltInMembers.IsException(instance.Type))
            {
                return instance.Message ?? string.Empty;
            }
        }

        // Last, so that a model declaring its own Value or ToString wins over these. An
        // optional holding a model is the value itself, so both names can land on one target
        // and the declared one has to be the one that runs.
        if (UniversalMemberCall(target, member.MemberName, arguments) is { } fallback)
        {
            return fallback.Value;
        }

        throw new ProfiCRuntimeException(
            $"'{member.MemberName}' cannot be called on this value.");
    }

    /// <summary>
    /// Finds a method by name and arity, nearest ancestor first. Starting at the runtime type
    /// is what makes an override take effect.
    /// </summary>
    private static FunctionSymbol? FindMethod(DeclaredTypeSymbol type, string name, int arity)
    {
        IEnumerable<DeclaredTypeSymbol> chain = type is ModelSymbol model
            ? model.SelfAndAncestors()
            : [type];

        foreach (DeclaredTypeSymbol current in chain)
        {
            if (current.Lookup(name)
                    .OfType<FunctionSymbol>()
                    .FirstOrDefault(f => f.Parameters.Count == arity) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private object? RunBaseConstructor(CallExpr call, Environment scope, Instance? receiver)
    {
        if (receiver?.Type is not ModelSymbol { BaseType: { } parent })
        {
            return null;
        }

        List<object?> arguments = [.. call.Arguments.Select(a => Evaluate(a, scope, receiver))];

        if (FindConstructor(parent, arguments.Count) is { } constructor
            && BodyOf(constructor) is { } body)
        {
            RunConstructor(body, receiver, arguments);
            return null;
        }

        // Exception declares no constructor a program can see, so its one argument is taken
        // here. This is what makes base("...") in a declared exception reach Message().
        if (BuiltInMembers.IsException(parent) && arguments.Count == 1)
        {
            receiver.Message = AsText(arguments[0]);
        }

        return null;
    }

    // ---- The members the language provides -------------------------------------------------------

    /// <summary>
    /// <para>Members of the built-in models reached through a type name.</para>
    /// <para>Returns null when the name is not one of these, so the caller can carry on
    /// looking. A wrapped result is used rather than a bare one because several of these
    /// legitimately produce nothing.</para>
    /// </summary>
    private StrongBox<object?>? BuiltInCall(string typeName, string member, List<object?> arguments)
    {
        switch (typeName)
        {
            case "Console":
                switch (member)
                {
                    case "Write":
                        _output.Write(ModelOperations.ToDisplayString(arguments.FirstOrDefault()));
                        return new StrongBox<object?>(null);

                    case "WriteLine":
                        _output.WriteLine(arguments.Count == 0
                            ? string.Empty
                            : ModelOperations.ToDisplayString(arguments[0]));
                        return new StrongBox<object?>(null);

                    case "Read":
                        return new StrongBox<object?>(Console.ReadLine());
                }

                return null;

            case "Reference":
                return member == "Equals"
                    ? new StrongBox<object?>(
                        ReferenceEquals(arguments.ElementAtOrDefault(0), arguments.ElementAtOrDefault(1)))
                    : null;

            case "Math":
                return MathCall(member, arguments);

            default:
                return null;
        }
    }

    private static StrongBox<object?>? MathCall(string member, List<object?> arguments)
    {
        double First() => arguments.ElementAtOrDefault(0) is double d ? d : 0;
        double Second() => arguments.ElementAtOrDefault(1) is double d ? d : 0;

        return member switch
        {
            "Sqrt" => new StrongBox<object?>(Math.Sqrt(First())),
            "Abs" => new StrongBox<object?>(Math.Abs(First())),
            "Floor" => new StrongBox<object?>(Math.Floor(First())),
            "Ceiling" => new StrongBox<object?>(Math.Ceiling(First())),
            "Pow" => new StrongBox<object?>(Math.Pow(First(), Second())),
            _ => null,
        };
    }

    /// <summary>
    /// <para>Members the language provides on a value: a set's, a string's, an optional's.</para>
    /// <para>An optional is represented as the value itself, or nothing at all, so its three
    /// members are answered from that directly rather than from a wrapper.</para>
    /// </summary>
    private StrongBox<object?>? ValueMemberCall(
        object? target,
        string member,
        List<object?> arguments)
    {
        object? First() => arguments.Count > 0 ? arguments[0] : null;

        switch (target)
        {
            case ProfiCSet<object?> set:
                switch (member)
                {
                    case "Count": return new StrongBox<object?>((long)set.Count);
                    case "Insert": set.Insert(First()); return new StrongBox<object?>(null);
                    case "InsertAt":
                        set.InsertAt((int)AsInteger(First()), arguments.ElementAtOrDefault(1));
                        return new StrongBox<object?>(null);
                    case "Remove": return new StrongBox<object?>(set.Remove(First()));
                    case "RemoveAt":
                        set.RemoveAt((int)AsInteger(First()));
                        return new StrongBox<object?>(null);
                    case "Contains": return new StrongBox<object?>(set.Contains(First()));
                    case "IndexOf": return new StrongBox<object?>((long)set.IndexOf(First()));
                    case "Clear": set.Clear(); return new StrongBox<object?>(null);
                }

                break;

            case string text:
                switch (member)
                {
                    case "Count": return new StrongBox<object?>((long)text.Length);
                    case "Contains":
                        return new StrongBox<object?>(
                            text.Contains(AsText(First()), StringComparison.Ordinal));
                    case "IndexOf":
                        return new StrongBox<object?>(
                            (long)text.IndexOf(AsText(First()), StringComparison.Ordinal));
                    case "Substring":
                        return new StrongBox<object?>(Substring(text, arguments));
                    case "Insert":
                        return new StrongBox<object?>(text + AsText(First()));
                    case "InsertAt":
                        return new StrongBox<object?>(
                            text.Insert((int)AsInteger(First()), AsText(arguments.ElementAtOrDefault(1))));
                    case "Remove":
                        return new StrongBox<object?>(
                            text.Replace(AsText(First()), string.Empty, StringComparison.Ordinal));
                    case "RemoveAt":
                        return new StrongBox<object?>(text.Remove((int)AsInteger(First()), 1));
                }

                break;

            case Fraction fraction when member == "ToReal":
                return new StrongBox<object?>(fraction.ToReal());

            case double real when member == "ToFraction":
                return new StrongBox<object?>(Fraction.FromReal(real));

            case EnumValue enumeration when member == "ToInteger":
                return new StrongBox<object?>(enumeration.Ordinal);

            case Exception error when member == "Message":
                return new StrongBox<object?>(error.Message);
        }

        return null;
    }

    /// <summary>
    /// <para>The members that answer on any value at all: an optional's three, and the two
    /// every type inherits from <c>Model</c>.</para>
    /// <para>Tried last, after a declared member has had its chance, because a model may
    /// perfectly well declare its own <c>Value</c> or <c>ToString</c> and an optional holding
    /// that model is the model itself — one target, two meanings, and the declared one wins.
    /// Absence is represented by nothing at all, so a null target is the empty case rather than
    /// a mistake.</para>
    /// </summary>
    private static StrongBox<object?>? UniversalMemberCall(
        object? target,
        string member,
        List<object?> arguments) => member switch
    {
        "HasValue" => new StrongBox<object?>(target is not null),
        "Or" => new StrongBox<object?>(target ?? (arguments.Count > 0 ? arguments[0] : null)),
        "Value" => target is not null
            ? new StrongBox<object?>(target)
            : throw new EmptyOptionalException(),
        "ToString" => new StrongBox<object?>(ModelOperations.ToDisplayString(target)),
        "ToInteger" => new StrongBox<object?>(AsInteger(target)),
        _ => null,
    };

    private static string Substring(string text, List<object?> arguments)
    {
        int start = (int)AsInteger(arguments.ElementAtOrDefault(0));
        int length = arguments.Count > 1
            ? (int)AsInteger(arguments[1])
            : Math.Max(0, text.Length - start);

        if (start < 0 || start > text.Length || start + length > text.Length)
        {
            throw new IndexOutOfRangeException(
                $"Cannot take {length} characters from position {start} of a string of "
                + $"{text.Length}.");
        }

        return text.Substring(start, length);
    }

    private static string AsText(object? value) => ModelOperations.ToDisplayString(value);
}

/// <summary>
/// Wraps a result so that "produced nothing" can be told apart from "did not handle this".
/// </summary>
internal sealed class StrongBox<T>(T value)
{
    public T Value { get; } = value;
}
