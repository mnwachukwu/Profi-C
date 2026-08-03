namespace ProfiC.Compiler.Semantics;

/// <summary>
/// <para>Every member reached through the name of a built-in model, one identifier each.</para>
/// <para>These join the two halves of a built-in — what the type checker knows about it, and
/// what happens when it runs — with something the C# compiler checks. The back end switches on this 
/// enumeration without a fallback arm, so a member declared here and implemented nowhere does not 
/// compile.</para>
/// </summary>
public enum BuiltInId
{
    ConsoleWrite,
    ConsoleWriteLine,
    ConsoleRead,

    ReferenceEquals,

    MathPi,
    MathE,

    /// <summary>
    /// <para>What each primitive knows about itself: where it runs out, and — for a float — the
    /// values it has that no other number type does.</para>
    /// <para>Read through a capitalized name beside the keyword, since a reserved word cannot
    /// stand in front of a dot.</para>
    /// </summary>
    IntegerMaxValue,
    IntegerMinValue,
    RealMaxValue,
    RealMinValue,
    FloatMaxValue,
    FloatMinValue,
    FloatInfinity,
    FloatNegativeInfinity,
    FloatNotANumber,
    StringEmpty,

    MathSqrt,
    MathCbrt,
    MathRoot,
    MathPow,
    MathFactorial,

    // .NET's names, and .NET's meanings with them: Log of one number is the natural
    // logarithm. Spelling it any other way here would give a reader who moves to C# the same
    // program and a different answer, which is the one outcome worth more than a nicer name.
    MathLog,
    MathLogInBase,
    MathLog10,
    MathLog2,

    MathSin,
    MathCos,
    MathTan,
    MathAsin,
    MathAcos,
    MathAtan,
    MathAtan2,

    MathSinh,
    MathCosh,
    MathTanh,
    MathAsinh,
    MathAcosh,
    MathAtanh,

    // One version per number the language has, since a fraction is a number like any other
    // and an answer that arrives as a real cannot be counted with. The name is shared; which
    // version runs is settled by the argument, and the identifier says which was chosen.
    MathAbsInteger,
    MathAbsReal,
    MathAbsFraction,

    // Rounding lands on a whole number, which is the point of asking. Each is the honest
    // spelling of a conversion to integer, and naming three of them is what saves the
    // language from a "ToInteger" that has to pick one silently.
    MathFloorReal,
    MathFloorFraction,
    MathCeilingReal,
    MathCeilingFraction,
    MathRoundReal,
    MathRoundFraction,

    MathMinInteger,
    MathMinReal,
    MathMinFraction,
    MathMaxInteger,
    MathMaxReal,
    MathMaxFraction,

    /// <summary>
    /// <para>The same members again, asked of a float.</para>
    /// <para><b>Every one of them exists twice because neither type can answer for the other.</b>
    /// A real cannot hold an infinity and a float cannot hold twenty-eight digits, so a single
    /// version would force a conversion on somebody at every call — and the conversion back from
    /// a float is the one that can fail three ways.</para>
    /// <para>The real forms keep the plain names, since a real is what a decimal point means in
    /// this language and the float is the one asked for by name. Where a member already carried a
    /// type — <c>MathAbsReal</c> beside <c>MathAbsInteger</c> — the float joins the family the
    /// same way.</para>
    /// </summary>
    MathSqrtFloat,
    MathCbrtFloat,
    MathRootFloat,
    MathPowFloat,

    MathLogFloat,
    MathLogInBaseFloat,
    MathLog10Float,
    MathLog2Float,

    MathSinFloat,
    MathCosFloat,
    MathTanFloat,
    MathAsinFloat,
    MathAcosFloat,
    MathAtanFloat,
    MathAtan2Float,

    MathSinhFloat,
    MathCoshFloat,
    MathTanhFloat,
    MathAsinhFloat,
    MathAcoshFloat,
    MathAtanhFloat,

    MathAbsFloat,
    MathFloorFloat,
    MathCeilingFloat,
    MathRoundFloat,
    MathRoundFloatPlaces,
    MathMinFloat,
    MathMaxFloat,

    FractionCreate,
    FractionCreateWhole,

    RandomNew,
    RandomNewSeeded,
    RandomNext,
    RandomNextBelow,
    RandomNextBetween,
    RandomNextDouble,

    DateTimeNewDate,
    DateTimeNewMoment,
    DateTimeNow,
    DateTimeToday,
    DateTimeYear,
    DateTimeMonth,
    DateTimeDay,
    DateTimeHour,
    DateTimeMinute,
    DateTimeSecond,
    DateTimeDayOfWeek,
    DateTimeDayOfYear,
    DateTimeAddDays,
    DateTimeAddHours,
    DateTimeAddMinutes,
    DateTimeAddSeconds,
    DateTimeAddYears,
    DateTimeAddMonths,
    DateTimeCompareTo,
    DateTimeAdd,
    DateTimeSubtract,
    DateTimeSubtractSpan,

    TimeSpanNewTime,
    TimeSpanNewSpan,
    TimeSpanZero,
    TimeSpanFromDays,
    TimeSpanFromHours,
    TimeSpanFromMinutes,
    TimeSpanFromSeconds,
    TimeSpanDays,
    TimeSpanHours,
    TimeSpanMinutes,
    TimeSpanSeconds,
    TimeSpanTotalDays,
    TimeSpanTotalHours,
    TimeSpanTotalMinutes,
    TimeSpanTotalSeconds,
    TimeSpanNegate,
    TimeSpanDuration,
    TimeSpanAdd,
    TimeSpanSubtract,
    TimeSpanCompareTo,

    DateNew,
    DateToday,
    DateFromMoment,
    DateYear,
    DateMonth,
    DateDay,
    DateDayOfWeek,
    DateDayOfYear,
    DateAddDays,
    DateAddMonths,
    DateAddYears,
    DateAtTime,
    DateCompareTo,

    TimeNewToMinute,
    TimeNewToSecond,
    TimeNow,
    TimeFromMoment,
    TimeHour,
    TimeMinute,
    TimeSecond,
    TimeAddHours,
    TimeAddMinutes,
    TimeToTimeSpan,
    TimeCompareTo,

    // ---- Members of a value, found by the receiver's type ----------------------------------

    SetCount,
    SetInsert,
    SetInsertAt,
    SetRemove,
    SetRemoveAt,
    SetContains,
    SetIndexOf,
    SetClear,
    SetSubsetFrom,
    SetSubsetBetween,

    // Only on a set of optionals, since only there is there anything empty to drop.
    SetTrim,
    SetTrimStart,
    SetTrimEnd,
    SetTrimAll,

    StringCount,
    StringContains,
    StringIndexOf,
    StringSubstring,
    StringInsert,
    StringInsertAt,
    StringRemove,
    StringRemoveAt,
    StringToCharacters,
    StringSubsetFrom,
    StringSubsetBetween,

    // Three forms each: whitespace, the characters of a string, or the characters of a set.
    StringTrim,
    StringTrimText,
    StringTrimSet,
    StringTrimStart,
    StringTrimStartText,
    StringTrimStartSet,
    StringTrimEnd,
    StringTrimEndText,
    StringTrimEndSet,

    StringSplit,
    StringReplace,
    StringToUpper,
    StringToLower,
    StringCapitalize,

    /// <summary>Joining a set of strings, which reads on the set rather than on a string.</summary>
    SetJoin,

    /// <summary>Two sets put together, what they have in common, and what only this one has.</summary>
    SetUnion,
    SetIntersect,
    SetExcept,

    /// <summary>One of each, which a Profi-C set does not otherwise guarantee.</summary>
    SetDistinct,

    /// <summary>
    /// <para>Reading a value back from text, which is the way in that Format is the way out.
    /// </para>
    /// <para>Each yields an optional rather than raising: text that will not read is the
    /// ordinary case, not an exceptional one, since most of it arrives from a person typing.
    /// The plain form takes the shapes the language writes; the other takes exactly the
    /// pattern it is given, which is how a value written by a pattern is read back by it.</para>
    /// </summary>
    DateTimeParse,
    DateTimeParseExact,
    TimeSpanParse,
    TimeSpanParseExact,
    DateParse,
    DateParseExact,
    TimeParse,
    TimeParseExact,

