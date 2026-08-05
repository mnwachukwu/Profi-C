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
                integer[] numbers = {1, 2};

                loop each _ in numbers
                    Console.WriteLine("one");
                end loop

                loop for _ = 1 to 2
                    Console.WriteLine("two");
                end loop
        """),
        Is.Empty);

    /// <summary>One inside another, which is where the no-shadowing rule would otherwise bite.</summary>
    [Test]
    public void AThrowawayInsideAnotherShadowsNothing() => Assert.That(
        IdsIn("""
                integer[] numbers = {1, 2};

                loop each _ in numbers
                    loop for _ = 1 to 2
                        Console.WriteLine("nested");
                    end loop
                end loop
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

    [TestCase("        Console.WriteLine(_);", TestName = "read")]
    [TestCase("        Program.Rolled(_);", TestName = "handed on")]
    [TestCase("        Console.WriteLine(_ + 1);", TestName = "read inside an expression")]
    public void ReadingAThrowawayIsRefused(string body) =>
        Assert.That(IdsIn(body), Does.Contain("PC0254"));

    /// <summary>
    /// <para>Nothing obliges a declaration or an assignment to name anything, so a throwaway in
    /// one saves no name and spends a line.</para>
    /// <para>Any expression is already a statement, which is what makes every one of these
    /// avoidable: <c>Rolled();</c> drops the value and says so in fewer words.</para>
    /// </summary>
    [TestCase("        let _ = Program.Rolled();", TestName = "an inferred local")]
    [TestCase("        integer _ = Program.Rolled();", TestName = "a local with a written type")]
    [TestCase("        _ = Program.Rolled();", TestName = "an assignment")]
    [TestCase("        integer _;", TestName = "a local with no value at all")]
    public void AThrowawayWhereNoNameIsAskedForIsRefused(string body) =>
        Assert.That(IdsIn(body), Does.Contain("PC0256"));

    /// <summary>The value still runs through the checker, so what it is made of is still said.</summary>
    [Test]
    public void TheValueOfARefusedThrowawayIsStillChecked() =>
        Assert.That(IdsIn("        let _ = Program.Missing();"), Does.Contain("PC0306"));

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
    public void AThrowawayIsNotReportedAsUnread() => Assert.That(
        IdsIn("""
                integer[] numbers = {1, 2};

                loop each _ in numbers
                    Console.WriteLine("one");
                end loop
        """),
        Is.Empty);
}
