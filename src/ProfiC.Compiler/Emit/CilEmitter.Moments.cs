using System.Reflection;
using System.Reflection.Emit;
using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Semantics;
using ProfiC.Runtime;

namespace ProfiC.Compiler.Emit;

/// <summary>
/// <para>The types the language provides that hold something rather than describe it: a moment, a
/// day, a time of day, a length of time, a generator, and the two ways to reach a disk.</para>
/// <para><b>Every one is a call into the runtime, and never the framework member of the same
/// name.</b> <c>DateTime.Year</c> answers in 32 bits where a Profi-C <c>integer</c> is 64;
/// <c>AddDays</c> asks for binary floating point where a <c>real</c> counts in tens; a listing
/// comes back in whatever order the file system felt like. Each of those is a place two engines
/// could quietly part company, and <see cref="ProfiCMoments"/> and <see cref="ProfiCFiles"/> are
/// where the answer is decided once.</para>
/// <para><b>The shape is uniform because the runtime was written for it.</b> Every method is
/// static and takes what it works on as its first argument, so emitting one is: push the receiver
/// if there is one, push the arguments, call. Nothing here converts anything — that was the point
/// of putting the crossings in the runtime.</para>
/// </summary>
public sealed partial class CilEmitter
{
    /// <summary>
    /// One member of a provided type: the value it was written on, the arguments, then the call.
    /// </summary>
    private void EmitProvidedMember(
        MemberExpr member,
        IReadOnlyList<Expression> arguments,
        BuiltInId id)
    {
        // A generator is the one shape here that is an object rather than a value, and the one
        // reachable two ways: through a program's own, or through the name, which means the one
        // the runtime keeps.
        if (CilBuiltIns.IsOnAGenerator(id))
        {
            if (IsThroughATypeName(member.Receiver))
            {
                _il.Emit(OpCodes.Call, SharedGenerator);
            }
            else
            {
                EmitExpression(member.Receiver);
            }

            EmitArguments(arguments);
            _il.Emit(OpCodes.Callvirt, GeneratorMethod(id));

            return;
        }

        // Everything else is static. A member reached through a type name — 'DateTime.Now',
        // 'File.Read(path)' — has no value in front of it to push.
        if (!IsThroughATypeName(member.Receiver))
        {
            EmitExpression(member.Receiver);
        }

        EmitArguments(arguments);
        _il.Emit(OpCodes.Call, ProvidedMethod(id));
    }

    /// <summary>
    /// <para>Makes one: a moment, a day, a time of day, a length, or a generator.</para>
    /// <para>Every one but the generator goes through a runtime factory rather than a
    /// constructor, because the thirty-first of February has to be refused in the language's
    /// words — the platform names a parameter, which tells a reader nothing about the date they
    /// wrote.</para>
    /// </summary>
    private void EmitProvidedConstruction(NewExpr construction, BuiltInId id)
    {
        EmitArguments(construction.Arguments);

        switch (id)
        {
            case BuiltInId.RandomNew:
                _il.Emit(OpCodes.Newobj, GeneratorFromNothing);
                return;

            case BuiltInId.RandomNewSeeded:
                _il.Emit(OpCodes.Newobj, GeneratorFromSeed);
                return;

            default:
                _il.Emit(OpCodes.Call, ProvidedMethod(id));
                return;
        }
    }

