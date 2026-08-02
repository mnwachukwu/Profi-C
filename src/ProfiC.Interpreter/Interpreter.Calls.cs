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

        ProfiCSet<object?> OtherSet(int index) => (ProfiCSet<object?>)Argument(index)!;
        string Subject() => (string)target!;

        char[] Characters(int index) => Argument(index) is ProfiCSet<object?> given
            ? [.. given.OfType<char>()]
            : [];

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

            BuiltInId.MathPi => new StrongBox<object?>(Math.PI),
            BuiltInId.MathE => new StrongBox<object?>(Math.E),

            BuiltInId.MathSqrt => new StrongBox<object?>(Math.Sqrt(Real(0))),
            BuiltInId.MathCbrt => new StrongBox<object?>(Exactly(Math.Cbrt(Real(0)), Real(0), 3)),
            BuiltInId.MathRoot => new StrongBox<object?>(Root(Real(0), Real(1))),
            BuiltInId.MathPow => new StrongBox<object?>(Math.Pow(Real(0), Real(1))),
            BuiltInId.MathFactorial => new StrongBox<object?>(Factorial(Integer(0))),

            BuiltInId.MathLog => new StrongBox<object?>(Math.Log(Real(0))),
            BuiltInId.MathLogInBase => new StrongBox<object?>(Math.Log(Real(0), Real(1))),
            BuiltInId.MathLog10 => new StrongBox<object?>(Math.Log10(Real(0))),
            BuiltInId.MathLog2 => new StrongBox<object?>(Math.Log2(Real(0))),

            BuiltInId.MathSin => new StrongBox<object?>(Math.Sin(Real(0))),
            BuiltInId.MathCos => new StrongBox<object?>(Math.Cos(Real(0))),
            BuiltInId.MathTan => new StrongBox<object?>(Math.Tan(Real(0))),
            BuiltInId.MathAsin => new StrongBox<object?>(Math.Asin(Real(0))),
            BuiltInId.MathAcos => new StrongBox<object?>(Math.Acos(Real(0))),
            BuiltInId.MathAtan => new StrongBox<object?>(Math.Atan(Real(0))),
            BuiltInId.MathAtan2 => new StrongBox<object?>(Math.Atan2(Real(0), Real(1))),

            BuiltInId.MathSinh => new StrongBox<object?>(Math.Sinh(Real(0))),
            BuiltInId.MathCosh => new StrongBox<object?>(Math.Cosh(Real(0))),
            BuiltInId.MathTanh => new StrongBox<object?>(Math.Tanh(Real(0))),
            BuiltInId.MathAsinh => new StrongBox<object?>(Math.Asinh(Real(0))),
            BuiltInId.MathAcosh => new StrongBox<object?>(Math.Acosh(Real(0))),
            BuiltInId.MathAtanh => new StrongBox<object?>(Math.Atanh(Real(0))),

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
            BuiltInId.MathRoundRealPlaces =>
                new StrongBox<object?>(
                    Math.Round(Real(0), (int)Integer(1), MidpointRounding.AwayFromZero)),

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

            BuiltInId.RandomNew => new StrongBox<object?>(new ProfiCRandom()),
            BuiltInId.RandomNewSeeded => new StrongBox<object?>(new ProfiCRandom(Integer(0))),

            // One set of members serves both shapes. Reached through a generator the program
            // holds, the target is that generator; reached through the name, there is none and
            // the one the language keeps answers instead.
            BuiltInId.RandomNext => new StrongBox<object?>(Chance().Next()),
            BuiltInId.RandomNextBelow => new StrongBox<object?>(Chance().Next(Integer(0))),
            BuiltInId.RandomNextBetween =>
                new StrongBox<object?>(Chance().Next(Integer(0), Integer(1))),
            BuiltInId.RandomNextDouble => new StrongBox<object?>(Chance().NextDouble()),

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
            BuiltInId.DateTimeAddDays => new StrongBox<object?>(Moment().AddDays(Real(0))),
            BuiltInId.DateTimeAddHours => new StrongBox<object?>(Moment().AddHours(Real(0))),
            BuiltInId.DateTimeAddMinutes => new StrongBox<object?>(Moment().AddMinutes(Real(0))),
            BuiltInId.DateTimeAddSeconds => new StrongBox<object?>(Moment().AddSeconds(Real(0))),
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
            BuiltInId.TimeSpanFromDays => new StrongBox<object?>(TimeSpan.FromDays(Real(0))),
            BuiltInId.TimeSpanFromHours => new StrongBox<object?>(TimeSpan.FromHours(Real(0))),
            BuiltInId.TimeSpanFromMinutes => new StrongBox<object?>(TimeSpan.FromMinutes(Real(0))),
            BuiltInId.TimeSpanFromSeconds => new StrongBox<object?>(TimeSpan.FromSeconds(Real(0))),

            BuiltInId.TimeSpanDays => new StrongBox<object?>((long)Length().Days),
            BuiltInId.TimeSpanHours => new StrongBox<object?>((long)Length().Hours),
            BuiltInId.TimeSpanMinutes => new StrongBox<object?>((long)Length().Minutes),
            BuiltInId.TimeSpanSeconds => new StrongBox<object?>((long)Length().Seconds),

            BuiltInId.TimeSpanTotalDays => new StrongBox<object?>(Length().TotalDays),
            BuiltInId.TimeSpanTotalHours => new StrongBox<object?>(Length().TotalHours),
            BuiltInId.TimeSpanTotalMinutes => new StrongBox<object?>(Length().TotalMinutes),
            BuiltInId.TimeSpanTotalSeconds => new StrongBox<object?>(Length().TotalSeconds),

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

            BuiltInId.TimeAddHours => new StrongBox<object?>(OnTheClock().AddHours(Real(0))),
            BuiltInId.TimeAddMinutes => new StrongBox<object?>(OnTheClock().AddMinutes(Real(0))),

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
            BuiltInId.SetUnion => new StrongBox<object?>(
                new ProfiCSet<object?>(Set().Concat(OtherSet(0)))),
            BuiltInId.SetIntersect => new StrongBox<object?>(
                new ProfiCSet<object?>(
                    Set().Where(element => OtherSet(0).Contains(element)))),
            BuiltInId.SetExcept => new StrongBox<object?>(
                new ProfiCSet<object?>(
                    Set().Where(element => !OtherSet(0).Contains(element)))),
            BuiltInId.SetDistinct => new StrongBox<object?>(OneOfEach(Set())),

            BuiltInId.SetSubsetFrom => new StrongBox<object?>(
                Subset(Set(), (int)Integer(0), Set().Count)),
            BuiltInId.SetSubsetBetween => new StrongBox<object?>(
                Subset(Set(), (int)Integer(0), (int)Integer(1))),

            // An element of a set of optionals is the value itself, or null for an empty one,
            // so dropping the empties is dropping the nulls.
            BuiltInId.SetTrim => new StrongBox<object?>(
                new ProfiCSet<object?>(WithoutEmptyEnds(Set(), start: true, end: true))),
            BuiltInId.SetTrimStart => new StrongBox<object?>(
                new ProfiCSet<object?>(WithoutEmptyEnds(Set(), start: true, end: false))),
            BuiltInId.SetTrimEnd => new StrongBox<object?>(
                new ProfiCSet<object?>(WithoutEmptyEnds(Set(), start: false, end: true))),
            BuiltInId.SetTrimAll => new StrongBox<object?>(
                new ProfiCSet<object?>(Set().Where(element => element is not null))),

            // Each element written the way it would be written on its own, so a set of
            // anything joins and not only a set of strings.
            BuiltInId.SetJoin => new StrongBox<object?>(
                string.Join(Text(0), Set().Select(ModelOperations.ToDisplayString))),

            BuiltInId.StringCount => new StrongBox<object?>((long)Subject().Length),
            BuiltInId.StringContains => new StrongBox<object?>(
                Subject().Contains(Text(0), StringComparison.Ordinal)),
            BuiltInId.StringIndexOf => new StrongBox<object?>(
                (long)Subject().IndexOf(Text(0), StringComparison.Ordinal)),
            BuiltInId.StringSubstring => new StrongBox<object?>(Substring(Subject(), arguments)),

            BuiltInId.StringSubsetFrom => new StrongBox<object?>(
                Subrun(Subject(), (int)Integer(0), Subject().Length)),
            BuiltInId.StringSubsetBetween => new StrongBox<object?>(
                Subrun(Subject(), (int)Integer(0), (int)Integer(1))),
            BuiltInId.StringInsert => new StrongBox<object?>(Subject() + Text(0)),
            BuiltInId.StringInsertAt => new StrongBox<object?>(
                Subject().Insert((int)Integer(0), Text(1))),
            BuiltInId.StringRemove => new StrongBox<object?>(
                Subject().Replace(Text(0), string.Empty, StringComparison.Ordinal)),
            BuiltInId.StringRemoveAt => new StrongBox<object?>(
                Subject().Remove((int)Integer(0), 1)),
            BuiltInId.StringToCharacters => new StrongBox<object?>(
                new ProfiCSet<object?>(Subject().Select(c => (object?)c))),

            BuiltInId.StringTrim => new StrongBox<object?>(Subject().Trim()),
            BuiltInId.StringTrimText => new StrongBox<object?>(Subject().Trim(Text(0).ToCharArray())),
            BuiltInId.StringTrimSet => new StrongBox<object?>(Subject().Trim(Characters(0))),

            BuiltInId.StringTrimStart => new StrongBox<object?>(Subject().TrimStart()),
            BuiltInId.StringTrimStartText =>
                new StrongBox<object?>(Subject().TrimStart(Text(0).ToCharArray())),
            BuiltInId.StringTrimStartSet =>
                new StrongBox<object?>(Subject().TrimStart(Characters(0))),

            BuiltInId.StringTrimEnd => new StrongBox<object?>(Subject().TrimEnd()),
            BuiltInId.StringTrimEndText =>
                new StrongBox<object?>(Subject().TrimEnd(Text(0).ToCharArray())),
            BuiltInId.StringTrimEndSet =>
                new StrongBox<object?>(Subject().TrimEnd(Characters(0))),

            // Splitting on an empty separator would give one empty piece per character with
            // nothing to show for it, so the whole string comes back as the only piece.
            BuiltInId.StringSplit => new StrongBox<object?>(
                new ProfiCSet<object?>(
                    Text(0).Length == 0
                        ? [Subject()]
                        : Subject().Split(Text(0), StringSplitOptions.None)
                                   .Select(piece => (object?)piece))),

            BuiltInId.StringReplace => new StrongBox<object?>(
                Text(0).Length == 0
                    ? Subject()
                    : Subject().Replace(Text(0), Text(1), StringComparison.Ordinal)),

            BuiltInId.StringToUpper => new StrongBox<object?>(
                Subject().ToUpperInvariant()),
            BuiltInId.StringToLower => new StrongBox<object?>(
                Subject().ToLowerInvariant()),

            // The first letter raised, the rest untouched. An empty string has no first
            // letter and comes back as it went in, rather than as an index nobody asked for.
            BuiltInId.StringCapitalize => new StrongBox<object?>(
                Subject().Length == 0
                    ? Subject()
                    : char.ToUpperInvariant(Subject()[0]) + Subject()[1..]),

            // An optional is the value itself, or nothing at all, so absence is a null target
            // rather than a wrapper to look inside.
            BuiltInId.OptionalHasValue => new StrongBox<object?>(target is not null),
            BuiltInId.OptionalOr => new StrongBox<object?>(target ?? Argument(0)),
            BuiltInId.OptionalValue => target is not null
                ? new StrongBox<object?>(target)
                : throw new EmptyOptionalException(),

            BuiltInId.StringToInteger => new StrongBox<object?>(
                long.TryParse(Subject(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                              out long whole)
                    ? whole
                    : null),
            BuiltInId.StringToReal => new StrongBox<object?>(
                double.TryParse(Subject(), NumberStyles.Float, CultureInfo.InvariantCulture,
                                out double measured)
                    ? measured
                    : null),

            // Only the two words the language writes, so "yes" and "1" are not truths. Read
            // without regard to case, since a person typing one is not thinking about that.
            BuiltInId.StringToBoolean => new StrongBox<object?>(
                bool.TryParse(Subject().Trim(), out bool truth) ? truth : null),

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
            BuiltInId.IntegerFormat => new StrongBox<object?>(
                AsInteger(target).ToString(Text(0), CultureInfo.InvariantCulture)),
            BuiltInId.RealFormat => new StrongBox<object?>(
                (target is double r ? r : 0).ToString(Text(0), CultureInfo.InvariantCulture)),
            BuiltInId.FractionFormat => new StrongBox<object?>(
                ((Fraction)target!).ToReal().ToString(Text(0), CultureInfo.InvariantCulture)),
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

    /// <summary>
    /// UTF-8 with no mark at the front, which is what everything else reads without being
    /// told. A mark would travel into files a Profi-C program wrote and nothing else expects.
    /// </summary>
    private static readonly System.Text.UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// <para>The elements of a set with the repeats taken out, keeping the first of each.
    /// </para>
    /// <para>Asked one at a time against what has been kept so far, rather than through a
    /// hash of each value. Equality here is the deep, cycle-safe comparison <c>==</c> uses,
    /// which no hash code is built to agree with — and asking the same way <c>Contains</c>
    /// and <c>Intersect</c> already do means all three answer alike.</para>
    /// </summary>
    private static ProfiCSet<object?> OneOfEach(ProfiCSet<object?> values)
    {
        ProfiCSet<object?> kept = new();

        foreach (object? value in values)
        {
            if (!kept.Contains(value))
            {
                kept.Insert(value);
            }
        }

        return kept;
    }

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

    /// <summary>A run of a string, with the end exclusive, as Subset has it on a set.</summary>
    private static string Subrun(string text, int start, int end)
    {
        if (start < 0 || start > text.Length || end < start || end > text.Length)
        {
            throw new IndexOutOfRangeException(
                $"Cannot take the run from {start} to {end} of a string of {text.Length}.");
        }

        return text[start..end];
    }

    /// <summary>
    /// <para>A run of a set, copied out, with the end exclusive.</para>
    /// <para>Both bounds are checked against the set rather than clamped to it, so asking for
    /// a run that is not there says so instead of quietly handing back a shorter one.</para>
    /// </summary>
    private static ProfiCSet<object?> Subset(ProfiCSet<object?> source, int start, int end)
    {
        if (start < 0 || start > source.Count || end < start || end > source.Count)
        {
            throw new IndexOutOfRangeException(
                $"Cannot take the run from {start} to {end} of a set of {source.Count} elements.");
        }

        return new ProfiCSet<object?>(source.Skip(start).Take(end - start));
    }

    /// <summary>
    /// Drops the empty elements from one or both ends of a set of optionals, keeping
    /// everything between the first and last that hold something.
    /// </summary>
    private static IEnumerable<object?> WithoutEmptyEnds(
        ProfiCSet<object?> source,
        bool start,
        bool end)
    {
        int first = 0;
        int last = source.Count - 1;

        if (start)
        {
            while (first <= last && source[first] is null)
            {
                first++;
            }
        }

        if (end)
        {
            while (last >= first && source[last] is null)
            {
                last--;
            }
        }

        for (int i = first; i <= last; i++)
        {
            yield return source[i];
        }
    }

    /// <summary>
    /// <para>The nth root, which .NET spells only for n of 2 and 3.</para>
    /// <para>A negative number has a real root when n is odd — the cube root of -8 is -2 —
    /// and none at all when n is even, so the odd case is worked out from the magnitude and
    /// the sign put back. Left to Pow it would be NaN, since a fractional power of a negative
    /// is not a real number.</para>
    /// </summary>
    private static double Root(double value, double degree)
    {
        if (degree == 0)
        {
            throw new ProfiCRuntimeException("A root of degree zero is not a number.");
        }

        if (value >= 0)
        {
            return Exactly(Math.Pow(value, 1.0 / degree), value, degree);
        }

        bool odd = degree == Math.Floor(degree) && Math.Abs(degree % 2) == 1;

        return odd
            ? Exactly(-Math.Pow(-value, 1.0 / degree), value, degree)
            : double.NaN;
    }

    /// <summary>
    /// <para>Corrects a root to the whole number it should be, where there is one.</para>
    /// <para>Roots are not required to be correctly rounded and the platforms disagree: the
    /// cube root of 27 comes back as 3 from one C runtime and as 3.0000000000000004 from
    /// another. Where raising the nearest whole number by the degree gives the value back
    /// exactly, that whole number <em>is</em> a root of it, and the drift is simply a worse
    /// answer than the type can hold.</para>
    /// <para>So this is more accurate rather than a fudge, and it is what lets a program
    /// print the same thing wherever it is run. Only a whole degree within reach is
    /// considered, and the check is a multiplication rather than another call to Pow, so
    /// nothing here rests on the library that produced the drift.</para>
    /// </summary>
    private static double Exactly(double approximate, double value, double degree)
    {
        if (degree < 1 || degree > 64 || degree != Math.Floor(degree))
        {
            return approximate;
        }

        double whole = Math.Round(approximate);
        double raised = 1;

        for (int i = 0; i < degree; i++)
        {
            raised *= whole;
        }

        return raised == value ? whole : approximate;
    }

    /// <summary>
    /// <para>Counts arrangements, so it counts in whole numbers.</para>
    /// <para>Twenty is the largest whose answer an integer holds; the twenty-first overflows,
    /// which is reported as an overflow rather than wrapping into a smaller wrong answer.</para>
    /// </summary>
    private static long Factorial(long n)
    {
        if (n < 0)
        {
            throw new ArgumentException("A factorial counts arrangements, so it needs a whole number that is not negative.");
        }

        long result = 1;

        try
        {
            for (long i = 2; i <= n; i++)
            {
                result = checked(result * i);
            }
        }
        catch (OverflowException)
        {
            throw Runtime.ArithmeticFailures.TooLargeForAnInteger();
        }

        return result;
    }
}

/// <summary>
/// Wraps a result so that "produced nothing" can be told apart from "did not handle this".
/// </summary>
internal sealed class StrongBox<T>(T value)
{
    public T Value { get; } = value;
}
