using ProfiC.Compiler.Semantics;

namespace ProfiC.Compiler.Emit;

/// <summary>
/// <para>The built-in operations the emitter knows a call sequence for.</para>
/// <para><c>Console</c> came first, and not arbitrarily: a program with no output cannot be told
/// from a program that did not run, so printing is what makes every other emitted instruction
/// checkable against the interpreter.</para>
/// <para>Named separately from the list of what is supported, because a refusal reads better
/// saying <c>Console.WriteLine</c> than saying <c>ConsoleWriteLine</c>.</para>
/// </summary>
internal static class CilBuiltIns
{
    public static bool IsSupported(BuiltInId id) =>
        id is BuiltInId.ConsoleWrite or BuiltInId.ConsoleWriteLine or BuiltInId.ConsoleRead
           or BuiltInId.ReferenceEquals
        || IsOnEveryValue(id)
        || IsOnASet(id)
        || IsOnAString(id)
        || IsOnMath(id)
        || IsFormatting(id)
        || IsCrossingBetweenReals(id)
        || IsOnAFraction(id)
        || IsABound(id)
        || IsOnAnOptional(id)
        || IsOnAMoment(id)
        || IsOnAFile(id)
        || IsOnAGenerator(id)
        || IsConstructingAProvidedType(id)
        || id is BuiltInId.ExceptionMessage or BuiltInId.EnumerationToInteger;

    /// <summary>
    /// <para>The four types that measure time: a moment, a length of one, a day, and a time of
    /// day.</para>
    /// <para>Together rather than four lists, because they are one family — every member of each
    /// is a call into <see cref="Runtime.ProfiCMoments"/>, and several take one and answer with
    /// another.</para>
    /// </summary>
    public static bool IsOnAMoment(BuiltInId id) =>
        id is BuiltInId.DateTimeNow
           or BuiltInId.DateTimeToday
           or BuiltInId.DateTimeYear
           or BuiltInId.DateTimeMonth
           or BuiltInId.DateTimeDay
           or BuiltInId.DateTimeHour
           or BuiltInId.DateTimeMinute
           or BuiltInId.DateTimeSecond
           or BuiltInId.DateTimeDayOfWeek
           or BuiltInId.DateTimeDayOfYear
           or BuiltInId.DateTimeAddDays
           or BuiltInId.DateTimeAddHours
           or BuiltInId.DateTimeAddMinutes
           or BuiltInId.DateTimeAddSeconds
           or BuiltInId.DateTimeAddYears
           or BuiltInId.DateTimeAddMonths
           or BuiltInId.DateTimeCompareTo
           or BuiltInId.DateTimeAdd
           or BuiltInId.DateTimeSubtract
           or BuiltInId.DateTimeSubtractSpan
           or BuiltInId.DateTimeDatePart
           or BuiltInId.DateTimeTimePart
           or BuiltInId.DateTimeFromDate
           or BuiltInId.DateTimeFromDateAndTime
           or BuiltInId.DateTimeFormat
           or BuiltInId.DateTimeParse
           or BuiltInId.DateTimeParseExact

           or BuiltInId.TimeSpanZero
           or BuiltInId.TimeSpanFromDays
           or BuiltInId.TimeSpanFromHours
           or BuiltInId.TimeSpanFromMinutes
           or BuiltInId.TimeSpanFromSeconds
           or BuiltInId.TimeSpanDays
           or BuiltInId.TimeSpanHours
           or BuiltInId.TimeSpanMinutes
           or BuiltInId.TimeSpanSeconds
           or BuiltInId.TimeSpanTotalDays
           or BuiltInId.TimeSpanTotalHours
           or BuiltInId.TimeSpanTotalMinutes
           or BuiltInId.TimeSpanTotalSeconds
           or BuiltInId.TimeSpanNegate
           or BuiltInId.TimeSpanDuration
           or BuiltInId.TimeSpanAdd
           or BuiltInId.TimeSpanSubtract
           or BuiltInId.TimeSpanCompareTo
           or BuiltInId.TimeSpanFormat
           or BuiltInId.TimeSpanParse
           or BuiltInId.TimeSpanParseExact

