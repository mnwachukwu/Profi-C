using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Semantics;
using ProfiC.Compiler.Text;

namespace ProfiC.Tests.Semantics;

/// <summary>
/// <para>The bare underscore, which binds nothing.</para>
/// <para>Two halves are worth pinning separately. A throwaway is accepted wherever the language
/// hands a body a value it did not ask for by name, and several of them in one scope are not a
/// clash — which is the whole point, and the thing a duplicate-name rule would quietly undo.
/// It is refused wherever a name has to be read back: in an expression, and on anything reached
/// by writing what it is called.</para>
/// <para>What is asserted is the identifier. The wording is pinned by
/// <c>samples/negatives/compile/throwaway.pc</c>, where a reader can see it in a program.</para>
/// </summary>
[TestFixture]
public sealed class ThrowawayTests
{
    private static string[] IdsIn(string body) => IdsInProgram($$"""
        shared model Program
            function Main()
        {{body}}
            end function

            integer function Rolled()
                yield 6;
            end function
        end model
        """);

    private static string[] IdsInProgram(string program)
    {
        DiagnosticBag diagnostics = new();

        CompilationUnit unit = Parser.Parse(new SourceText(program, "<test>"), diagnostics);
        SemanticModel model = Resolver.Resolve(unit, diagnostics);
        TypeChecker.Check(unit, model, diagnostics);
        DefiniteAssignment.Analyze(unit, model, diagnostics);
        UnusedLocals.Analyze(unit, model, diagnostics);

        return [.. diagnostics.Sorted().Select(d => d.Id)];
    }

    // ---- Where a throwaway is taken ------------------------------------------------------

    [TestCase("        let _ = 41;", TestName = "an inferred local")]
    [TestCase("        integer _ = 41;", TestName = "a local with a written type")]
    [TestCase("        let _ = Program.Rolled();", TestName = "a result dropped")]
    [TestCase(
        """
                loop for _ = 1 to 3
                    Console.WriteLine("tick");
                end loop
        """,
        TestName = "a range loop's counter")]
    [TestCase(
        """
                integer[] numbers = {1, 2};
                loop each _ in numbers
                    Console.WriteLine("one");
                end loop
        """,
        TestName = "a walked element")]
    [TestCase(
        """
                try
                    throw new ArgumentException("thrown");
                catch ArgumentException _
                    Console.WriteLine("caught");
                end try
        """,
        TestName = "a caught exception")]
    public void AThrowawayIsTakenWhereAValueArrives(string body) =>
        Assert.That(IdsIn(body), Is.Empty);

    /// <summary>
    /// Several in one body, which is the point of the feature: a throwaway enters no scope, so
    /// there is nothing for the duplicate-name rule or the no-shadowing rule to find.
    /// </summary>
    [Test]
    public void SeveralThrowawaysInOneBodyAreNotAClash() => Assert.That(
        IdsIn("""
                let _ = 1;
                integer _ = 2;
                let _ = 3;
        """),
        Is.Empty);

    [Test]
    public void AThrowawayInsideAnotherScopeShadowsNothing() => Assert.That(
        IdsIn("""
                integer depth = 1;
                let _ = 1;

                if depth > 0
                    let _ = 2;
                end if
        """),
        Is.Empty);

    /// <summary>Only the bare underscore is taken; a name that merely begins with one is not.</summary>
    [Test]
    public void ANameBeginningWithAnUnderscoreIsAnOrdinaryName() => Assert.That(
        IdsIn("""
                integer _count = 3;
                Console.WriteLine(_count);
        """),
        Is.Empty);

    // ---- Where it is refused -------------------------------------------------------------

    [TestCase("        let _ = 1;\n        Console.WriteLine(_);", TestName = "read")]
    [TestCase("        let _ = 1;\n        Program.Rolled(_);", TestName = "handed on")]
    [TestCase("        Console.WriteLine(_ + 1);", TestName = "read without one declared")]
    public void ReadingAThrowawayIsRefused(string body) =>
        Assert.That(IdsIn(body), Does.Contain("PC0254"));

    /// <summary>A throwaway with no value drops nothing, so the line does nothing at all.</summary>
    [Test]
    public void AThrowawayWithNothingToDropIsRefused() =>
        Assert.That(IdsIn("        integer _;"), Does.Contain("PC0256"));

