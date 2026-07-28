namespace ProfiC.Compiler.Diagnostics;

/// <summary>
/// <para>Every diagnostic the compiler can report.</para>
/// <para>Identifiers are allocated in blocks by compilation phase so that a reader can tell
/// from the number alone which part of the compiler objected.</para>
/// </summary>
/// <remarks>
/// <list type="table">
///   <item><term>PFC0001-0099</term><description>Lexical</description></item>
///   <item><term>PFC0100-0199</term><description>Syntax</description></item>
///   <item><term>PFC0200-0299</term><description>Name resolution</description></item>
///   <item><term>PFC0300-0399</term><description>Type checking</description></item>
///   <item><term>PFC0400-0499</term><description>Definite assignment and flow</description></item>
///   <item><term>PFC0500-0599</term><description>Lowering and emit</description></item>
///   <item><term>PFC0900+</term><description>Internal compiler errors</description></item>
/// </list>
/// </remarks>
public static class DiagnosticDescriptors
{
    private static DiagnosticDescriptor Error(string id, string title, string format) =>
        new(id, DiagnosticSeverity.Error, title, format);

    private static DiagnosticDescriptor Warning(string id, string title, string format) =>
        new(id, DiagnosticSeverity.Warning, title, format);

    // ---- Lexical, PFC0001 to PFC0099 ----------------------------------------------------

    public static readonly DiagnosticDescriptor UnrecognizedCharacter = Error(
        "PFC0001",
        "Unrecognized character",
        "Unrecognized character '{0}'.");

    public static readonly DiagnosticDescriptor UnterminatedString = Error(
        "PFC0002",
        "Unterminated string literal",
        "Unterminated string literal.");

    public static readonly DiagnosticDescriptor UnterminatedCharacter = Error(
        "PFC0003",
        "Unterminated character literal",
        "Unterminated character literal.");

    public static readonly DiagnosticDescriptor MalformedCharacterLiteral = Error(
        "PFC0004",
        "Malformed character literal",
        "A character literal must contain exactly one character.");

    public static readonly DiagnosticDescriptor UnterminatedBlockComment = Error(
        "PFC0005",
        "Unterminated block comment",
        "Unterminated block comment; expected 'end comment'.");

    /// <summary>
    /// Reported for operators a C# author reaches for that Profi-C does not have. Naming the
    /// replacement is the whole point; the alternative is either a bare "unrecognized
    /// character" or, worse, silently scanning "+=" as two separate tokens.
    /// </summary>
    public static readonly DiagnosticDescriptor NotAnOperator = Error(
        "PFC0006",
        "Not an operator in Profi-C",
        "'{0}' is not an operator in Profi-C. {1}");

    public static readonly DiagnosticDescriptor UnrecognizedEscape = Error(
        "PFC0007",
        "Unrecognized escape sequence",
        "Unrecognized escape sequence '\\{0}'.");

    public static readonly DiagnosticDescriptor MalformedUnicodeEscape = Error(
        "PFC0008",
        "Malformed Unicode escape sequence",
        "A Unicode escape must be '\\u' followed by four hexadecimal digits.");

    // ---- Syntax, PFC0100 to PFC0199 -----------------------------------------------------

    public static readonly DiagnosticDescriptor UnexpectedToken = Error(
        "PFC0100",
        "Unexpected token",
        "Expected {0}, but found {1}.");

    public static readonly DiagnosticDescriptor ExpectedExpression = Error(
        "PFC0101",
        "Expected an expression",
        "Expected an expression, but found {0}.");

    public static readonly DiagnosticDescriptor ExpectedType = Error(
        "PFC0102",
        "Expected a type",
        "Expected a type, but found {0}.");

    public static readonly DiagnosticDescriptor ExpectedIdentifier = Error(
        "PFC0103",
        "Expected a name",
        "Expected a name, but found {0}.");

    /// <summary>
    /// The diagnostic qualified <c>end</c> exists to produce. Naming both the closer written
    /// and the construct it fails to match is the whole point of requiring the qualifier.
    /// </summary>
    public static readonly DiagnosticDescriptor MismatchedEnd = Error(
        "PFC0104",
        "Mismatched block closer",
        "Expected 'end {0}' to close the {0} beginning on line {1}, but found 'end {2}'.");

