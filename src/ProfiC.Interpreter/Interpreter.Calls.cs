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
            && _model.GetSymbol(name) is DeclaredTypeSymbol)
        {
            // Which version of an overloaded name this is was settled while checking, weighing
            // what the arguments actually are. Looking it up again by name would find the
            // first one written, so Math.Abs on a real would run the version taking integers.
            if (_model.GetBuiltIn(member) is { } onType)
            {
                return Perform(onType, target: null, arguments).Value;
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

        // The type checker already settled which member this name refers to, weighing the
        // receiver's type, what narrowing proved about it, and whether that type declares a
        // member of the same name. Deciding again from the value in hand would be answering a
        // different question.
        if (_model.GetBuiltIn(member) is { } id)
        {
            return Perform(id, target, arguments).Value;
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
    /// <para>Carries out a built-in call.</para>
    /// <para>A switch expression over the whole enumeration with no fallback arm, so that a
    /// member in the catalog with no implementation here is a build error rather than a call
    /// that quietly produces nothing.</para>
    /// <para>The target is the value on the left, and is null for a member reached through a
    /// model's name rather than through a value.</para>
    /// </summary>
    // CS8524 asks for a fallback arm covering values cast into the enumeration from outside
    // it. A fallback arm also satisfies CS8509, which is the warning that reports a catalog
    // member with no implementation here, so the narrower one is suppressed instead.
#pragma warning disable CS8524
    private StrongBox<object?> Perform(BuiltInId id, object? target, List<object?> arguments)
    {
        object? Argument(int index) => arguments.ElementAtOrDefault(index);
        double Real(int index) => Argument(index) is double d ? d : 0;
        long Integer(int index) => AsInteger(Argument(index));
        string Text(int index) => AsText(Argument(index));

        // An integer widens to a fraction on the way in, and the widening is recorded on the
        // argument rather than carried out here, so one that arrives whole is converted.
        Fraction Ratio(int index) => Argument(index) switch
        {
            Fraction fraction => fraction,
            long whole => Fraction.FromInteger(whole),
            _ => Fraction.Zero,
        };

        ProfiCSet<object?> Set() => (ProfiCSet<object?>)target!;
        string Subject() => (string)target!;

        return id switch
        {
            // ---- Reached through a model's name ------------------------------------------

            BuiltInId.ConsoleWrite =>
                Then(() => _output.Write(ModelOperations.ToDisplayString(Argument(0)))),

            BuiltInId.ConsoleWriteLine =>
                Then(() => _output.WriteLine(arguments.Count == 0
                    ? string.Empty
                    : ModelOperations.ToDisplayString(arguments[0]))),

            BuiltInId.ConsoleRead => new StrongBox<object?>(Console.ReadLine()),

            BuiltInId.ReferenceEquals =>
                new StrongBox<object?>(ReferenceEquals(Argument(0), Argument(1))),

            BuiltInId.MathSqrt => new StrongBox<object?>(Math.Sqrt(Real(0))),
            BuiltInId.MathPow => new StrongBox<object?>(Math.Pow(Real(0), Real(1))),

            BuiltInId.MathAbsInteger => new StrongBox<object?>(Math.Abs(Integer(0))),
            BuiltInId.MathAbsReal => new StrongBox<object?>(Math.Abs(Real(0))),
            BuiltInId.MathAbsFraction => new StrongBox<object?>(Fraction.Abs(Ratio(0))),

            // Each rounding lands on a whole number, so each yields an integer rather than a
            // real that happens to have nothing after the point.
            BuiltInId.MathFloorReal =>
                new StrongBox<object?>((long)Math.Floor(Real(0))),
            BuiltInId.MathFloorFraction =>
                new StrongBox<object?>(Fraction.Floor(Ratio(0))),
            BuiltInId.MathCeilingReal =>
                new StrongBox<object?>((long)Math.Ceiling(Real(0))),
            BuiltInId.MathCeilingFraction =>
                new StrongBox<object?>(Fraction.Ceiling(Ratio(0))),

            // A half goes away from zero, the rule taught in school. .NET rounds a half to the
            // even neighbor by default, so this says which it wants rather than taking it.
            BuiltInId.MathRoundReal =>
                new StrongBox<object?>((long)Math.Round(Real(0), MidpointRounding.AwayFromZero)),
            BuiltInId.MathRoundFraction =>
                new StrongBox<object?>(Fraction.Round(Ratio(0))),

            BuiltInId.MathMinInteger => new StrongBox<object?>(Math.Min(Integer(0), Integer(1))),
            BuiltInId.MathMinReal => new StrongBox<object?>(Math.Min(Real(0), Real(1))),
            BuiltInId.MathMinFraction =>
                new StrongBox<object?>(Ratio(0) <= Ratio(1) ? Ratio(0) : Ratio(1)),
            BuiltInId.MathMaxInteger => new StrongBox<object?>(Math.Max(Integer(0), Integer(1))),
            BuiltInId.MathMaxReal => new StrongBox<object?>(Math.Max(Real(0), Real(1))),
            BuiltInId.MathMaxFraction =>
                new StrongBox<object?>(Ratio(0) >= Ratio(1) ? Ratio(0) : Ratio(1)),

            BuiltInId.FractionCreate =>
                new StrongBox<object?>(new Fraction(Integer(0), Integer(1))),
            BuiltInId.FractionCreateWhole =>
                new StrongBox<object?>(Fraction.FromInteger(Integer(0))),

            // ---- Reached through a value --------------------------------------------------

            BuiltInId.SetCount => new StrongBox<object?>((long)Set().Count),
            BuiltInId.SetInsert => Then(() => Set().Insert(Argument(0))),
            BuiltInId.SetInsertAt => Then(() => Set().InsertAt((int)Integer(0), Argument(1))),
            BuiltInId.SetRemove => new StrongBox<object?>(Set().Remove(Argument(0))),
            BuiltInId.SetRemoveAt => Then(() => Set().RemoveAt((int)Integer(0))),
            BuiltInId.SetContains => new StrongBox<object?>(Set().Contains(Argument(0))),
            BuiltInId.SetIndexOf => new StrongBox<object?>((long)Set().IndexOf(Argument(0))),
            BuiltInId.SetClear => Then(Set().Clear),

            BuiltInId.StringCount => new StrongBox<object?>((long)Subject().Length),
            BuiltInId.StringContains => new StrongBox<object?>(
                Subject().Contains(Text(0), StringComparison.Ordinal)),
            BuiltInId.StringIndexOf => new StrongBox<object?>(
                (long)Subject().IndexOf(Text(0), StringComparison.Ordinal)),
            BuiltInId.StringSubstring => new StrongBox<object?>(Substring(Subject(), arguments)),
            BuiltInId.StringInsert => new StrongBox<object?>(Subject() + Text(0)),
            BuiltInId.StringInsertAt => new StrongBox<object?>(
                Subject().Insert((int)Integer(0), Text(1))),
            BuiltInId.StringRemove => new StrongBox<object?>(
                Subject().Replace(Text(0), string.Empty, StringComparison.Ordinal)),
            BuiltInId.StringRemoveAt => new StrongBox<object?>(
                Subject().Remove((int)Integer(0), 1)),
            BuiltInId.StringToCharacters => new StrongBox<object?>(
                new ProfiCSet<object?>(Subject().Select(c => (object?)c))),

            // An optional is the value itself, or nothing at all, so absence is a null target
            // rather than a wrapper to look inside.
            BuiltInId.OptionalHasValue => new StrongBox<object?>(target is not null),
            BuiltInId.OptionalOr => new StrongBox<object?>(target ?? Argument(0)),
            BuiltInId.OptionalValue => target is not null
                ? new StrongBox<object?>(target)
                : throw new EmptyOptionalException(),

            BuiltInId.FractionToReal => new StrongBox<object?>(((Fraction)target!).ToReal()),
            BuiltInId.RealToFraction => new StrongBox<object?>(
                Fraction.FromReal(target is double d ? d : 0)),
            BuiltInId.EnumerationToInteger => new StrongBox<object?>(
                target is EnumValue enumeration ? enumeration.Ordinal : AsInteger(target)),

            // A declared exception carries its message on the instance; one the language
            // raises is a .NET exception and carries its own.
            BuiltInId.ExceptionMessage => new StrongBox<object?>(target switch
            {
                Instance instance => instance.Message ?? string.Empty,
                Exception error => error.Message,
                _ => string.Empty,
            }),

            BuiltInId.ModelToString =>
                new StrongBox<object?>(ModelOperations.ToDisplayString(target)),
            BuiltInId.ModelEquals =>
                new StrongBox<object?>(ModelOperations.DeepEquals(target, Argument(0))),
        };
    }
#pragma warning restore CS8524

    /// <summary>Runs something that produces no value, and reports that it produced none.</summary>
    private static StrongBox<object?> Then(Action action)
    {
        action();
        return new StrongBox<object?>(null);
    }

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