    /// <summary>
    /// <para>Reading a number, a truth or a ratio out of text, each yielding an optional for
    /// the same reason the dates do.</para>
    /// <para>These read off the string rather than off the type, because <c>integer</c> is a
    /// reserved word and cannot stand in front of a dot. Asking the text is the reading that
    /// is available, and it is the one a reader has the value for anyway.</para>
    /// </summary>
    StringToInteger,
    StringToReal,
    StringToBoolean,
    StringToFraction,

    /// <summary>
    /// <para>Files, whole at a time.</para>
    /// <para>There is no way to read part of one, because holding a file open needs an object
    /// with state to close afterwards, and v1 has neither interfaces nor anything that closes
    /// itself. Whole-file is also what a program being taught with actually wants.</para>
    /// <para>A file that is not there is an absent optional, since asking whether one exists
    /// is an ordinary question. Everything else that can go wrong raises IOException, because
    /// absence cannot say which of them happened.</para>
    /// </summary>
    FileRead,
    FileReadLines,
    FileWrite,
    FileWriteLines,
    FileAppend,
    FileExists,
    FileDelete,
    FileCopy,
    FileMove,
    FileSize,
    FileChanged,

    DirectoryExists,
    DirectoryCreate,
    DirectoryDelete,
    DirectoryFiles,
    DirectoryFolders,
    DirectoryCurrent,

    /// <summary>The two halves of a moment, and the ways of putting one together.</summary>
    DateTimeDatePart,
    DateTimeTimePart,
    DateTimeFromDate,
    DateTimeFromDateAndTime,

    /// <summary>Writing a value out by a pattern. One id per type, since each formats itself.</summary>
    IntegerFormat,
    RealFormat,
    FloatFormat,
    FractionFormat,
    DateTimeFormat,
    TimeSpanFormat,
    DateFormat,
    TimeFormat,

    /// <summary>Rounding to a given number of decimal places, rather than to a whole number.</summary>
    MathRoundRealPlaces,

    OptionalHasValue,
    OptionalOr,
    OptionalValue,

    FractionToReal,
    FractionReciprocal,

    /// <summary>
    /// <para>A float as the fraction it exactly is.</para>
    /// <para>Explicit, unlike the same conversion from a real, and not because it loses anything
    /// — every finite float is a rational. It is explicit because the answer is startling:
    /// <c>(0.1f).ToFraction()</c> is 3602879701896397|36028797018963968, which is the number a
    /// float actually holds for a tenth. Asking for it is how a reader finds out.</para>
    /// </summary>
    FloatToFraction,

    /// <summary>
    /// <para>The crossing between the two kinds of decimal-point number, in both directions and
    /// explicit in both.</para>
    /// <para><b>Going out loses digits and nothing else.</b> A real holds twenty-eight of them
    /// and a float about sixteen, and every real fits inside a float's range — so the answer is
    /// always a number, just a shorter one.</para>
    /// <para><b>Coming back can fail three ways and quietly succeed in a fourth.</b> A float
    /// reaches far past what a real holds, so a large one has no real to become; an infinity and
    /// a value that is not a number have none either. And what does convert is <em>tidied</em>:
    /// the float holding a tenth becomes exactly <c>0.1</c>, which is not the number it was
    /// holding. That last one is the reason this is written out rather than done quietly — the
    /// mess disappearing is worth a reader's attention.</para>
    /// </summary>
    RealToFloat,
    FloatToReal,

    /// <summary>
    /// <para>Reaching a float from the two types that do not widen into one.</para>
    /// <para>Neither is implicit, and the reason is <c>Math</c>: every member of it takes a real
    /// and a float, so a whole number silently becoming either would leave <c>Math.Sqrt(2)</c>
    /// with two readings and no way to choose. Widening to a real is the one that happens, and
    /// this is how a program says it wanted the other.</para>
    /// </summary>
    IntegerToFloat,
    FractionToFloat,

    EnumerationToInteger,
    ExceptionMessage,

    // Inherited by every type from Model.
    ModelToString,
    ModelEquals,
}

/// <summary>A built-in model, and everything the language knows about it.</summary>
/// <param name="Name">The name a program writes.</param>
/// <param name="Namespace">The namespace the model belongs to.</param>
/// <param name="MayBeExtended">Whether a program may write <c>extends</c> against it.</param>
/// <param name="Members">Members reached through the model's name.</param>
/// <param name="Constructors">
/// The forms of <c>new</c> this model accepts, empty for one a program cannot construct.
/// Held apart from the members rather than being named for the type as a declared
/// constructor is, so that no spelling reaches one through the model's name.
/// </param>
/// <param name="HasNoInstances">
/// Whether nothing can ever be of this type, so that naming it as a variable's type is a
/// mistake rather than a declaration nothing can fill.
/// </param>
public sealed record BuiltInModelInfo(
    string Name,
    string Namespace,
    bool MayBeExtended,
    IReadOnlyList<BuiltInMember> Members,
    IReadOnlyList<BuiltInMember>? Constructors = null,
    bool HasNoInstances = false)
{
    public IReadOnlyList<BuiltInMember> Constructors { get; } = Constructors ?? [];

    /// <summary>Whether a program may write <c>new</c> against this model at all.</summary>
    public bool MayBeConstructed => Constructors.Count > 0;
}

/// <summary>
/// <para>The catalog of models the language provides.</para>
/// <para>One place to read to learn what exists, and one place to edit to add something. The
/// resolver takes the names it protects from here, and the type checker takes the signatures,
/// so neither can disagree with this or with the other.</para>
/// </summary>
public static class BuiltIns
{
    private static BuiltInMember Member(
        BuiltInId id, string name, TypeSymbol? returns, params TypeSymbol?[] parameters) =>
        new(name, returns, parameters, id);

    /// <summary>A member that is a value rather than something to call, such as Math.Pi.</summary>
    private static BuiltInMember Value(BuiltInId id, string name, TypeSymbol type) =>
        new(name, type, [], id, IsValue: true);