    public static readonly DiagnosticDescriptor UnterminatedConstruct = Error(
        "PFC0105",
        "Unterminated construct",
        "The {0} beginning on line {1} is never closed; expected 'end {0}'.");

    /// <summary>
    /// A construct's body has no opening token, so a condition ends at the first token that
    /// cannot continue an expression. Both '(' and '-' can continue one, so a statement
    /// starting with either would be swallowed by the condition before it.
    /// </summary>
    public static readonly DiagnosticDescriptor StatementCannotStartWith = Error(
        "PFC0106",
        "Statement cannot start here",
        "A statement may not begin with '{0}'. Give the value a name first, "
        + "as in 'let value = ...;', and then use it.");

    public static readonly DiagnosticDescriptor ExpectedStatement = Error(
        "PFC0107",
        "Expected a statement",
        "Expected a statement, but found {0}.");

    public static readonly DiagnosticDescriptor ExpectedDeclaration = Error(
        "PFC0108",
        "Expected a declaration",
        "Expected a declaration, but found {0}.");

    public static readonly DiagnosticDescriptor AssignmentTargetNotAssignable = Error(
        "PFC0109",
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
        "PFC0110",
        "Type declared inside a function",
        "A {0} cannot be declared inside a function. Move it out to the enclosing model or "
        + "namespace.");

