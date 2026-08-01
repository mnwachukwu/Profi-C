using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Semantics;
using ProfiC.Compiler.Text;

namespace ProfiC.Tests.Semantics;

/// <summary>
/// <para>Every operator that the compiler accepts on a pair of operands produces the answer
/// recorded for it here.</para>
/// <para>Accepting an expression and evaluating it correctly are different claims, and the
/// checker only makes the first. An arm of the interpreter that returns the wrong value — or
/// returns null and falls through to nothing — type-checks exactly as well as the right one,
/// so a suite that only asks whether a program compiles cannot tell them apart.</para>
/// <para>The completeness test below does not read a list of legal combinations; it asks the
/// compiler. Every operator is tried against every pair of operand types, and whatever checks
/// cleanly is required to have an answer recorded. Adding an operand type or widening an
/// operator therefore fails here until the answer is written down.</para>
/// </summary>
[TestFixture]
public sealed class OperatorResultTests
{
    /// <summary>A pair of literals for each type, distinct so an ordering can be told apart.</summary>
    private static readonly (string Type, string Left, string Right)[] Operands =
    [
        ("integer", "7", "2"),
        ("real", "7.0", "2.0"),
        ("fraction", "1|2", "1|3"),
        ("character", "'a'", "'b'"),
        ("boolean", "true", "false"),
        ("string", "\"ab\"", "\"cd\""),
    ];

    /// <summary>
    /// What each accepted combination produces, keyed by the expression itself so a row cannot
    /// drift from what it claims to measure.
    /// </summary>
    private static readonly Dictionary<string, string> Answers = new(StringComparer.Ordinal)
    {
        // Integers.
        ["7 == 2"] = "false", ["7 != 2"] = "true",
        ["7 < 2"] = "false", ["7 > 2"] = "true",
        ["7 <= 2"] = "false", ["7 >= 2"] = "true",
        ["7 + 2"] = "9", ["7 - 2"] = "5", ["7 * 2"] = "14",
        ["7 / 2"] = "3", ["7 % 2"] = "1", ["7 ^ 2"] = "49",

        // On the bits: 7 is 111 and 2 is 010.
        ["7 bitwise and 2"] = "2", ["7 bitwise or 2"] = "7", ["7 xor 2"] = "5",
        ["7 shiftleft 2"] = "28", ["7 shiftright 2"] = "1",

        // Reals. Division does not truncate, which is the difference from an integer.
        ["7.0 == 2.0"] = "false", ["7.0 != 2.0"] = "true",
        ["7.0 < 2.0"] = "false", ["7.0 > 2.0"] = "true",
        ["7.0 <= 2.0"] = "false", ["7.0 >= 2.0"] = "true",
        ["7.0 + 2.0"] = "9", ["7.0 - 2.0"] = "5", ["7.0 * 2.0"] = "14",
        ["7.0 / 2.0"] = "3.5", ["7.0 % 2.0"] = "1", ["7.0 ^ 2.0"] = "49",

        // Fractions stay exact, and print reduced.
        ["1|2 == 1|3"] = "false", ["1|2 != 1|3"] = "true",
        ["1|2 < 1|3"] = "false", ["1|2 > 1|3"] = "true",
        ["1|2 <= 1|3"] = "false", ["1|2 >= 1|3"] = "true",
        ["1|2 + 1|3"] = "5|6", ["1|2 - 1|3"] = "1|6",
        ["1|2 * 1|3"] = "1|6", ["1|2 / 1|3"] = "3|2",

        // One third goes into one half once, leaving a sixth — exactly, where a real would
        // answer with whatever the nearest double happens to be.
        ["1|2 % 1|3"] = "1|6",

        // A fractional exponent takes a root, which has no exact rational form, so this is
        // the one place a fraction widens to a real without being asked.
        ["1|2 ^ 1|3"] = "0.7937005259840998",

        // Characters compare by their place in the alphabet, and nothing else.
        ["'a' == 'b'"] = "false", ["'a' != 'b'"] = "true",
        ["'a' < 'b'"] = "true", ["'a' > 'b'"] = "false",
        ["'a' <= 'b'"] = "true", ["'a' >= 'b'"] = "false",

        // Booleans. 'and' and 'or' short-circuit, which is what the samples cover; here they
        // are asked only for their answer.
        ["true == false"] = "false", ["true != false"] = "true",
        ["true and false"] = "false", ["true or false"] = "true",

        // Strings compare by value, and '+' joins.
        ["\"ab\" == \"cd\""] = "false", ["\"ab\" != \"cd\""] = "true",
        ["\"ab\" + \"cd\""] = "abcd",
    };

    /// <summary>Every combination the compiler accepts, discovered by asking it.</summary>
    public static IEnumerable<string> Accepted =>
        from operand in Operands
        from op in Enum.GetValues<BinaryOperator>()
        let expression = $"{operand.Left} {op.Spelling()} {operand.Right}"
        where Checks(expression)
        select expression;

    private static bool Checks(string expression)
    {
        DiagnosticBag diagnostics = new();
        CompilationUnit unit = Parser.Parse(new SourceText(Program(expression), "<test>"), diagnostics);
        SemanticModel model = Resolver.Resolve(unit, diagnostics, requireEntryPoint: true);
        TypeChecker.Check(unit, model, diagnostics);

        return !diagnostics.Any();
    }

    private static string Program(string expression) =>
        $$"""
        shared model Program
            function Main()
                Console.WriteLine({{expression}});
            end function
        end model
        """;

    [TestCaseSource(nameof(Accepted))]
    public void TheAcceptedCombinationProducesItsAnswer(string expression)
    {
        Assert.That(
            Answers.ContainsKey(expression),
            $"'{expression}' checks cleanly and has no answer recorded. Add one.");

        DiagnosticBag diagnostics = new();
        CompilationUnit unit = Parser.Parse(new SourceText(Program(expression), "<test>"), diagnostics);
        SemanticModel model = Resolver.Resolve(unit, diagnostics, requireEntryPoint: true);
        TypeChecker.Check(unit, model, diagnostics);
        DefiniteAssignment.Analyze(unit, model, diagnostics);

        StringWriter output = new();
        ProfiC.Interpreter.Interpreter.Run(Lowering.Lower(unit, model), model, output);

        Assert.That(output.ToString().Trim(), Is.EqualTo(Answers[expression]), expression);
    }

    /// <summary>
    /// The other direction. An answer recorded for something the compiler no longer accepts is
    /// a row nothing runs, which reads as coverage and is not.
    /// </summary>
    [Test]
    public void EveryRecordedAnswerIsForAnAcceptedCombination() => Assert.That(
        Answers.Keys.Except(Accepted).Order(StringComparer.Ordinal),
        Is.Empty,
        "answers recorded for expressions the compiler does not accept");
}
