using ProfiC.Compiler;
using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Text;

namespace ProfiC.Tests.Semantics;

/// <summary>
/// <para>Private members that nothing reaches.</para>
/// <para>The rule is worth pinning from both sides, because the interesting half is what it
/// stays quiet about. Anything wider than private may be reached from code that is not being
/// compiled, and three private things are not reached by name at all — a constructor, an
/// overridable function, and the entry point — so each would read as unused however used it is.
/// </para>
/// <para>What is asserted is the identifier. The wording is pinned by
/// <c>samples/negatives/compile/throwaway.pc</c>.</para>
/// </summary>
[TestFixture]
public sealed class UnusedMemberTests
{
    private static string[] IdsIn(string program)
    {
        DiagnosticBag diagnostics = new();

        CompilationUnit unit = Parser.Parse(new SourceText(program, "<test>"), diagnostics);
        FrontEnd.Check(unit, diagnostics, reportUnusedSuppressions: false);

        return [.. diagnostics.Sorted().Select(d => d.Id)];
    }

    /// <summary>A model with one member, and a Main that may or may not reach it.</summary>
    private static string Holding(string member, string body = "") => $$"""
        shared model Program
        {{member}}

            function Main()
        {{body}}
            end function
        end model
        """;

    /// <summary>A body, with a pair of helpers to call: one that answers and one that does not.</summary>
    private static string[] IdsInBody(string body) => IdsIn($$"""
        shared model Program
            public shared integer function Rolled()
                yield 6;
            end function

            public shared function Silent()
                Console.WriteLine("done");
            end function

            function Main()
        {{body}}
            end function
        end model
        """);

    // ---- Reported ------------------------------------------------------------------------

    [TestCase("    shared integer Count = 3;", TestName = "a field")]
    [TestCase("    shared constant integer Limit = 3;", TestName = "a constant field")]
    [TestCase(
        """
            shared function Helper()
                Console.WriteLine("nothing calls this");
            end function
        """,
        TestName = "a function")]
    public void APrivateMemberNothingReachesIsReported(string member) =>
        Assert.That(IdsIn(Holding(member)), Does.Contain("PC0410"));

    // ---- Not reported --------------------------------------------------------------------

    [TestCase(
        "    shared integer Count = 3;",
        "        Console.WriteLine(Program.Count);",
        TestName = "a field something reads")]
    [TestCase(
        """
            shared function Helper()
                Console.WriteLine("called");
            end function
        """,
        "        Program.Helper();",
        TestName = "a function something calls")]
    [TestCase(
        """
            shared integer function Twice(integer n)
                yield n * 2;
            end function
        """,
        "        let f = Program.Twice;\n        Console.WriteLine(f(2));",
        TestName = "a function handed on as a value")]
    public void AMemberSomethingReachesIsNotReported(string member, string body) =>
        Assert.That(IdsIn(Holding(member, body)), Is.Empty);

    /// <summary>
    /// Anything wider than private may be reached from a file, a project, or a program that is
    /// not being compiled, so nothing here can say it is unused.
    /// </summary>
    [TestCase("public", TestName = "public")]
    [TestCase("internal", TestName = "internal")]
    [TestCase("protected", TestName = "protected")]
    public void AMemberWiderThanPrivateIsNotReported(string visibility) => Assert.That(
        IdsIn(Holding($"    {visibility} shared integer Count = 3;")),
        Is.Empty);

    /// <summary>The entry point is reached by the runtime rather than by its name.</summary>
    [Test]
    public void TheEntryPointIsNotReported() => Assert.That(
        IdsIn("""
            shared model Program
                function Main()
                end function
            end model
            """),
        Is.Empty);

    /// <summary>A constructor answers to 'new', so its name is never written to call it.</summary>
    [Test]
    public void AConstructorIsNotReported() => Assert.That(
        IdsIn("""
            model Box
                integer held;

                public function Box(integer value)
                    this.held = value;
                end function

                public integer function Held()
                    yield this.held;
                end function
            end model

            shared model Program
                function Main()
                    Box box = new Box(1);
                    Console.WriteLine(box.Held());
                end function
            end model
            """),
        Is.Empty);

