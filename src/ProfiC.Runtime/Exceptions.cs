namespace ProfiC.Runtime;

/// <summary>
/// <para>Thrown when an empty optional is unwrapped with <c>Value()</c>.</para>
/// <para>This is the only exception Profi-C names that the base class library does not
/// already have. The rest — dividing by zero, indexing out of range, an invalid cast, a bad
/// format, a bad argument, an overflow, and anything going wrong with a file — map onto
/// <c>System</c> types verbatim, which is what will make an eventual bridge between .NET
/// exceptions and Profi-C ones nearly free.</para>
/// <para>Reaching this is rare by design: optional access is checked while compiling, and
/// <c>Value()</c> is the deliberate escape hatch, as Kotlin's <c>!!</c> is.</para>
/// </summary>
public sealed class EmptyOptionalException : InvalidOperationException
{
    public EmptyOptionalException()
        : base("Cannot read the value of an empty optional.")
    {
    }

    public EmptyOptionalException(string message)
        : base(message)
    {
    }

    public EmptyOptionalException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// <para>Thrown when a set is changed while a <c>for each</c> is walking it.</para>
/// <para>A walk reads the set's length once, when it begins, so a change made during one does
/// not move with it: inserting leaves elements never reached, and removing leaves the walk
/// running past the end. The second used to arrive as an index out of range, several frames
/// from the line responsible and saying nothing about the walk.</para>
/// <para>Most of these are refused while compiling (`PC0243`). This catches the rest — a set
/// reached under a second name, or handed to a function that changes it — which no local rule
/// can see. The same division as dividing by zero: refused when it is visible, raised when it
/// is not.</para>
/// </summary>
public sealed class SequenceChangedException : InvalidOperationException
{
    public SequenceChangedException()
        : base("This set was changed while a 'for each' was walking it. A walk reads the set's "
               + "length when it begins, so it cannot follow a change made during one. Collect "
               + "what you want into another set, or count with a range loop instead.")
    {
    }

    public SequenceChangedException(string message)
        : base(message)
    {
    }

    public SequenceChangedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// <para>Thrown when calls nest deeper than the language will follow, which is almost always a
/// function that calls itself without ever reaching a base case.</para>
/// <para><b>This is not a stack overflow.</b> The limit is a count the language keeps, and it
/// is reached long before the machine is anywhere near out of room — deliberately, so that the
/// program stops while it can still say why. A real stack overflow gives no such chance: by the
/// time it happens there is no room left to report it in.</para>
/// <para>It has a name because a reader deserves to know what stopped their program, and it
/// cannot be caught because there is nothing useful a program could do with it. The depth is
/// the language's number rather than the program's, so a handler would run at an arbitrary
/// point with every frame beneath it abandoned half-finished. .NET's
/// <c>StackOverflowException</c> is uncatchable for the same reason, and this is the same
/// bargain — a name to read, and no pretence that catching it would help.</para>
/// </summary>
public sealed class RecursionTooDeepException : Exception
{
    public RecursionTooDeepException(int depth)
        : base($"Calls nested more than {depth} deep, so the program stopped. This nearly "
               + "always means a function calls itself without ever reaching the case that "
               + "stops it. This is not the machine running out of room — the language counts "
               + "the calls and stops early, while it can still say what happened.")
    {
    }

    public RecursionTooDeepException(string message)
        : base(message)
    {
    }

    public RecursionTooDeepException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// <para>The arithmetic that cannot answer, worded once.</para>
/// <para>These carry <c>System</c> types, because the name is what a reader takes with them to
/// another language and those names are already right. The wording is not: a message is read
/// once and thrown away, so there is nothing to be gained by keeping .NET's and something to
/// lose, which is the only voice a program speaks in while it is failing.</para>
/// <para>Both back ends read from here, so the interpreter and the emitter cannot drift into
/// saying different things about the same fault.</para>
/// </summary>
public static class ArithmeticFailures
{
    /// <summary>Dividing by a divisor that turned out to be zero.</summary>
    public static DivideByZeroException DivideByZero() => new(
        "Cannot divide by zero. A divisor written down is refused while compiling as PC0324, "
        + "so a zero reaching here arrived in a variable and could only be found now.");

    /// <summary>Taking a remainder against a divisor that turned out to be zero.</summary>
    public static DivideByZeroException RemainderByZero() => new(
        "Cannot take the remainder of a division by zero. A remainder is what a division "
        + "leaves behind, so it needs a division that can happen.");

    /// <summary>
    /// A whole-number result too large to hold. The bound is stated because "too large" means
    /// nothing without it, and the design decision is stated because a reader arriving from a
    /// language that wraps round will otherwise expect a wrong answer rather than a stop.
    /// </summary>
    public static OverflowException TooLargeForAnInteger() => new(
        "This result is too large to hold. An integer counts up to about 9.2 quintillion, and "
        + "arithmetic is checked rather than wrapped round, so it stops here instead of "
        + "carrying on with a number that would look plausible and be wrong.");

    /// <summary>
    /// <para>A real result too large to hold.</para>
    /// <para>The bound is stated because it is the surprising one: a real counts in tens and runs
    /// out far sooner than binary floating point, which does not run out at all. That it stops
    /// rather than answering with an infinity is the same choice an integer makes, and is worth
    /// saying beside the number.</para>
    /// </summary>
    public static OverflowException TooLargeForAReal() => new(
        "This result is too large to hold. A real counts in tens up to about 79 followed by 27 "
        + "zeros, and it stops at the end rather than carrying on into an infinity — which is "
        + "what a float does, and what makes the two worth telling apart.");