           or BuiltInId.DateToday
           or BuiltInId.DateFromMoment
           or BuiltInId.DateYear
           or BuiltInId.DateMonth
           or BuiltInId.DateDay
           or BuiltInId.DateDayOfWeek
           or BuiltInId.DateDayOfYear
           or BuiltInId.DateAddDays
           or BuiltInId.DateAddMonths
           or BuiltInId.DateAddYears
           or BuiltInId.DateAtTime
           or BuiltInId.DateCompareTo
           or BuiltInId.DateFormat
           or BuiltInId.DateParse
           or BuiltInId.DateParseExact

           or BuiltInId.TimeNow
           or BuiltInId.TimeFromMoment
           or BuiltInId.TimeHour
           or BuiltInId.TimeMinute
           or BuiltInId.TimeSecond
           or BuiltInId.TimeAddHours
           or BuiltInId.TimeAddMinutes
           or BuiltInId.TimeToTimeSpan
           or BuiltInId.TimeCompareTo
           or BuiltInId.TimeFormat
           or BuiltInId.TimeParse
           or BuiltInId.TimeParseExact;

    /// <summary>Reaching a disk, which is <c>File</c> and <c>Directory</c>.</summary>
    public static bool IsOnAFile(BuiltInId id) =>
        id is BuiltInId.FileRead
           or BuiltInId.FileReadLines
           or BuiltInId.FileWrite
           or BuiltInId.FileWriteLines
           or BuiltInId.FileAppend
           or BuiltInId.FileExists
           or BuiltInId.FileDelete
           or BuiltInId.FileCopy
           or BuiltInId.FileMove
           or BuiltInId.FileSize
           or BuiltInId.FileChanged
           or BuiltInId.DirectoryCurrent
           or BuiltInId.DirectoryExists
           or BuiltInId.DirectoryCreate
           or BuiltInId.DirectoryDelete
           or BuiltInId.DirectoryFiles
           or BuiltInId.DirectoryFolders;

    /// <summary>
    /// What a generator answers. Told apart from the rest because it is an object rather than a
    /// value, so its members are called on it rather than passed it.
    /// </summary>
    public static bool IsOnAGenerator(BuiltInId id) =>
        id is BuiltInId.RandomNext
           or BuiltInId.RandomNextBelow
           or BuiltInId.RandomNextBetween
           or BuiltInId.RandomNextDouble;

    /// <summary>
    /// <para>Making one of the provided types, which is a <c>new</c> rather than a member.</para>
    /// <para>Every one but a generator goes through a runtime factory rather than a constructor,
    /// so that a date nobody could write is refused in the language's words.</para>
    /// </summary>
    public static bool IsConstructingAProvidedType(BuiltInId id) =>
        id is BuiltInId.DateTimeNewDate
           or BuiltInId.DateTimeNewMoment
           or BuiltInId.DateNew
           or BuiltInId.TimeNewToMinute
           or BuiltInId.TimeNewToSecond
           or BuiltInId.TimeSpanNewTime
           or BuiltInId.TimeSpanNewSpan
           or BuiltInId.RandomNew
           or BuiltInId.RandomNewSeeded;

    /// <summary>
    /// <para>The members a fraction answers, each one call on the runtime's own struct.</para>
    /// <para>Making one is here too: <c>Fraction.Of</c> and its whole-number form are the two
    /// ways a program builds a ratio from values rather than from a literal.</para>
    /// </summary>
    /// <summary>
    /// <para>What each primitive knows about itself, read through a capitalized name beside its
    /// keyword.</para>
    /// <para>Every one is a constant, so each arrives as the value itself rather than as a field
    /// to read — which is what lets the emitter answer them without the runtime holding
    /// anything.</para>
    /// </summary>
    public static bool IsABound(BuiltInId id) =>
        id is BuiltInId.IntegerMaxValue
           or BuiltInId.IntegerMinValue
           or BuiltInId.RealMaxValue
           or BuiltInId.RealMinValue
           or BuiltInId.FloatMaxValue
           or BuiltInId.FloatMinValue
           or BuiltInId.FloatInfinity
           or BuiltInId.FloatNegativeInfinity
           or BuiltInId.FloatNotANumber
           or BuiltInId.StringEmpty;

    public static bool IsOnAFraction(BuiltInId id) =>
        id is BuiltInId.FractionToReal
           or BuiltInId.FractionToFloat
           or BuiltInId.FractionReciprocal
           or BuiltInId.FractionFormat
           or BuiltInId.FractionCreate
           or BuiltInId.FractionCreateWhole