    /// <summary>
    /// <para>A range loop's counter is written without a type.</para>
    /// <para>Counting is done with integers, so there was never a choice to record — and a
    /// loop that says <c>integer</c> reads as though some other type were available. This
    /// exists because a reader arriving from C# or Java will write one out of habit, and
    /// "expected '='" would explain nothing.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor RangeLoopTakesNoType = Error(
        "PFC0111",
        "A range loop's counter has no written type",
        "A range loop counts with integers, so its counter takes no type. Remove the "
        + "'{0}'.");

    public static readonly DiagnosticDescriptor TooManyErrors = Error(
        "PFC0199",
        "Too many errors",
        "Too many errors; parsing stopped.");

    // ---- Name resolution, PFC0200 to PFC0299 --------------------------------------------

    public static readonly DiagnosticDescriptor NameNotFound = Error(
        "PFC0200",
        "Name not found",
        "'{0}' is not defined here.");

    public static readonly DiagnosticDescriptor TypeNotFound = Error(
        "PFC0201",
        "Type not found",
        "There is no type named '{0}'.");

    public static readonly DiagnosticDescriptor DuplicateDeclaration = Error(
        "PFC0202",
        "Name already declared",
        "'{0}' is already declared in this scope.");

    public static readonly DiagnosticDescriptor ReservedTypeName = Error(
        "PFC0203",
        "Reserved type name",
        "'{0}' is a built-in type and cannot be redeclared.");

    /// <summary>
    /// <para>The diagnostic that pays for requiring <c>this.</c> on every member access.</para>
    /// <para>A bare name reaches only locals and parameters, so a name that matches a field
    /// is a mistake with exactly one fix. Saying so is far better than "not defined here",
    /// which is technically true and useless.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor MemberNeedsReceiver = Error(
        "PFC0204",
        "Member access needs a receiver",
        "'{0}' is a {1} of '{2}', so it must be written as '{3}.{0}'. "
        + "A bare name reaches only locals and parameters.");

    public static readonly DiagnosticDescriptor CannotAssignToConstant = Error(
        "PFC0205",
        "Cannot assign to a constant",
        "'{0}' is a constant and cannot be assigned to.");

    public static readonly DiagnosticDescriptor CannotAssignToLoopVariable = Error(
        "PFC0206",
        "Cannot assign to a loop variable",
        "'{0}' is a loop variable and is read-only inside the loop. Each iteration binds a "
        + "fresh one, so assigning to it would change nothing.");

    public static readonly DiagnosticDescriptor CircularInheritance = Error(
        "PFC0207",
        "Circular inheritance",
        "'{0}' cannot extend itself, directly or through its ancestors.");

    public static readonly DiagnosticDescriptor CannotExtendSealed = Error(
        "PFC0208",
        "Cannot extend a sealed model",
        "'{0}' is sealed and cannot be extended.");

    public static readonly DiagnosticDescriptor CannotExtendNonModel = Error(
        "PFC0209",
        "Cannot extend this type",
        "'{0}' is a {1}, and only a model can be extended.");

    public static readonly DiagnosticDescriptor SealedAndAbstract = Error(
        "PFC0210",
        "Sealed and abstract together",
        "'{0}' cannot be both sealed and abstract; it could then be neither extended nor "
        + "instantiated, so nothing could use it.");

    public static readonly DiagnosticDescriptor GlobalModelMemberNotGlobal = Error(
        "PFC0211",
        "Instance member on a global model",
        "'{0}' cannot have instance members, because a global model is never instantiated.");

    public static readonly DiagnosticDescriptor EntryPointMissing = Error(
        "PFC0212",
        "No entry point",
        "A program needs a 'global model Program' containing a function named 'Main'.");

    public static readonly DiagnosticDescriptor EntryPointNotGlobalModel = Error(
        "PFC0213",
        "Program must be a global model",
        "'Program' must be declared 'global model', since there is no such thing as an "
        + "instance of a running program.");

    public static readonly DiagnosticDescriptor ProgramDeclaredMoreThanOnce = Error(
        "PFC0214",
        "Program declared more than once",
        "'Program' may be declared only once in a compilation.");

    public static readonly DiagnosticDescriptor ThisOutsideModel = Error(
        "PFC0215",
        "'{0}' used outside a model",
        "'{0}' can only be used inside a model's instance member.");

    public static readonly DiagnosticDescriptor BaseWithoutParent = Error(
        "PFC0216",
        "No parent to reach",
        "'base' needs a parent model, and '{0}' extends nothing.");

    /// <summary>
    /// <para>Separate from <see cref="CannotExtendNonModel"/>, which names the kind the base
    /// turned out to be. These are models as far as the compiler is concerned — that is only
    /// how their members resolve — so that message would read "'Console' is a model, and only
    /// a model can be extended", which explains nothing.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor CannotExtendBuiltInType = Error(
        "PFC0217",
        "Cannot extend a built-in type",
        "'{0}' is provided by the language and has nothing to inherit. Of the built-in types "
        + "only 'Model' and the exceptions may follow 'extends'.");

    // ---- Type checking, PFC0300 to PFC0399 -----------------------------------------------

    public static readonly DiagnosticDescriptor CannotConvert = Error(
        "PFC0300",
        "Cannot convert",
        "Cannot use {0} where {1} is expected.");

    /// <summary>
    /// A conversion that exists but must be written. The message names the call, since
    /// knowing one is possible is not much use without knowing how to ask for it.
    /// </summary>
    public static readonly DiagnosticDescriptor ConversionMustBeExplicit = Error(
        "PFC0301",
        "Conversion must be written out",
        "{0} does not become {1} on its own, because the result would surprise you. "
        + "Write '{2}' to ask for it.");

    public static readonly DiagnosticDescriptor ConditionMustBeBoolean = Error(
        "PFC0302",
        "Condition must be a boolean",
        "A {0} condition must be a boolean, and this is {1}.");

    public static readonly DiagnosticDescriptor OperatorNotDefined = Error(
        "PFC0303",
        "Operator not defined for these types",
        "'{0}' is not defined for {1} and {2}.");

    public static readonly DiagnosticDescriptor UnaryOperatorNotDefined = Error(
        "PFC0304",
        "Operator not defined for this type",
        "'{0}' is not defined for {1}.");

    /// <summary>
    /// Branch types must agree exactly rather than finding a common type, so that the type of
    /// a conditional is always the type its branches are written as.
    /// </summary>
    public static readonly DiagnosticDescriptor ConditionalBranchesDiffer = Error(
        "PFC0305",
        "Conditional branches have different types",
        "The branches of an 'if ... then ... else' must have the same type, and these are "
        + "{0} and {1}.");

    public static readonly DiagnosticDescriptor MemberNotFound = Error(
        "PFC0306",
        "Member not found",
        "{0} has no member named '{1}'.");

    public static readonly DiagnosticDescriptor NotCallable = Error(
        "PFC0307",
        "Not something that can be called",
        "{0} cannot be called.");

    public static readonly DiagnosticDescriptor WrongArgumentCount = Error(
        "PFC0308",
        "Wrong number of arguments",
        "'{0}' takes {1} argument(s), but {2} were given.");

    public static readonly DiagnosticDescriptor NoMatchingOverload = Error(
        "PFC0309",
        "No overload matches",
        "No version of '{0}' accepts these arguments.");

    public static readonly DiagnosticDescriptor AmbiguousOverload = Error(
        "PFC0310",
        "Ambiguous call",
        "Several versions of '{0}' match these arguments equally well.");

    public static readonly DiagnosticDescriptor NotIndexable = Error(
        "PFC0311",
        "Not something that can be indexed",
        "{0} cannot be indexed. Only a set and a string can.");

    public static readonly DiagnosticDescriptor IndexMustBeInteger = Error(
        "PFC0312",
        "Index must be an integer",
        "An index must be an integer, and this is {0}.");

    public static readonly DiagnosticDescriptor CannotInferEmptyCollection = Error(
        "PFC0313",
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
        "PFC0314",
        "Set elements have different types",
        "With no type to measure them against, every element of a set must have the same "
        + "type; found {0} and {1}. Write the type the set should have, as in "
        + "'Shape[] values = {{...}};'.");

    public static readonly DiagnosticDescriptor NotSwitchable = Error(
        "PFC0315",
        "Cannot switch on this type",
        "A switch cannot examine {0}. Equality on it is unreliable, so a case label could "
        + "never be trusted to match.");

    public static readonly DiagnosticDescriptor ForEachNeedsSequence = Error(
        "PFC0316",
        "Cannot iterate this type",
        "'for each' needs a set or a string, and this is {0}.");

    public static readonly DiagnosticDescriptor RangeLoopNeedsInteger = Error(
        "PFC0317",
        "Range loop needs integers",
        "A range loop counts with integers, and this is {0}.");

    public static readonly DiagnosticDescriptor YieldValueInVoidFunction = Error(
        "PFC0318",
        "This function yields nothing",
        "'{0}' declares no result, so 'yield' cannot carry a value.");

    public static readonly DiagnosticDescriptor YieldMissingValue = Error(
        "PFC0319",
        "Missing value to yield",
        "'{0}' yields a {1}, so 'yield' needs a value.");

    public static readonly DiagnosticDescriptor ConstantNeedsInitializer = Error(
        "PFC0320",
        "Constant needs a value",
        "'{0}' is a constant, so it must be given a value where it is declared.");

    public static readonly DiagnosticDescriptor ConstantNotFoldable = Error(
        "PFC0321",
        "Constant value must be known while compiling",
        "The value of '{0}' must be worked out while compiling, so it can only be built from "
        + "literals and other constants.");

    /// <summary>
    /// A constant is only permitted where an immutable binding really means an unchanging
    /// value, which rules out the types that could change behind it.
    /// </summary>
    public static readonly DiagnosticDescriptor ConstantTypeNotAllowed = Error(
        "PFC0322",
        "This type cannot be constant",
        "{0} cannot be declared constant, because the binding could stay fixed while what "
        + "it names changed. This may widen in a later version.");

    public static readonly DiagnosticDescriptor InferredDeclarationNeedsInitializer = Error(
        "PFC0323",
        "Nothing to infer from",
        "'let' works out the type from the value, so it needs one.");

    public static readonly DiagnosticDescriptor DivisionByZero = Error(
        "PFC0324",
        "Division by zero",
        "This divides by zero.");

    public static readonly DiagnosticDescriptor CaseLabelNotConstant = Error(
        "PFC0325",
        "Case label must be a constant",
        "A case label must be known while compiling.");

    public static readonly DiagnosticDescriptor DuplicateCaseLabel = Error(
        "PFC0326",
        "Duplicate case label",
        "The value {0} is already handled by another case.");

    public static readonly DiagnosticDescriptor CannotTestOrCast = Error(
        "PFC0327",
        "Types are unrelated",
        "{0} can never be {1}, so this test can only ever give one answer.");

    public static readonly DiagnosticDescriptor CannotInstantiate = Error(
        "PFC0328",
        "Cannot be instantiated",
        "'{0}' is {1} and cannot be instantiated.");

    public static readonly DiagnosticDescriptor OptionalMustBeUnwrapped = Error(
        "PFC0329",
        "Optional must be unwrapped first",
        "This is {0}, which may be empty. Use 'HasValue()' to check, 'Or(...)' for a "
        + "fallback, or 'Value()' to insist.");

    /// <summary>
    /// <para>The language provides no properties, so every member it provides is a function
    /// and every use of one is a call.</para>
    /// <para>Worth its own diagnostic because the mistake is quiet otherwise — a reader coming
    /// from a language with properties writes <c>xs.Count</c>, means the number, and gets
    /// something that is not one.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor BuiltInMemberNeedsCall = Error(
        "PFC0330",
        "This member is a function",
        "'{0}' is a function, so it has to be called: write '{0}()'.");

    /// <summary>
    /// <para>An instance member reached through the name of its type.</para>
    /// <para>There is no instance for it to belong to, so there is nothing to read. Marking
    /// the member <c>global</c> is usually what was meant — a <c>constant</c> is not global on
    /// its own, exactly as it is written <c>global constant</c> when it should be.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor MemberNeedsInstance = Error(
        "PFC0331",
        "Member needs an instance",
        "'{0}' belongs to each {1} rather than to the {1} type, so it cannot be reached "
        + "through the name '{1}'. Mark it 'global', or read it from a value.");

    // PFC0332 rejected a fraction-typed exponent. Withdrawn, and the identifier is not reused:
    // raising to m/n is the nth root of the mth power, which is ordinary arithmetic, and
    // "2 ^ (1|3)" says one third more faithfully than "2 ^ (1.0/3.0)" does. The answer is a
    // real either way. The rule that a fraction never widens to a real protects exactness that
    // could have been kept, and a root has none to keep.

    /// <summary>
    /// <para>A negative exponent on an integer base, caught while compiling.</para>
    /// <para>Two to the minus one is one half, which is not an integer. The same expression
    /// written on fractions is exact and allowed, which is what the message points at.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor NegativeIntegerExponent = Error(
        "PFC0333",
        "Negative exponent on an integer",
        "An integer raised to the power {0} is not a whole number. Raise a fraction instead, "
        + "as in '(1|2) ^ {0}', or use 'Math.Pow(...)' for a real result.");

    // ---- Definite assignment and flow, PFC0400 to PFC0499 --------------------------------

    /// <summary>
    /// <para>The rule that lets the language do without null.</para>
    /// <para>A variable that has not been given a value cannot be read, so there is no
    /// "unset" state for a program to stumble into and no null to represent it.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor UseBeforeAssignment = Error(
        "PFC0400",
        "Used before it is given a value",
        "'{0}' is used here before it has been given a value.");

    public static readonly DiagnosticDescriptor UseBeforeAssignmentOnSomePath = Error(
        "PFC0401",
        "Not given a value on every path",
        "'{0}' is not given a value on every path that reaches this point.");

    /// <summary>
    /// A field has no default, so a constructor must supply one. An optional field escapes
    /// this, which is exactly what makes a self-referential model constructible: a Node whose
    /// 'next' is optional has a base case, where one that had to be assigned would not.
    /// </summary>
    public static readonly DiagnosticDescriptor FieldNotAssignedInConstructor = Error(
        "PFC0402",
        "Field not given a value",
        "'{0}' must be given a value before this constructor ends. Give it one here, or an "
        + "initializer where it is declared, or make it optional.");

    public static readonly DiagnosticDescriptor UnreachableCode = Warning(
        "PFC0403",
        "Unreachable code",
        "This can never be reached.");
}