    /// <summary>
    /// <para>Models a program may name but never declare.</para>
    /// <para><c>Model</c> and <c>Exception</c> carry no members of their own here: what they
    /// contribute is inherited by every type and by every exception respectively, and is
    /// answered on the value rather than through the model's name.</para>
    /// </summary>
    public static readonly IReadOnlyList<BuiltInModelInfo> Models =
    [
        new("Model", "Standard", MayBeExtended: true, []),

        // Every function type descends from this one, so a function may be held without its
        // signature being named. It may not be extended: a program adding a child to it would
        // be declaring something that is a function without being any particular function.
        new("Function", "Standard", MayBeExtended: false, []),

        new("Exception", "Standard", MayBeExtended: true, []),

        new("Console", "Standard", MayBeExtended: false, HasNoInstances: true, Members:
        [
            // Both take a value of any type — a null parameter type means "anything" — so no
            // overload per primitive is needed in a version with no generics.
            Member(BuiltInId.ConsoleWrite, "Write", null, [null]),
            Member(BuiltInId.ConsoleWriteLine, "WriteLine", null, [null]),
            Member(BuiltInId.ConsoleRead, "Read", new OptionalType(PrimitiveType.String)),
        ]),

        new("Reference", "Standard", MayBeExtended: false, HasNoInstances: true, Members:
        [
            Member(BuiltInId.ReferenceEquals, "Equals", PrimitiveType.Boolean, [null, null]),
        ]),

        // Reading and writing whole files. A name to reach members through rather than
        // something to make one of, since a file is not a thing a program holds — it is
        // somewhere a program puts text and takes it back.
        //
        // Reading yields an optional: a file that is not there is an ordinary answer, and the
        // alternative is asking Exists first, which is the pattern that races. Everything else
        // that can go wrong raises IOException, since absence cannot say which.
        //
        // Text is UTF-8 with no mark at the front. Writing ends every line with "\n"; reading
        // accepts either that or "\r\n" and gives back neither, so a file written on one
        // machine reads the same on another.
        new("File", "Standard", MayBeExtended: false, HasNoInstances: true, Members:
        [
            Member(BuiltInId.FileRead, "Read", new OptionalType(PrimitiveType.String),
                   PrimitiveType.String),
            Member(BuiltInId.FileReadLines, "ReadLines",
                   new OptionalType(new SetType(PrimitiveType.String)), PrimitiveType.String),

            // Writing replaces what was there; appending adds to the end. Both make the file
            // when there is none, and neither makes the folder it sits in — a path with a
            // typo in it should fail rather than quietly build somewhere new.
            Member(BuiltInId.FileWrite, "Write", null, PrimitiveType.String, PrimitiveType.String),
            Member(BuiltInId.FileWriteLines, "WriteLines", null,
                   PrimitiveType.String, new SetType(PrimitiveType.String)),
            Member(BuiltInId.FileAppend, "Append", null,
                   PrimitiveType.String, PrimitiveType.String),

            Member(BuiltInId.FileExists, "Exists", PrimitiveType.Boolean, PrimitiveType.String),

            // Yields whether there was one to delete, as removing from a set does.
            Member(BuiltInId.FileDelete, "Delete", PrimitiveType.Boolean, PrimitiveType.String),

            Member(BuiltInId.FileCopy, "Copy", null, PrimitiveType.String, PrimitiveType.String),
            Member(BuiltInId.FileMove, "Move", null, PrimitiveType.String, PrimitiveType.String),

            // Absent for the same reason Read is: there is no size and no date for a file
            // that is not there.
            Member(BuiltInId.FileSize, "Size", new OptionalType(PrimitiveType.Integer),
                   PrimitiveType.String),
            Member(BuiltInId.FileChanged, "Changed", new OptionalType(BuiltInTypes.Of("DateTime")),
                   PrimitiveType.String),
        ]),

        // The folders files sit in. 'Folders' rather than 'Directories' only because
        // Directory.Directories reads as a stutter; the two words mean the same thing.
        new("Directory", "Standard", MayBeExtended: false, HasNoInstances: true, Members:
        [
            Value(BuiltInId.DirectoryCurrent, "Current", PrimitiveType.String),

            Member(BuiltInId.DirectoryExists, "Exists", PrimitiveType.Boolean, PrimitiveType.String),

            // Makes every folder on the way, since making one inside another that is not
            // there yet is the ordinary reason to ask.
            Member(BuiltInId.DirectoryCreate, "Create", null, PrimitiveType.String),
            Member(BuiltInId.DirectoryDelete, "Delete", PrimitiveType.Boolean, PrimitiveType.String),

            Member(BuiltInId.DirectoryFiles, "Files",
                   new OptionalType(new SetType(PrimitiveType.String)), PrimitiveType.String),
            Member(BuiltInId.DirectoryFolders, "Folders",
                   new OptionalType(new SetType(PrimitiveType.String)), PrimitiveType.String),
        ]),

        // ---- What each primitive knows about itself ----------------------------------------
        //
        // A name in capitals beside the keyword, which is how 'Fraction' already reads next to
        // 'fraction'. The keyword names the type and the capital names the place its facts are
        // kept, because a reserved word cannot stand in front of a dot: 'integer.MaxValue' is
        // not something the grammar can read, and 'Integer.MaxValue' is.
        //
        // Bounds belong to the numbers. Where a number runs out is a fact about the number,
        // and meeting it is how a beginner learns that a type has an edge at all. A character
        // has no such fact to tell — where the alphabet stops is a fact about how text is
        // stored rather than about the language, and not every value in that range names a
        // character anyway — so 'character' has no capitalized name beside it.
        //
        // None of these can be constructed or extended. They hold values and nothing else.
        new("Integer", "Standard", MayBeExtended: false, HasNoInstances: true, Members:
        [
            Value(BuiltInId.IntegerMaxValue, "MaxValue", PrimitiveType.Integer),
            Value(BuiltInId.IntegerMinValue, "MinValue", PrimitiveType.Integer),
        ]),

        new("Real", "Standard", MayBeExtended: false, HasNoInstances: true, Members:
        [
            Value(BuiltInId.RealMaxValue, "MaxValue", PrimitiveType.Real),
            Value(BuiltInId.RealMinValue, "MinValue", PrimitiveType.Real),
        ]),

        // A float knows three things a real has no answer for, and that is the difference
        // between the two types written down. Each is a value its own arithmetic produces, so
        // '1.0f / 0.0f == Float.Infinity' is true — the constant names what already happens
        // rather than standing apart from it. That expression is also why a float is the one
        // type PC0324 leaves alone: there is an answer here, so there is nothing to refuse.
        new("Float", "Standard", MayBeExtended: false, HasNoInstances: true, Members:
        [
            Value(BuiltInId.FloatMaxValue, "MaxValue", PrimitiveType.Float),
            Value(BuiltInId.FloatMinValue, "MinValue", PrimitiveType.Float),
            Value(BuiltInId.FloatInfinity, "Infinity", PrimitiveType.Float),
            Value(BuiltInId.FloatNegativeInfinity, "NegativeInfinity", PrimitiveType.Float),

            // Spelled out rather than abbreviated, as this language spells out 'shiftleft' and
            // 'bitwise and'. A reader meeting it for the first time should be able to read it.
            Value(BuiltInId.FloatNotANumber, "NotANumber", PrimitiveType.Float),
        ]),

        // Not a bound but a name for the string with nothing in it, which reads better than an
        // empty pair of quotes wherever the emptiness is the point.
        new("String", "Standard", MayBeExtended: false, HasNoInstances: true, Members:
        [
            Value(BuiltInId.StringEmpty, "Empty", PrimitiveType.String),
        ]),

        // Every version taking a number is written for each number the language has. A
        // fraction is a number like any other, and an answer that arrives as a real cannot be
        // counted with, so one version per type is what keeps either from being a dead end.
        //
        // The exact-match rule picks among them: an integer widens to both real and fraction,
        // so without it the order these are written in would decide what "Abs(-3)" means.
        new("Math", "Standard", MayBeExtended: false, HasNoInstances: true, Members:
        [
            // Written without parentheses, since neither is something to do.
            Value(BuiltInId.MathPi, "Pi", PrimitiveType.Real),
            Value(BuiltInId.MathE, "E", PrimitiveType.Real),

            // Roots and powers are the ones that genuinely leave the rationals: the square
            // root of a fraction is usually irrational, so all of these answer in reals
            // whatever they were given. The "^" operator is the exact form, and keeps a
            // fraction base exact where the exponent is whole.
            Member(BuiltInId.MathSqrt, "Sqrt", PrimitiveType.Real, PrimitiveType.Real),
            Member(BuiltInId.MathCbrt, "Cbrt", PrimitiveType.Real, PrimitiveType.Real),
            Member(BuiltInId.MathRoot, "Root", PrimitiveType.Real,
                   PrimitiveType.Real, PrimitiveType.Real),
            Member(BuiltInId.MathPow, "Pow", PrimitiveType.Real,
                   PrimitiveType.Real, PrimitiveType.Real),

            // Counting arrangements, so it counts: whole in, whole out. Past 20 the answer
            // outgrows an integer and it throws, as any other overflow does.
            Member(BuiltInId.MathFactorial, "Factorial", PrimitiveType.Integer,
                   PrimitiveType.Integer),

            // Log of one number is the NATURAL logarithm, which is what .NET, Java and C mean
            // by the name. Log10 and Log2 are the other two .NET spells out.
            Member(BuiltInId.MathLog, "Log", PrimitiveType.Real, PrimitiveType.Real),
            Member(BuiltInId.MathLogInBase, "Log", PrimitiveType.Real,
                   PrimitiveType.Real, PrimitiveType.Real),
            Member(BuiltInId.MathLog10, "Log10", PrimitiveType.Real, PrimitiveType.Real),
            Member(BuiltInId.MathLog2, "Log2", PrimitiveType.Real, PrimitiveType.Real),

            // Angles are in radians, as everywhere else that has these. Abbreviated because
            // they are borrowed rather than invented here, and every one of these names is
            // what the mathematics is written as on paper.
            Member(BuiltInId.MathSin, "Sin", PrimitiveType.Real, PrimitiveType.Real),
            Member(BuiltInId.MathCos, "Cos", PrimitiveType.Real, PrimitiveType.Real),
            Member(BuiltInId.MathTan, "Tan", PrimitiveType.Real, PrimitiveType.Real),
            Member(BuiltInId.MathAsin, "Asin", PrimitiveType.Real, PrimitiveType.Real),
            Member(BuiltInId.MathAcos, "Acos", PrimitiveType.Real, PrimitiveType.Real),
            Member(BuiltInId.MathAtan, "Atan", PrimitiveType.Real, PrimitiveType.Real),
            Member(BuiltInId.MathAtan2, "Atan2", PrimitiveType.Real,
                   PrimitiveType.Real, PrimitiveType.Real),

            // The hyperbolic six. Named for the circular ones they sit beside, and shaped the
            // same way, but measured against a hyperbola rather than a circle. A hanging chain
            // takes the shape of Cosh, which is the one place most people meet them.
            Member(BuiltInId.MathSinh, "Sinh", PrimitiveType.Real, PrimitiveType.Real),
            Member(BuiltInId.MathCosh, "Cosh", PrimitiveType.Real, PrimitiveType.Real),
            Member(BuiltInId.MathTanh, "Tanh", PrimitiveType.Real, PrimitiveType.Real),
            Member(BuiltInId.MathAsinh, "Asinh", PrimitiveType.Real, PrimitiveType.Real),
            Member(BuiltInId.MathAcosh, "Acosh", PrimitiveType.Real, PrimitiveType.Real),
            Member(BuiltInId.MathAtanh, "Atanh", PrimitiveType.Real, PrimitiveType.Real),

            // Measuring keeps the type it measured, so a distance between integers is one.
            Member(BuiltInId.MathAbsInteger, "Abs", PrimitiveType.Integer, PrimitiveType.Integer),
            Member(BuiltInId.MathAbsReal, "Abs", PrimitiveType.Real, PrimitiveType.Real),
            Member(BuiltInId.MathAbsFraction, "Abs", PrimitiveType.Fraction, PrimitiveType.Fraction),

            // Rounding lands on a whole number. Returning a real would leave the answer
            // unusable as a count, an index, or a bound — which is most of what it is for.
            Member(BuiltInId.MathFloorReal, "Floor", PrimitiveType.Integer, PrimitiveType.Real),
            Member(BuiltInId.MathFloorFraction, "Floor", PrimitiveType.Integer, PrimitiveType.Fraction),
            Member(BuiltInId.MathCeilingReal, "Ceiling", PrimitiveType.Integer, PrimitiveType.Real),
            Member(BuiltInId.MathCeilingFraction, "Ceiling", PrimitiveType.Integer, PrimitiveType.Fraction),
            Member(BuiltInId.MathRoundReal, "Round", PrimitiveType.Integer, PrimitiveType.Real),

            // Rounding to a whole number gives an integer, since that is what the answer is.
            // Rounding to a place gives a real, since 2.50 still has a fraction to hold.
            Member(BuiltInId.MathRoundRealPlaces, "Round", PrimitiveType.Real,
                   PrimitiveType.Real, PrimitiveType.Integer),
            Member(BuiltInId.MathRoundFraction, "Round", PrimitiveType.Integer, PrimitiveType.Fraction),

            // Choosing between two values gives back one of them, so the answer is whatever
            // they both were.
            Member(BuiltInId.MathMinInteger, "Min", PrimitiveType.Integer,
                   PrimitiveType.Integer, PrimitiveType.Integer),
            Member(BuiltInId.MathMinReal, "Min", PrimitiveType.Real,
                   PrimitiveType.Real, PrimitiveType.Real),
            Member(BuiltInId.MathMinFraction, "Min", PrimitiveType.Fraction,
                   PrimitiveType.Fraction, PrimitiveType.Fraction),
            Member(BuiltInId.MathMaxInteger, "Max", PrimitiveType.Integer,
                   PrimitiveType.Integer, PrimitiveType.Integer),
            Member(BuiltInId.MathMaxReal, "Max", PrimitiveType.Real,
                   PrimitiveType.Real, PrimitiveType.Real),
            Member(BuiltInId.MathMaxFraction, "Max", PrimitiveType.Fraction,
                   PrimitiveType.Fraction, PrimitiveType.Fraction),

            // ---- The same, asked of a float -----------------------------------------------
            //
            // A float answers every one of these itself rather than being converted first, since
            // the conversion back from one can fail three ways and the conversion out loses
            // digits. Which version runs is settled by the argument, exactly as it is between an
            // integer and a real.
            Member(BuiltInId.MathSqrtFloat, "Sqrt", PrimitiveType.Float, PrimitiveType.Float),
            Member(BuiltInId.MathCbrtFloat, "Cbrt", PrimitiveType.Float, PrimitiveType.Float),
            Member(BuiltInId.MathRootFloat, "Root", PrimitiveType.Float,
                   PrimitiveType.Float, PrimitiveType.Float),
            Member(BuiltInId.MathPowFloat, "Pow", PrimitiveType.Float,
                   PrimitiveType.Float, PrimitiveType.Float),

            Member(BuiltInId.MathLogFloat, "Log", PrimitiveType.Float, PrimitiveType.Float),
            Member(BuiltInId.MathLogInBaseFloat, "Log", PrimitiveType.Float,
                   PrimitiveType.Float, PrimitiveType.Float),
            Member(BuiltInId.MathLog10Float, "Log10", PrimitiveType.Float, PrimitiveType.Float),
            Member(BuiltInId.MathLog2Float, "Log2", PrimitiveType.Float, PrimitiveType.Float),

            Member(BuiltInId.MathSinFloat, "Sin", PrimitiveType.Float, PrimitiveType.Float),
            Member(BuiltInId.MathCosFloat, "Cos", PrimitiveType.Float, PrimitiveType.Float),
            Member(BuiltInId.MathTanFloat, "Tan", PrimitiveType.Float, PrimitiveType.Float),
            Member(BuiltInId.MathAsinFloat, "Asin", PrimitiveType.Float, PrimitiveType.Float),
            Member(BuiltInId.MathAcosFloat, "Acos", PrimitiveType.Float, PrimitiveType.Float),
            Member(BuiltInId.MathAtanFloat, "Atan", PrimitiveType.Float, PrimitiveType.Float),
            Member(BuiltInId.MathAtan2Float, "Atan2", PrimitiveType.Float,
                   PrimitiveType.Float, PrimitiveType.Float),

            Member(BuiltInId.MathSinhFloat, "Sinh", PrimitiveType.Float, PrimitiveType.Float),
            Member(BuiltInId.MathCoshFloat, "Cosh", PrimitiveType.Float, PrimitiveType.Float),
            Member(BuiltInId.MathTanhFloat, "Tanh", PrimitiveType.Float, PrimitiveType.Float),
            Member(BuiltInId.MathAsinhFloat, "Asinh", PrimitiveType.Float, PrimitiveType.Float),
            Member(BuiltInId.MathAcoshFloat, "Acosh", PrimitiveType.Float, PrimitiveType.Float),
            Member(BuiltInId.MathAtanhFloat, "Atanh", PrimitiveType.Float, PrimitiveType.Float),

            Member(BuiltInId.MathAbsFloat, "Abs", PrimitiveType.Float, PrimitiveType.Float),

            // These land on a whole number whichever type they were given, so each yields an
            // integer — the same choice the real forms make.
            Member(BuiltInId.MathFloorFloat, "Floor", PrimitiveType.Integer, PrimitiveType.Float),
            Member(BuiltInId.MathCeilingFloat, "Ceiling", PrimitiveType.Integer,
                   PrimitiveType.Float),
            Member(BuiltInId.MathRoundFloat, "Round", PrimitiveType.Integer, PrimitiveType.Float),
            Member(BuiltInId.MathRoundFloatPlaces, "Round", PrimitiveType.Float,
                   PrimitiveType.Float, PrimitiveType.Integer),

            Member(BuiltInId.MathMinFloat, "Min", PrimitiveType.Float,
                   PrimitiveType.Float, PrimitiveType.Float),
            Member(BuiltInId.MathMaxFloat, "Max", PrimitiveType.Float,
                   PrimitiveType.Float, PrimitiveType.Float),
        ]),

        // "fraction" is the type and a reserved word; "Fraction" is the model beside it,
        // holding what a fraction needs that is not a member of one.
        new("Fraction", "Standard", MayBeExtended: false, HasNoInstances: true, Members:
        [
            Member(BuiltInId.FractionCreate, "Create", PrimitiveType.Fraction,
                   PrimitiveType.Integer, PrimitiveType.Integer),

            // A whole number over one. An integer already widens to a fraction wherever one is
            // wanted, so this earns its place only where nothing says a fraction is wanted:
            // "let f = 3;" holds an integer, and "let f = Fraction.Create(3);" holds 3|1.
            Member(BuiltInId.FractionCreateWhole, "Create", PrimitiveType.Fraction,
                   PrimitiveType.Integer),
        ]),

        // Chance, in two shapes: a generator a program holds, which disturbs nothing of
        // anyone else's, and the same questions through the name, drawing from one the
        // language keeps. Most programs want the second and should not have to build
        // anything to get it.
        //
        // The names and the meanings are .NET's, unchanged. Next excludes its upper bound, so
        // a die is Next(1, 7) — which surprises everyone exactly once, and would surprise them
        // a second time if this were the one language that read it differently.
        //
        // There is no way to seed the shared one, also as in .NET: a program that needs the
        // same sequence twice holds its own, which is the thing that makes it reproducible.
        new("Random", "Standard", MayBeExtended: false,
        [
            Member(BuiltInId.RandomNext, "Next", PrimitiveType.Integer),
            Member(BuiltInId.RandomNextBelow, "Next", PrimitiveType.Integer, PrimitiveType.Integer),
            Member(BuiltInId.RandomNextBetween, "Next", PrimitiveType.Integer,
                   PrimitiveType.Integer, PrimitiveType.Integer),
            Member(BuiltInId.RandomNextDouble, "NextDouble", PrimitiveType.Real),
        ],
        [
            Member(BuiltInId.RandomNew, "Random", RandomType),
            Member(BuiltInId.RandomNewSeeded, "Random", RandomType, PrimitiveType.Integer),
        ]),

        // A moment in time. Held as a value would be — two with the same moment are equal —
        // and never changed: adding to one yields another, as adding to a string does.
        //
        // What .NET reads as a property is read as one here, without parentheses. That is
        // what a value member is for, and it keeps "moment.Year" spelled the way it already
        // is everywhere else.
        new("DateTime", "Standard", MayBeExtended: false,
        [
            Value(BuiltInId.DateTimeNow, "Now", DateTimeType),
            Value(BuiltInId.DateTimeToday, "Today", DateTimeType),

            Value(BuiltInId.DateTimeYear, "Year", PrimitiveType.Integer),
            Value(BuiltInId.DateTimeMonth, "Month", PrimitiveType.Integer),
            Value(BuiltInId.DateTimeDay, "Day", PrimitiveType.Integer),
            Value(BuiltInId.DateTimeHour, "Hour", PrimitiveType.Integer),
            Value(BuiltInId.DateTimeMinute, "Minute", PrimitiveType.Integer),
            Value(BuiltInId.DateTimeSecond, "Second", PrimitiveType.Integer),
            Value(BuiltInId.DateTimeDayOfWeek, "DayOfWeek", PrimitiveType.Integer),
            Value(BuiltInId.DateTimeDayOfYear, "DayOfYear", PrimitiveType.Integer),

            // The two halves a moment is made of. Values rather than functions, because
            // neither is worked out: a moment already holds both, and asking for one is
            // reading a part rather than performing a conversion.
            Value(BuiltInId.DateTimeDatePart, "Date", DateType),
            Value(BuiltInId.DateTimeTimePart, "Time", TimeType),

            // Each takes a real, as .NET's do, so half a day is sayable and a whole one still
            // reads as AddDays(10) — an integer widens on the way in.
            Member(BuiltInId.DateTimeAddDays, "AddDays", DateTimeType, PrimitiveType.Real),
            Member(BuiltInId.DateTimeAddHours, "AddHours", DateTimeType, PrimitiveType.Real),
            Member(BuiltInId.DateTimeAddMinutes, "AddMinutes", DateTimeType, PrimitiveType.Real),
            Member(BuiltInId.DateTimeAddSeconds, "AddSeconds", DateTimeType, PrimitiveType.Real),
            Member(BuiltInId.DateTimeAddYears, "AddYears", DateTimeType, PrimitiveType.Integer),
            Member(BuiltInId.DateTimeAddMonths, "AddMonths", DateTimeType, PrimitiveType.Integer),

            // Ordering without operators, which is how .NET spells it too. Negative when this
            // moment comes first, zero when they are the same, positive when it comes after.
            Member(BuiltInId.DateTimeCompareTo, "CompareTo", PrimitiveType.Integer, DateTimeType),

            // How far apart two moments are, and moving one by that much. Subtract is
            // overloaded on what it is given, as .NET's is: a moment leaves a span behind,
            // and a span leaves an earlier moment.
            Member(BuiltInId.DateTimeSubtract, "Subtract", TimeSpanType, DateTimeType),
            Member(BuiltInId.DateTimeSubtractSpan, "Subtract", DateTimeType, TimeSpanType),
            Member(BuiltInId.DateTimeAdd, "Add", DateTimeType, TimeSpanType),
            Member(BuiltInId.DateTimeFormat, "Format", PrimitiveType.String, PrimitiveType.String),
            Member(BuiltInId.DateTimeParse, "Parse", new OptionalType(DateTimeType),
                   PrimitiveType.String),
            Member(BuiltInId.DateTimeParseExact, "Parse", new OptionalType(DateTimeType),
                   PrimitiveType.String, PrimitiveType.String),
        ],
        [
            Member(BuiltInId.DateTimeNewDate, "DateTime", DateTimeType,
                   PrimitiveType.Integer, PrimitiveType.Integer, PrimitiveType.Integer),
            Member(BuiltInId.DateTimeNewMoment, "DateTime", DateTimeType,
                   PrimitiveType.Integer, PrimitiveType.Integer, PrimitiveType.Integer,
                   PrimitiveType.Integer, PrimitiveType.Integer, PrimitiveType.Integer),

            // Built from the halves rather than from six numbers. The one-argument form takes
            // midnight, which is the only time of day a bare date can mean.
            Member(BuiltInId.DateTimeFromDate, "DateTime", DateTimeType, DateType),
            Member(BuiltInId.DateTimeFromDateAndTime, "DateTime", DateTimeType,
                   DateType, TimeType),
        ]),

        // How long something lasts, as against when it happened. This is what a moment
        // subtracted from a moment leaves behind, and what adding to a moment takes.
        //
        // Components against totals is the distinction worth reading twice: an hour and a half
        // has Hours of 1 and Minutes of 30, but TotalMinutes of 90. The first pair is how you
        // would say it aloud, the second is how you would measure it.
        new("TimeSpan", "Standard", MayBeExtended: false,
        [
            Value(BuiltInId.TimeSpanZero, "Zero", TimeSpanType),

            Member(BuiltInId.TimeSpanFromDays, "FromDays", TimeSpanType, PrimitiveType.Real),
            Member(BuiltInId.TimeSpanFromHours, "FromHours", TimeSpanType, PrimitiveType.Real),
            Member(BuiltInId.TimeSpanFromMinutes, "FromMinutes", TimeSpanType, PrimitiveType.Real),
            Member(BuiltInId.TimeSpanFromSeconds, "FromSeconds", TimeSpanType, PrimitiveType.Real),

            // The parts, as you would say them.
            Value(BuiltInId.TimeSpanDays, "Days", PrimitiveType.Integer),
            Value(BuiltInId.TimeSpanHours, "Hours", PrimitiveType.Integer),
            Value(BuiltInId.TimeSpanMinutes, "Minutes", PrimitiveType.Integer),
            Value(BuiltInId.TimeSpanSeconds, "Seconds", PrimitiveType.Integer),

            // The whole of it, measured in one unit. A real, since most spans are not a whole
            // number of anything.
            Value(BuiltInId.TimeSpanTotalDays, "TotalDays", PrimitiveType.Real),
            Value(BuiltInId.TimeSpanTotalHours, "TotalHours", PrimitiveType.Real),
            Value(BuiltInId.TimeSpanTotalMinutes, "TotalMinutes", PrimitiveType.Real),
            Value(BuiltInId.TimeSpanTotalSeconds, "TotalSeconds", PrimitiveType.Real),

            Member(BuiltInId.TimeSpanAdd, "Add", TimeSpanType, TimeSpanType),
            Member(BuiltInId.TimeSpanSubtract, "Subtract", TimeSpanType, TimeSpanType),
            Member(BuiltInId.TimeSpanNegate, "Negate", TimeSpanType),
            Member(BuiltInId.TimeSpanDuration, "Duration", TimeSpanType),
            Member(BuiltInId.TimeSpanCompareTo, "CompareTo", PrimitiveType.Integer, TimeSpanType),
            Member(BuiltInId.TimeSpanFormat, "Format", PrimitiveType.String, PrimitiveType.String),
            Member(BuiltInId.TimeSpanParse, "Parse", new OptionalType(TimeSpanType),
                   PrimitiveType.String),
            Member(BuiltInId.TimeSpanParseExact, "Parse", new OptionalType(TimeSpanType),
                   PrimitiveType.String, PrimitiveType.String),
        ],
        [
            Member(BuiltInId.TimeSpanNewTime, "TimeSpan", TimeSpanType,
                   PrimitiveType.Integer, PrimitiveType.Integer, PrimitiveType.Integer),
            Member(BuiltInId.TimeSpanNewSpan, "TimeSpan", TimeSpanType,
                   PrimitiveType.Integer, PrimitiveType.Integer, PrimitiveType.Integer,
                   PrimitiveType.Integer),
        ]),

        // A day with no time of day. A birthday is one of these: it is the same day wherever
        // you are and whatever hour it is, and holding it as a moment forces a midnight
        // nobody meant onto it.
        //
        // .NET spells this DateOnly, having already given the plain name away to DateTime
        // twenty years earlier. Nothing here is committed to that history, so it takes the
        // name that says what it is.
        new("Date", "Standard", MayBeExtended: false,
        [
            Value(BuiltInId.DateToday, "Today", DateType),
            Member(BuiltInId.DateFromMoment, "FromDateTime", DateType, DateTimeType),

            Value(BuiltInId.DateYear, "Year", PrimitiveType.Integer),
            Value(BuiltInId.DateMonth, "Month", PrimitiveType.Integer),
            Value(BuiltInId.DateDay, "Day", PrimitiveType.Integer),
            Value(BuiltInId.DateDayOfWeek, "DayOfWeek", PrimitiveType.Integer),
            Value(BuiltInId.DateDayOfYear, "DayOfYear", PrimitiveType.Integer),

            Member(BuiltInId.DateAddDays, "AddDays", DateType, PrimitiveType.Integer),
            Member(BuiltInId.DateAddMonths, "AddMonths", DateType, PrimitiveType.Integer),
            Member(BuiltInId.DateAddYears, "AddYears", DateType, PrimitiveType.Integer),

            // A day and a time of day together make a moment, which is the way back.
            Member(BuiltInId.DateAtTime, "ToDateTime", DateTimeType, TimeType),
            Member(BuiltInId.DateCompareTo, "CompareTo", PrimitiveType.Integer, DateType),
            Member(BuiltInId.DateFormat, "Format", PrimitiveType.String, PrimitiveType.String),
            Member(BuiltInId.DateParse, "Parse", new OptionalType(DateType), PrimitiveType.String),
            Member(BuiltInId.DateParseExact, "Parse", new OptionalType(DateType),
                   PrimitiveType.String, PrimitiveType.String),
        ],
        [
            Member(BuiltInId.DateNew, "Date", DateType,
                   PrimitiveType.Integer, PrimitiveType.Integer, PrimitiveType.Integer),
        ]),

        // A time of day with no day. Opening hours are these: nine in the morning is nine
        // every day, and pinning it to one would say something nobody meant.
        //
        // Not a span, though both are written with colons: a span is how long something
        // lasted and may be longer than a day or run backwards, while this is a reading on a
        // clock and always sits between midnight and the next.
        new("Time", "Standard", MayBeExtended: false,
        [
            Value(BuiltInId.TimeNow, "Now", TimeType),
            Member(BuiltInId.TimeFromMoment, "FromDateTime", TimeType, DateTimeType),

            Value(BuiltInId.TimeHour, "Hour", PrimitiveType.Integer),
            Value(BuiltInId.TimeMinute, "Minute", PrimitiveType.Integer),
            Value(BuiltInId.TimeSecond, "Second", PrimitiveType.Integer),

            // Adding wraps around midnight rather than overflowing, since a clock does.
            Member(BuiltInId.TimeAddHours, "AddHours", TimeType, PrimitiveType.Real),
            Member(BuiltInId.TimeAddMinutes, "AddMinutes", TimeType, PrimitiveType.Real),

            // How far into the day it is, which is a span.
            Member(BuiltInId.TimeToTimeSpan, "ToTimeSpan", TimeSpanType),
            Member(BuiltInId.TimeCompareTo, "CompareTo", PrimitiveType.Integer, TimeType),
            Member(BuiltInId.TimeFormat, "Format", PrimitiveType.String, PrimitiveType.String),
            Member(BuiltInId.TimeParse, "Parse", new OptionalType(TimeType), PrimitiveType.String),
            Member(BuiltInId.TimeParseExact, "Parse", new OptionalType(TimeType),
                   PrimitiveType.String, PrimitiveType.String),
        ],
        [
            Member(BuiltInId.TimeNewToMinute, "Time", TimeType,
                   PrimitiveType.Integer, PrimitiveType.Integer),
            Member(BuiltInId.TimeNewToSecond, "Time", TimeType,
                   PrimitiveType.Integer, PrimitiveType.Integer, PrimitiveType.Integer),
        ]),
    ];