           // Reached on a float rather than on a fraction, but answering with one — so it belongs
           // here, where the type it makes is.
           or BuiltInId.FloatToFraction;

    /// <summary>
    /// <para>Writing a number by a pattern, which is the way out that reading one out of text is
    /// the way in.</para>
    /// <para>A fraction formats too, and is not here: it has no CLR type in an emitted program to
    /// be written from.</para>
    /// </summary>
    public static bool IsFormatting(BuiltInId id) =>
        id is BuiltInId.IntegerFormat or BuiltInId.RealFormat or BuiltInId.FloatFormat;

    /// <summary>
    /// Crossing between the two kinds of decimal-point number, which is one call each way — the
    /// runtime's, so that the three ways back from a float can fail are said in the language's
    /// words rather than reported as one overflow.
    /// </summary>
    public static bool IsCrossingBetweenReals(BuiltInId id) =>
        id is BuiltInId.RealToFloat or BuiltInId.FloatToReal or BuiltInId.IntegerToFloat;

    /// <summary>
    /// <para>What every value answers, <c>Model</c> being the root of them all.</para>
    /// <para><c>ToString</c> goes to the runtime rather than the framework, because how a value
    /// reads is the language's decision: a boolean reads as <c>true</c> and not <c>True</c>, and
    /// a set reads with its braces.</para>
    /// <para><c>Equals</c> is the same walk <c>==</c> is, and reaches the fields of an emitted
    /// model through <see cref="Runtime.IProfiCModel"/>, which every model implements.</para>
    /// </summary>
    public static bool IsOnEveryValue(BuiltInId id) =>
        id is BuiltInId.ModelToString or BuiltInId.ModelEquals;

    /// <summary>
    /// <para>The members of a string, each one call into the runtime's own text.</para>
    /// <para>Every one of them but <c>ToFraction</c>, which waits on the fraction: reading text
    /// into one means constructing one, and the emitter has no way to make a fraction yet.</para>
    /// </summary>
    public static bool IsOnAString(BuiltInId id) =>
        id is BuiltInId.StringCount
           or BuiltInId.StringContains
           or BuiltInId.StringIndexOf
           or BuiltInId.StringSubstring
           or BuiltInId.StringSubsetFrom
           or BuiltInId.StringSubsetBetween
           or BuiltInId.StringInsert
           or BuiltInId.StringInsertAt
           or BuiltInId.StringRemove
           or BuiltInId.StringRemoveAt
           or BuiltInId.StringToCharacters
           or BuiltInId.StringTrim
           or BuiltInId.StringTrimText
           or BuiltInId.StringTrimSet
           or BuiltInId.StringTrimStart
           or BuiltInId.StringTrimStartText
           or BuiltInId.StringTrimStartSet
           or BuiltInId.StringTrimEnd
           or BuiltInId.StringTrimEndText
           or BuiltInId.StringTrimEndSet
           or BuiltInId.StringSplit
           or BuiltInId.StringReplace
           or BuiltInId.StringToUpper
           or BuiltInId.StringToLower
           or BuiltInId.StringCapitalize
           or BuiltInId.StringToInteger
           or BuiltInId.StringToReal
           or BuiltInId.StringToBoolean
           or BuiltInId.StringToFraction;

    /// <summary>
    /// <para>The members of <c>Math</c>, each one call into the runtime's own arithmetic.</para>
    /// <para>Every one of them that does not take or give back a fraction. Those wait on the
    /// fraction itself, which is a type the emitter has no way to make.</para>
    /// </summary>
    public static bool IsOnMath(BuiltInId id) =>
        id is BuiltInId.MathPi
           or BuiltInId.MathE
           or BuiltInId.MathSqrt
           or BuiltInId.MathCbrt
           or BuiltInId.MathRoot
           or BuiltInId.MathPow
           or BuiltInId.MathFactorial
           or BuiltInId.MathLog
           or BuiltInId.MathLogInBase
           or BuiltInId.MathLog10
           or BuiltInId.MathLog2
           or BuiltInId.MathSin
           or BuiltInId.MathCos
           or BuiltInId.MathTan
           or BuiltInId.MathAsin
           or BuiltInId.MathAcos
           or BuiltInId.MathAtan
           or BuiltInId.MathAtan2
           or BuiltInId.MathSinh
           or BuiltInId.MathCosh
           or BuiltInId.MathTanh
           or BuiltInId.MathAsinh
           or BuiltInId.MathAcosh
           or BuiltInId.MathAtanh
           or BuiltInId.MathAbsInteger
           or BuiltInId.MathAbsReal
           or BuiltInId.MathFloorReal
           or BuiltInId.MathCeilingReal
           or BuiltInId.MathRoundReal
           or BuiltInId.MathRoundRealPlaces
           or BuiltInId.MathMinInteger
           or BuiltInId.MathMinReal
           or BuiltInId.MathMaxInteger
           or BuiltInId.MathMaxReal

