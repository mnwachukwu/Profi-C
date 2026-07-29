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
    private static DiagnosticDescriptor Error(string id, string title, string format) =>
        new(id, DiagnosticSeverity.Error, title, format);

    private static DiagnosticDescriptor Warning(string id, string title, string format) =>
        new(id, DiagnosticSeverity.Warning, title, format);

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
        "Unterminated block comment; expected 'end comment'.");

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
    public static readonly DiagnosticDescriptor UnnecessaryEscapedName = Warning(
        "PC0009",
        "This name needs no '@'",
        "'{0}' is not a reserved word, so the '@' does nothing. Write '{0}'.");

    public static readonly DiagnosticDescriptor EscapeNeedsAName = Error(
        "PC0010",
        "Nothing to escape",
        "'@' marks a reserved word being used as a name, so a name must follow it.");

    // ---- Syntax, PC0100 to PC0113 -----------------------------------------------------

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
    public static readonly DiagnosticDescriptor ParameterTypeAlreadyKnown = Warning(
        "PC0115",
        "This parameter's type is already known",
        "The surrounding code already says what '{0}' holds, so writing its type says it "
        + "twice. Leave the type out.");

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
    public static readonly DiagnosticDescriptor RangeLoopTakesNoType = Warning(
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

    public static readonly DiagnosticDescriptor ReservedTypeName = Error(
        "PC0203",
        "Reserved type name",
        "'{0}' is a built-in type and cannot be redeclared.");

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

    public static readonly DiagnosticDescriptor GlobalModelMemberNotGlobal = Error(
        "PC0211",
        "Instance member on a global model",
        "'{0}' cannot have instance members, because a global model is never instantiated.");

    public static readonly DiagnosticDescriptor EntryPointMissing = Error(
        "PC0212",
        "No entry point",
        "A program needs a 'global model Program' containing a function named 'Main'.");

    public static readonly DiagnosticDescriptor EntryPointNotGlobalModel = Error(
        "PC0213",
        "Program must be a global model",
        "'Program' must be declared 'global model', since there is no such thing as an "
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

    public static readonly DiagnosticDescriptor DuplicateTypeDeclaration = Error(
        "PC0217",
        "Type already declared",
        "'{0}' is already declared {1}. Two types cannot share a name, whether they are "
        + "written in one file or across several. Rename one of them.");

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
    /// <para>The language provides no properties, so every member it provides is a function
    /// and every use of one is a call.</para>
    /// <para>A reader coming from a language with properties writes <c>xs.Count</c>, means
    /// the number, and would otherwise get something that is not one.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor BuiltInMemberNeedsCall = Error(
        "PC0330",
        "This member is a function",
        "'{0}' is a function, so it has to be called: write '{0}()'.");

    /// <summary>
    /// <para>An instance member reached through the name of its type.</para>
    /// <para>There is no instance for it to belong to, so there is nothing to read. Marking
    /// the member <c>global</c> is usually what was meant — a <c>constant</c> is not global on
    /// its own, exactly as it is written <c>global constant</c> when it should be.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor MemberNeedsInstance = Error(
        "PC0331",
        "Member needs an instance",
        "'{0}' belongs to each {1} rather than to the {1} type, so it cannot be reached "
        + "through the name '{1}'. Mark it 'global', or read it from a value.");

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
        "'{0}' is not something a project file says. Write 'source' and a path.");

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
}