    /// <summary>
    /// The DateTime model as a type, for the members that yield one. Taken from the shared
    /// registry rather than made here, since a member's signature has to name the very same
    /// symbol the resolver hands a program.
    /// </summary>
    private static ModelSymbol DateTimeType => BuiltInTypes.Of("DateTime");

    private static ModelSymbol TimeSpanType => BuiltInTypes.Of("TimeSpan");

    private static ModelSymbol RandomType => BuiltInTypes.Of("Random");

    private static ModelSymbol DateType => BuiltInTypes.Of("Date");

    private static ModelSymbol TimeType => BuiltInTypes.Of("Time");

    /// <summary>Every built-in model name. No program may declare one of these.</summary>
    public static readonly IReadOnlySet<string> ModelNames =
        Models.Select(m => m.Name).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// <para>The built-in exceptions, which descend from <c>Exception</c> and may be extended.
    /// Kept beside the models because they share the rule that a program cannot declare
    /// them.</para>
    /// <para>Read from the runtime's catalog rather than listed again, so that a name the
    /// language can raise is a name a program can catch. <c>Exception</c> itself is the root
    /// and is cataloged above as a model.</para>
    /// </summary>
    public static readonly IReadOnlySet<string> ExceptionNames =
        Runtime.BuiltInExceptions.Names
            .Where(name => !string.Equals(name, "Exception", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>Every name the language owns, whether a model or an exception.</summary>
    public static readonly IReadOnlySet<string> AllTypeNames =
        ModelNames.Concat(ExceptionNames).ToHashSet(StringComparer.Ordinal);

    /// <summary>Whether a name belongs to the language rather than to a program.</summary>
    public static bool IsBuiltInType(string name) => AllTypeNames.Contains(name);

    /// <summary>
    /// <para>Whether <c>extends</c> may name this type.</para>
    /// <para>An uncatchable exception may not be extended. A program's own type descending from
    /// one could be caught while its parent could not, which reads as though catching the
    /// parent were merely something nobody had tried.</para>
    /// </summary>
    public static bool MayBeExtended(string name) =>
        (ExceptionNames.Contains(name) && Runtime.BuiltInExceptions.MayBeCaught(name))
        || Models.FirstOrDefault(m => m.Name == name)?.MayBeExtended == true;

    /// <summary>
    /// <para>Whether nothing can ever be of this type.</para>
    /// <para>Not read off having no constructors: Model and Function have none and hold values
    /// all the same, since every model and every function converts to one. These four are
    /// names to reach members through, and a variable of one could never be filled.</para>
    /// </summary>
    public static bool HasNoInstances(string name) => FindModel(name)?.HasNoInstances == true;

    /// <summary>The model of that name, or null if the language does not own it.</summary>
    public static BuiltInModelInfo? FindModel(string name) =>
        Models.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.Ordinal));

