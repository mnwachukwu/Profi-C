using System.Collections.Frozen;

namespace ProfiC.Compiler.Semantics;

/// <summary>
/// <para>One line saying what each member the language provides is for.</para>
/// <para><b>Held apart from the catalog, and keyed by <see cref="BuiltInId"/>.</b> The catalog
/// says what a member's shape is — what it takes, what it yields, whether it is called or read —
/// and that is what the resolver and the type checker read. This says what it is <em>for</em>,
/// which nothing in the compiler needs and everything in front of a reader does. Threading it
/// through the catalog's factories would have put a sentence of prose in the middle of every one
/// of two hundred signatures, and made the file that answers "what exists" harder to read for the
/// sake of a file that answers "what does it do".</para>
/// <para>The id is the right key because it is already what identifies a member exactly:
/// <c>Count</c> on a string and <c>Count</c> on a set are two entries here, as they are two
/// entries there, and a name could not tell them apart.</para>
/// <para>Written the way the reference writes them — a clause rather than a sentence, and no full
/// stop, so that it reads as a label in a table, in a tooltip, and beside a name in a completion
/// list without being reworded for any of the three.</para>
/// </summary>
public static class BuiltInDocs
{
    /// <summary>What a member is for, or an empty string where nothing is recorded.</summary>
    public static string Summary(BuiltInId id) =>
        Summaries.TryGetValue(id, out string? said) ? said : string.Empty;

    /// <summary>Whether anything is recorded for a member.</summary>
    public static bool Describes(BuiltInId id) => Summaries.ContainsKey(id);

    /// <summary>
    /// What a type the language provides is for, or an empty string where nothing is recorded.
    /// Named rather than keyed by an id, because a type has none — nothing resolves <em>to</em> a
    /// type the way a call resolves to a member, so the name is what there is.
    /// </summary>
    public static string SummaryOf(string typeName) =>
        Types.TryGetValue(typeName, out string? said) ? said : string.Empty;

    private static readonly FrozenDictionary<string, string> Types =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Model"] = "The root of every type; what a value answers when nothing nearer does",
            ["Function"] = "The root of every function type, for holding one without naming its "
                + "shape",
            ["Exception"] = "The root of everything that can be thrown or caught",

            ["Console"] = "Writing to the screen and reading what is typed",
            ["Reference"] = "Asking whether two names reach the same object",
            ["Math"] = "Arithmetic beyond the operators: roots, powers, logarithms and angles",
            ["File"] = "Reading and writing whole files",
            ["Directory"] = "Asking about folders and what is in them",
            ["Random"] = "A source of chance, seeded or not",

            ["Integer"] = "What a whole number can hold",
            ["Real"] = "What an exact decimal number can hold",
            ["Float"] = "What a binary floating-point number can hold, including its oddities",
            ["String"] = "The string of no characters",
            ["Fraction"] = "Building an exact fraction from values worked out while running",

            ["DateTime"] = "A moment: a day and a time of day together",
            ["Date"] = "A day, with no time of day in it",
            ["Time"] = "A time of day, belonging to no particular day",
            ["TimeSpan"] = "A length of time, which is what separates two moments",