    [TestCase("shared integer _ = 1;", TestName = "a field")]
    [TestCase("shared function _()\n    end function", TestName = "a function")]
    public void AThrowawayCannotNameAMember(string member) => Assert.That(
        IdsInProgram($$"""
            shared model Program
                {{member}}

                function Main()
                end function
            end model
            """),
        Does.Contain("PC0255"));

    [TestCase("model _\nend model", TestName = "a model")]
    [TestCase("structure _\nend structure", TestName = "a structure")]
    [TestCase("enumeration _\n    One\nend enumeration", TestName = "an enumeration")]
    [TestCase("enumeration Weekday\n    _\nend enumeration", TestName = "an enumeration member")]
    [TestCase("namespace _\n    model Inside\n    end model\nend namespace", TestName = "a namespace")]
    public void AThrowawayCannotNameAType(string declaration) => Assert.That(
        IdsInProgram($$"""
            shared model Program
                function Main()
                end function
            end model

            {{declaration}}
            """),
        Does.Contain("PC0255"));

    /// <summary>
    /// A parameter is refused where every other receiving position is taken, because it is the
    /// only one a caller reads. Both kinds are covered: a declared function's and a lambda's.
    /// </summary>
    [Test]
    public void AThrowawayCannotNameADeclaredFunctionsParameter() => Assert.That(
        IdsInProgram("""
            shared model Program
                shared function Draw(integer _, integer height)
                    Console.WriteLine(height);
                end function

                function Main()
                    Program.Draw(1, 2);
                end function
            end model
            """),
        Does.Contain("PC0257"));

    [Test]
    public void AThrowawayCannotNameALambdasParameter() => Assert.That(
        IdsIn("""
                integer delegate(integer) one = function(_)
                    yield 1;
                end function;

                Console.WriteLine(one(5));
        """),
        Does.Contain("PC0257"));

    // ---- Assigning to one, which is allowed and says nothing ------------------------------

    /// <summary>
    /// A statement drops whatever it does not use, so the assignment is the value and nothing
    /// else. Allowed, and the language has an opinion about writing it.
    /// </summary>
    [Test]
    public void AssigningToAThrowawayIsAnOpinionRatherThanARefusal() => Assert.That(
        IdsIn("        _ = Program.Rolled();"),
        Is.EqualTo(new[] { "PC0258" }));

    // ---- A local nothing reads ------------------------------------------------------------

    [TestCase("        integer forgotten = 9;", TestName = "declared and left")]
    [TestCase("        integer written = 1;\n        written = 2;", TestName = "written, never read")]
    [TestCase(
        """
                integer[] numbers = {1, 2};
                loop each item in numbers
                    Console.WriteLine("one");
                end loop
        """,
        TestName = "a walked element nothing reads")]
    [TestCase(
        """
                try
                    throw new ArgumentException("thrown");
                catch ArgumentException problem
                    Console.WriteLine("caught");
                end try
        """,
        TestName = "a caught exception nothing reads")]
    public void ALocalNothingReadsIsReported(string body) =>
        Assert.That(IdsIn(body), Does.Contain("PC0409"));

    /// <summary>Reading is what counts, however the value is reached.</summary>
    [TestCase(
        "        integer kept = 1;\n        Console.WriteLine(kept);",
        TestName = "read plainly")]
    [TestCase(
        "        integer[] numbers = {1, 2};\n        numbers[0] = 9;\n"
        + "        Console.WriteLine(numbers[0]);",
        TestName = "read to work out where to store")]
    [TestCase(
        "        integer captured = 1;\n"
        + "        integer delegate() read = () yield captured;\n"
        + "        Console.WriteLine(read());",
        TestName = "read from inside a lambda")]
    public void ALocalSomethingReadsIsNotReported(string body) =>
        Assert.That(IdsIn(body), Is.Empty);

    /// <summary>A throwaway is exempt, because it has already said nothing will read it.</summary>
    [Test]
    public void AThrowawayIsNotReportedAsUnread() =>
        Assert.That(IdsIn("        let _ = Program.Rolled();"), Is.Empty);
}