    /// <summary>A member reached through a model's name, or null if there is none.</summary>
    public static BuiltInMember? Find(string modelName, string memberName) =>
        FindModel(modelName)?.Members
            .FirstOrDefault(m => string.Equals(m.Name, memberName, StringComparison.Ordinal));

    // ---- Members of a value --------------------------------------------------------------
    // Found by the receiver's type rather than by a name. Several depend on the receiver: a
    // set's Insert takes its element type, an optional's Value yields its underlying one, so
    // each list is built for the receiver it is asked about.

    /// <summary>Members every type inherits from <c>Model</c>.</summary>
    public static IReadOnlyList<BuiltInMember> OnEveryType() =>
    [
        Member(BuiltInId.ModelToString, "ToString", PrimitiveType.String),

        // Structural, the same question '==' asks. Takes a value of any type.
        Member(BuiltInId.ModelEquals, "Equals", PrimitiveType.Boolean, [null]),
    ];

    public static IReadOnlyList<BuiltInMember> OnSet(SetType set) =>
    [
        Value(BuiltInId.SetCount, "Count", PrimitiveType.Integer),
        Member(BuiltInId.SetInsert, "Insert", null, set.ElementType),
        Member(BuiltInId.SetInsertAt, "InsertAt", null, PrimitiveType.Integer, set.ElementType),

        // The only mutator that yields anything, matching the list it is built on.
        Member(BuiltInId.SetRemove, "Remove", PrimitiveType.Boolean, set.ElementType),

        Member(BuiltInId.SetRemoveAt, "RemoveAt", null, PrimitiveType.Integer),
        Member(BuiltInId.SetContains, "Contains", PrimitiveType.Boolean, set.ElementType),
        Member(BuiltInId.SetIndexOf, "IndexOf", PrimitiveType.Integer, set.ElementType),
        Member(BuiltInId.SetClear, "Clear", null),

        // A run of the set, copied out. The end is exclusive, so Subset(1, 3) is two
        // elements — the same reading "until" has, and the one that makes
        // Subset(0, n) + Subset(n, count) put the whole set back together.
        Member(BuiltInId.SetSubsetFrom, "Subset", set, PrimitiveType.Integer),
        Member(BuiltInId.SetSubsetBetween, "Subset", set,
               PrimitiveType.Integer, PrimitiveType.Integer),

        // Reads on the set rather than on a string, because the thing being joined is the
        // collection. Any set answers it, not only a set of strings: each element is written
        // out the way it would be on its own, which is what a reader joining numbers expects
        // and what they would otherwise have to write a loop for.
        Member(BuiltInId.SetJoin, "Join", PrimitiveType.String, PrimitiveType.String),

        // <para>Two sets read together. Both give back a new set and leave both originals
        // alone, as Subset does.</para>
        //
        // <para>A Profi-C set keeps its order and allows a value twice, so these are not the
        // operations of the same name in mathematics: Union appends rather than merging, so
        // what was in both is in the answer twice. Intersect keeps a run of what appears in
        // the other, in this set's order.</para>
        Member(BuiltInId.SetUnion, "Union", set, set),
        Member(BuiltInId.SetIntersect, "Intersect", set, set),

        // What this set has that the other does not. The counterpart of Intersect: between
        // them they divide this set in two, so Intersect and Except put it back together.
        Member(BuiltInId.SetExcept, "Except", set, set),

        // One of each, keeping the first of every run of equals so that what comes back is in
        // the order the values were first met. This is what makes a Profi-C set into the set
        // of mathematics, which it is not until asked: order is kept and a value may appear
        // twice, so Union appends rather than merging and Distinct is how you say otherwise.
        Member(BuiltInId.SetDistinct, "Distinct", set),

        .. TrimmingEmpties(set),
        .. OnEveryType(),
    ];