            ["ArgumentException"] = "A value a member cannot work with",
            ["DivideByZeroException"] = "Dividing by a zero the compiler could not see",
            ["EmptyOptionalException"] = "Reading the value of an optional that turned out empty",
            ["FormatException"] = "A pattern that is not recognized",
            ["IndexOutOfRangeException"] = "An index outside the set or string it was used on",
            ["InvalidCastException"] = "An 'as' to a type the value is not",
            ["IOException"] = "Anything that goes wrong with a file except its absence",
            ["OverflowException"] = "A number grown too large to hold",
            ["SequenceChangedException"] = "A set changed while a loop was walking it",
            ["RecursionTooDeepException"] =
                "Recursion with no base case; nothing catches this one",
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<BuiltInId, string> Summaries =
        new Dictionary<BuiltInId, string>
        {
            // ---- Writing and reading ---------------------------------------------------------
            [BuiltInId.ConsoleWrite] = "Writes a value, staying on the line",
            [BuiltInId.ConsoleWriteLine] = "Writes a value and ends the line",
            [BuiltInId.ConsoleRead] = "The next line typed, or nothing when input has run out",

            // ---- Every value -----------------------------------------------------------------
            [BuiltInId.ModelToString] =
                "The value written out the way Console.WriteLine would write it",
            [BuiltInId.ModelEquals] =
                "Whether two values are the same, compared by what they hold",
            [BuiltInId.ReferenceEquals] = "Whether both names reach the same object",
            [BuiltInId.EnumerationToInteger] = "The ordinal behind the member",
            [BuiltInId.ExceptionMessage] = "What went wrong, as text",

            // ---- What a number can hold ------------------------------------------------------
            [BuiltInId.IntegerMaxValue] = "The largest integer",
            [BuiltInId.IntegerMinValue] = "The smallest integer",
            [BuiltInId.RealMaxValue] = "The largest real",
            [BuiltInId.RealMinValue] = "The smallest real",
            [BuiltInId.FloatMaxValue] = "The largest float",
            [BuiltInId.FloatMinValue] = "The smallest float",
            [BuiltInId.FloatInfinity] = "A float larger than every other",
            [BuiltInId.FloatNegativeInfinity] = "A float smaller than every other",
            [BuiltInId.FloatNotANumber] = "The float that is not a number, and equals nothing",
            [BuiltInId.StringEmpty] = "The string of no characters",

            // ---- Math: constants -------------------------------------------------------------
            [BuiltInId.MathPi] = "The ratio of a circle's circumference to its diameter",
            [BuiltInId.MathE] = "The base of the natural logarithm",

            // ---- Math: roots and powers ------------------------------------------------------
            [BuiltInId.MathSqrt] = "The square root",
            [BuiltInId.MathCbrt] = "The cube root",
            [BuiltInId.MathRoot] = "The root of any degree",
            [BuiltInId.MathPow] = "One number raised by another",
            [BuiltInId.MathFactorial] = "The product of every whole number up to this one",
            [BuiltInId.MathSqrtFloat] = "The square root",
            [BuiltInId.MathCbrtFloat] = "The cube root",
            [BuiltInId.MathRootFloat] = "The root of any degree",
            [BuiltInId.MathPowFloat] = "One number raised by another",

            // ---- Math: logarithms ------------------------------------------------------------
            [BuiltInId.MathLog] = "The natural logarithm",
            [BuiltInId.MathLogInBase] = "The logarithm in any base",
            [BuiltInId.MathLog10] = "The logarithm base ten",
            [BuiltInId.MathLog2] = "The logarithm base two",
            [BuiltInId.MathLogFloat] = "The natural logarithm",
            [BuiltInId.MathLogInBaseFloat] = "The logarithm in any base",
            [BuiltInId.MathLog10Float] = "The logarithm base ten",
            [BuiltInId.MathLog2Float] = "The logarithm base two",

            // ---- Math: angles ----------------------------------------------------------------
            [BuiltInId.MathSin] = "The sine of an angle in radians",
            [BuiltInId.MathCos] = "The cosine of an angle in radians",
            [BuiltInId.MathTan] = "The tangent of an angle in radians",
            [BuiltInId.MathAsin] = "The angle whose sine this is",
            [BuiltInId.MathAcos] = "The angle whose cosine this is",
            [BuiltInId.MathAtan] = "The angle whose tangent this is",
            [BuiltInId.MathAtan2] = "The angle to a point, keeping the quadrant",
            [BuiltInId.MathSinh] = "The hyperbolic sine",
            [BuiltInId.MathCosh] = "The hyperbolic cosine",
            [BuiltInId.MathTanh] = "The hyperbolic tangent",
            [BuiltInId.MathAsinh] = "The value whose hyperbolic sine this is",
            [BuiltInId.MathAcosh] = "The value whose hyperbolic cosine this is",
            [BuiltInId.MathAtanh] = "The value whose hyperbolic tangent this is",
            [BuiltInId.MathSinFloat] = "The sine of an angle in radians",
            [BuiltInId.MathCosFloat] = "The cosine of an angle in radians",
            [BuiltInId.MathTanFloat] = "The tangent of an angle in radians",
            [BuiltInId.MathAsinFloat] = "The angle whose sine this is",
            [BuiltInId.MathAcosFloat] = "The angle whose cosine this is",
            [BuiltInId.MathAtanFloat] = "The angle whose tangent this is",
            [BuiltInId.MathAtan2Float] = "The angle to a point, keeping the quadrant",
            [BuiltInId.MathSinhFloat] = "The hyperbolic sine",
            [BuiltInId.MathCoshFloat] = "The hyperbolic cosine",
            [BuiltInId.MathTanhFloat] = "The hyperbolic tangent",
            [BuiltInId.MathAsinhFloat] = "The value whose hyperbolic sine this is",
            [BuiltInId.MathAcoshFloat] = "The value whose hyperbolic cosine this is",
            [BuiltInId.MathAtanhFloat] = "The value whose hyperbolic tangent this is",

            // ---- Math: choosing and rounding -------------------------------------------------
            [BuiltInId.MathAbsInteger] = "How far from zero, without the sign",
            [BuiltInId.MathAbsReal] = "How far from zero, without the sign",
            [BuiltInId.MathAbsFraction] = "How far from zero, without the sign",
            [BuiltInId.MathAbsFloat] = "How far from zero, without the sign",
            [BuiltInId.MathMinInteger] = "The smaller of two",
            [BuiltInId.MathMinReal] = "The smaller of two",
            [BuiltInId.MathMinFraction] = "The smaller of two",
            [BuiltInId.MathMinFloat] = "The smaller of two",
            [BuiltInId.MathMaxInteger] = "The larger of two",
            [BuiltInId.MathMaxReal] = "The larger of two",
            [BuiltInId.MathMaxFraction] = "The larger of two",
            [BuiltInId.MathMaxFloat] = "The larger of two",
            [BuiltInId.MathFloorReal] = "To a whole number, down",
            [BuiltInId.MathFloorFraction] = "To a whole number, down",
            [BuiltInId.MathFloorFloat] = "To a whole number, down",
            [BuiltInId.MathCeilingReal] = "To a whole number, up",
            [BuiltInId.MathCeilingFraction] = "To a whole number, up",
            [BuiltInId.MathCeilingFloat] = "To a whole number, up",
            [BuiltInId.MathRoundReal] = "To the nearest whole number",
            [BuiltInId.MathRoundFraction] = "To the nearest whole number",
            [BuiltInId.MathRoundFloat] = "To the nearest whole number",
            [BuiltInId.MathRoundRealPlaces] = "To that many decimal places",
            [BuiltInId.MathRoundFloatPlaces] = "To that many decimal places",

            // ---- Fractions -------------------------------------------------------------------
            [BuiltInId.FractionCreate] = "A fraction from a numerator and a denominator",
            [BuiltInId.FractionCreateWhole] = "A whole number as a fraction",
            [BuiltInId.FractionReciprocal] = "The fraction turned upside down",
            [BuiltInId.FractionNumerator] = "The number above the line, once reduced",
            [BuiltInId.FractionDenominator] = "The number below the line, once reduced",

            // ---- Conversions between numbers -------------------------------------------------
            [BuiltInId.FractionToReal] = "The fraction as a real, which may not be exact",
            [BuiltInId.FractionToFloat] = "The fraction as a float, which may not be exact",
            [BuiltInId.RealToFloat] = "The real as a float, which may not be exact",
            [BuiltInId.FloatToReal] = "The float as a real, which may not be exact",
            [BuiltInId.FloatToFraction] = "The float as a fraction, which may not be exact",
            [BuiltInId.IntegerToFloat] = "The whole number as a float",

            // ---- Writing a number as text ----------------------------------------------------
            [BuiltInId.IntegerFormat] = "Written out by a pattern",
            [BuiltInId.RealFormat] = "Written out by a pattern",
            [BuiltInId.FloatFormat] = "Written out by a pattern",
            [BuiltInId.FractionFormat] = "Written out by a pattern",

            // ---- Reading a value out of text -------------------------------------------------
            [BuiltInId.StringToInteger] = "Read as a whole number, or nothing",
            [BuiltInId.StringToReal] = "Read as a real, or nothing",
            [BuiltInId.StringToFloat] = "Read as a float, or nothing",
            [BuiltInId.StringToBoolean] = "Read as true or false, or nothing",
            [BuiltInId.StringToCharacter] = "Read as one character, or nothing where it is not one",
            [BuiltInId.StringToFraction] = "Read as a fraction, or nothing",

            // The same readings, reached through the type rather than through the text. Written
            // the other way round because that is how each reads at its own call: a string in
            // hand answers what it can become, and a type asked to read text says what it wants.
            [BuiltInId.IntegerParse] = "Read text as a whole number, or nothing",
            [BuiltInId.RealParse] = "Read text as a real, or nothing",
            [BuiltInId.FloatParse] = "Read text as a float, or nothing",
            [BuiltInId.BooleanParse] = "Read text as true or false, or nothing",
            [BuiltInId.CharacterParse] = "Read text as one character, or nothing where it is not one",
            [BuiltInId.FractionParse] = "Read text as a fraction, or nothing",

            // ---- Optionals -------------------------------------------------------------------
            [BuiltInId.OptionalHasValue] = "Whether there is anything in it",
            [BuiltInId.OptionalValue] = "What is in it, raising where there is nothing",
            [BuiltInId.OptionalOr] = "What is in it, or the value given instead",

            // ---- Chance ----------------------------------------------------------------------
            [BuiltInId.RandomNew] = "A new source of chance",
            [BuiltInId.RandomNewSeeded] = "A source of chance that repeats, given the same seed",
            [BuiltInId.RandomNext] = "A whole number, any at all",
            [BuiltInId.RandomNextBelow] = "A whole number from zero up to but not including one",
            [BuiltInId.RandomNextBetween] =
                "A whole number from one up to but not including the other",
            [BuiltInId.RandomNextDouble] = "A float from zero up to but not including one",

            // ---- Text: asking about it -------------------------------------------------------
            [BuiltInId.StringCount] = "How many characters",
            [BuiltInId.StringContains] = "Whether the text appears anywhere",
            [BuiltInId.StringIndexOf] = "Where the text starts, or -1 where it does not appear",

            // ---- Text: taking a piece --------------------------------------------------------
            [BuiltInId.StringSubstring] = "That many characters from a place",
            [BuiltInId.StringSubsetFrom] = "From a place to the end",
            [BuiltInId.StringSubsetBetween] = "From one place up to but not including another",

            // ---- Text: building a new one ----------------------------------------------------
            [BuiltInId.StringInsert] = "The text put in at a place",
            [BuiltInId.StringInsertAt] = "The character put in at a place",
            [BuiltInId.StringRemove] = "Every appearance of the text taken out",
            [BuiltInId.StringRemoveAt] = "The character at a place taken out",
            [BuiltInId.StringReplace] = "Every appearance of one piece of text swapped for another",

            // ---- Text: trimming --------------------------------------------------------------
            [BuiltInId.StringTrim] = "Whitespace off both ends",
            [BuiltInId.StringTrimText] = "The text off both ends, as often as it appears",
            [BuiltInId.StringTrimSet] = "Any of those characters off both ends",
            [BuiltInId.StringTrimStart] = "Whitespace off the front",
            [BuiltInId.StringTrimStartText] = "The text off the front, as often as it appears",
            [BuiltInId.StringTrimStartSet] = "Any of those characters off the front",
            [BuiltInId.StringTrimEnd] = "Whitespace off the end",
            [BuiltInId.StringTrimEndText] = "The text off the end, as often as it appears",
            [BuiltInId.StringTrimEndSet] = "Any of those characters off the end",

            // ---- Text: case ------------------------------------------------------------------
            [BuiltInId.StringToUpper] = "Every letter raised",
            [BuiltInId.StringToLower] = "Every letter lowered",
            [BuiltInId.StringCapitalize] = "The first letter raised, the rest left alone",

            // ---- Text: splitting and joining -------------------------------------------------
            [BuiltInId.StringSplit] = "The pieces between each appearance of the separator",
            [BuiltInId.StringToCharacters] = "The characters, one to an element",
            [BuiltInId.SetJoin] = "The elements written out with the separator between them",

            // ---- Sets: asking about one ------------------------------------------------------
            [BuiltInId.SetCount] = "How many elements",
            [BuiltInId.SetContains] = "Whether the element is in it",
            [BuiltInId.SetIndexOf] = "Where the element is, or -1 where it is not in it",

            // ---- Sets: changing one ----------------------------------------------------------
            [BuiltInId.SetInsert] = "Adds the element to the end",
            [BuiltInId.SetInsertAt] = "Adds the element at a place",
            [BuiltInId.SetRemove] = "Takes the first appearance of the element out",
            [BuiltInId.SetRemoveAt] = "Takes the element at a place out",
            [BuiltInId.SetClear] = "Takes every element out",

            // ---- Sets: taking a piece --------------------------------------------------------
            [BuiltInId.SetSubsetFrom] = "From a place to the end",
            [BuiltInId.SetSubsetBetween] = "From one place up to but not including another",
            // Only on a set of optionals, and about the empties in it rather than its elements.
            [BuiltInId.SetTrim] = "Empties off both ends",
            [BuiltInId.SetTrimStart] = "Empties off the front",
            [BuiltInId.SetTrimEnd] = "Empties off the end",
            [BuiltInId.SetTrimAll] = "Every empty gone, anywhere",

            // ---- Sets: one set against another -----------------------------------------------
            [BuiltInId.SetUnion] = "Everything in either, without repeats",
            [BuiltInId.SetIntersect] = "Everything in both, without repeats",
            [BuiltInId.SetExcept] = "Everything in this one and not the other",
            [BuiltInId.SetDistinct] = "Every element once",

            // ---- Moments ---------------------------------------------------------------------
            [BuiltInId.DateTimeNewDate] = "A moment at midnight on a day",
            [BuiltInId.DateTimeNewMoment] = "A moment on a day at a time",
            [BuiltInId.DateTimeFromDate] = "A moment at midnight on a day",
            [BuiltInId.DateTimeFromDateAndTime] = "A day and a time joined into a moment",
            [BuiltInId.DateTimeNow] = "This moment",
            [BuiltInId.DateTimeToday] = "Midnight at the start of today",
            [BuiltInId.DateTimeYear] = "Which year",
            [BuiltInId.DateTimeMonth] = "Which month, from one",
            [BuiltInId.DateTimeDay] = "Which day of the month, from one",
            [BuiltInId.DateTimeHour] = "Which hour, from zero",
            [BuiltInId.DateTimeMinute] = "Which minute, from zero",
            [BuiltInId.DateTimeSecond] = "Which second, from zero",
            [BuiltInId.DateTimeDayOfWeek] = "Which day of the week, Sunday being zero",
            [BuiltInId.DateTimeDayOfYear] = "Which day of the year, from one",
            [BuiltInId.DateTimeDatePart] = "The day, without the time of day",
            [BuiltInId.DateTimeTimePart] = "The time of day, without the day",
            [BuiltInId.DateTimeAddDays] = "That many days later",
            [BuiltInId.DateTimeAddHours] = "That many hours later",
            [BuiltInId.DateTimeAddMinutes] = "That many minutes later",
            [BuiltInId.DateTimeAddSeconds] = "That many seconds later",
            [BuiltInId.DateTimeAddYears] = "That many years later",
            [BuiltInId.DateTimeAddMonths] = "That many months later",
            [BuiltInId.DateTimeAdd] = "That much time later",
            [BuiltInId.DateTimeSubtract] = "That much time earlier",
            [BuiltInId.DateTimeSubtractSpan] = "How long between the two moments",
            [BuiltInId.DateTimeCompareTo] =
                "Negative if earlier, zero if equal, positive if later",
            [BuiltInId.DateTimeFormat] = "Written out by a pattern",
            [BuiltInId.DateTimeParse] = "Read back from text, or nothing",
            [BuiltInId.DateTimeParseExact] = "Read back from text by a pattern, or nothing",

            // ---- Days ------------------------------------------------------------------------
            [BuiltInId.DateNew] = "A day",
            [BuiltInId.DateToday] = "Today's day",
            [BuiltInId.DateFromMoment] = "The day part of a moment",
            [BuiltInId.DateYear] = "Which year",
            [BuiltInId.DateMonth] = "Which month, from one",
            [BuiltInId.DateDay] = "Which day of the month, from one",
            [BuiltInId.DateDayOfWeek] = "Which day of the week, Sunday being zero",
            [BuiltInId.DateDayOfYear] = "Which day of the year, from one",
            [BuiltInId.DateAddDays] = "That many days later",
            [BuiltInId.DateAddMonths] = "That many months later",
            [BuiltInId.DateAddYears] = "That many years later",
            [BuiltInId.DateAtTime] = "The day at a time of day, as a moment",
            [BuiltInId.DateCompareTo] = "Negative if earlier, zero if equal, positive if later",
            [BuiltInId.DateFormat] = "Written out by a pattern",
            [BuiltInId.DateParse] = "Read back from text, or nothing",
            [BuiltInId.DateParseExact] = "Read back from text by a pattern, or nothing",

            // ---- Times of day ----------------------------------------------------------------
            [BuiltInId.TimeNewToMinute] = "A time of day, to the minute",
            [BuiltInId.TimeNewToSecond] = "A time of day, to the second",
            [BuiltInId.TimeNow] = "The time of day now",
            [BuiltInId.TimeFromMoment] = "The time-of-day part of a moment",
            [BuiltInId.TimeHour] = "Which hour, from zero",
            [BuiltInId.TimeMinute] = "Which minute, from zero",
            [BuiltInId.TimeSecond] = "Which second, from zero",
            [BuiltInId.TimeAddHours] = "That many hours later",
            [BuiltInId.TimeAddMinutes] = "That many minutes later",
            [BuiltInId.TimeToTimeSpan] = "Time of day as time since midnight",
            [BuiltInId.TimeCompareTo] =
                "Negative if earlier, zero if equal, positive if later",
            [BuiltInId.TimeFormat] = "Written out by a pattern",
            [BuiltInId.TimeParse] = "Read back from text, or nothing",
            [BuiltInId.TimeParseExact] = "Read back from text by a pattern, or nothing",

            // ---- Lengths of time -------------------------------------------------------------
            [BuiltInId.TimeSpanNewTime] = "A length of time, in hours, minutes and seconds",
            [BuiltInId.TimeSpanNewSpan] = "A length of time, in days, hours, minutes and seconds",
            [BuiltInId.TimeSpanZero] = "No time at all",
            [BuiltInId.TimeSpanFromDays] = "That many days as a length of time",
            [BuiltInId.TimeSpanFromHours] = "That many hours as a length of time",
            [BuiltInId.TimeSpanFromMinutes] = "That many minutes as a length of time",
            [BuiltInId.TimeSpanFromSeconds] = "That many seconds as a length of time",
            [BuiltInId.TimeSpanDays] = "The whole days in it",
            [BuiltInId.TimeSpanHours] = "The hours left over after the days",
            [BuiltInId.TimeSpanMinutes] = "The minutes left over after the hours",
            [BuiltInId.TimeSpanSeconds] = "The seconds left over after the minutes",
            [BuiltInId.TimeSpanTotalDays] = "The whole thing counted in days",
            [BuiltInId.TimeSpanTotalHours] = "The whole thing counted in hours",
            [BuiltInId.TimeSpanTotalMinutes] = "The whole thing counted in minutes",
            [BuiltInId.TimeSpanTotalSeconds] = "The whole thing counted in seconds",
            [BuiltInId.TimeSpanNegate] = "The same length, the other way",
            [BuiltInId.TimeSpanDuration] = "The same length, forwards",
            [BuiltInId.TimeSpanAdd] = "The two lengths together",
            [BuiltInId.TimeSpanSubtract] = "The second length taken off the first",
            [BuiltInId.TimeSpanCompareTo] =
                "Negative if shorter, zero if equal, positive if longer",
            [BuiltInId.TimeSpanFormat] = "Written out by a pattern",
            [BuiltInId.TimeSpanParse] = "Read back from text, or nothing",
            [BuiltInId.TimeSpanParseExact] = "Read back from text by a pattern, or nothing",

            // ---- Files -----------------------------------------------------------------------
            [BuiltInId.FileRead] = "The whole file as text, or nothing where there is no file",
            [BuiltInId.FileReadLines] = "The lines of the file, or nothing where there is no file",
            [BuiltInId.FileWrite] = "Writes the text, replacing whatever was there",
            [BuiltInId.FileWriteLines] = "Writes the lines, replacing whatever was there",
            [BuiltInId.FileAppend] = "Adds the text to the end",
            [BuiltInId.FileExists] = "Whether the file is there",
            [BuiltInId.FileDelete] = "Removes the file, and whether there was one",
            [BuiltInId.FileCopy] = "Copies the file",
            [BuiltInId.FileMove] = "Moves or renames the file",
            [BuiltInId.FileSize] = "How many bytes, or nothing where there is no file",
            [BuiltInId.FileChanged] = "When it last changed, or nothing where there is no file",

            // ---- Folders ---------------------------------------------------------------------
            [BuiltInId.DirectoryCurrent] = "The folder the program is running in",
            [BuiltInId.DirectoryExists] = "Whether the folder is there",
            [BuiltInId.DirectoryCreate] = "Makes the folder, and every folder on the way",
            [BuiltInId.DirectoryDelete] = "Removes the folder, and whether there was one",
            [BuiltInId.DirectoryFiles] =
                "The files directly inside, or nothing where there is no folder",
            [BuiltInId.DirectoryFolders] =
                "The folders directly inside, or nothing where there is no folder",
        }.ToFrozenDictionary();
}