    /// <summary>
    /// An overridable function is reached through whatever the value turns out to be, so the
    /// name of the one being overridden need never be written.
    /// </summary>
    [Test]
    public void AnOverridableFunctionIsNotReported() => Assert.That(
        IdsIn("""
            model Shape
                protected virtual integer function Sides()
                    yield 0;
                end function

                public integer function Count()
                    yield this.Sides();
                end function
            end model

            model Square extends Shape
                protected override integer function Sides()
                    yield 4;
                end function
            end model

            shared model Program
                function Main()
                    Shape shape = new Square();
                    Console.WriteLine(shape.Count());
                end function
            end model
            """),
        Is.Empty);

    /// <summary>
    /// A member is not kept alive by the line that declares it. Without that the walk would find
    /// the declaration's own name and every member would look reached.
    /// </summary>
    [Test]
    public void ADeclarationDoesNotCountAsAUseOfItself() =>
        Assert.That(IdsIn(Holding("    shared integer Count = 3;")), Is.EqualTo(new[] { "PC0410" }));

    // ---- A statement that keeps nothing --------------------------------------------------

    /// <summary>
    /// <para>Where the value settles while compiling, or is a bare name, nothing happens at all.
    /// </para>
    /// <para>A statement keeps nothing by design — that is how a value is dropped on purpose —
    /// so what is reported is not the dropping but that working the value out could do nothing
    /// either.</para>
    /// </summary>
    [TestCase("        1 + 2;", TestName = "arithmetic that settles")]
    [TestCase("        \"hello\";", TestName = "a literal")]
    [TestCase("        true;", TestName = "a boolean literal")]
    [TestCase("        kept;", TestName = "a bare name")]
    public void AStatementThatCanDoNothingIsReported(string body) => Assert.That(
        IdsInBody($"        integer kept = 1;\n{body}"),
        Does.Contain("PC0411"));

    /// <summary>
    /// <para>Arithmetic is checked, so arithmetic can fail, and failing is something happening.
    /// </para>
    /// <para>Each of these is written on its own in <c>samples/exceptions.pc</c> to raise what it
    /// raises. Calling them "nothing" would be false rather than merely unhelpful — which is why
    /// the test is what the compiler can settle and not whether a call appears.</para>
    /// </summary>
    [TestCase("        1 / zero;", TestName = "a division that may not settle")]
    [TestCase("        2147483647 * 2147483647 * 4;", TestName = "arithmetic that overflows")]
    [TestCase("        three[7];", TestName = "an index that may be past the end")]
    public void AStatementThatCanFailIsNotReported(string body) => Assert.That(
        IdsInBody($"        integer zero = 0;\n        integer[] three = {{1, 2}};\n{body}"),
        Does.Not.Contain("PC0411"));

    /// <summary>A call that yields something nobody keeps: an opinion, and about the function.</summary>
    [Test]
    public void ACallWhoseResultIsDroppedIsAnOpinion() => Assert.That(
        IdsInBody("        Program.Rolled();"),
        Is.EqualTo(new[] { "PC0412" }));

    [TestCase("        Program.Silent();", TestName = "a call that yields nothing")]
    [TestCase("        Console.WriteLine(Program.Rolled());", TestName = "a result that is used")]
    public void ACallWhoseResultGoesNowhereUnaskedIsNotReported(string body) =>
        Assert.That(IdsInBody(body), Is.Empty);

    /// <summary>
    /// Not where the call did not resolve. Its type is the error type, and remarking on a
    /// dropped value would stack a second thing to read onto a line whose one mistake is named.
    /// </summary>
    [Test]
    public void ACallThatDidNotResolveEarnsNoOpinionOnTop() => Assert.That(
        IdsInBody("        Program.Missing();"),
        Is.EqualTo(new[] { "PC0306" }));

    /// <summary>
    /// <para>Two dead members naming each other keep each other alive, and nothing is reported.
    /// </para>
    /// <para>The question asked is whether a name is written anywhere, not whether the line
    /// writing it can ever run. Answering the second would mean tracing reachability from
    /// everything a program can start at — the entry point, and every member wide enough to be
    /// called from outside — which is a different and much larger analysis than this one.</para>
    /// <para>Held as a test rather than left undiscovered, because it is the limit of what the
    /// warning claims: it finds a member nothing mentions, and says nothing about a knot of dead
    /// code that mentions itself.</para>
    /// </summary>
    [Test]
    public void MembersThatOnlyNameEachOtherAreNotReported() => Assert.That(
        IdsIn("""
            shared model Program
                shared function First()
                    Program.Second();
                end function

                shared function Second()
                    Program.First();
                end function

                function Main()
                end function
            end model
            """),
        Is.Empty);
}