    /// <summary>
    /// <para>The four ways to drop the empties out of a set of optionals, and nothing at all
    /// for a set of anything else.</para>
    /// <para><c>TrimAll</c> is the one that changes the type. Removing every empty leaves a
    /// set where nothing can be absent, so it yields the underlying type and the caller stops
    /// having to unwrap. The other three take from the ends only, so an empty in the middle
    /// survives and the type has to keep saying so.</para>
    /// </summary>
    private static IReadOnlyList<BuiltInMember> TrimmingEmpties(SetType set) =>
        set.ElementType is OptionalType present
            ?
            [
                Member(BuiltInId.SetTrim, "Trim", set),
                Member(BuiltInId.SetTrimStart, "TrimStart", set),
                Member(BuiltInId.SetTrimEnd, "TrimEnd", set),
                Member(BuiltInId.SetTrimAll, "TrimAll", new SetType(present.UnderlyingType)),
            ]
            : [];

    /// <summary>
    /// A string's members mirror a set's, so that the two read alike. It reports its length
    /// with <c>Count()</c> rather than a differently named member for the same idea, and every
    /// one of these yields a new string rather than changing the original.
    /// </summary>
    public static IReadOnlyList<BuiltInMember> OnString() =>
    [
        Value(BuiltInId.StringCount, "Count", PrimitiveType.Integer),
        Member(BuiltInId.StringContains, "Contains", PrimitiveType.Boolean, PrimitiveType.String),
        Member(BuiltInId.StringIndexOf, "IndexOf", PrimitiveType.Integer, PrimitiveType.String),
        Member(BuiltInId.StringSubstring, "Substring", PrimitiveType.String,
               PrimitiveType.Integer, PrimitiveType.Integer),

        // A string answers Subset as a set does, since it is one when read that way. The two
        // differ in their second argument rather than in what they do: Substring takes how
        // many, Subset takes where to stop. Whichever number you have is the one to write.
        //
        // Both give back a string, because a run of a string is a string — the same rule
        // Subset follows on a set, where a run of one is a set.
        Member(BuiltInId.StringSubsetFrom, "Subset", PrimitiveType.String, PrimitiveType.Integer),
        Member(BuiltInId.StringSubsetBetween, "Subset", PrimitiveType.String,
               PrimitiveType.Integer, PrimitiveType.Integer),
        Member(BuiltInId.StringInsert, "Insert", PrimitiveType.String, PrimitiveType.String),
        Member(BuiltInId.StringInsertAt, "InsertAt", PrimitiveType.String,
               PrimitiveType.Integer, PrimitiveType.String),
        Member(BuiltInId.StringRemove, "Remove", PrimitiveType.String, PrimitiveType.String),
        Member(BuiltInId.StringRemoveAt, "RemoveAt", PrimitiveType.String, PrimitiveType.Integer),
        Member(BuiltInId.StringToCharacters, "ToCharacters", new SetType(PrimitiveType.Character)),

        // Three forms each. Written with nothing, whitespace goes; written with a string,
        // any of its characters go; written with a set, any in the set goes. The middle form
        // is the one people reach for, and the set form is there because a set of characters
        // is what you already have when the characters were worked out rather than typed.
        Member(BuiltInId.StringTrim, "Trim", PrimitiveType.String),
        Member(BuiltInId.StringTrimText, "Trim", PrimitiveType.String, PrimitiveType.String),
        Member(BuiltInId.StringTrimSet, "Trim", PrimitiveType.String,
               new SetType(PrimitiveType.Character)),

        Member(BuiltInId.StringTrimStart, "TrimStart", PrimitiveType.String),
        Member(BuiltInId.StringTrimStartText, "TrimStart", PrimitiveType.String, PrimitiveType.String),
        Member(BuiltInId.StringTrimStartSet, "TrimStart", PrimitiveType.String,
               new SetType(PrimitiveType.Character)),

        Member(BuiltInId.StringTrimEnd, "TrimEnd", PrimitiveType.String),
        Member(BuiltInId.StringTrimEndText, "TrimEnd", PrimitiveType.String, PrimitiveType.String),
        Member(BuiltInId.StringTrimEndSet, "TrimEnd", PrimitiveType.String,
               new SetType(PrimitiveType.Character)),

        // Splitting gives a set, and joining one back together is a member of the set rather
        // than of a string — the thing being joined is the collection, and reading it off the
        // separator would put the sentence the wrong way round.
        Member(BuiltInId.StringSplit, "Split", new SetType(PrimitiveType.String),
               PrimitiveType.String),

        Member(BuiltInId.StringReplace, "Replace", PrimitiveType.String,
               PrimitiveType.String, PrimitiveType.String),

        Member(BuiltInId.StringToUpper, "ToUpper", PrimitiveType.String),
        Member(BuiltInId.StringToLower, "ToLower", PrimitiveType.String),

        // Not a .NET member. The first letter is raised and the rest is left exactly as it
        // was, which is what you want for a name or the start of a sentence — and is not what
        // .NET's title-casing does, since that also lowers everything it did not raise.
        Member(BuiltInId.StringCapitalize, "Capitalize", PrimitiveType.String),

        // Text back into a value. Each yields an optional rather than raising: text that will
        // not read is the ordinary case, since most of it was typed by somebody.
        Member(BuiltInId.StringToInteger, "ToInteger", new OptionalType(PrimitiveType.Integer)),
        Member(BuiltInId.StringToReal, "ToReal", new OptionalType(PrimitiveType.Real)),
        Member(BuiltInId.StringToBoolean, "ToBoolean", new OptionalType(PrimitiveType.Boolean)),

        // A ratio reads with either mark between its halves. The language writes '22|7',
        // because a slash is already division; a person writes '22/7', because that is what a
        // fraction looks like everywhere outside a compiler. Reading takes both.
        Member(BuiltInId.StringToFraction, "ToFraction", new OptionalType(PrimitiveType.Fraction)),

        .. OnEveryType(),
    ];

