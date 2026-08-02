using System.Collections.Frozen;
using System.Reflection;

namespace ProfiC.Compiler.Diagnostics;

/// <summary>
/// <para>Every diagnostic the compiler can report.</para>
/// <para>Identifiers are allocated in blocks by compilation phase so that a reader can tell
/// from the number alone which part of the compiler objected.</para>
/// </summary>
/// <remarks>
/// <list type="table">
///   <item><term>PC0001-0099</term><description>Lexical</description></item>
///   <item><term>PC0100-0199</term><description>Syntax</description></item>
///   <item><term>PC0200-0299</term><description>Name resolution</description></item>
///   <item><term>PC0300-0399</term><description>Type checking</description></item>
///   <item><term>PC0400-0499</term><description>Definite assignment and flow</description></item>
///   <item><term>PC0500-0599</term><description>Lowering and emit</description></item>
///   <item><term>PC0600-0699</term><description>Project files and imports</description></item>
///   <item><term>PC0900+</term><description>Internal compiler errors</description></item>
/// </list>
/// </remarks>
public static class DiagnosticDescriptors
{
    private static readonly Lazy<FrozenDictionary<string, DiagnosticDescriptor>> Table =
        new(() => typeof(DiagnosticDescriptors)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(DiagnosticDescriptor))
            .Select(field => (DiagnosticDescriptor)field.GetValue(null)!)
            .ToFrozenDictionary(descriptor => descriptor.Id, StringComparer.Ordinal));

    /// <summary>
    /// <para>Every diagnostic, by the identifier it carries.</para>
    /// <para>Read off the fields rather than listed again, so that adding one below is the
    /// whole of adding one. An <c>ignore</c> directive naming an identifier is answered from
    /// here: whether it names anything, and whether what it names can be silenced.</para>
    /// <para>Built on first use rather than in a field initializer, since a field initializer
    /// would run in declaration order and read the descriptors below it as null.</para>
    /// </summary>
    public static FrozenDictionary<string, DiagnosticDescriptor> ById => Table.Value;

    private static DiagnosticDescriptor Error(string id, string title, string format) =>
        new(id, DiagnosticSeverity.Error, title, format);

    private static DiagnosticDescriptor Warning(string id, string title, string format) =>
        new(id, DiagnosticSeverity.Warning, title, format);

    /// <summary>
    /// A program that compiles, runs, and does what its author meant, written a way the
    /// language would rather it were not. Every opinion says some written token has no effect.
    /// </summary>
    private static DiagnosticDescriptor Opinion(string id, string title, string format) =>
        new(id, DiagnosticSeverity.Opinion, title, format);

    // ---- Lexical, PC0001 to PC0099 ----------------------------------------------------

    public static readonly DiagnosticDescriptor UnrecognizedCharacter = Error(
        "PC0001",
        "Unrecognized character",
        "Unrecognized character '{0}'.");

    public static readonly DiagnosticDescriptor UnterminatedString = Error(
        "PC0002",
        "Unterminated string literal",
        "Unterminated string literal.");

    public static readonly DiagnosticDescriptor UnterminatedCharacter = Error(
        "PC0003",
        "Unterminated character literal",
        "Unterminated character literal.");

    public static readonly DiagnosticDescriptor MalformedCharacterLiteral = Error(
        "PC0004",
        "Malformed character literal",
        "A character literal must contain exactly one character.");

    public static readonly DiagnosticDescriptor UnterminatedBlockComment = Error(
        "PC0005",
        "Unterminated block comment",
        "Unterminated block comment; expected '##'.");

    /// <summary>
    /// Reported for operators a C# author reaches for that Profi-C does not have. Naming the
    /// replacement is the whole point; the alternative is either a bare "unrecognized
    /// character" or, worse, silently scanning "+=" as two separate tokens.
    /// </summary>
    public static readonly DiagnosticDescriptor NotAnOperator = Error(
        "PC0006",
        "Not an operator in Profi-C",
        "'{0}' is not an operator in Profi-C. {1}");

    public static readonly DiagnosticDescriptor UnrecognizedEscape = Error(
        "PC0007",
        "Unrecognized escape sequence",
        "Unrecognized escape sequence '\\{0}'.");

    public static readonly DiagnosticDescriptor MalformedUnicodeEscape = Error(
        "PC0008",
        "Malformed Unicode escape sequence",
        "A Unicode escape must be '\\u' followed by four hexadecimal digits.");

    /// <summary>
    /// <para>An <c>@</c> before a name that needed no escaping.</para>
    /// <para>A warning, not an error: the name means what it says either way. It is worth
    /// saying because the mark tells a reader "this word is otherwise taken", and one in front
    /// of a word that never was misleads them.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor UnnecessaryEscapedName = Opinion(
        "PC0009",
        "This name needs no '@'",
        "'{0}' is not a reserved word, so the '@' does nothing. Write '{0}'.");

    public static readonly DiagnosticDescriptor EscapeNeedsAName = Error(
        "PC0010",
        "Nothing to escape",
        "'@' marks a reserved word being used as a name, so a name must follow it.");

    /// <summary>
    /// Reported at the <c>{{</c>, which is where the missing closer belongs. A hole runs to
    /// the end of the line at worst, for the same reason a string literal does: letting one
    /// run further turns a single missing brace into every following line being read as an
    /// expression.
    /// </summary>
    public static readonly DiagnosticDescriptor UnterminatedInterpolation = Error(
        "PC0011",
        "Unterminated interpolation",
        "Unterminated interpolation; expected '}}'.");

    public static readonly DiagnosticDescriptor EmptyInterpolation = Error(
        "PC0012",
        "Nothing to interpolate",
        "An interpolation holds an expression. Write '{{name}}', or a single brace for a "
        + "literal one.");

    public static readonly DiagnosticDescriptor UnterminatedBlockString = Error(
        "PC0013",
        "Unterminated block string",
        "Unterminated block string; expected '{0}'.");

    /// <summary>
    /// A base written with nothing after it. There is no number here to read, and the prefix
    /// alone is not one.
    /// </summary>
    public static readonly DiagnosticDescriptor BaseNeedsDigits = Error(
        "PC0017",
        "This base has no digits",
        "'{0}' says the number that follows is {1}, and none follows.");

    /// <summary>
    /// <para>A digit that does not exist in the base it was written in — <c>0b12</c>, or
    /// <c>0xG</c>.</para>
    /// <para>Everything after the prefix is taken before any of it is judged, so this is one
    /// message about one number rather than a number that stopped early and a name that began
    /// where it stopped.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor DigitOutsideBase = Error(
        "PC0018",
        "This digit is not in the base",
        "'{0}' is not a {1} digit, which is {2}.");

    /// <summary>
    /// <para>An <c>e</c> that opens an exponent with no digits behind it.</para>
    /// <para>Only reported where a name could not have been meant: an <c>e</c> followed by a
    /// letter is a name and is left alone, so this fires on <c>1e</c> and <c>1e+</c> where
    /// nothing else was being written.</para>
    /// </summary>
    /// <summary>
    /// An underscore in a number with no digit after it. A separator groups digits, so one at
    /// the end has nothing to group — and left alone it would read as the name <c>_</c> sitting
    /// against a number, which nothing means.
    /// </summary>
    /// <summary>
    /// <para>A name written against a number, with nothing between them.</para>
    /// <para>Nothing in the language puts two values side by side, so this is never a number
    /// and then a name — it is one thing that was written wrong. A name may not begin with a
    /// digit, which is the rule this enforces from the number's side.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor NameAgainstNumber = Error(
        "PC0021",
        "A name cannot begin with a digit",
        "'{0}' is written against the number before it. A name begins with a letter or an "
        + "underscore, and nothing in the language puts two values side by side.");

    /// <summary>
    /// <para><c>let</c> where a field is being declared.</para>
    /// <para>Reported here rather than left to the type parser, which would say only that
    /// <c>let</c> is not a type and then fail three more times on the rest of the line.</para>
    /// <para>The rule is about distance rather than about what the compiler can work out. A
    /// local is read where it is written, so its initializer is on the same screen and a
    /// reader infers the type as easily as the compiler does. A field is read in every
    /// function of its type, and by callers who never see the initializer at all — which is
    /// the same reason <c>this.</c> is required on one and not the other.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor LetIsForLocals = Error(
        "PC0119",
        "'let' declares a local, not a field",
        "'let' works inside a function, where the value it holds is written beside it. A "
        + "field is read far from here, so it says its type: '{0} {1} = ...'.");

    // ---- Ignoring a diagnostic, PC0022 to PC0025 ------------------------------------------
    //
    // Nothing here is an error. A writer reaches for 'ignore' to make the compiler quieter, so
    // a mistake in the line itself must never stop a build — a feature that turns a typo into
    // a fatal is worse than no feature.

    /// <summary>
    /// An identifier no diagnostic carries. Almost always a typo, since the alternative is a
    /// reader having invented one.
    /// </summary>
    public static readonly DiagnosticDescriptor IgnoreNamesNoDiagnostic = Warning(
        "PC0022",
        "This 'ignore' names no diagnostic",
        "'{0}' is not something this compiler reports, so this line silences nothing. Check "
        + "the identifier against the one in the message.");

    /// <summary>
    /// <para>An identifier that stops compilation.</para>
    /// <para>This is the one of the four that most needs to exist. Without it a writer
    /// silences an error, meets the error anyway, and concludes the mechanism is broken.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor IgnoreCannotSilenceAnError = Warning(
        "PC0023",
        "That diagnostic cannot be ignored",
        "'{0}' stops compilation, and only a warning or an opinion can be ignored. This line "
        + "cannot do anything; what it names has to be fixed.");

    /// <summary>
    /// <para>An identifier nothing within reach reports, so the line is dead weight.</para>
    /// <para>Only the by-identifier form draws this. Naming one asserts that a particular
    /// thing is there, and a wrong assertion is worth hearing; <c>ignore opinion</c> is a
    /// standing preference that claims nothing and stays silent with nothing to silence.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor IgnoreSilencedNothing = Opinion(
        "PC0024",
        "This 'ignore' silences nothing",
        "Nothing it reaches reports '{0}', so this line has no effect. Remove it.");

    /// <summary>
    /// <para>A word in a project file's <c>ignore</c> line that names neither.</para>
    /// <para>A comment is prose first, so <c>#&#160;ignore whatever</c> is a remark and draws
    /// nothing. A project file has no prose, so the same words there are a mistake.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor IgnoreNamesNeither = Warning(
        "PC0025",
        "This 'ignore' names neither a severity nor a diagnostic",
        "'{0}' is not 'warning', not 'opinion', and not an identifier such as 'PC0340'.");

    public static readonly DiagnosticDescriptor SeparatorNeedsDigits = Error(
        "PC0020",
        "This separator has no digits after it",
        "An '_' in a number separates digits, so more have to follow it — '1_000' is a "
        + "thousand.");

    public static readonly DiagnosticDescriptor ExponentNeedsDigits = Error(
        "PC0019",
        "This exponent has no digits",
        "An 'e' says how many places to move the point, so it needs digits after it — '1e3' "
        + "is 1000.0.");

    /// <summary>
    /// <para>More quotes in a row than opened the block string.</para>
    /// <para>The last of them close it and the rest are held, which is almost always what was
    /// meant — <c>"""He said "hi""""</c> is <c>He said "hi"</c>. A warning rather than an error
    /// because the reading is settled and is the one a reader would predict; it is worth saying
    /// because a run of quotes against the closer is the one place the rule is not obvious.
    /// </para>
    /// </summary>
    public static readonly DiagnosticDescriptor BlockStringRunIsTooLong = Warning(
        "PC0015",
        "More quotes in a row than close the block",
        "{0} quotes in a row, where {1} close the block string. The last {1} end it and the "
        + "rest are held. Open and close it with '{2}' to hold all {0}.");

    /// <summary>
    /// <para>A run of quotes that all but closed a block string, in one that never closes.</para>
    /// <para>A run shorter than the delimiter is text, which is the rule that lets a block hold
    /// quotes. So a closer written a quote short does not fail where it stands: the block runs
    /// on, swallowing the rest of the file, and the only complaint is "unterminated" at an
    /// opener pages earlier. Naming the run instead puts the message where the edit goes.</para>
    /// <para>Reported only where the block never closes. Where it does, a shorter run inside is
    /// exactly what it looks like and saying anything would be noise.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor BlockStringDelimitersDiffer = Error(
        "PC0016",
        "Block string delimiters differ in length",
        "{0} quotes do not close a block string opened with {1}, so this is text and the block "
        + "runs on. Open it with '{2}', or close it with '{3}'.");

    public static readonly DiagnosticDescriptor EmptyFormatSpecifier = Error(
        "PC0014",
        "Nothing to format by",
        "A ':' in an interpolation is followed by how to format the value, as in "
        + "'{{total:F2}}'. Leave it out to format the value the ordinary way.");

    /// <summary>
    /// <para><c>Console.WriteLine("")</c>, where the empty string does nothing.</para>
    /// <para>A warning rather than an error, because it prints exactly what was meant. It is
    /// worth saying because the argument reads as though it were doing the work, when the
    /// newline comes from <c>WriteLine</c> itself and the string is only standing there.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor EmptyLineNeedsNoArgument = Opinion(
        "PC0340",
        "This empty string does nothing",
        "'WriteLine' ends the line by itself. Write 'Console.WriteLine()'.");

    /// <summary>
    /// A hole that says how to write its value, held by something that has no say in how it is
    /// written. Only the measured and the dated answer <c>Format</c>.
    /// </summary>
    public static readonly DiagnosticDescriptor NoFormatForThisType = Error(
        "PC0341",
        "This cannot be formatted",
        "{0} has no 'Format', so ':{1}' says nothing. Leave the ':' out to write it the "
        + "ordinary way.");

    // ---- Syntax, PC0100 to PC0199 -----------------------------------------------------

    public static readonly DiagnosticDescriptor UnexpectedToken = Error(
        "PC0100",
        "Unexpected token",
        "Expected {0}, but found {1}.");

    public static readonly DiagnosticDescriptor ExpectedExpression = Error(
        "PC0101",
        "Expected an expression",
        "Expected an expression, but found {0}.");

    public static readonly DiagnosticDescriptor ExpectedType = Error(
        "PC0102",
        "Expected a type",
        "Expected a type, but found {0}.");

    public static readonly DiagnosticDescriptor ExpectedIdentifier = Error(
        "PC0103",
        "Expected a name",
        "Expected a name, but found {0}.");

    /// <summary>
    /// <para>A reserved word written where a name was wanted.</para>
    /// <para>Its own message rather than the general one above, because the general one names
    /// the symptom and leaves the reader to guess. Several reserved words — <c>end</c>,
    /// <c>base</c>, <c>to</c>, <c>each</c>, <c>step</c> — are ordinary things to call a
    /// variable, so this is met by anyone writing real code, and the fix belongs in the
    /// message.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor ReservedWordAsName = Error(
        "PC0114",
        "This word is reserved",
        "'{0}' is a reserved word, so it cannot be a name on its own. Write '@{0}' to use it "
        + "as one.");

    /// <summary>
    /// <para>A lambda parameter written with a type the surrounding code already supplies.</para>
    /// <para>The same argument as the range loop above: the program says one thing, and says
    /// it twice. A declared type, a set's element type, the parameter being passed to, and the
    /// result being yielded each settle the whole list, so a type written under one of them
    /// adds nothing that was not already fixed.</para>
    /// <para>This leaves exactly one place a lambda writes its own types — a <c>let</c>, where
    /// nothing on the left says anything, and where leaving them out instead reports
    /// <c>PC0336</c>. The two rules meet with no gap and no overlap: a lambda writes its types
    /// where it must, and nowhere else.</para>
    /// <para>A warning rather than an error, since the type written is the one that was going
    /// to be used either way and nothing about the program is in doubt.</para>
    /// </summary>

    public static readonly DiagnosticDescriptor ParameterTypeAlreadyKnown = Opinion(
        "PC0115",
        "This parameter's type is already known",
        "The surrounding code already says what '{0}' holds, so writing its type says it "
        + "twice. Leave the type out.");

    /// <summary>
    /// <para><c>Function(string)</c>, written where a function type was meant.</para>
    /// <para>Reached by meeting <c>Function</c> as the root every function type descends from
    /// and taking it for the way one is spelled. The two really are different words: the root
    /// holds a function without saying what shape it is, and the lowercase form says the
    /// shape.</para>
    /// <para>Its own message rather than the wreckage of reading it as a call, which is what
    /// it looks like to a parser and which reports on everything except the mistake.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor FunctionTypeIsLowercase = Error(
        "PC0116",
        "A function's type is written with 'delegate'",
        "'Function' is the type every function descends from and takes no parentheses. For a "
        + "function of a particular shape write 'delegate(...)', with a result before it if it "
        + "has one, as in 'integer delegate(string)'.");

    /// <summary>
    /// <para><c>function</c> written where a type belongs.</para>
    /// <para>The mistake anyone will make who learned the language before <c>delegate</c>
    /// existed, or who reasons that the word declaring a function ought to describe one. It is
    /// read as the type that was meant, so the rest of the declaration is checked normally and
    /// this is said once rather than as the wreckage of parsing a declaration that never
    /// arrives.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor FunctionTypeIsDelegate = Error(
        "PC0117",
        "A function's type is written with 'delegate'",
        "'function' declares a function or makes one on the spot. To write the type of one, "
        + "use 'delegate' — 'integer delegate(string)' takes a string and yields an integer.");

    /// <summary>
    /// <para>A word after <c>bitwise</c> that is not <c>and</c> or <c>or</c>.</para>
    /// <para>Those two are the only ones that need qualifying, because they already mean
    /// something on their own. Everything else the language does to bits — <c>xor</c>,
    /// <c>shiftleft</c>, <c>shiftright</c> — is a word nothing else claims, so it stands
    /// alone.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor BitwiseNeedsAndOrOr = Error(
        "PC0118",
        "Only 'and' or 'or' may follow 'bitwise'",
        "'bitwise' says which of two operations follows, and {0} is neither. Write 'bitwise "
        + "and' or 'bitwise or' — 'xor', 'shiftleft' and 'shiftright' need no word before "
        + "them.");

    /// <summary>
    /// The diagnostic qualified <c>end</c> exists to produce. Naming both the closer written
    /// and the construct it fails to match is the whole point of requiring the qualifier.
    /// </summary>
    public static readonly DiagnosticDescriptor MismatchedEnd = Error(
        "PC0104",
        "Mismatched block closer",
        "Expected 'end {0}' to close the {0} beginning on line {1}, but found 'end {2}'.");

    public static readonly DiagnosticDescriptor UnterminatedConstruct = Error(
        "PC0105",
        "Unterminated construct",
        "The {0} beginning on line {1} is never closed; expected 'end {0}'.");

    /// <summary>
    /// A construct's body has no opening token, so a condition ends at the first token that
    /// cannot continue an expression. Both '(' and '-' can continue one, so a statement
    /// starting with either would be swallowed by the condition before it.
    /// </summary>
    public static readonly DiagnosticDescriptor StatementCannotStartWith = Error(
        "PC0106",
        "Statement cannot start here",
        "A statement may not begin with '{0}'. Give the value a name first, "
        + "as in 'let value = ...;', and then use it.");

    public static readonly DiagnosticDescriptor ExpectedStatement = Error(
        "PC0107",
        "Expected a statement",
        "Expected a statement, but found {0}.");

    public static readonly DiagnosticDescriptor ExpectedDeclaration = Error(
        "PC0108",
        "Expected a declaration",
        "Expected a declaration, but found {0}.");

    public static readonly DiagnosticDescriptor AssignmentTargetNotAssignable = Error(
        "PC0109",
        "Cannot assign to this expression",
        "The left side of an assignment must be a name, an index, or a member access.");

    /// <summary>
    /// <para>A type may be declared at namespace level or inside a model, but not inside a
    /// function.</para>
    /// <para>Permitting it would mean a type could be introduced by a statement, which forces
    /// name resolution to interleave collecting types with binding bodies rather than doing
    /// each once. C# has no local classes either, for much the same reason.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor TypeInsideFunction = Error(
        "PC0110",
        "Type declared inside a function",
        "A {0} cannot be declared inside a function. Move it out to the enclosing model or "
        + "namespace.");

    /// <summary>
    /// <para>A range loop's counter is written with a type.</para>
    /// <para>Counting is done with integers, so there was never a choice to record — and a
    /// loop that says <c>integer</c> reads as though some other type were available. This
    /// exists because a reader arriving from C# or Java will write one out of habit, and
    /// "expected '='" would explain nothing.</para>
    /// <para>A warning rather than an error: the loop says exactly one thing, and it says it
    /// twice. Nothing about what the program means is in doubt, so the compiler corrects the
    /// spelling rather than refusing the program.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor RangeLoopTakesNoType = Opinion(
        "PC0111",
        "A range loop's counter has no written type",
        "A range loop counts with integers, so its counter takes no type. Remove the "
        + "'{0}'.");

    /// <summary>
    /// <para>An if expression that never says what it produces otherwise.</para>
    /// <para>Reported against the <c>if</c> rather than the token that turned up, because the
    /// token in hand is usually innocent: a semicolon written after a nested if expression ends
    /// the whole statement, leaving the outer one with nothing to complete it. Pointing at the
    /// semicolon would name the symptom, and pointing at the <c>if</c> names the conditional
    /// that is short.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor IfExpressionWithoutElse = Error(
        "PC0112",
        "An if expression has no 'else'",
        "This if expression has no 'else'. It produces a value, so it must say what the value "
        + "is when the condition is false.");

    public static readonly DiagnosticDescriptor TooManyErrors = Error(
        "PC0113",
        "Too many problems",
        "Too many problems; stopped after {0}. Fixing the ones above may account for the rest.");

    // ---- Name resolution, PC0200 to PC0299 --------------------------------------------

    public static readonly DiagnosticDescriptor NameNotFound = Error(
        "PC0200",
        "Name not found",
        "'{0}' is not defined here.");

    public static readonly DiagnosticDescriptor TypeNotFound = Error(
        "PC0201",
        "Type not found",
        "There is no type named '{0}'.");

    public static readonly DiagnosticDescriptor DuplicateDeclaration = Error(
        "PC0202",
        "Name already declared",
        "'{0}' is already declared in this scope.");

    /// <summary>
    /// <para>A type declared with the name of one the language provides.</para>
    /// <para>A warning rather than a refusal, because the library sits in a namespace like
    /// anything else and a nearer name wins over one merely in scope. Declaring a
    /// <c>Math</c> is a program saying it has its own, which is allowed and reversible:
    /// <c>Standard.Math</c> still reaches the other one.</para>
    /// <para>Worth saying all the same. Losing <c>Math.Sqrt</c> to a model of your own is
    /// almost never what was meant, and the reader who finds out by the call failing has a
    /// worse afternoon than the one told here.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor ShadowsStandardType = Warning(
        "PC0203",
        "This shadows a type the language provides",
        "'{0}' is also the name of a type in Standard, and a name declared here wins over one "
        + "in scope. Write 'Standard.{0}' to reach the other, or rename this.");

    /// <summary>
    /// <para>A program declaring the namespace the language owns.</para>
    /// <para>Namespaces merge, so this would let a program add types that then read as
    /// provided by Profi-C. <c>Standard.X</c> means "the language gives you this", and it can
    /// only keep meaning that if nothing else may write there.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor StandardNamespaceIsReserved = Error(
        "PC0229",
        "Standard belongs to the language",
        "'Standard' is the namespace the language's own types live in, and a program may not "
        + "add to it. Name this namespace something else.");

    /// <summary>
    /// <para><c>using Standard;</c>, which brings nothing.</para>
    /// <para>Standard is in scope in every file with nothing written, and at the same rank a
    /// using would put it at — so this line changes no name in the file, including when
    /// another using offers the same one.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor StandardNeedsNoUsing = Opinion(
        "PC0230",
        "Standard is already in scope",
        "Every file reaches Standard without saying so, so this line brings nothing.");

    /// <summary>
    /// <para>A <c>using</c> or <c>import</c> written inside a namespace.</para>
    /// <para>Both are answers about the file: which names it reaches, and which files are
    /// compiled with it. Neither narrows to part of one, so writing one inside a namespace
    /// would say something the language has no way to mean.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor DirectiveInsideNamespace = Error(
        "PC0231",
        "This belongs above any namespace",
        "'{0}' is a statement about the whole file, so it goes above every namespace in it.");

    /// <summary>
    /// <para>A namespace whose name repeats one it sits inside.</para>
    /// <para>Legal, and almost never meant: <c>Shapes.Shapes.Circle</c> is what a reader has to
    /// write afterwards, and it reads as a slip rather than as a distinction. A warning rather
    /// than an error because it is only a name, and a program that means it works.</para>
    /// </summary>
    /// <summary>
    /// <para>A type nothing can ever be, written where a value's type belongs.</para>
    /// <para>A shared model has no instances, and four of the language's own are names to
    /// reach members through rather than things to hold. Declaring one is accepted by every
    /// rule taken singly and produces a variable that can never be filled: nothing assigns to
    /// it, nothing reads from it, and the program runs.</para>
    /// <para>The mistake it usually is: <c>Fraction</c> for <c>fraction</c>, a capital letter
    /// away, where the alternative is an error about a fraction not fitting a Fraction.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor TypeHasNoValues = Error(
        "PC0233",
        "Nothing can be of this type",
        "'{0}' has no instances, so nothing can ever be held here. {1}");

    public static readonly DiagnosticDescriptor NamespaceRepeatsEnclosingName = Opinion(
        "PC0232",
        "This namespace repeats one around it",
        "'{0}' already sits inside a namespace of that name, so its types are reached as "
        + "'{0}.{0}.…'. Rename this one if that was not meant.");

    /// <summary>
    /// <para>The diagnostic that pays for requiring <c>this.</c> on every member access.</para>
    /// <para>A bare name reaches only locals and parameters, so a name that matches a field
    /// is a mistake with exactly one fix. Saying so is far better than "not defined here",
    /// which is technically true and useless.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor MemberNeedsReceiver = Error(
        "PC0204",
        "Member access needs a receiver",
        "'{0}' is a {1} of '{2}', so it must be written as '{3}.{0}'. "
        + "A bare name reaches only locals and parameters.");

    public static readonly DiagnosticDescriptor CannotAssignToConstant = Error(
        "PC0205",
        "Cannot assign to a constant",
        "'{0}' is a constant and cannot be assigned to.");

    public static readonly DiagnosticDescriptor CannotAssignToLoopVariable = Error(
        "PC0206",
        "Cannot assign to a loop variable",
        "'{0}' is a loop variable and is read-only inside the loop. Each iteration binds a "
        + "fresh one, so assigning to it would change nothing.");

    public static readonly DiagnosticDescriptor CircularInheritance = Error(
        "PC0207",
        "Circular inheritance",
        "'{0}' cannot extend itself, directly or through its ancestors.");

    public static readonly DiagnosticDescriptor CannotExtendSealed = Error(
        "PC0208",
        "Cannot extend a sealed model",
        "'{0}' is sealed and cannot be extended.");

    public static readonly DiagnosticDescriptor CannotExtendNonModel = Error(
        "PC0209",
        "Cannot extend this type",
        "'{0}' is a {1}, and only a model can be extended.");

    public static readonly DiagnosticDescriptor SealedAndAbstract = Error(
        "PC0210",
        "Sealed and abstract together",
        "'{0}' cannot be both sealed and abstract; it could then be neither extended nor "
        + "instantiated, so nothing could use it.");

    public static readonly DiagnosticDescriptor SharedModelMemberNotShared = Error(
        "PC0211",
        "Instance member on a shared model",
        "'{0}' cannot have instance members, because a shared model is never instantiated.");

    public static readonly DiagnosticDescriptor EntryPointMissing = Error(
        "PC0212",
        "No entry point",
        "A program needs a 'shared model Program' containing a function named 'Main'.");

    public static readonly DiagnosticDescriptor EntryPointNotSharedModel = Error(
        "PC0213",
        "Program must be a shared model",
        "'Program' must be declared 'shared model', since there is no such thing as an "
        + "instance of a running program.");

    public static readonly DiagnosticDescriptor ThisOutsideModel = Error(
        "PC0214",
        "'{0}' used outside a model",
        "'{0}' can only be used inside a model's instance member.");

    public static readonly DiagnosticDescriptor BaseWithoutParent = Error(
        "PC0215",
        "No parent to reach",
        "'base' needs a parent model, and '{0}' extends nothing.");

    /// <summary>
    /// <para>Separate from <see cref="CannotExtendNonModel"/>, which names the kind the base
    /// turned out to be. These are models as far as the compiler is concerned — that is only
    /// how their members resolve — so that message would read "'Console' is a model, and only
    /// a model can be extended", which explains nothing.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor CannotExtendBuiltInType = Error(
        "PC0216",
        "Cannot extend a built-in type",
        "'{0}' is provided by the language and has nothing to inherit. Of the built-in types "
        + "only 'Model' and the exceptions may follow 'extends'.");

    /// <summary>
    /// <para>Two types of one name.</para>
    /// <para>A compilation is one set of declarations however many files it spans, so this is
    /// the same mistake whether the two are written together or apart, and the message says
    /// where the other one is because that is the part a reader cannot see.</para>
    /// <para>Nothing merges them. There is no implicit partial type: a name appearing twice is
    /// far more often two people writing the same thing than one type deliberately split, and
    /// a language that silently joined them would make the first case invisible. Whether an
    /// explicit <c>partial</c> should exist is a question for a later version, and one that
    /// interoperating with .NET may force; it is left open here rather than settled by
    /// accident.</para>
    /// </summary>
    /// <summary>
    /// <para><c>Main</c> declares a result that is not an integer.</para>
    /// <para>An integer is what a program hands back to whatever ran it, and is the only kind
    /// of result an entry point has anywhere to put. A result of any other type would be
    /// computed and then dropped, which is worse than being refused: the program would look
    /// like it reported something.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor EntryPointResultNotInteger = Error(
        "PC0218",
        "Main declares no result or an integer",
        "'Main' must declare no result, or an integer, which becomes the program's exit code.");

    /// <summary>
    /// <para>Two visibility words on one declaration.</para>
    /// <para>Each names a different reach, so writing two says two different things about the
    /// same declaration and neither can be the one that was meant.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor ConflictingVisibility = Error(
        "PC0219",
        "Two visibilities on one declaration",
        "'{0}' is written {1}, and one declaration has one visibility. Keep the word that "
        + "says how far this should reach.");

    /// <summary>
    /// <para><c>protected</c> on a type.</para>
    /// <para>Protected means "and anything extending the type that declares it", which is a
    /// sentence about a member. A type has no declaring type to extend, so the word has
    /// nothing to name. Its reach is the project or the world: <c>internal</c> or
    /// <c>public</c>.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor TypeCannotBeProtected = Error(
        "PC0220",
        "A type cannot be protected",
        "'protected' says a member is reachable from anything extending the type that declares "
        + "it, and '{0}' is a type. Write 'internal' for its project, or 'public' for anywhere.");

    /// <summary>
    /// <para>A type named from outside the project that declares it.</para>
    /// <para>A type with no visibility written reaches its own project, which is the narrowest
    /// thing that could own it — the same rule that makes an unmarked member reach its own
    /// type. Only <c>public</c> widens it, and only <c>internal</c> can fail here.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor TypeIsNotVisible = Error(
        "PC0221",
        "Type belongs to another project",
        "'{0}' is internal to {1}, and this is {2}. Mark it 'public' if {2} is meant to use it.");

    /// <summary>
    /// <para><c>override</c> on a function that overrides nothing.</para>
    /// <para>The word is a claim about a base type, and a claim nothing checked is worth
    /// nothing: a renamed base function, or a parameter list that drifted by one type, leaves
    /// a function still marked <c>override</c> and now overriding nothing at all. It would
    /// compile, and every call through the base type would reach the old one.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor NothingToOverride = Error(
        "PC0222",
        "Nothing to override",
        "'{0}' is marked 'override', but {1} declares no '{0}' with these parameters. Check "
        + "the name and the parameter types, or drop 'override' if this is a new function.");

    /// <summary>
    /// <para><c>override</c> on a function whose base was never marked <c>virtual</c>.</para>
    /// <para>Overriding is something a base type offers rather than something a derived type
    /// takes. A base that never said <c>virtual</c> is entitled to assume its own version is
    /// the one that runs.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor BaseIsNotVirtual = Error(
        "PC0223",
        "Overridden function is not virtual",
        "'{0}' overrides a function in {1} that is not marked 'virtual', so {1} did not offer "
        + "it for overriding. Mark the one in {1} 'virtual'.");

    /// <summary>
    /// <para>A function redeclaring one from a base type without saying <c>override</c>.</para>
    /// <para>There is no way to write "hide the base one deliberately", so this is always
    /// either an override that forgot to say so or a name collision. Both want reporting, and
    /// the message names the word that resolves the first.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor HidesBaseFunction = Error(
        "PC0224",
        "This hides a function from the base",
        "{1} already declares '{0}' with these parameters. Write 'override' to replace it, or "
        + "rename this one.");

    /// <summary>
    /// <para>An override that yields something other than what it overrides.</para>
    /// <para>A caller holding the base type reads the result as the base declared it, so a
    /// different one would be a lie told through every such call.</para>
    /// </summary>
    /// <summary>
    /// <para>A name that more than one used namespace offers.</para>
    /// <para>Nothing breaks the tie. A namespace around the name is nearer than one merely
    /// used, and an enclosing namespace is nearer than the one outside it — but usings name no
    /// order between themselves, so choosing either would make what a program means depend on
    /// which line was written first.</para>
    /// <para>Reported where the name is read rather than at the usings, because two namespaces
    /// sharing a name is only a problem once something needs it.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor AmbiguousTypeName = Error(
        "PC0226",
        "This name is offered by more than one namespace",
        "'{0}' could mean {1}. Both are used here and neither is nearer, so write the one you "
        + "mean in full.");

    public static readonly DiagnosticDescriptor NamespaceNotFound = Error(
        "PC0227",
        "No such namespace",
        "No namespace named '{0}' is declared in this compilation.");

    /// <summary>
    /// A namespace used twice in one file. The second brings nothing the first did not, so it
    /// is a line that could be deleted without changing what the file means.
    /// </summary>
    public static readonly DiagnosticDescriptor NamespaceUsedTwice = Error(
        "PC0228",
        "This namespace is already used here",
        "'{0}' is already used in this file.");

    public static readonly DiagnosticDescriptor OverrideResultDiffers = Error(
        "PC0225",
        "Override yields a different result",
        "'{0}' yields {1}, and the one it overrides in {2} yields {3}. An override yields what "
        + "it overrides, since a caller holding a {2} reads the result as {2} declared it.");

    public static readonly DiagnosticDescriptor DuplicateTypeDeclaration = Error(
        "PC0217",
        "Type already declared",
        "'{0}' is already declared {1}. Two types cannot share a name, whether they are "
        + "written in one file or across several. Rename one of them.");

    /// <summary>
    /// <para>Several <c>Program</c>s and nothing saying which one begins.</para>
    /// <para>Namespaces make <c>Tools.Program</c> and <c>App.Program</c> two types rather than
    /// one name used twice, so both may sit in one compilation without colliding. The choice
    /// between them is the build's to make: an assembly holds one entry point in its metadata,
    /// and picking by the order the sources are listed would make a program's behavior depend
    /// on the order of its own file list.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor EntryPointAmbiguous = Error(
        "PC0234",
        "Which program starts?",
        "These sources declare more than one Program: {0}. Write 'entry {1}' in the project "
        + "file to say which one begins.");

    /// <summary>
    /// An <c>entry</c> naming something that is not among the compilation's programs — most
    /// often a misspelling, or a name that moved when its namespace did.
    /// </summary>
    public static readonly DiagnosticDescriptor EntryPointNotFound = Error(
        "PC0235",
        "No such program",
        "'{0}' is not a Program among these sources. {1}");

    /// <summary>
    /// An <c>entry</c> written where there is nothing to choose between. Harmless, and worth
    /// saying because it reads as though a choice were being made.
    /// </summary>
    public static readonly DiagnosticDescriptor EntryPointUnnecessary = Opinion(
        "PC0236",
        "This 'entry' decides nothing",
        "Only '{0}' declares a Program, so it begins whether or not this line is here.");

    /// <summary>
    /// <para>A bitwise operation asked of two booleans.</para>
    /// <para>The mistake a C# reader makes, since <c>^</c> there is exclusive or on booleans as
    /// well as on the bits of an integer. Here it is only the second, because <c>!=</c> already
    /// asks the first and the language keeps one spelling for one idea.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor BitsOnBooleans = Error(
        "PC0342",
        "This works on bits, not on booleans",
        "'{0}' works on the bits of a whole number. For two booleans, '!=' asks whether "
        + "exactly one of them holds.");

    /// <summary>
    /// <para>A shift by more places than an integer has, or by a negative number of them.</para>
    /// <para>C# folds the amount into range, so a shift of 64 there quietly means a shift of
    /// none. Nothing in the line suggests that, so it is refused instead — and the direction is
    /// already in the word, which is what leaves a negative amount with nothing to mean.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor ShiftOutsideTheWidth = Error(
        "PC0343",
        "This shift is outside the width of an integer",
        "An integer holds 64 bits, so a shift of {0} places moves past all of them. An amount "
        + "from 0 to 63 is what there is to move.");

    /// <summary>
    /// <para>A <c>catch</c> naming an exception no <c>catch</c> can take.</para>
    /// <para>Such a name is written so a reader can be told what stopped their program, not so a
    /// program can go on from it. Nothing in the line says which kind it is, so a clause naming
    /// one would sit there looking like a handler and never run.</para>
    /// <para>A warning rather than an error, for the reason <c>PC0327</c> is one: the program
    /// says exactly one thing and nothing about its meaning is in doubt. The clause simply does
    /// not run, and the rest of the try works as written.</para>
    /// </summary>
    /// <summary>
    /// <para>A loop written without the <c>loop</c> that opens it.</para>
    /// <para>The commonest thing a reader will type, from habit in another language or from
    /// an older Profi-C program. Left to the ordinary machinery it is a cascade — <c>for</c>
    /// begins no statement, so the counter, the bound and the body are each reported in turn —
    /// and none of the messages names the one thing that is wrong.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor LoopNeedsItsOpener = Error(
        "PC0120",
        "A loop begins with 'loop'",
        "Every loop opens with 'loop', so this is written 'loop {0}'. The word after 'loop' "
        + "says which kind: 'for' counts, 'each' walks a set, 'while' asks before each turn, "
        + "and nothing at all asks after.");

    public static readonly DiagnosticDescriptor ExceptionCannotBeCaught = Warning(
        "PC0344",
        "This exception cannot be caught",
        "Nothing catches {0}. It has a name so that a reader can be told what stopped the "
        + "program, and it stops the program because there is nothing useful to do about it. "
        + "This clause would never run.");

    /// <summary>
    /// <para>A local, parameter, or loop binding reusing a name already visible from a scope
    /// around it.</para>
    /// <para>Inside a function body a bare name means exactly one thing throughout, and this is
    /// the rule that makes that true. It is what lets a lambda or a nested function reach an
    /// enclosing local unmarked: there is nothing the name could be confused with, so a marking
    /// would carry no information. <c>this.</c> earns its keep on the other side of the
    /// boundary, where a field may be declared in an ancestor model or another file.</para>
    /// <para>An error rather than a warning. The inner name would win and the program would run,
    /// but the reader has to hold two meanings for one word, and the rule that makes capture
    /// legible is only worth having if it holds everywhere.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor NameShadowsEnclosing = Error(
        "PC0237",
        "This name is already in use here",
        "'{0}' is already the name of something in an enclosing scope, so this one would hide "
        + "it. Give it a name of its own.");

    /// <summary>
    /// A function declared without a body where nothing is going to supply one. Only an
    /// <c>abstract</c> function may be left open, since only it obliges a descendant to fill it.
    /// </summary>
    public static readonly DiagnosticDescriptor BodyIsMissing = Error(
        "PC0238",
        "This function needs a body",
        "'{0}' ends at the semicolon, so nothing says what it does. Give it a body, or mark it "
        + "'abstract' to leave it to whatever extends this model.");

    /// <summary>
    /// The other direction. An <c>abstract</c> function is the one every descendant must write,
    /// so a body on it is one nothing would ever run.
    /// </summary>
    public static readonly DiagnosticDescriptor AbstractHasABody = Error(
        "PC0239",
        "An abstract function has no body",
        "'{0}' is abstract, so every model extending this one writes what it does and this "
        + "body would never run. End the declaration at ';', or drop the 'abstract'.");

    /// <summary>
    /// <para>An <c>abstract</c> function on a model that can be constructed.</para>
    /// <para>An abstract function is a promise that a descendant fills it, and an instance of
    /// the model itself would be one on which the promise was never kept.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor AbstractInConcreteModel = Error(
        "PC0240",
        "Only an abstract model may leave a function open",
        "'{0}' is abstract, but '{1}' can be constructed — so an instance of it would reach a "
        + "function nothing ever wrote. Mark '{1}' abstract too.");

    /// <summary>
    /// <para>A model that can be constructed and has inherited a function nobody wrote.</para>
    /// <para>Reported on the model rather than at each function, since one message naming all
    /// of them is what a reader acts on. This is the whole point of the word: the obligation
    /// travels down until a model that can be built discharges it.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor AbstractIsNotOverridden = Error(
        "PC0241",
        "An inherited function is still open",
        "'{0}' can be constructed, so every function left open above it has to be written "
        + "here. Still open: {1}. Write an override for each, or mark '{0}' abstract and leave "
        + "them to whatever extends it.");

    /// <summary>
    /// <c>virtual</c> beside <c>abstract</c>. A warning: abstract already offers the function
    /// for overriding, so the second word is true and adds nothing.
    /// </summary>
    public static readonly DiagnosticDescriptor AbstractIsAlreadyVirtual = Opinion(
        "PC0242",
        "An abstract function is already virtual",
        "'{0}' is abstract, which is what offers it for overriding, so 'virtual' says nothing "
        + "further. Remove it.");

    /// <summary>
    /// <para>Inserting into or removing from the very sequence a <c>for each</c> is walking.
    /// </para>
    /// <para>A <c>for each</c> reads its sequence's length once, when the loop begins, so a
    /// change made inside it does not lengthen the walk — and one that shortens the sequence
    /// leaves the loop reaching past the end. Neither is what the line looks like it does.</para>
    /// <para>Refused rather than given a rule, because there is no reading of it worth
    /// teaching. A range loop is the construct for a count that moves, and building a second
    /// set is the answer when the walk itself must not.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor SequenceChangedWhileWalked = Error(
        "PC0243",
        "This changes the sequence being walked",
        "'{0}' is the sequence this 'for each' is walking, and '{1}' changes it. Its length was "
        + "read when the loop began, so the walk would not follow. Collect what you want into "
        + "another set and change that, or count with a range loop instead.");

    // ---- Documentation, PC0244 to PC0247 --------------------------------------------------
    //
    // A doc that has gone stale never stops a build. The first three are warnings rather than
    // opinions all the same: each leaves a reader believing they documented something they did
    // not, and a belief about their own work that is false is worth more than a remark about
    // how it is written. A missing doc is not reported at all — requiring one everywhere is
    // how documentation stops being a help and becomes a tax.

    /// <summary>
    /// A documentation comment above something that cannot carry one. It is inert rather than
    /// merely redundant: the reader wrote documentation and there is nothing holding it.
    /// </summary>
    public static readonly DiagnosticDescriptor DocumentsNothing = Warning(
        "PC0244",
        "This documentation has nothing to document",
        "An '@summary:' comment goes directly above a namespace, a type, a member of one, or "
        + "an enumeration's member, with nothing in between. Nothing like that follows this "
        + "one, so nothing will show it. A heading about the whole file is an ordinary "
        + "comment.");

    /// <summary>
    /// A documented parameter the function does not take, which is where documentation and
    /// code most often come apart: a parameter is renamed and the line describing it is not.
    /// </summary>
    public static readonly DiagnosticDescriptor DocumentsUnknownParameter = Warning(
        "PC0245",
        "This documents a parameter that is not there",
        "'{0}' is documented, but '{1}' takes {2}. Rename the line or take it out.");

    /// <summary>Describing what is given back, where nothing is.</summary>
    public static readonly DiagnosticDescriptor DocumentsNothingYielded = Warning(
        "PC0246",
        "This describes a value that is never given back",
        "'{0}' yields nothing, so there is no value for '@yields:' to describe.");

    /// <summary>
    /// <para>The same label twice, whichever it is: a parameter, the summary, or one of the
    /// other fixed parts.</para>
    /// <para>An opinion rather than a warning, since the reader documented what they meant to
    /// and the second line is the one saying nothing new. A summary running to several
    /// paragraphs needs no second 'summary:' — a blank line inside one continues it.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor DocumentsTheSameThingTwice = Opinion(
        "PC0247",
        "This is documented twice",
        "'{0}' already has a line above this one, and the first is the one that shows. For a "
        + "second paragraph, leave a blank line and keep writing.");

    // ---- Type checking, PC0300 to PC0399 -----------------------------------------------

    public static readonly DiagnosticDescriptor CannotConvert = Error(
        "PC0300",
        "Cannot convert",
        "Cannot use {0} where {1} is expected.");

    /// <summary>
    /// A conversion that exists but must be written. The message names the call, since
    /// knowing one is possible is not much use without knowing how to ask for it.
    /// </summary>
    public static readonly DiagnosticDescriptor ConversionMustBeExplicit = Error(
        "PC0301",
        "Conversion must be written out",
        "{0} does not become {1} on its own, because the result would surprise you. "
        + "Write '{2}' to ask for it.");

    /// <summary>
    /// The caller supplies the whole subject, article included, because the subjects do not
    /// share an article or a noun: "An if condition", "A while condition", "An operand of
    /// 'and' or 'or'".
    /// </summary>
    public static readonly DiagnosticDescriptor ConditionMustBeBoolean = Error(
        "PC0302",
        "Condition must be a boolean",
        "{0} must be a boolean, and this is {1}.");

    public static readonly DiagnosticDescriptor OperatorNotDefined = Error(
        "PC0303",
        "Operator not defined for these types",
        "'{0}' is not defined for {1} and {2}.");

    public static readonly DiagnosticDescriptor UnaryOperatorNotDefined = Error(
        "PC0304",
        "Operator not defined for this type",
        "'{0}' is not defined for {1}.");

    /// <summary>
    /// Branch types must agree exactly rather than finding a common type, so that the type of
    /// a conditional is always the type its branches are written as.
    /// </summary>
    public static readonly DiagnosticDescriptor ConditionalBranchesDiffer = Error(
        "PC0305",
        "Branches of an if expression have different types",
        "The branches of an if expression must have the same type, and these are "
        + "{0} and {1}.");

    public static readonly DiagnosticDescriptor MemberNotFound = Error(
        "PC0306",
        "Member not found",
        "{0} has no member named '{1}'.");

    public static readonly DiagnosticDescriptor NotCallable = Error(
        "PC0307",
        "Not something that can be called",
        "{0} cannot be called.");

    /// <summary>
    /// Phrased so the verb agrees with the function rather than with either number, which is
    /// what lets one wording serve every count. See <see cref="Wording.Count"/>.
    /// </summary>
    public static readonly DiagnosticDescriptor WrongArgumentCount = Error(
        "PC0308",
        "Wrong number of arguments",
        "'{0}' takes {1}, but was given {2}.");

    public static readonly DiagnosticDescriptor NoMatchingOverload = Error(
        "PC0309",
        "No overload matches",
        "No version of '{0}' accepts these arguments.");

    public static readonly DiagnosticDescriptor AmbiguousOverload = Error(
        "PC0310",
        "Ambiguous call",
        "Several versions of '{0}' match these arguments equally well.");

    public static readonly DiagnosticDescriptor NotIndexable = Error(
        "PC0311",
        "Not something that can be indexed",
        "{0} cannot be indexed. Only a set and a string can.");

    public static readonly DiagnosticDescriptor IndexMustBeInteger = Error(
        "PC0312",
        "Index must be an integer",
        "An index must be an integer, and this is {0}.");

    public static readonly DiagnosticDescriptor CannotInferEmptyCollection = Error(
        "PC0313",
        "Cannot infer the type of an empty set",
        "The type of an empty set cannot be worked out from the set alone. "
        + "Write the type, as in 'integer[] values = {};'.");

    /// <summary>
    /// <para>Reported only where nothing says what the set should hold.</para>
    /// <para>Given a type to measure against, elements of several kinds are fine and each is
    /// converted on its own — a set of shapes may be written as the rectangles and circles it
    /// holds. It is inference that needs one type, so the fix is to say which.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor CollectionElementsDiffer = Error(
        "PC0314",
        "Set elements have different types",
        "With no type to measure them against, every element of a set must have the same "
        + "type; found {0} and {1}. Write the type the set should have, as in "
        + "'Shape[] values = {{...}};'.");

    public static readonly DiagnosticDescriptor NotSwitchable = Error(
        "PC0315",
        "Cannot switch on this type",
        "A switch cannot examine {0}. Equality on it is unreliable, so a case label could "
        + "never be trusted to match.");

    public static readonly DiagnosticDescriptor ForEachNeedsSequence = Error(
        "PC0316",
        "Cannot iterate this type",
        "'for each' needs a set or a string, and this is {0}.");

    public static readonly DiagnosticDescriptor RangeLoopNeedsInteger = Error(
        "PC0317",
        "Range loop needs integers",
        "A range loop counts with integers, and this is {0}.");

    public static readonly DiagnosticDescriptor YieldValueInVoidFunction = Error(
        "PC0318",
        "This function yields nothing",
        "'{0}' declares no result, so 'yield' cannot carry a value.");

    public static readonly DiagnosticDescriptor YieldMissingValue = Error(
        "PC0319",
        "Missing value to yield",
        "'{0}' yields a {1}, so 'yield' needs a value.");

    public static readonly DiagnosticDescriptor ConstantNeedsInitializer = Error(
        "PC0320",
        "Constant needs a value",
        "'{0}' is a constant, so it must be given a value where it is declared.");

    public static readonly DiagnosticDescriptor ConstantNotFoldable = Error(
        "PC0321",
        "Constant value must be known while compiling",
        "The value of '{0}' must be worked out while compiling, so it can only be built from "
        + "literals and other constants.");

    /// <summary>
    /// A constant is only permitted where an immutable binding really means an unchanging
    /// value, which rules out the types that could change behind it.
    /// </summary>
    public static readonly DiagnosticDescriptor ConstantTypeNotAllowed = Error(
        "PC0322",
        "This type cannot be constant",
        "{0} cannot be declared constant, because the binding could stay fixed while what "
        + "it names changed. This may widen in a later version.");

    public static readonly DiagnosticDescriptor InferredDeclarationNeedsInitializer = Error(
        "PC0323",
        "Nothing to infer from",
        "'let' works out the type from the value, so it needs one.");

    public static readonly DiagnosticDescriptor DivisionByZero = Error(
        "PC0324",
        "Division by zero",
        "This divides by zero.");

    public static readonly DiagnosticDescriptor CaseLabelNotConstant = Error(
        "PC0325",
        "Case label must be a constant",
        "A case label must be known while compiling.");

    public static readonly DiagnosticDescriptor DuplicateCaseLabel = Error(
        "PC0326",
        "Duplicate case label",
        "The value {0} is already handled by another case.");

    /// <summary>
    /// <para>A type test whose answer follows from the types alone.</para>
    /// <para>A warning rather than an error, for the reason a redundant loop counter is: the
    /// program says exactly one thing and nothing about its meaning is in doubt. The answer is
    /// settled while compiling and the test does not run.</para>
    /// <para>Both directions are worth saying. A test that can never pass is usually a mistake
    /// about which types are related; one that always passes is usually a guard someone thinks
    /// is doing something.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor TypeTestIsAlwaysFalse = Warning(
        "PC0327",
        "This test is always false",
        "{0} can never be {1}, so this is always false.");

    public static readonly DiagnosticDescriptor TypeTestIsAlwaysTrue = Warning(
        "PC0334",
        "This test is always true",
        "{0} is always {1}, so this is always true.");

    /// <summary>
    /// <para>A cast naming a structure or a primitive.</para>
    /// <para>An error rather than a warning, because unlike the two above there is no answer
    /// to settle on: value types have no inheritance, so asking whether one value is some
    /// other value type is not a question about identity at all. An enumeration is exempt,
    /// since an integer names one of its members.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor CannotCastToValueType = Error(
        "PC0335",
        "Cannot cast to a value type",
        "{0} is a value type, and value types have no inheritance for a cast to follow.");

    /// <summary>
    /// <para>A lambda parameter written as a bare name where nothing says what it holds.</para>
    /// <para>Leaving a type out asks the surrounding code for it, so this is reported where
    /// there is no surrounding code to ask: a <c>let</c>, an argument to a name that could not
    /// be resolved, or a lambda standing on its own. The fix is to write the type, which is
    /// also the only form that reads on its own terms.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor ParameterTypeNotInferable = Error(
        "PC0336",
        "Parameter needs a type",
        "Nothing here says what '{0}' holds. Write its type, as in '(integer {0})'.");

    /// <summary>
    /// <para>A switch over an enumeration that handles some of its members and has no
    /// default.</para>
    /// <para>This is what makes adding a member to an enumeration safe. Every switch that has
    /// to change says so, at the place it has to change, rather than the new member falling
    /// quietly through every one of them.</para>
    /// <para>Silent where a default is written, because a default handles the rest and saying
    /// so is the whole point of writing one. A warning rather than an error, since the switch
    /// runs and does something defensible either way.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor SwitchNotExhaustive = Warning(
        "PC0337",
        "Not every member is handled",
        "This switch does not handle every {0}: {1} {2} no case. Add one for each, or a "
        + "'default' for everything else.");

    public static readonly DiagnosticDescriptor CannotInstantiate = Error(
        "PC0328",
        "Cannot be instantiated",
        "'{0}' is {1} and cannot be instantiated.");

    public static readonly DiagnosticDescriptor OptionalMustBeUnwrapped = Error(
        "PC0329",
        "Optional must be unwrapped first",
        "This is {0}, which may be empty. Use 'HasValue()' to check, 'Or(...)' for a "
        + "fallback, or 'Value()' to insist.");

    /// <summary>
    /// <para>A function the language provides, named without being called.</para>
    /// <para>A reader coming from a language with properties writes <c>xs.Count</c>, means
    /// the number, and would otherwise get something that is not one.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor BuiltInMemberNeedsCall = Error(
        "PC0330",
        "This member is a function",
        "'{0}' is a function, so it has to be called: write '{0}()'.");

    /// <summary>
    /// <para>A value the language provides, written as though it were a function.</para>
    /// <para>The mirror of the diagnostic above, and it exists for the same reason: a reader
    /// should never have to remember which of the two a name is, because whichever they guess
    /// the compiler says so plainly.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor BuiltInMemberIsNotCalled = Error(
        "PC0338",
        "This member is a value",
        "'{0}' is a value rather than a function, so it is written without '()'.");

    /// <summary>
    /// <para>A member reached from further away than it is declared to reach.</para>
    /// <para>One message for all four levels, because what a reader needs is the same each
    /// time: how far the member reaches, and the word that would widen it. Naming the
    /// visibility it has rather than describing it keeps the message true whichever level was
    /// written, including the one written by writing nothing.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor MemberIsNotVisible = Error(
        "PC0339",
        "Member cannot be reached from here",
        "'{0}' is {1} in {2}, so it cannot be reached here. {3}");

    /// <summary>
    /// <para>An instance member reached through the name of its type.</para>
    /// <para>There is no instance for it to belong to, so there is nothing to read. Marking
    /// the member <c>shared</c> is usually what was meant — a <c>constant</c> is not shared on
    /// its own, exactly as it is written <c>shared constant</c> when it should be.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor MemberNeedsInstance = Error(
        "PC0331",
        "Member needs an instance",
        "'{0}' belongs to each {1} rather than to the {1} type, so it cannot be reached "
        + "through the name '{1}'. Mark it 'shared', or read it from a value.");

    /// <summary>
    /// <para>The result of a function that yields nothing, used where a value is wanted.</para>
    /// <para>Its own message because what went wrong is that the call had no result to give,
    /// which is a plainer thing to say than naming the type of the absence.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor ValueExpected = Error(
        "PC0332",
        "This produces no value",
        "This produces no value, so there is nothing to use here.");

    /// <summary>
    /// <para>A negative exponent on an integer base, caught while compiling.</para>
    /// <para>Two to the minus one is one half, which is not an integer. The same expression
    /// written on fractions is exact and allowed, which is what the message points at.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor NegativeIntegerExponent = Error(
        "PC0333",
        "Negative exponent on an integer",
        "An integer raised to the power {0} is not a whole number. Raise a fraction instead, "
        + "as in '(1|2) ^ {0}', or use 'Math.Pow(...)' for a real result.");

    // ---- Definite assignment and flow, PC0400 to PC0499 --------------------------------

    /// <summary>
    /// <para>The rule that lets the language do without null.</para>
    /// <para>A variable that has not been given a value cannot be read, so there is no
    /// "unset" state for a program to stumble into and no null to represent it.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor UseBeforeAssignment = Error(
        "PC0400",
        "Used before it is given a value",
        "'{0}' is used here before it has been given a value.");

    public static readonly DiagnosticDescriptor UseBeforeAssignmentOnSomePath = Error(
        "PC0401",
        "Not given a value on every path",
        "'{0}' is not given a value on every path that reaches this point.");

    /// <summary>
    /// A field has no default, so a constructor must supply one. An optional field escapes
    /// this, which is exactly what makes a self-referential model constructible: a Node whose
    /// 'next' is optional has a base case, where one that had to be assigned would not.
    /// </summary>
    public static readonly DiagnosticDescriptor FieldNotAssignedInConstructor = Error(
        "PC0402",
        "Field not given a value",
        "'{0}' must be given a value before this constructor ends. Give it one here, or an "
        + "initializer where it is declared, or make it optional.");

    public static readonly DiagnosticDescriptor UnreachableCode = Warning(
        "PC0403",
        "Unreachable code",
        "This can never be reached.");

    /// <summary>
    /// <para>A function that declares a result can reach its end without producing one.</para>
    /// <para>The same forward walk that proves a variable holds a value proves this: if the end
    /// of the body is still reachable, some path through it yields nothing. What such a call
    /// would give back is the question with no good answer — every language that has allowed it
    /// has had to invent a value nobody asked for.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor NotEveryPathYields = Error(
        "PC0404",
        "Not every path yields a value",
        "'{0}' yields {1}, but it can reach its end without yielding one.");

    /// <summary>
    /// <para>A function declared among statements, reached before a name it uses has a value.
    /// </para>
    /// <para>Such a function is in scope for the whole run it is declared in, so it may be
    /// called from above its own line — but the locals it names come into being in order, and a
    /// call written above one of them would read a place holding nothing yet.</para>
    /// <para>This is what buys the freedom to write the declarations in whatever order reads
    /// best. Without it the mistake is silent: the call answers with an empty value and the
    /// program carries on, and nothing in the line says which name was not ready.</para>
    /// </summary>
    /// <summary>
    /// <para>A <c>loop</c> with no condition that nothing inside it can leave.</para>
    /// <para>Every loop should be able to end. This one has no condition, no <c>break</c>, no
    /// <c>yield</c> and no <c>throw</c>, so nothing written in it will ever stop it — which is
    /// almost always a case that was meant to be handled and was left out.</para>
    /// <para>An opinion rather than an error, because a program that really does mean to run
    /// until it is stopped from outside is a program someone may legitimately write, and the
    /// language does not know which one it has. What it can do is say the shape is unusual, and
    /// name the three things that would end it.</para>
    /// <para>It does not suppress anything else. A function that yields a value and ends in
    /// such a loop still gets <c>PC0404</c>, because a function promising a result and holding
    /// a loop that cannot end is not a loop question, it is a broken promise.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor LoopCannotBeLeft = Opinion(
        "PC0406",
        "Nothing here can end this loop",
        "This loop has no condition, and nothing in it breaks, yields, or throws — so nothing "
        + "written here will stop it. Add a 'break' where it should end, or write "
        + "'loop while' or 'until' and say the condition.");

    public static readonly DiagnosticDescriptor CalledBeforeWhatItUsesIsReady = Error(
        "PC0405",
        "Called before a name it uses is ready",
        "'{0}' uses '{1}', which has not been given a value yet. Call it after '{1}' is set, "
        + "or move what it needs above this line.");

    // ---- Project files, PC0600 to PC0699 -----------------------------------------------

    public static readonly DiagnosticDescriptor ProjectFileNotFound = Error(
        "PC0600",
        "Project file not found",
        "There is no project file at '{0}'.");

    public static readonly DiagnosticDescriptor ProjectMissingHeader = Error(
        "PC0601",
        "Project has no header",
        "A project file opens with 'project' and a name.");

    public static readonly DiagnosticDescriptor ProjectMissingName = Error(
        "PC0602",
        "Project has no name",
        "'project' must be followed by a name.");

    public static readonly DiagnosticDescriptor ProjectNotClosed = Error(
        "PC0603",
        "Project is not closed",
        "This project is never closed. Add 'end project'.");

    public static readonly DiagnosticDescriptor ProjectUnknownEntry = Error(
        "PC0604",
        "Unrecognized project entry",
        "'{0}' is not something a project file says. A project names files with 'source' and "
        + "other projects with 'reference'.");

    public static readonly DiagnosticDescriptor ProjectSourceMissingPath = Error(
        "PC0605",
        "Source with no path",
        "'source' must be followed by a file or folder path.");

    public static readonly DiagnosticDescriptor ProjectSourceNotFound = Error(
        "PC0606",
        "Source not found",
        "There is no file or folder at '{0}'.");

    public static readonly DiagnosticDescriptor ProjectSourceWrongExtension = Error(
        "PC0607",
        "Source is not Profi-C",
        "'{0}' is not a .pc file, so a project cannot build it.");

    public static readonly DiagnosticDescriptor ProjectSourceListedTwice = Error(
        "PC0608",
        "Source listed more than once",
        "'{0}' is already part of this project.");

    public static readonly DiagnosticDescriptor ProjectFolderIsEmpty = Error(
        "PC0609",
        "Folder holds no source",
        "'{0}' holds no .pc files.");

    public static readonly DiagnosticDescriptor ProjectHasNoSources = Error(
        "PC0610",
        "Project builds nothing",
        "This project lists no source, so there is nothing to build.");

    public static readonly DiagnosticDescriptor ProjectEntryMissingName = Error(
        "PC0626",
        "Nothing named to start at",
        "'entry' says which Program begins, so a name must follow it, as in "
        + "'entry Tools.Program'.");

    /// <summary>
    /// One build makes one thing and starts in one place, so a second <c>entry</c> is either a
    /// leftover or a disagreement. Either way the project has to say which it means.
    /// </summary>
    public static readonly DiagnosticDescriptor ProjectEntryRepeated = Error(
        "PC0627",
        "More than one 'entry'",
        "A project starts in one place, so it names one 'entry'.");

    // ---- Imports, PC0611 to PC0619 -------------------------------------------------------

    public static readonly DiagnosticDescriptor ImportNotFound = Error(
        "PC0611",
        "Imported file not found",
        "There is no file at '{0}', which is looked for beside {1}.");

    public static readonly DiagnosticDescriptor ImportNotSource = Error(
        "PC0612",
        "Import is not Profi-C",
        "'{0}' is not a .pc file, so it cannot be compiled with this one.");

    /// <summary>
    /// <para>An import that names a path from the root of a disk.</para>
    /// <para>A warning rather than an error: the program is correct, and while it is being
    /// written the path is correct too. It stops being correct the moment the file is copied,
    /// shared, cloned, or built anywhere but here, and that is worth saying before then.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor ImportPathIsAbsolute = Warning(
        "PC0613",
        "Import names an absolute path",
        "'{0}' names a path from the root of a disk, so it resolves only on the machine it "
        + "was written on. A path relative to this file travels with it.");

    /// <summary>
    /// <para>An import reaching a file that reaches back, however many files apart.</para>
    /// <para>A warning rather than an error, because nothing about a circle is unbuildable: a
    /// compilation reads every file it gathers together, and reaching one twice adds nothing
    /// the first reach did not. What a circle costs is a reader, who has no file to open first
    /// — and a circle drawn across four files is one nobody meant to draw.</para>
    /// <para>The fix is usually to write less. Files beside one another are compiled together
    /// with no import between them, so a circle is always drawn across folders, and a project
    /// file names files across folders without one of them importing another.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor CircularImport = Warning(
        "PC0614",
        "Imports form a circle",
        "This import closes a circle: {0}. It builds — a compilation reads every file it "
        + "gathers together — but no file in the circle is the one to open first. Files beside "
        + "one another need no import between them, and a project file spans folders without "
        + "one file importing another.");

    // ---- Project references, PC0620 to PC0629 ---------------------------------------------

    public static readonly DiagnosticDescriptor ProjectReferenceMissingPath = Error(
        "PC0620",
        "Reference with no path",
        "'reference' must be followed by the path of a project file.");

    public static readonly DiagnosticDescriptor ProjectReferenceNotFound = Error(
        "PC0621",
        "Referenced project not found",
        "There is no project file at '{0}'.");

    public static readonly DiagnosticDescriptor ProjectReferenceIsNotAProject = Error(
        "PC0622",
        "Reference is not a project",
        "'{0}' is not a .pcp file. A project references projects; it names files with 'source'.");

    public static readonly DiagnosticDescriptor ProjectReferencedTwice = Error(
        "PC0623",
        "Project referenced more than once",
        "'{0}' is already referenced by this project.");

    /// <summary>
    /// <para>Projects referencing one another, however many projects apart.</para>
    /// <para>An error where the same shape between files is only a warning, and the difference
    /// is what a project is. Files in a circle still all belong to one compilation, so nothing
    /// about reading them together is in question. A reference crosses from one build to
    /// another, and a build that has to exist before itself cannot be produced — which stays
    /// true, and becomes literal, the moment a project is something separately built.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor CircularProjectReference = Error(
        "PC0624",
        "Projects reference each other",
        "This reference closes a circle: {0}. Neither project can be built before the other, "
        + "so neither can be built. Code both need belongs in a third project they both "
        + "reference.");

    /// <summary>
    /// <para>One file listed by two projects in the same build.</para>
    /// <para>A file belongs to the project that names it, and two claims on one file leave that
    /// undecided. Compiling it twice would report every type in it as declared twice, which
    /// says where the copies are without saying that nothing was copied.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor SourceBelongsToTwoProjects = Error(
        "PC0625",
        "Two projects claim one file",
        "'{0}' is listed by {1} and by {2}. A file belongs to one project. Let the project "
        + "that owns it keep it, and have the other reference that project.");

    /// <summary>
    /// <para>A construct the emitter cannot turn into CIL yet.</para>
    /// <para>Temporary, and says so: the language accepts the program and the interpreter runs
    /// it, so nothing is wrong with what was written. What is missing is the back end, and the
    /// message points at the way to run it in the meantime rather than leaving a reader to
    /// wonder what they did.</para>
    /// <para>Reported before anything is written, so a build either produces a whole assembly
    /// or produces no file at all. Emitting what is understood and leaving the rest out would
    /// write an assembly that verifies and then fails partway through a run, which is a worse
    /// answer than not building.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor CannotEmitYet = Error(
        "PC0501",
        "The compiler cannot emit this yet",
        "{0} cannot be compiled to an assembly yet. Run the program with 'run' until it can.");
}