    /// <summary>
    /// <para>The runtime method behind one member.</para>
    /// <para>Named here rather than at each use because several share a name and are told apart
    /// by what they take — <c>Format</c> has four forms and <c>MakeSpan</c> and <c>MakeTime</c>
    /// two each — so choosing the overload is one question answered in one place.</para>
    /// </summary>
    private static MethodInfo ProvidedMethod(BuiltInId id) => id switch
    {
        // ---- Making one ---------------------------------------------------------------------
        BuiltInId.DateTimeNewDate => Moment(nameof(ProfiCMoments.MakeDay), L, L, L),
        BuiltInId.DateTimeNewMoment => Moment(nameof(ProfiCMoments.MakeMoment), L, L, L, L, L, L),
        BuiltInId.DateNew => Moment(nameof(ProfiCMoments.MakeDate), L, L, L),
        BuiltInId.TimeNewToMinute => Moment(nameof(ProfiCMoments.MakeTime), L, L),
        BuiltInId.TimeNewToSecond => Moment(nameof(ProfiCMoments.MakeTime), L, L, L),
        BuiltInId.TimeSpanNewTime => Moment(nameof(ProfiCMoments.MakeSpan), L, L, L),
        BuiltInId.TimeSpanNewSpan => Moment(nameof(ProfiCMoments.MakeSpan), L, L, L, L),

        // ---- A moment -----------------------------------------------------------------------
        BuiltInId.DateTimeNow => Moment(nameof(ProfiCMoments.Now)),
        BuiltInId.DateTimeToday => Moment(nameof(ProfiCMoments.Today)),

        BuiltInId.DateTimeYear => Moment(nameof(ProfiCMoments.Year), M),
        BuiltInId.DateTimeMonth => Moment(nameof(ProfiCMoments.Month), M),
        BuiltInId.DateTimeDay => Moment(nameof(ProfiCMoments.Day), M),
        BuiltInId.DateTimeHour => Moment(nameof(ProfiCMoments.Hour), M),
        BuiltInId.DateTimeMinute => Moment(nameof(ProfiCMoments.Minute), M),
        BuiltInId.DateTimeSecond => Moment(nameof(ProfiCMoments.Second), M),
        BuiltInId.DateTimeDayOfWeek => Moment(nameof(ProfiCMoments.DayOfWeek), M),
        BuiltInId.DateTimeDayOfYear => Moment(nameof(ProfiCMoments.DayOfYear), M),

        BuiltInId.DateTimeAddDays => Moment(nameof(ProfiCMoments.AddDays), M, R),
        BuiltInId.DateTimeAddHours => Moment(nameof(ProfiCMoments.AddHours), M, R),
        BuiltInId.DateTimeAddMinutes => Moment(nameof(ProfiCMoments.AddMinutes), M, R),
        BuiltInId.DateTimeAddSeconds => Moment(nameof(ProfiCMoments.AddSeconds), M, R),
        BuiltInId.DateTimeAddYears => Moment(nameof(ProfiCMoments.AddYears), M, L),
        BuiltInId.DateTimeAddMonths => Moment(nameof(ProfiCMoments.AddMonths), M, L),

        BuiltInId.DateTimeCompareTo => Moment(nameof(ProfiCMoments.CompareMoments), M, M),
        BuiltInId.DateTimeAdd => Moment(nameof(ProfiCMoments.Add), M, S),
        BuiltInId.DateTimeSubtract => Moment(nameof(ProfiCMoments.Subtract), M, M),
        BuiltInId.DateTimeSubtractSpan => Moment(nameof(ProfiCMoments.SubtractSpan), M, S),

        BuiltInId.DateTimeDatePart => Moment(nameof(ProfiCMoments.DatePart), M),
        BuiltInId.DateTimeTimePart => Moment(nameof(ProfiCMoments.TimePart), M),
        BuiltInId.DateTimeFromDate => Moment(nameof(ProfiCMoments.FromDate), D),
        BuiltInId.DateTimeFromDateAndTime => Moment(nameof(ProfiCMoments.FromDateAndTime), D, T),

        // ---- A length of time ---------------------------------------------------------------
        BuiltInId.TimeSpanZero => Moment(nameof(ProfiCMoments.Zero)),
        BuiltInId.TimeSpanFromDays => Moment(nameof(ProfiCMoments.FromDays), R),
        BuiltInId.TimeSpanFromHours => Moment(nameof(ProfiCMoments.FromHours), R),
        BuiltInId.TimeSpanFromMinutes => Moment(nameof(ProfiCMoments.FromMinutes), R),
        BuiltInId.TimeSpanFromSeconds => Moment(nameof(ProfiCMoments.FromSeconds), R),

        BuiltInId.TimeSpanDays => Moment(nameof(ProfiCMoments.Days), S),
        BuiltInId.TimeSpanHours => Moment(nameof(ProfiCMoments.Hours), S),
        BuiltInId.TimeSpanMinutes => Moment(nameof(ProfiCMoments.Minutes), S),
        BuiltInId.TimeSpanSeconds => Moment(nameof(ProfiCMoments.Seconds), S),

        BuiltInId.TimeSpanTotalDays => Moment(nameof(ProfiCMoments.TotalDays), S),
        BuiltInId.TimeSpanTotalHours => Moment(nameof(ProfiCMoments.TotalHours), S),
        BuiltInId.TimeSpanTotalMinutes => Moment(nameof(ProfiCMoments.TotalMinutes), S),
        BuiltInId.TimeSpanTotalSeconds => Moment(nameof(ProfiCMoments.TotalSeconds), S),

        BuiltInId.TimeSpanNegate => Moment(nameof(ProfiCMoments.Negate), S),
        BuiltInId.TimeSpanDuration => Moment(nameof(ProfiCMoments.Duration), S),
        BuiltInId.TimeSpanAdd => Moment(nameof(ProfiCMoments.AddSpan), S, S),
        BuiltInId.TimeSpanSubtract => Moment(nameof(ProfiCMoments.SubtractSpans), S, S),
        BuiltInId.TimeSpanCompareTo => Moment(nameof(ProfiCMoments.CompareSpans), S, S),

        // ---- A day --------------------------------------------------------------------------
        BuiltInId.DateToday => Moment(nameof(ProfiCMoments.TodayOnly)),
        BuiltInId.DateFromMoment => Moment(nameof(ProfiCMoments.DateFromMoment), M),

        BuiltInId.DateYear => Moment(nameof(ProfiCMoments.DateYear), D),
        BuiltInId.DateMonth => Moment(nameof(ProfiCMoments.DateMonth), D),
        BuiltInId.DateDay => Moment(nameof(ProfiCMoments.DateDay), D),
        BuiltInId.DateDayOfWeek => Moment(nameof(ProfiCMoments.DateDayOfWeek), D),
        BuiltInId.DateDayOfYear => Moment(nameof(ProfiCMoments.DateDayOfYear), D),

        BuiltInId.DateAddDays => Moment(nameof(ProfiCMoments.DateAddDays), D, L),
        BuiltInId.DateAddMonths => Moment(nameof(ProfiCMoments.DateAddMonths), D, L),
        BuiltInId.DateAddYears => Moment(nameof(ProfiCMoments.DateAddYears), D, L),
        BuiltInId.DateAtTime => Moment(nameof(ProfiCMoments.DateAtTime), D, T),
        BuiltInId.DateCompareTo => Moment(nameof(ProfiCMoments.CompareDates), D, D),

        // ---- A time of day ------------------------------------------------------------------
        BuiltInId.TimeNow => Moment(nameof(ProfiCMoments.TimeNow)),
        BuiltInId.TimeFromMoment => Moment(nameof(ProfiCMoments.TimeFromMoment), M),

        BuiltInId.TimeHour => Moment(nameof(ProfiCMoments.TimeHour), T),
        BuiltInId.TimeMinute => Moment(nameof(ProfiCMoments.TimeMinute), T),
        BuiltInId.TimeSecond => Moment(nameof(ProfiCMoments.TimeSecond), T),

        BuiltInId.TimeAddHours => Moment(nameof(ProfiCMoments.TimeAddHours), T, R),
        BuiltInId.TimeAddMinutes => Moment(nameof(ProfiCMoments.TimeAddMinutes), T, R),
        BuiltInId.TimeToTimeSpan => Moment(nameof(ProfiCMoments.TimeToSpan), T),
        BuiltInId.TimeCompareTo => Moment(nameof(ProfiCMoments.CompareTimes), T, T),

        // ---- Written by a pattern, and read back from text ----------------------------------
        BuiltInId.DateTimeFormat => Moment(nameof(ProfiCMoments.Format), M, X),
        BuiltInId.TimeSpanFormat => Moment(nameof(ProfiCMoments.Format), S, X),
        BuiltInId.DateFormat => Moment(nameof(ProfiCMoments.Format), D, X),
        BuiltInId.TimeFormat => Moment(nameof(ProfiCMoments.Format), T, X),

        BuiltInId.DateTimeParse => Moment(nameof(ProfiCMoments.ParseMoment), X),
        BuiltInId.DateTimeParseExact => Moment(nameof(ProfiCMoments.ParseMomentExactly), X, X),
        BuiltInId.TimeSpanParse => Moment(nameof(ProfiCMoments.ParseSpan), X),
        BuiltInId.TimeSpanParseExact => Moment(nameof(ProfiCMoments.ParseSpanExactly), X, X),
        BuiltInId.DateParse => Moment(nameof(ProfiCMoments.ParseDate), X),
        BuiltInId.DateParseExact => Moment(nameof(ProfiCMoments.ParseDateExactly), X, X),
        BuiltInId.TimeParse => Moment(nameof(ProfiCMoments.ParseTime), X),
        BuiltInId.TimeParseExact => Moment(nameof(ProfiCMoments.ParseTimeExactly), X, X),

        // ---- Files and folders --------------------------------------------------------------
        BuiltInId.FileRead => Files(nameof(ProfiCFiles.Read), X),
        BuiltInId.FileReadLines => Files(nameof(ProfiCFiles.ReadLines), X),
        BuiltInId.FileWrite => Files(nameof(ProfiCFiles.Write), X, X),
        BuiltInId.FileWriteLines => Files(nameof(ProfiCFiles.WriteLines), X, typeof(IProfiCSet)),
        BuiltInId.FileAppend => Files(nameof(ProfiCFiles.Append), X, X),
        BuiltInId.FileExists => Files(nameof(ProfiCFiles.Exists), X),
        BuiltInId.FileDelete => Files(nameof(ProfiCFiles.Delete), X),
        BuiltInId.FileCopy => Files(nameof(ProfiCFiles.Copy), X, X),
        BuiltInId.FileMove => Files(nameof(ProfiCFiles.Move), X, X),
        BuiltInId.FileSize => Files(nameof(ProfiCFiles.Size), X),
        BuiltInId.FileChanged => Files(nameof(ProfiCFiles.Changed), X),

        BuiltInId.DirectoryCurrent => Files(nameof(ProfiCFiles.Current)),
        BuiltInId.DirectoryExists => Files(nameof(ProfiCFiles.FolderExists), X),
        BuiltInId.DirectoryCreate => Files(nameof(ProfiCFiles.CreateFolder), X),
        BuiltInId.DirectoryDelete => Files(nameof(ProfiCFiles.DeleteFolder), X),
        BuiltInId.DirectoryFiles => Files(nameof(ProfiCFiles.Files), X),
        BuiltInId.DirectoryFolders => Files(nameof(ProfiCFiles.Folders), X),

        _ => throw new InvalidOperationException($"No runtime method stands behind '{id}'."),
    };