    /// <summary>
    /// <para>The three members of an optional, and there are only three.</para>
    /// <para><c>Or</c> has two forms: given a plain value it ends the chain with a definite
    /// one, and given another optional it keeps the chain going, which is what makes
    /// <c>a.Or(b).Or(c)</c> work. The second form is added by the caller that knows the
    /// argument.</para>
    /// </summary>
    public static IReadOnlyList<BuiltInMember> OnOptional(OptionalType optional) =>
    [
        Member(BuiltInId.OptionalHasValue, "HasValue", PrimitiveType.Boolean),
        Member(BuiltInId.OptionalOr, "Or", optional.UnderlyingType, optional.UnderlyingType),
        Member(BuiltInId.OptionalValue, "Value", optional.UnderlyingType),
    ];

    /// <summary>
    /// <para>Writing a value out by a pattern, which every type that can be measured or dated
    /// answers.</para>
    /// <para>The patterns are .NET's own, unchanged, for the same reason the rest of the
    /// library keeps its shapes: what a reader learns here is what they will type next
    /// somewhere else. <c>F2</c> is two decimal places, <c>N0</c> is a whole number with
    /// separators, <c>yyyy-MM-dd</c> is a date. A pattern the runtime does not recognize is a
    /// FormatException rather than a silent oddity.</para>
    /// </summary>
    public static IReadOnlyList<BuiltInMember> OnInteger() =>
    [
        // No ToReal or ToFraction: an integer widens to either on its own. A float is the one
        // it does not reach, since letting it would leave every member of Math ambiguous.
        Member(BuiltInId.IntegerToFloat, "ToFloat", PrimitiveType.Float),
        Member(BuiltInId.IntegerFormat, "Format", PrimitiveType.String, PrimitiveType.String),
        .. OnEveryType(),
    ];

