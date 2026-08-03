using System.Globalization;
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
        // 'Or' before anything is evaluated, because its fallback must not run unless it is
        // wanted. Every other built-in takes its arguments as values and cannot tell how they
        // were arrived at; this one is the only place in the language, besides 'and' and 'or',
        // where whether an expression runs at all is part of what the member means.
        if (_model.GetBuiltIn(member) is BuiltInId.OptionalOr)
        {
            return EvaluateOr(call, member, scope, receiver);
        }

        List<object?> arguments = [.. call.Arguments.Select(a => Evaluate(a, scope, receiver))];

        // A type name on the left: either a built-in like Console, or a shared function.
        if (TypeNamedBy(member.Receiver) is not null)
        {
            // Which version of an overloaded name this is was settled while checking, weighing
            // what the arguments actually are. Looking it up again by name would find the
            // first one written, so Math.Abs on a real would run the version taking integers.
            if (_model.GetBuiltIn(member) is { } onType)
            {
                return Perform(onType, target: null, arguments).Value;
            }

            if (_model.GetSymbol(member) is FunctionSymbol callee
                && BodyOf(callee) is { } shared)
            {
                return Invoke(
                    new FunctionValue(shared.Parameters, shared.Body, null, _shared, null, shared.Name, _fileOf.GetValueOrDefault(shared, _file)),
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
                new FunctionValue(parentMethod.Parameters, parentMethod.Body, null, _shared, receiver, parentMethod.Name, _fileOf.GetValueOrDefault(parentMethod, _file)),
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
                    new FunctionValue(method.Parameters, method.Body, null, _shared, instance, method.Name, _fileOf.GetValueOrDefault(method, _file)),
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

    /// <summary>
    /// <para>An optional's <c>Or</c>, which reaches its fallback only where there is nothing to
    /// give back.</para>
    /// <para>An empty optional is null here, so the whole of the rule is the null-coalescing
    /// below — and the argument is an expression until that decides it is needed.</para>
    /// </summary>
    private object? EvaluateOr(
        CallExpr call,
        MemberExpr member,
        Environment scope,
        Instance? receiver) =>
        Evaluate(member.Receiver, scope, receiver)
        ?? Evaluate(call.Arguments[0], scope, receiver);

    private object? RunBaseConstructor(CallExpr call, Environment scope, Instance? receiver)
    {
        // Whose constructor this 'base' was written in, not what the instance turned out to
        // be. A three-deep chain asking the instance would find the same parent three times.
        if (receiver is null || _constructing is not ModelSymbol { BaseType: { } parent })
        {
            return null;
        }

        List<object?> arguments = [.. call.Arguments.Select(a => Evaluate(a, scope, receiver))];

        if (FindConstructor(parent, arguments.Count) is { } constructor
            && BodyOf(constructor) is { } body)
        {
            RunConstructor(body, receiver, arguments, parent);
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
    /// <para>Carries out a built-in call and checks that what came back is what the catalog
    /// said would.</para>
    /// <para>The catalog is what the type checker believed; this is what actually happened.
    /// Nothing else compares the two, and the mistake is invisible from a program: a member
    /// declared to yield an integer that hands back a real prints the same characters, passes
    /// every recorded output, and only goes wrong somewhere far away where the value is used
    /// as a count.</para>
    /// </summary>
    private StrongBox<object?> Perform(BuiltInId id, object? target, List<object?> arguments)
    {
        StrongBox<object?> produced = PerformCore(id, target, arguments);

        if (BuiltInResults.Disagrees(id, produced.Value) is { } complaint)
        {
            throw new ProfiCRuntimeException(complaint);
        }

        return produced;
    }

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
    private StrongBox<object?> PerformCore(BuiltInId id, object? target, List<object?> arguments)
    {
        object? Argument(int index) => arguments.ElementAtOrDefault(index);
        decimal Real(int index) => Argument(index) is decimal d ? d : 0;
        double Float(int index) => Argument(index) is double f ? f : 0;
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

        ProfiCSet<object?> OtherSet(int index) => (ProfiCSet<object?>)Argument(index)!;
        string Subject() => (string)target!;

        ProfiCSet<object?> CharacterSet(int index) =>
            Argument(index) as ProfiCSet<object?> ?? [];

        // An optional the runtime answered with, put back into the shape this engine holds one
        // in: the value itself, or a null where there is none.
        static object? Held<T>(Optional<T> answered) =>
            answered.HasValue ? answered.Value : null;

        // The generator being asked: the one the program is holding, or the one the language
        // keeps for everything that did not ask for its own.
        ProfiCRandom Chance() => target as ProfiCRandom ?? _chance;

        DateTime Moment() => target is DateTime moment ? moment : default;
        DateTime OtherMoment(int index) => Argument(index) is DateTime other ? other : default;

        TimeSpan Length() => target is TimeSpan span ? span : default;
        TimeSpan Span(int index) => Argument(index) is TimeSpan given ? given : default;

        DateOnly Day() => target is DateOnly day ? day : default;
        DateOnly OtherDay(int index) => Argument(index) is DateOnly other ? other : default;

        TimeOnly OnTheClock() => target is TimeOnly clock ? clock : default;
        TimeOnly Clock(int index) => Argument(index) is TimeOnly given ? given : default;

        return id switch
        {
            // ---- Reached through a model's name ------------------------------------------

            BuiltInId.ConsoleWrite =>
                Then(() => _output.Write(ModelOperations.ToDisplayString(Argument(0)))),

            BuiltInId.ConsoleWriteLine =>
                Then(() => _output.WriteLine(arguments.Count == 0
                    ? string.Empty
                    : ModelOperations.ToDisplayString(arguments[0]))),

            // A null line means the input ran out, which is exactly what an absent optional
            // says, so nothing has to translate it.
            BuiltInId.ConsoleRead => new StrongBox<object?>(_input.ReadLine()),

            BuiltInId.ReferenceEquals =>
                new StrongBox<object?>(ReferenceEquals(Argument(0), Argument(1))),

            BuiltInId.MathPi => new StrongBox<object?>(ProfiCMath.Pi),
            BuiltInId.MathE => new StrongBox<object?>(ProfiCMath.E),

            // Where each number type runs out. The framework's own bounds, since these describe
            // what the value actually holds rather than anything the language decides.
            BuiltInId.IntegerMaxValue => new StrongBox<object?>(long.MaxValue),
            BuiltInId.IntegerMinValue => new StrongBox<object?>(long.MinValue),
            BuiltInId.RealMaxValue => new StrongBox<object?>(decimal.MaxValue),
            BuiltInId.RealMinValue => new StrongBox<object?>(decimal.MinValue),
            BuiltInId.FloatMaxValue => new StrongBox<object?>(double.MaxValue),
            BuiltInId.FloatMinValue => new StrongBox<object?>(double.MinValue),

            // The three a real has no answer for, and the same values a float's own arithmetic
            // produces — so comparing one against '1.0f / 0.0f' is true.
            BuiltInId.FloatInfinity => new StrongBox<object?>(double.PositiveInfinity),
            BuiltInId.FloatNegativeInfinity => new StrongBox<object?>(double.NegativeInfinity),
            BuiltInId.FloatNotANumber => new StrongBox<object?>(double.NaN),

            BuiltInId.CharacterMaxValue => new StrongBox<object?>(char.MaxValue),
            BuiltInId.CharacterMinValue => new StrongBox<object?>(char.MinValue),

            BuiltInId.StringEmpty => new StrongBox<object?>(string.Empty),

            BuiltInId.MathSqrt => new StrongBox<object?>(ProfiCMath.Sqrt(Real(0))),
            BuiltInId.MathCbrt => new StrongBox<object?>(ProfiCMath.Cbrt(Real(0))),
            BuiltInId.MathRoot => new StrongBox<object?>(ProfiCMath.Root(Real(0), Real(1))),
            BuiltInId.MathPow => new StrongBox<object?>(ProfiCMath.Pow(Real(0), Real(1))),
            BuiltInId.MathFactorial => new StrongBox<object?>(ProfiCMath.Factorial(Integer(0))),

            BuiltInId.MathLog => new StrongBox<object?>(ProfiCMath.Log(Real(0))),
            BuiltInId.MathLogInBase => new StrongBox<object?>(ProfiCMath.Log(Real(0), Real(1))),
            BuiltInId.MathLog10 => new StrongBox<object?>(ProfiCMath.Log10(Real(0))),
            BuiltInId.MathLog2 => new StrongBox<object?>(ProfiCMath.Log2(Real(0))),

            BuiltInId.MathSin => new StrongBox<object?>(ProfiCMath.Sin(Real(0))),
            BuiltInId.MathCos => new StrongBox<object?>(ProfiCMath.Cos(Real(0))),
            BuiltInId.MathTan => new StrongBox<object?>(ProfiCMath.Tan(Real(0))),
            BuiltInId.MathAsin => new StrongBox<object?>(ProfiCMath.Asin(Real(0))),
            BuiltInId.MathAcos => new StrongBox<object?>(ProfiCMath.Acos(Real(0))),
            BuiltInId.MathAtan => new StrongBox<object?>(ProfiCMath.Atan(Real(0))),
            BuiltInId.MathAtan2 => new StrongBox<object?>(ProfiCMath.Atan2(Real(0), Real(1))),

            BuiltInId.MathSinh => new StrongBox<object?>(ProfiCMath.Sinh(Real(0))),
            BuiltInId.MathCosh => new StrongBox<object?>(ProfiCMath.Cosh(Real(0))),
            BuiltInId.MathTanh => new StrongBox<object?>(ProfiCMath.Tanh(Real(0))),
            BuiltInId.MathAsinh => new StrongBox<object?>(ProfiCMath.Asinh(Real(0))),
            BuiltInId.MathAcosh => new StrongBox<object?>(ProfiCMath.Acosh(Real(0))),
            BuiltInId.MathAtanh => new StrongBox<object?>(ProfiCMath.Atanh(Real(0))),

            BuiltInId.MathAbsInteger => new StrongBox<object?>(ProfiCMath.Abs(Integer(0))),
            BuiltInId.MathAbsReal => new StrongBox<object?>(ProfiCMath.Abs(Real(0))),
            BuiltInId.MathAbsFraction => new StrongBox<object?>(ProfiCMath.Abs(Ratio(0))),

            BuiltInId.MathFloorReal => new StrongBox<object?>(ProfiCMath.Floor(Real(0))),
            BuiltInId.MathFloorFraction => new StrongBox<object?>(ProfiCMath.Floor(Ratio(0))),
            BuiltInId.MathCeilingReal => new StrongBox<object?>(ProfiCMath.Ceiling(Real(0))),
            BuiltInId.MathCeilingFraction => new StrongBox<object?>(ProfiCMath.Ceiling(Ratio(0))),

            BuiltInId.MathRoundReal => new StrongBox<object?>(ProfiCMath.Round(Real(0))),
            BuiltInId.MathRoundFraction => new StrongBox<object?>(ProfiCMath.Round(Ratio(0))),
            BuiltInId.MathRoundRealPlaces =>
                new StrongBox<object?>(ProfiCMath.Round(Real(0), Integer(1))),

            BuiltInId.MathMinInteger => new StrongBox<object?>(ProfiCMath.Min(Integer(0), Integer(1))),
            BuiltInId.MathMinReal => new StrongBox<object?>(ProfiCMath.Min(Real(0), Real(1))),
            BuiltInId.MathMinFraction => new StrongBox<object?>(ProfiCMath.Min(Ratio(0), Ratio(1))),
            BuiltInId.MathMaxInteger => new StrongBox<object?>(ProfiCMath.Max(Integer(0), Integer(1))),
            BuiltInId.MathMaxReal => new StrongBox<object?>(ProfiCMath.Max(Real(0), Real(1))),
            BuiltInId.MathMaxFraction => new StrongBox<object?>(ProfiCMath.Max(Ratio(0), Ratio(1))),

            // The same again on a float, reaching the binary half of each pair. Nothing is
            // converted on the way in or out: that is the point of having both.
            BuiltInId.MathSqrtFloat => new StrongBox<object?>(ProfiCMath.Sqrt(Float(0))),
            BuiltInId.MathCbrtFloat => new StrongBox<object?>(ProfiCMath.Cbrt(Float(0))),
            BuiltInId.MathRootFloat => new StrongBox<object?>(ProfiCMath.Root(Float(0), Float(1))),
            BuiltInId.MathPowFloat => new StrongBox<object?>(ProfiCMath.Pow(Float(0), Float(1))),

            BuiltInId.MathLogFloat => new StrongBox<object?>(ProfiCMath.Log(Float(0))),
            BuiltInId.MathLogInBaseFloat =>
                new StrongBox<object?>(ProfiCMath.Log(Float(0), Float(1))),
            BuiltInId.MathLog10Float => new StrongBox<object?>(ProfiCMath.Log10(Float(0))),
            BuiltInId.MathLog2Float => new StrongBox<object?>(ProfiCMath.Log2(Float(0))),

            BuiltInId.MathSinFloat => new StrongBox<object?>(ProfiCMath.Sin(Float(0))),
            BuiltInId.MathCosFloat => new StrongBox<object?>(ProfiCMath.Cos(Float(0))),
            BuiltInId.MathTanFloat => new StrongBox<object?>(ProfiCMath.Tan(Float(0))),
            BuiltInId.MathAsinFloat => new StrongBox<object?>(ProfiCMath.Asin(Float(0))),
            BuiltInId.MathAcosFloat => new StrongBox<object?>(ProfiCMath.Acos(Float(0))),
            BuiltInId.MathAtanFloat => new StrongBox<object?>(ProfiCMath.Atan(Float(0))),
            BuiltInId.MathAtan2Float =>
                new StrongBox<object?>(ProfiCMath.Atan2(Float(0), Float(1))),

            BuiltInId.MathSinhFloat => new StrongBox<object?>(ProfiCMath.Sinh(Float(0))),
            BuiltInId.MathCoshFloat => new StrongBox<object?>(ProfiCMath.Cosh(Float(0))),
            BuiltInId.MathTanhFloat => new StrongBox<object?>(ProfiCMath.Tanh(Float(0))),
            BuiltInId.MathAsinhFloat => new StrongBox<object?>(ProfiCMath.Asinh(Float(0))),
            BuiltInId.MathAcoshFloat => new StrongBox<object?>(ProfiCMath.Acosh(Float(0))),
            BuiltInId.MathAtanhFloat => new StrongBox<object?>(ProfiCMath.Atanh(Float(0))),

            BuiltInId.MathAbsFloat => new StrongBox<object?>(ProfiCMath.Abs(Float(0))),
            BuiltInId.MathFloorFloat => new StrongBox<object?>(ProfiCMath.Floor(Float(0))),
            BuiltInId.MathCeilingFloat => new StrongBox<object?>(ProfiCMath.Ceiling(Float(0))),
            BuiltInId.MathRoundFloat => new StrongBox<object?>(ProfiCMath.Round(Float(0))),
            BuiltInId.MathRoundFloatPlaces =>
                new StrongBox<object?>(ProfiCMath.Round(Float(0), Integer(1))),
            BuiltInId.MathMinFloat => new StrongBox<object?>(ProfiCMath.Min(Float(0), Float(1))),
            BuiltInId.MathMaxFloat => new StrongBox<object?>(ProfiCMath.Max(Float(0), Float(1))),

            BuiltInId.FractionCreate =>
                new StrongBox<object?>(new Fraction(Integer(0), Integer(1))),
            BuiltInId.FractionCreateWhole =>
                new StrongBox<object?>(Fraction.FromInteger(Integer(0))),

            BuiltInId.RandomNew => new StrongBox<object?>(new ProfiCRandom()),
            BuiltInId.RandomNewSeeded => new StrongBox<object?>(new ProfiCRandom(Integer(0))),

            // One set of members serves both shapes. Reached through a generator the program
            // holds, the target is that generator; reached through the name, there is none and
            // the one the language keeps answers instead.
            BuiltInId.RandomNext => new StrongBox<object?>(Chance().Next()),
            BuiltInId.RandomNextBelow => new StrongBox<object?>(Chance().Next(Integer(0))),
            BuiltInId.RandomNextBetween =>
                new StrongBox<object?>(Chance().Next(Integer(0), Integer(1))),
            BuiltInId.RandomNextDouble => new StrongBox<object?>((decimal)Chance().NextDouble()),

            BuiltInId.DateTimeNewDate => new StrongBox<object?>(
                MakeMoment(Integer(0), Integer(1), Integer(2), 0, 0, 0)),
            BuiltInId.DateTimeNewMoment => new StrongBox<object?>(
                MakeMoment(Integer(0), Integer(1), Integer(2), Integer(3), Integer(4), Integer(5))),

            BuiltInId.DateTimeNow => new StrongBox<object?>(DateTime.Now),
            BuiltInId.DateTimeToday => new StrongBox<object?>(DateTime.Today),

            BuiltInId.DateTimeYear => new StrongBox<object?>((long)Moment().Year),
            BuiltInId.DateTimeMonth => new StrongBox<object?>((long)Moment().Month),
            BuiltInId.DateTimeDay => new StrongBox<object?>((long)Moment().Day),
            BuiltInId.DateTimeHour => new StrongBox<object?>((long)Moment().Hour),
            BuiltInId.DateTimeMinute => new StrongBox<object?>((long)Moment().Minute),
            BuiltInId.DateTimeSecond => new StrongBox<object?>((long)Moment().Second),
            BuiltInId.DateTimeDayOfWeek => new StrongBox<object?>((long)Moment().DayOfWeek),
            BuiltInId.DateTimeDayOfYear => new StrongBox<object?>((long)Moment().DayOfYear),

            // A moment never changes, so each of these yields another one.
            BuiltInId.DateTimeAddDays => new StrongBox<object?>(Moment().AddDays((double)Real(0))),
            BuiltInId.DateTimeAddHours => new StrongBox<object?>(Moment().AddHours((double)Real(0))),
            BuiltInId.DateTimeAddMinutes => new StrongBox<object?>(Moment().AddMinutes((double)Real(0))),
            BuiltInId.DateTimeAddSeconds => new StrongBox<object?>(Moment().AddSeconds((double)Real(0))),
            BuiltInId.DateTimeAddYears => new StrongBox<object?>(Moment().AddYears((int)Integer(0))),
            BuiltInId.DateTimeAddMonths => new StrongBox<object?>(Moment().AddMonths((int)Integer(0))),

            BuiltInId.DateTimeCompareTo =>
                new StrongBox<object?>((long)Moment().CompareTo(OtherMoment(0))),

            BuiltInId.DateTimeSubtract => new StrongBox<object?>(Moment() - OtherMoment(0)),
            BuiltInId.DateTimeSubtractSpan => new StrongBox<object?>(Moment() - Span(0)),
            BuiltInId.DateTimeAdd => new StrongBox<object?>(Moment() + Span(0)),

            BuiltInId.TimeSpanNewTime => new StrongBox<object?>(
                MakeSpan(0, Integer(0), Integer(1), Integer(2))),
            BuiltInId.TimeSpanNewSpan => new StrongBox<object?>(
                MakeSpan(Integer(0), Integer(1), Integer(2), Integer(3))),

            BuiltInId.TimeSpanZero => new StrongBox<object?>(TimeSpan.Zero),
            BuiltInId.TimeSpanFromDays => new StrongBox<object?>(TimeSpan.FromDays((double)Real(0))),
            BuiltInId.TimeSpanFromHours => new StrongBox<object?>(TimeSpan.FromHours((double)Real(0))),
            BuiltInId.TimeSpanFromMinutes => new StrongBox<object?>(TimeSpan.FromMinutes((double)Real(0))),
            BuiltInId.TimeSpanFromSeconds => new StrongBox<object?>(TimeSpan.FromSeconds((double)Real(0))),

            BuiltInId.TimeSpanDays => new StrongBox<object?>((long)Length().Days),
            BuiltInId.TimeSpanHours => new StrongBox<object?>((long)Length().Hours),
            BuiltInId.TimeSpanMinutes => new StrongBox<object?>((long)Length().Minutes),
            BuiltInId.TimeSpanSeconds => new StrongBox<object?>((long)Length().Seconds),

            BuiltInId.TimeSpanTotalDays => new StrongBox<object?>((decimal)Length().TotalDays),
            BuiltInId.TimeSpanTotalHours => new StrongBox<object?>((decimal)Length().TotalHours),
            BuiltInId.TimeSpanTotalMinutes => new StrongBox<object?>((decimal)Length().TotalMinutes),
            BuiltInId.TimeSpanTotalSeconds => new StrongBox<object?>((decimal)Length().TotalSeconds),

            BuiltInId.TimeSpanAdd => new StrongBox<object?>(Length() + Span(0)),
            BuiltInId.TimeSpanSubtract => new StrongBox<object?>(Length() - Span(0)),
            BuiltInId.TimeSpanNegate => new StrongBox<object?>(Length().Negate()),
            BuiltInId.TimeSpanDuration => new StrongBox<object?>(Length().Duration()),
            BuiltInId.TimeSpanCompareTo =>
                new StrongBox<object?>((long)Length().CompareTo(Span(0))),

            BuiltInId.DateNew => new StrongBox<object?>(
                MakeDate(Integer(0), Integer(1), Integer(2))),
            BuiltInId.DateToday => new StrongBox<object?>(DateOnly.FromDateTime(DateTime.Now)),
            BuiltInId.DateFromMoment =>
                new StrongBox<object?>(DateOnly.FromDateTime(OtherMoment(0))),

            BuiltInId.DateYear => new StrongBox<object?>((long)Day().Year),
            BuiltInId.DateMonth => new StrongBox<object?>((long)Day().Month),
            BuiltInId.DateDay => new StrongBox<object?>((long)Day().Day),
            BuiltInId.DateDayOfWeek => new StrongBox<object?>((long)Day().DayOfWeek),
            BuiltInId.DateDayOfYear => new StrongBox<object?>((long)Day().DayOfYear),

            BuiltInId.DateAddDays => new StrongBox<object?>(Day().AddDays((int)Integer(0))),
            BuiltInId.DateAddMonths => new StrongBox<object?>(Day().AddMonths((int)Integer(0))),
            BuiltInId.DateAddYears => new StrongBox<object?>(Day().AddYears((int)Integer(0))),

            BuiltInId.DateAtTime => new StrongBox<object?>(Day().ToDateTime(Clock(0))),
            BuiltInId.DateCompareTo => new StrongBox<object?>((long)Day().CompareTo(OtherDay(0))),

            BuiltInId.TimeNewToMinute => new StrongBox<object?>(
                MakeTime(Integer(0), Integer(1), 0)),
            BuiltInId.TimeNewToSecond => new StrongBox<object?>(
                MakeTime(Integer(0), Integer(1), Integer(2))),
            BuiltInId.TimeNow => new StrongBox<object?>(TimeOnly.FromDateTime(DateTime.Now)),
            BuiltInId.TimeFromMoment =>
                new StrongBox<object?>(TimeOnly.FromDateTime(OtherMoment(0))),

            BuiltInId.TimeHour => new StrongBox<object?>((long)OnTheClock().Hour),
            BuiltInId.TimeMinute => new StrongBox<object?>((long)OnTheClock().Minute),
            BuiltInId.TimeSecond => new StrongBox<object?>((long)OnTheClock().Second),

            BuiltInId.TimeAddHours => new StrongBox<object?>(OnTheClock().AddHours((double)Real(0))),
            BuiltInId.TimeAddMinutes => new StrongBox<object?>(OnTheClock().AddMinutes((double)Real(0))),

            BuiltInId.TimeToTimeSpan => new StrongBox<object?>(OnTheClock().ToTimeSpan()),
            BuiltInId.TimeCompareTo =>
                new StrongBox<object?>((long)OnTheClock().CompareTo(Clock(0))),

            // ---- Reached through a value --------------------------------------------------

            BuiltInId.SetCount => new StrongBox<object?>((long)Set().Count),
            BuiltInId.SetInsert => Then(() => Set().Insert(Argument(0))),
            BuiltInId.SetInsertAt => Then(() => Set().InsertAt((int)Integer(0), Argument(1))),
            BuiltInId.SetRemove => new StrongBox<object?>(Set().Remove(Argument(0))),
            BuiltInId.SetRemoveAt => Then(() => Set().RemoveAt((int)Integer(0))),
            BuiltInId.SetContains => new StrongBox<object?>(Set().Contains(Argument(0))),
            BuiltInId.SetIndexOf => new StrongBox<object?>((long)Set().IndexOf(Argument(0))),
            BuiltInId.SetClear => Then(Set().Clear),

            // Both leave their two originals alone and hand back a new set, as Subset does.
            // Membership is asked of the other set, so it is the same structural question
            // '==' asks rather than a reference check.
            // Each of these is the set's own, so that the emitter calling the same method is
            // calling the same code rather than a second version of it that agrees today.
            BuiltInId.SetUnion => new StrongBox<object?>(Set().Union(OtherSet(0))),
            BuiltInId.SetIntersect => new StrongBox<object?>(Set().Intersect(OtherSet(0))),
            BuiltInId.SetExcept => new StrongBox<object?>(Set().Except(OtherSet(0))),
            BuiltInId.SetDistinct => new StrongBox<object?>(Set().Distinct()),

            BuiltInId.SetSubsetFrom => new StrongBox<object?>(Set().Subset((int)Integer(0))),
            BuiltInId.SetSubsetBetween => new StrongBox<object?>(
                Set().Subset((int)Integer(0), (int)Integer(1))),

            // An element of a set of optionals is the value itself, or null for an empty one, so
            // there is nothing to unwrap here and TrimAll is the plain filter. The runtime knows
            // both shapes, which is what keeps this and an emitted program agreeing.
            BuiltInId.SetTrim => new StrongBox<object?>(Set().Trim()),
            BuiltInId.SetTrimStart => new StrongBox<object?>(Set().TrimStart()),
            BuiltInId.SetTrimEnd => new StrongBox<object?>(Set().TrimEnd()),
            BuiltInId.SetTrimAll => new StrongBox<object?>(Set().TrimAll()),

            BuiltInId.SetJoin => new StrongBox<object?>(Set().Join(Text(0))),

            BuiltInId.StringCount => new StrongBox<object?>((long)Subject().Length),
            BuiltInId.StringContains =>
                new StrongBox<object?>(ProfiCText.Contains(Subject(), Text(0))),
            BuiltInId.StringIndexOf =>
                new StrongBox<object?>(ProfiCText.IndexOf(Subject(), Text(0))),
            BuiltInId.StringSubstring =>
                new StrongBox<object?>(ProfiCText.Substring(Subject(), Integer(0), Integer(1))),

            BuiltInId.StringSubsetFrom =>
                new StrongBox<object?>(ProfiCText.Subset(Subject(), Integer(0))),
            BuiltInId.StringSubsetBetween =>
                new StrongBox<object?>(ProfiCText.Subset(Subject(), Integer(0), Integer(1))),
            BuiltInId.StringInsert =>
                new StrongBox<object?>(ProfiCText.Insert(Subject(), Text(0))),
            BuiltInId.StringInsertAt =>
                new StrongBox<object?>(ProfiCText.InsertAt(Subject(), Integer(0), Text(1))),
            BuiltInId.StringRemove =>
                new StrongBox<object?>(ProfiCText.Remove(Subject(), Text(0))),
            BuiltInId.StringRemoveAt =>
                new StrongBox<object?>(ProfiCText.RemoveAt(Subject(), Integer(0))),
            BuiltInId.StringToCharacters =>
                new StrongBox<object?>(ProfiCText.ToCharactersUntyped(Subject())),

            BuiltInId.StringTrim => new StrongBox<object?>(ProfiCText.Trim(Subject())),
            BuiltInId.StringTrimText =>
                new StrongBox<object?>(ProfiCText.Trim(Subject(), Text(0))),
            BuiltInId.StringTrimSet =>
                new StrongBox<object?>(ProfiCText.Trim(Subject(), CharacterSet(0))),

            BuiltInId.StringTrimStart => new StrongBox<object?>(ProfiCText.TrimStart(Subject())),
            BuiltInId.StringTrimStartText =>
                new StrongBox<object?>(ProfiCText.TrimStart(Subject(), Text(0))),
            BuiltInId.StringTrimStartSet =>
                new StrongBox<object?>(ProfiCText.TrimStart(Subject(), CharacterSet(0))),

            BuiltInId.StringTrimEnd => new StrongBox<object?>(ProfiCText.TrimEnd(Subject())),
            BuiltInId.StringTrimEndText =>
                new StrongBox<object?>(ProfiCText.TrimEnd(Subject(), Text(0))),
            BuiltInId.StringTrimEndSet =>
                new StrongBox<object?>(ProfiCText.TrimEnd(Subject(), CharacterSet(0))),

            BuiltInId.StringSplit =>
                new StrongBox<object?>(ProfiCText.SplitUntyped(Subject(), Text(0))),

            BuiltInId.StringReplace =>
                new StrongBox<object?>(ProfiCText.Replace(Subject(), Text(0), Text(1))),

            BuiltInId.StringToUpper => new StrongBox<object?>(ProfiCText.ToUpper(Subject())),
            BuiltInId.StringToLower => new StrongBox<object?>(ProfiCText.ToLower(Subject())),
            BuiltInId.StringCapitalize =>
                new StrongBox<object?>(ProfiCText.Capitalize(Subject())),

            // An optional is the value itself, or nothing at all, so absence is a null target
            // rather than a wrapper to look inside.
            BuiltInId.OptionalHasValue => new StrongBox<object?>(target is not null),
            BuiltInId.OptionalOr => new StrongBox<object?>(target ?? Argument(0)),
            BuiltInId.OptionalValue => target is not null
                ? new StrongBox<object?>(target)
                : throw new EmptyOptionalException(),

            // Each reads as an optional, and this engine holds an empty one as a null — so
            // what the runtime answers is unwrapped on the way out rather than kept.
            BuiltInId.StringToInteger =>
                new StrongBox<object?>(Held(ProfiCText.ToInteger(Subject()))),
            BuiltInId.StringToReal =>
                new StrongBox<object?>(Held(ProfiCText.ToReal(Subject()))),
            BuiltInId.StringToBoolean =>
                new StrongBox<object?>(Held(ProfiCText.ToBoolean(Subject()))),

            BuiltInId.StringToFraction => new StrongBox<object?>(ReadFraction(Subject())),

            // ---- Files ----------------------------------------------------------------
            //
            // A file that is not there gives nothing back, so the ordinary question needs no
            // guard. Every other failure travels as the IOException it already is, which is
            // the type a program names after 'catch'.
            BuiltInId.FileRead => new StrongBox<object?>(
                File.Exists(Text(0)) ? File.ReadAllText(Text(0), Utf8) : null),
            BuiltInId.FileReadLines => new StrongBox<object?>(
                File.Exists(Text(0))
                    ? new ProfiCSet<object?>(
                        File.ReadAllLines(Text(0), Utf8).Select(line => (object?)line))
                    : null),

            BuiltInId.FileWrite => Then(() => File.WriteAllText(Text(0), Text(1), Utf8)),
            BuiltInId.FileWriteLines => Then(() => File.WriteAllText(
                Text(0),
                string.Concat(OtherSet(1).Select(line => AsText(line) + "\n")),
                Utf8)),
            BuiltInId.FileAppend => Then(() => File.AppendAllText(Text(0), Text(1), Utf8)),

            BuiltInId.FileExists => new StrongBox<object?>(File.Exists(Text(0))),

            BuiltInId.FileDelete => new StrongBox<object?>(Removed(Text(0))),

            BuiltInId.FileCopy => Then(() => File.Copy(Text(0), Text(1), overwrite: true)),
            BuiltInId.FileMove => Then(() => File.Move(Text(0), Text(1), overwrite: true)),

            BuiltInId.FileSize => new StrongBox<object?>(
                File.Exists(Text(0)) ? new FileInfo(Text(0)).Length : null),
            BuiltInId.FileChanged => new StrongBox<object?>(
                File.Exists(Text(0)) ? File.GetLastWriteTime(Text(0)) : null),

            BuiltInId.DirectoryCurrent => new StrongBox<object?>(
                System.IO.Directory.GetCurrentDirectory()),
            BuiltInId.DirectoryExists => new StrongBox<object?>(
                System.IO.Directory.Exists(Text(0))),
            BuiltInId.DirectoryCreate => Then(
                () => System.IO.Directory.CreateDirectory(Text(0))),
            BuiltInId.DirectoryDelete => new StrongBox<object?>(RemovedFolder(Text(0))),

            // Named in a settled order rather than whatever the file system offers, so a
            // program prints the same list twice and on two machines.
            BuiltInId.DirectoryFiles => new StrongBox<object?>(
                System.IO.Directory.Exists(Text(0))
                    ? new ProfiCSet<object?>(
                        System.IO.Directory.GetFiles(Text(0))
                            .OrderBy(p => p, StringComparer.Ordinal)
                            .Select(p => (object?)p))
                    : null),
            BuiltInId.DirectoryFolders => new StrongBox<object?>(
                System.IO.Directory.Exists(Text(0))
                    ? new ProfiCSet<object?>(
                        System.IO.Directory.GetDirectories(Text(0))
                            .OrderBy(p => p, StringComparer.Ordinal)
                            .Select(p => (object?)p))
                    : null),

            // The halves of a moment, and the ways of building one from them.
            BuiltInId.DateTimeDatePart => new StrongBox<object?>(DateOnly.FromDateTime(Moment())),
            BuiltInId.DateTimeTimePart => new StrongBox<object?>(TimeOnly.FromDateTime(Moment())),
            BuiltInId.DateTimeFromDate => new StrongBox<object?>(
                OtherDay(0).ToDateTime(TimeOnly.MinValue)),
            BuiltInId.DateTimeFromDateAndTime => new StrongBox<object?>(
                OtherDay(0).ToDateTime(Clock(1))),

            // Read back from text. Nothing is raised: an optional is what says the text did
            // not read, and text that does not read is the ordinary case rather than a fault.
            //
            // Invariant here too, so a value written on one machine reads on another. The
            // second form takes exactly the pattern given, which is how something written by
            // a pattern is read back by the same one.
            BuiltInId.DateTimeParse => new StrongBox<object?>(
                DateTime.TryParse(Text(0), CultureInfo.InvariantCulture,
                                  DateTimeStyles.None, out DateTime moment)
                    ? moment
                    : null),
            BuiltInId.DateTimeParseExact => new StrongBox<object?>(
                DateTime.TryParseExact(Text(0), Text(1), CultureInfo.InvariantCulture,
                                       DateTimeStyles.None, out DateTime exact)
                    ? exact
                    : null),

            BuiltInId.TimeSpanParse => new StrongBox<object?>(
                TimeSpan.TryParse(Text(0), CultureInfo.InvariantCulture, out TimeSpan length)
                    ? length
                    : null),
            BuiltInId.TimeSpanParseExact => new StrongBox<object?>(
                TimeSpan.TryParseExact(Text(0), Text(1), CultureInfo.InvariantCulture,
                                       out TimeSpan exactLength)
                    ? exactLength
                    : null),

            BuiltInId.DateParse => new StrongBox<object?>(
                DateOnly.TryParse(Text(0), CultureInfo.InvariantCulture,
                                  DateTimeStyles.None, out DateOnly day)
                    ? day
                    : null),
            BuiltInId.DateParseExact => new StrongBox<object?>(
                DateOnly.TryParseExact(Text(0), Text(1), CultureInfo.InvariantCulture,
                                       DateTimeStyles.None, out DateOnly exactDay)
                    ? exactDay
                    : null),

            BuiltInId.TimeParse => new StrongBox<object?>(
                TimeOnly.TryParse(Text(0), CultureInfo.InvariantCulture,
                                  DateTimeStyles.None, out TimeOnly clock)
                    ? clock
                    : null),
            BuiltInId.TimeParseExact => new StrongBox<object?>(
                TimeOnly.TryParseExact(Text(0), Text(1), CultureInfo.InvariantCulture,
                                       DateTimeStyles.None, out TimeOnly exactClock)
                    ? exactClock
                    : null),

            // Written by a pattern. Invariant, as everything else here is: a program prints
            // the same on every machine, and the pattern is what says otherwise.
            //
            // A pattern the runtime cannot read raises a FormatException, which is already the
            // one a Profi-C program catches — the two are the same type — so nothing has to
            // translate it.
            BuiltInId.IntegerFormat =>
                new StrongBox<object?>(ProfiCText.Format(AsInteger(target), Text(0))),
            BuiltInId.RealFormat =>
                new StrongBox<object?>(ProfiCText.Format(target is decimal r ? r : 0, Text(0))),
            BuiltInId.FloatFormat =>
                new StrongBox<object?>(ProfiCText.Format(target is double f ? f : 0, Text(0))),
            BuiltInId.FractionFormat => new StrongBox<object?>(
                ProfiCText.Format(((Fraction)target!).ToReal(), Text(0))),
            BuiltInId.DateTimeFormat => new StrongBox<object?>(
                Moment().ToString(Text(0), CultureInfo.InvariantCulture)),
            BuiltInId.TimeSpanFormat => new StrongBox<object?>(
                Length().ToString(Text(0), CultureInfo.InvariantCulture)),
            BuiltInId.DateFormat => new StrongBox<object?>(
                Day().ToString(Text(0), CultureInfo.InvariantCulture)),
            BuiltInId.TimeFormat => new StrongBox<object?>(
                OnTheClock().ToString(Text(0), CultureInfo.InvariantCulture)),

            BuiltInId.FractionToReal => new StrongBox<object?>(((Fraction)target!).ToReal()),
            BuiltInId.FractionReciprocal =>
                new StrongBox<object?>(((Fraction)target!).Reciprocal()),
            BuiltInId.FloatToFraction => new StrongBox<object?>(
                Fraction.FromFloat(target is double f ? f : 0)),
            BuiltInId.RealToFloat => new StrongBox<object?>(
                ProfiCArithmetic.ToFloat(target is decimal toward ? toward : 0)),
            BuiltInId.FloatToReal => new StrongBox<object?>(
                ProfiCArithmetic.ToReal(target is double back ? back : 0)),
            BuiltInId.IntegerToFloat => new StrongBox<object?>((double)AsInteger(target)),
            BuiltInId.FractionToFloat => new StrongBox<object?>(((Fraction)target!).ToFloat()),
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

    /// <summary>
    /// UTF-8 with no mark at the front, which is what everything else reads without being
    /// told. A mark would travel into files a Profi-C program wrote and nothing else expects.
    /// </summary>
    private static readonly System.Text.UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Deletes a file if there is one, saying whether there was. Asked and done in one step
    /// rather than checked first, so that nothing can slip in between the two.
    /// </summary>
    private static bool Removed(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    }

    /// <summary>The same for a folder, and everything inside it.</summary>
    private static bool RemovedFolder(string path)
    {
        if (!System.IO.Directory.Exists(path))
        {
            return false;
        }

        System.IO.Directory.Delete(path, recursive: true);
        return true;
    }

    /// <summary>
    /// <para>Reads a ratio written with either mark between its halves, or a whole number.
    /// </para>
    /// <para><c>22|7</c> is how the language writes one, because a slash already means
    /// division. <c>22/7</c> is how a person writes one, because that is what a fraction looks
    /// like everywhere outside a compiler. Both are read, and a bare <c>22</c> is a ratio over
    /// one — the same three shapes <c>Fraction.Create</c> accepts.</para>
    /// </summary>
    private static object? ReadFraction(string text)
    {
        string trimmed = text.Trim();
        int mark = trimmed.IndexOfAny(['|', '/']);

        if (mark < 0)
        {
            return long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture,
                                 out long whole)
                ? new Fraction(whole, 1)
                : null;
        }

        if (!long.TryParse(trimmed[..mark].Trim(), NumberStyles.Integer,
                           CultureInfo.InvariantCulture, out long numerator)
            || !long.TryParse(trimmed[(mark + 1)..].Trim(), NumberStyles.Integer,
                              CultureInfo.InvariantCulture, out long denominator)
            || denominator == 0)
        {
            return null;
        }

        return new Fraction(numerator, denominator);
    }

    /// <summary>Runs something that produces no value, and reports that it produced none.</summary>
    private static StrongBox<object?> Then(Action action)
    {
        action();
        return new StrongBox<object?>(null);
    }

    private static string AsText(object? value) => ModelOperations.ToDisplayString(value);

    /// <summary>
    /// <para>Builds a moment, reporting a date that is not one.</para>
    /// <para>The platform raises an argument error for the thirty-first of February, which is
    /// the right answer; it is caught and thrown again as the language's own so that the
    /// message names the numbers that were written.</para>
    /// </summary>
    private static DateTime MakeMoment(
        long year, long month, long day, long hour, long minute, long second)
    {
        try
        {
            return new DateTime(
                (int)year, (int)month, (int)day, (int)hour, (int)minute, (int)second);
        }
        catch (ArgumentOutOfRangeException)
        {
            string written = hour == 0 && minute == 0 && second == 0
                ? $"{year}-{month}-{day}"
                : $"{year}-{month}-{day} {hour}:{minute}:{second}";

            throw new ArgumentException($"There is no such moment as {written}.");
        }
    }

    /// <summary>Builds a day, reporting one that is not a day.</summary>
    private static DateOnly MakeDate(long year, long month, long day)
    {
        try
        {
            return new DateOnly((int)year, (int)month, (int)day);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new ArgumentException($"There is no such date as {year}-{month}-{day}.");
        }
    }

    /// <summary>Builds a time of day, reporting one that no clock reads.</summary>
    private static TimeOnly MakeTime(long hour, long minute, long second)
    {
        try
        {
            return new TimeOnly((int)hour, (int)minute, (int)second);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new ArgumentException(
                $"There is no such time of day as {hour}:{minute}:{second}.");
        }
    }

    /// <summary>
    /// Builds a span, reporting one too large to hold. Days are counted separately rather
    /// than folded in, so that a span of hours beyond a day still reads as hours.
    /// </summary>
    private static TimeSpan MakeSpan(long days, long hours, long minutes, long seconds)
    {
        try
        {
            return new TimeSpan((int)days, (int)hours, (int)minutes, (int)seconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new OverflowException(
                $"A span of {days} days, {hours} hours, {minutes} minutes and {seconds} "
                + "seconds is too long to hold.");
        }
    }

}

/// <summary>
/// Wraps a result so that "produced nothing" can be told apart from "did not handle this".
/// </summary>
internal sealed class StrongBox<T>(T value)
{
    public T Value { get; } = value;
}