    private static MethodInfo GeneratorMethod(BuiltInId id) => id switch
    {
        BuiltInId.RandomNext => Generator(nameof(ProfiCRandom.Next)),
        BuiltInId.RandomNextBelow => Generator(nameof(ProfiCRandom.Next), L),
        BuiltInId.RandomNextBetween => Generator(nameof(ProfiCRandom.Next), L, L),
        BuiltInId.RandomNextDouble => Generator(nameof(ProfiCRandom.NextReal)),

        _ => throw new InvalidOperationException($"No generator method stands behind '{id}'."),
    };

    // The types these are written in, short because the table above is long and reads better as
    // a shape than as prose. L is an integer, R a real, X a string.
    private static readonly Type L = typeof(long);
    private static readonly Type R = typeof(decimal);
    private static readonly Type X = typeof(string);
    private static readonly Type M = typeof(DateTime);
    private static readonly Type S = typeof(TimeSpan);
    private static readonly Type D = typeof(DateOnly);
    private static readonly Type T = typeof(TimeOnly);

    private static MethodInfo Moment(string name, params Type[] taking) =>
        typeof(ProfiCMoments).GetMethod(name, taking)
        ?? throw new InvalidOperationException($"The runtime has no '{name}' taking those.");

    private static MethodInfo Files(string name, params Type[] taking) =>
        typeof(ProfiCFiles).GetMethod(name, taking)
        ?? throw new InvalidOperationException($"The runtime has no '{name}' taking those.");

    private static MethodInfo Generator(string name, params Type[] taking) =>
        typeof(ProfiCRandom).GetMethod(name, taking)
        ?? throw new InvalidOperationException($"A generator has no '{name}' taking those.");

    private static readonly MethodInfo SharedGenerator =
        typeof(ProfiCRandom).GetProperty(nameof(ProfiCRandom.Shared))!.GetMethod!;

    private static readonly System.Reflection.ConstructorInfo GeneratorFromNothing =
        typeof(ProfiCRandom).GetConstructor(Type.EmptyTypes)!;

    private static readonly System.Reflection.ConstructorInfo GeneratorFromSeed =
        typeof(ProfiCRandom).GetConstructor([typeof(long)])!;
}