    /// <summary>
    /// <para>What a fraction answers.</para>
    /// <para><c>ToReal</c> is asked for rather than happening on its own, and not because of
    /// what it costs: a third has no decimal that ends, so what comes back no longer multiplies
    /// back to one. The answer is surprising enough to be worth writing down. A float's
    /// <c>ToFraction</c> is the other conversion held back for that reason.</para>
    /// </summary>
    public static IReadOnlyList<BuiltInMember> OnFraction() =>
    [
        Member(BuiltInId.FractionToReal, "ToReal", PrimitiveType.Real),
        Member(BuiltInId.FractionToFloat, "ToFloat", PrimitiveType.Float),

        // Exact, where a real's reciprocal is only nearly one: a third turned over is three,
        // and 1.0 / (1.0 / 3.0) is not quite one.
        Member(BuiltInId.FractionReciprocal, "Reciprocal", PrimitiveType.Fraction),
        Member(BuiltInId.FractionFormat, "Format", PrimitiveType.String, PrimitiveType.String),
        .. OnEveryType(),
    ];

    public static IReadOnlyList<BuiltInMember> OnReal() =>
    [
        // Nothing converts a real to a fraction here, because nothing has to: a real counts in
        // tens and so already is a fraction over a power of ten, which the language widens to
        // on its own.
        Member(BuiltInId.RealToFloat, "ToFloat", PrimitiveType.Float),
        Member(BuiltInId.RealFormat, "Format", PrimitiveType.String, PrimitiveType.String),
        .. OnEveryType(),
    ];

    /// <summary>
    /// <para>What a float answers, which is nearly what a real does and one member more.</para>
    /// <para>That member is the point of the type. A float converts to a fraction only when
    /// asked, and what comes back is the number it was really holding all along.</para>
    /// </summary>
    public static IReadOnlyList<BuiltInMember> OnFloat() =>
    [
        Member(BuiltInId.FloatToFraction, "ToFraction", PrimitiveType.Fraction),
        Member(BuiltInId.FloatToReal, "ToReal", PrimitiveType.Real),
        Member(BuiltInId.FloatFormat, "Format", PrimitiveType.String, PrimitiveType.String),
        .. OnEveryType(),
    ];

    public static IReadOnlyList<BuiltInMember> OnEnumeration() =>
    [
        Member(BuiltInId.EnumerationToInteger, "ToInteger", PrimitiveType.Integer),
        .. OnEveryType(),
    ];

    /// <summary>Carried by every exception, including one a program declares.</summary>
    public static IReadOnlyList<BuiltInMember> OnException() =>
    [
        Member(BuiltInId.ExceptionMessage, "Message", PrimitiveType.String),
        .. OnEveryType(),
    ];
}