           // The binary half of every pair, all of which emit — a float is what the framework's
           // own functions were written for, so none of them waits on anything.
           or BuiltInId.MathSqrtFloat
           or BuiltInId.MathCbrtFloat
           or BuiltInId.MathRootFloat
           or BuiltInId.MathPowFloat
           or BuiltInId.MathLogFloat
           or BuiltInId.MathLogInBaseFloat
           or BuiltInId.MathLog10Float
           or BuiltInId.MathLog2Float
           or BuiltInId.MathSinFloat
           or BuiltInId.MathCosFloat
           or BuiltInId.MathTanFloat
           or BuiltInId.MathAsinFloat
           or BuiltInId.MathAcosFloat
           or BuiltInId.MathAtanFloat
           or BuiltInId.MathAtan2Float
           or BuiltInId.MathSinhFloat
           or BuiltInId.MathCoshFloat
           or BuiltInId.MathTanhFloat
           or BuiltInId.MathAsinhFloat
           or BuiltInId.MathAcoshFloat
           or BuiltInId.MathAtanhFloat
           or BuiltInId.MathAbsFloat
           or BuiltInId.MathFloorFloat
           or BuiltInId.MathCeilingFloat
           or BuiltInId.MathRoundFloat
           or BuiltInId.MathRoundFloatPlaces
           or BuiltInId.MathMinFloat
           or BuiltInId.MathMaxFloat

           // And the fraction forms, now that a fraction is a type the emitter can make.
           or BuiltInId.MathAbsFraction
           or BuiltInId.MathFloorFraction
           or BuiltInId.MathCeilingFraction
           or BuiltInId.MathRoundFraction
           or BuiltInId.MathMinFraction
           or BuiltInId.MathMaxFraction;

    /// <summary>The three members an optional has, and there are only three.</summary>
    public static bool IsOnAnOptional(BuiltInId id) =>
        id is BuiltInId.OptionalHasValue
           or BuiltInId.OptionalValue
           or BuiltInId.OptionalOr;

    /// <summary>
    /// <para>The members of a set, each one call on the runtime's own.</para>
    /// <para>All of them, the <c>Trim</c> family included — that one only exists on a set of
    /// optionals, and <c>TrimAll</c> is the single member here that answers with a different
    /// kind of set than it was asked of.</para>
    /// </summary>
    public static bool IsOnASet(BuiltInId id) =>
        id is BuiltInId.SetCount
           or BuiltInId.SetInsert
           or BuiltInId.SetInsertAt
           or BuiltInId.SetRemove
           or BuiltInId.SetRemoveAt
           or BuiltInId.SetContains
           or BuiltInId.SetIndexOf
           or BuiltInId.SetClear
           or BuiltInId.SetSubsetFrom
           or BuiltInId.SetSubsetBetween
           or BuiltInId.SetUnion
           or BuiltInId.SetIntersect
           or BuiltInId.SetExcept
           or BuiltInId.SetDistinct
           or BuiltInId.SetJoin
           or BuiltInId.SetTrim
           or BuiltInId.SetTrimStart
           or BuiltInId.SetTrimEnd
           or BuiltInId.SetTrimAll;

    /// <summary>How a reader wrote it, for a message about it.</summary>
    public static string NameOf(BuiltInId id) => id switch
    {
        BuiltInId.ConsoleWrite => "Console.Write",
        BuiltInId.ConsoleWriteLine => "Console.WriteLine",
        BuiltInId.ConsoleRead => "Console.Read",
        _ => $"'{id}'",
    };
}
