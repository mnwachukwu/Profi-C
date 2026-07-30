using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Semantics;
using ProfiC.Compiler.Text;

namespace ProfiC.Tests.Semantics;

/// <summary>
/// <para>Naming a type by the namespace it sits in.</para>
/// <para>Qualifying is what makes a namespace usable rather than merely correct: without it a
/// name a nearer one shadows is unreachable, and two namespaces offering the same name have
/// no way to be told apart. It needs no <c>using</c> — a using shortens a name, and a
/// qualified one is already saying where to look.</para>
/// </summary>
[TestFixture]
public sealed class QualifiedNameTests
{
    private static string[] Check(string source)
    {
        DiagnosticBag diagnostics = new();
        CompilationUnit unit = Parser.Parse(new SourceText(source, "<test>"), diagnostics);
        SemanticModel model = Resolver.Resolve(unit, diagnostics);
        TypeChecker.Check(unit, model, diagnostics);

        return [.. diagnostics.Sorted().Select(d => d.Id)];
    }

    private static string Run(string source)
    {
        DiagnosticBag diagnostics = new();
        CompilationUnit unit = Parser.Parse(new SourceText(source, "<test>"), diagnostics);
        SemanticModel model = Resolver.Resolve(unit, diagnostics, requireEntryPoint: true);
        TypeChecker.Check(unit, model, diagnostics);
        DefiniteAssignment.Analyze(unit, model, diagnostics);

        Assert.That(
            diagnostics.Sorted().Where(d => d.Severity == DiagnosticSeverity.Error)
                       .Select(d => $"{d.Id}: {d.Message}"),
            Is.Empty);

        StringWriter output = new();
        ProfiC.Interpreter.Interpreter.Run(Lowering.Lower(unit, model), model, output);

        return output.ToString().ReplaceLineEndings("\n");
    }

    private const string TwoNamespaces = """
        namespace Shapes
            public model Circle
                public string function Name() yield "flat"; end function
            end model
        end namespace

        namespace Solids
            public model Circle
                public string function Name() yield "round"; end function
            end model

            public model Sphere
                public global string function Describe() yield "a sphere"; end function
            end model
        end namespace
        """;

    /// <summary>
    /// <para>The point of the whole thing: two namespaces each declaring a <c>Circle</c>, and a
    /// program reaching both in one line.</para>
    /// <para>Nothing else can do this. A bare name reaches whichever is nearer, and a
    /// <c>using</c> of both makes it ambiguous rather than choosable.</para>
    /// </summary>
    [Test]
    public void TwoTypesOfOneNameAreBothReachable() => Assert.That(
        Run(TwoNamespaces + """

            global model Program
                function Main()
                    Shapes.Circle flat = new Shapes.Circle();
                    Solids.Circle round = new Solids.Circle();

                    Console.WriteLine(flat.Name() + " and " + round.Name());
                end function
            end model
            """),
        Is.EqualTo("flat and round\n"));

    /// <summary>A qualified name reaching a global member, which is a run of member accesses
    /// the resolver has to read as one name before any of it is an expression.</summary>
    [Test]
    public void AQualifiedNameReachesAGlobalMember() => Assert.That(
        Run(TwoNamespaces + """

            global model Program
                function Main()
                    Console.WriteLine(Solids.Sphere.Describe());
                end function
            end model
            """),
        Is.EqualTo("a sphere\n"));

    /// <summary>And in a signature, where nothing is being evaluated at all.</summary>
    [Test]
    public void AQualifiedNameWorksInASignature() => Assert.That(
        Run(TwoNamespaces + """

            global model Program
                function Main()
                    Console.WriteLine(Program.NameOf(new Shapes.Circle()));
                end function

                string function NameOf(Shapes.Circle c)
                    yield c.Name();
                end function
            end model
            """),
        Is.EqualTo("flat\n"));

    // ---- Standard ---------------------------------------------------------------------------