    /// <summary>
    /// <para>A real with no fraction to become.</para>
    /// <para>Not a rounding problem — every real is exactly a fraction — but a size one: the
    /// parts of a fraction are integers, and either the digits or the power of ten the point
    /// implies has outgrown one. Written down, this is refused while compiling; here it arrived
    /// in a variable and could only be found now.</para>
    /// </summary>
    public static OverflowException TooWideForAFraction(decimal value) => new(
        $"{value} has no fraction to become. Every real is exactly a fraction, but the parts of "
        + "one are whole numbers — and this needs a numerator or a denominator larger than an "
        + "integer holds. Up to eighteen places after the point will convert.");

    /// <summary>
    /// A fraction whose parts no longer fit. Denominators multiply on every unlike addition, so
    /// this arrives from a chain of them rather than from one large number, which is worth
    /// saying: the operand a reader is looking at is rarely the one at fault.
    /// </summary>
    public static OverflowException TooLargeForAFraction() => new(
        "The parts of this fraction have grown too large to hold. Denominators multiply every "
        + "time two unlike fractions are added, so a long chain of them can outgrow an integer "
        + "even where no single fraction looks large.");
}

/// <summary>
/// <para>The exceptions a Profi-C program can name, and what each is at run time.</para>
/// <para>Recorded here so that one place answers the question, rather than the mapping being
/// implicit in the emitter.</para>
/// </summary>
public static class BuiltInExceptions
{
    /// <summary>
    /// Every exception name the language defines, paired with the type it denotes. The name a
    /// program writes after <c>catch</c> and the type that travels at run time are the same
    /// entry, so a name the language can raise is a name the language can catch.
    /// </summary>
    /// <summary>
    /// <para>One entry in the catalog: a name a program writes, and the type it denotes.</para>
    /// <para>The type is marked as one whose constructors are reached without being named,
    /// because that is what happens to it — the interpreter builds one with
    /// <see cref="Activator"/> when a program throws. Nothing in the source says so, so a build
    /// that removes what it cannot see removes exactly the constructors this depends on, and the
    /// failure arrives when somebody throws rather than when anything is built.</para>
    /// </summary>
    /// <remarks>
    /// Marked on the parameter as well as the property. A positional record is a constructor and
    /// a property, the value passes through both, and what is not said on each end is dropped at
    /// that end.
    /// </remarks>
    private readonly record struct BuiltIn(
        string Name,
        [property: System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
            System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)]
        [param: System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
            System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)]
        Type Type);

    private static readonly BuiltIn[] Catalog =
    [
        new("Exception", typeof(Exception)),
        new("DivideByZeroException", typeof(DivideByZeroException)),
        new("IndexOutOfRangeException", typeof(IndexOutOfRangeException)),
        new("EmptyOptionalException", typeof(EmptyOptionalException)),
        new("SequenceChangedException", typeof(SequenceChangedException)),
        new("InvalidCastException", typeof(InvalidCastException)),
        new("FormatException", typeof(FormatException)),
        new("ArgumentException", typeof(ArgumentException)),
        new("OverflowException", typeof(OverflowException)),
        new("RecursionTooDeepException", typeof(RecursionTooDeepException)),

        // Everything that can go wrong with a file except the file not being there, which is
        // an absent optional rather than a fault. Maps onto System.IOException, which is
        // already the parent of the more particular ones the framework raises, so a locked
        // file, a bad path and a full disk all arrive here without being listed separately.
        new("IOException", typeof(System.IO.IOException)),
    ];

    /// <summary>Every exception name the language defines.</summary>
    public static IReadOnlyList<string> Names { get; } = [.. Catalog.Select(entry => entry.Name)];

    /// <summary>
    /// <para>The names a program may write but never catch.</para>
    /// <para>Being nameable and being catchable are separate: a reader needs the name to
    /// understand what stopped their program, which is why these are in the catalog at all,
    /// but no <c>catch</c> takes one. A clause naming one is reported rather than left to sit
    /// there looking like a handler.</para>
    /// </summary>
    public static IReadOnlySet<string> Uncatchable { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "RecursionTooDeepException" };

    /// <summary>Whether a <c>catch</c> naming this type could ever take anything.</summary>
    public static bool MayBeCaught(string profiCName) => !Uncatchable.Contains(profiCName);

    /// <summary>
    /// <para>Maps a Profi-C exception name to the type it denotes.</para>
    /// <para>What comes back is built with <see cref="Activator"/> by whoever asked, so it is
    /// marked as a type whose constructors are reached without being named — otherwise a build
    /// that removes what it cannot see removes them, and throwing fails at the moment a program
    /// throws rather than at the moment anything is built.</para>
    /// </summary>
    [return: System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
        System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)]
    public static Type? Resolve(string profiCName)
    {
        // Read through the entry rather than by taking it apart, so that what the property says
        // about the type travels with it. A deconstruction drops the annotation on the way out.
        foreach (BuiltIn one in Catalog)
        {
            if (one.Name == profiCName)
            {
                return one.Type;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether an exception that reached the top of a program is one the language raises, and so
    /// one a program could have caught. Anything else is a fault in the compiler itself.
    /// </summary>
    public static bool IsBuiltIn(Exception thrown)
    {
        ArgumentNullException.ThrowIfNull(thrown);

        foreach ((_, Type type) in Catalog)
        {
            if (type != typeof(Exception) && type.IsInstanceOfType(thrown))
            {
                return true;
            }
        }

        return false;
    }
}