    /// <summary>
    /// Qualifying needs no <c>using</c>, which is what makes the library reachable past a name
    /// of your own that took its place.
    /// </summary>
    [Test]
    public void StandardIsReachablePastATypeThatShadowsIt() => Assert.That(
        Run("""
            model Math
                public global string function Sqrt(integer n) yield "mine"; end function
            end model

            global model Program
                function Main()
                    Console.WriteLine(Math.Sqrt(9));
                    Console.WriteLine(Standard.Math.Sqrt(9.0));
                end function
            end model
            """),
        Is.EqualTo("mine\n3\n"));

    /// <summary>A type the language owns, qualified in both the positions a type appears.</summary>
    [Test]
    public void StandardQualifiesATypeWhereverOneIsWritten() => Assert.That(
        Check("""
            global model Program
                function Main()
                    Standard.Random chance = new Standard.Random();
                    Console.WriteLine(Program.Roll(chance));
                end function

                integer function Roll(Standard.Random chance)
                    yield chance.Next(1, 7);
                end function
            end model
            """),
        Is.Empty);

    // ---- Where it points nowhere -------------------------------------------------------------

    [TestCase("Shapes.Nothing a;", TestName = "no such type in a real namespace")]
    [TestCase("Nowhere.Circle a;", TestName = "no such namespace")]
    [TestCase("Shapes.Circle.Nothing a;", TestName = "one part too many")]
    public void AQualifiedNameThatReachesNothingIsReported(string written) => Assert.That(
        Check($$"""
            namespace Shapes
                public model Circle
                end model
            end namespace

            global model Program
                function Main()
                    {{written}}
                end function
            end model
            """),
        Does.Contain("PC0201"));

    /// <summary>The whole name is quoted back, since half of it would not be what was written.</summary>
    [Test]
    public void TheMessageQuotesTheWholeName()
    {
        DiagnosticBag diagnostics = new();

        CompilationUnit unit = Parser.Parse(
            new SourceText("""
                global model Program
                    function Main()
                        Shapes.Circle a;
                    end function
                end model
                """, "<test>"),
            diagnostics);

        Resolver.Resolve(unit, diagnostics);

        Assert.That(
            diagnostics.Sorted().Single(d => d.Id == "PC0201").Message,
            Does.Contain("'Shapes.Circle'"));
    }

    // ---- Reading from where it is written ----------------------------------------------------

    /// <summary>
    /// A prefix is found the way any name is, so a namespace beside you beats one at the root.
    /// Here <c>Tour.Shapes</c> wins over the top-level <c>Shapes</c>, and only the qualified
    /// form could have said either.
    /// </summary>
    [Test]
    public void AQualifiedNameIsReadFromWhereItIsWritten() => Assert.That(
        Run("""
            namespace Shapes
                public model Circle
                    public string function Name() yield "outer"; end function
                end model
            end namespace

            namespace Tour.Shapes
                public model Circle
                    public string function Name() yield "inner"; end function
                end model
            end namespace

            namespace Tour
                public model Asking
                    public global string function Which()
                        yield new Shapes.Circle().Name();
                    end function
                end model
            end namespace

            global model Program
                function Main()
                    Console.WriteLine(Tour.Asking.Which());
                    Console.WriteLine(new Shapes.Circle().Name());
                end function
            end model
            """),
        Is.EqualTo("inner\nouter\n"));

    /// <summary>
    /// A value's member is not a qualified name, however much it looks like one. What settles
    /// it is that the run of names reaches no type.
    /// </summary>
    [Test]
    public void AValueWhoseMembersChainIsStillAValue() => Assert.That(
        Run("""
            model Inner
                public string function Say() yield "inner"; end function
            end model

            model Outer
                public Inner held;
                public function Outer() this.held = new Inner(); end function
            end model

            global model Program
                function Main()
                    Outer o = new Outer();
                    Console.WriteLine(o.held.Say());
                end function
            end model
            """),
        Is.EqualTo("inner\n"));
}
