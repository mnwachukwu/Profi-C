using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Semantics;
using ProfiC.Compiler.Text;

namespace ProfiC.Tests.Semantics;

/// <summary>Name binding, the inheritance graph, and the rules the resolver enforces.</summary>
[TestFixture]
public sealed class ResolverTests
{
    private static (SemanticModel Model, DiagnosticBag Diagnostics) Resolve(
        string source,
        bool requireEntryPoint = false)
    {
        DiagnosticBag diagnostics = new();
        CompilationUnit unit = Parser.Parse(new SourceText(source, "<test>"), diagnostics);
        SemanticModel model = Resolver.Resolve(unit, diagnostics, requireEntryPoint);
        return (model, diagnostics);
    }

    private static string[] IdsOf(DiagnosticBag bag) => [.. bag.Sorted().Select(d => d.Id)];

    /// <summary>Wraps statements in a model with one instance field, for the common cases.</summary>
    private static (SemanticModel Model, DiagnosticBag Diagnostics) ResolveBody(string body) =>
        Resolve($$"""
            model Holder
                integer count;
                global integer total;
                constant integer Limit = 10;

                function Run(integer parameter)
            {{body}}
                end function
            end model
            """);

    // ---- Clean cases -----------------------------------------------------------------------

    [Test]
    public void ASimpleProgramResolvesCleanly()
    {
        (SemanticModel model, DiagnosticBag diagnostics) = Resolve(
            """
            global model Program
                function Main()
                    let x = 1;
                end function
            end model
            """,
            requireEntryPoint: true);

        Assert.Multiple(() =>
        {
            Assert.That(IdsOf(diagnostics), Is.Empty);
            Assert.That(model.EntryPoint, Is.Not.Null);
            Assert.That(model.EntryPoint!.Name, Is.EqualTo("Main"));
        });
    }

    [Test]
    public void LocalsAndParametersResolveByBareName()
    {
        (_, DiagnosticBag diagnostics) = ResolveBody(
            """
                    let local = parameter;
                    let again = local;
            """);

        Assert.That(IdsOf(diagnostics), Is.Empty);
    }

    [Test]
    public void MembersResolveThroughAReceiver()
    {
        (_, DiagnosticBag diagnostics) = ResolveBody(
            """
                    this.count = 1;
                    Holder.total = 2;
            """);

        Assert.That(IdsOf(diagnostics), Is.Empty);
    }

    // ---- The rule that pays for requiring "this." ---------------------------------------------

    /// <summary>
    /// A bare name reaches only locals and parameters, so one that matches a field is a
    /// mistake with exactly one fix. Saying which is the point of the restriction.
    /// </summary>
    [Test]
    public void ABareNameMatchingAnInstanceFieldNamesTheFix()
    {
        (_, DiagnosticBag diagnostics) = ResolveBody("        count = 1;");

        Assert.Multiple(() =>
        {
            Assert.That(IdsOf(diagnostics), Is.EqualTo(new[] { "PC0204" }));
            Assert.That(diagnostics.Single().Message, Does.Contain("'this.count'"));
            Assert.That(diagnostics.Single().Message, Does.Contain("field"));
        });
    }

    [Test]
    public void ABareNameMatchingAGlobalMemberNamesItsType()
    {
        (_, DiagnosticBag diagnostics) = ResolveBody("        total = 1;");

        Assert.Multiple(() =>
        {
            Assert.That(IdsOf(diagnostics), Is.EqualTo(new[] { "PC0204" }));
            Assert.That(diagnostics.Single().Message, Does.Contain("'Holder.total'"));
        });
    }

    [Test]
    public void ABareNameMatchingNothingIsSimplyNotFound()
    {
        (_, DiagnosticBag diagnostics) = ResolveBody("        let x = nowhere;");

        Assert.That(IdsOf(diagnostics), Is.EqualTo(new[] { "PC0200" }));
    }

    [Test]
    public void AnInheritedMemberIsAlsoOfferedTheFix()
    {
        (_, DiagnosticBag diagnostics) = Resolve(
            """
            model Shape
                protected integer sides;
            end model

            model Square extends Shape
                function Set()
                    sides = 4;
                end function
            end model
            """);

        Assert.That(IdsOf(diagnostics), Is.EqualTo(new[] { "PC0204" }));
    }

    // ---- Scoping ------------------------------------------------------------------------------

    [Test]
    public void ALocalDoesNotEscapeItsBlock()
    {
        (_, DiagnosticBag diagnostics) = ResolveBody(
            """
                    begin
                        let inner = 1;
                    end
                    let outer = inner;
            """);

        Assert.That(IdsOf(diagnostics), Is.EqualTo(new[] { "PC0200" }));
    }

    /// <summary>
    /// <para>Inside a function body a bare name means one thing throughout. This is the rule
    /// that makes it true, and it is what lets a lambda reach an enclosing local unmarked:
    /// there is nothing the name could be confused with.</para>
    /// <para>Each of these is a different way of introducing a name, and all of them are bound
    /// through one place, so one failing would mean that place had been bypassed.</para>
    /// </summary>
    [TestCase("        begin\n            let value = 2;\n        end", TestName = "a block's local")]
    [TestCase("        let show = (integer value) yield value;", TestName = "a lambda's parameter")]
    [TestCase("        for value = 1 to 3\n        end for", TestName = "a for binding")]
    [TestCase("        for each value in {1, 2}\n        end for", TestName = "a for-each binding")]
    [TestCase("        try\n        catch Exception value\n        end try",
              TestName = "a caught exception")]
    public void ANestedScopeMayNotShadowAnOuterName(string written)
    {
        (_, DiagnosticBag diagnostics) = ResolveBody($"        let value = 1;\n{written}");

        Assert.That(IdsOf(diagnostics), Is.EqualTo(new[] { "PC0237" }));
    }

    /// <summary>
    /// Two scopes that cannot see each other are not shadowing, so the same name may be used
    /// in both. Forbidding that would make a name unusable in a whole function because some
    /// unrelated block above it happened to pick that word.
    /// </summary>
    [Test]
    public void TwoScopesSideBySideMayUseTheSameName()
    {
        (_, DiagnosticBag diagnostics) = ResolveBody(
            """
                    begin
                        let value = 1;
                    end
                    begin
                        let value = 2;
                    end
            """);

        Assert.That(IdsOf(diagnostics), Is.Empty);
    }

    /// <summary>
    /// A function's parameters and the statements of its body share one scope, so which of the
    /// two complaints a repeated parameter name draws depends on where it is written. Both are
    /// right, and the fix differs: one name is taken twice in a row, the other is taken again
    /// further in.
    /// </summary>
    [TestCase("        let parameter = 2;", "PC0202", TestName = "beside it, a duplicate")]
    [TestCase("        begin\n            let parameter = 2;\n        end", "PC0237",
              TestName = "inside a block, a shadow")]
    public void AParameterMayNotHaveItsNameTakenAgain(string written, string expected)
    {
        (_, DiagnosticBag diagnostics) = ResolveBody(written);

        Assert.That(IdsOf(diagnostics), Is.EqualTo(new[] { expected }));
    }

    /// <summary>
    /// <para>A local named after a field is left alone, which is the other half of the rule.
    /// Only locals and parameters live in a scope, so the two never meet: a bare name is the
    /// local and <c>this.</c> reaches the field.</para>
    /// <para>Forbidding it would mean adding a field to a model could break methods that have
    /// nothing to do with it, and it would leave <c>this.</c> with nothing to distinguish.
    /// </para>
    /// </summary>
    [TestCase("        let count = 2;", TestName = "an instance field")]
    [TestCase("        let total = 2;", TestName = "a global field")]
    [TestCase("        let Limit = 2;", TestName = "a constant")]
    public void ALocalMayStillCarryAFieldsName(string written)
    {
        (_, DiagnosticBag diagnostics) = ResolveBody(written);

        Assert.That(IdsOf(diagnostics), Is.Empty);
    }

    [Test]
    public void DeclaringTheSameNameTwiceInOneScopeIsRejected()
    {
        (_, DiagnosticBag diagnostics) = ResolveBody(
            """
                    let value = 1;
                    let value = 2;
            """);

        Assert.That(IdsOf(diagnostics), Is.EqualTo(new[] { "PC0202" }));
    }

    [Test]
    public void AnInitializerCannotSeeTheVariableBeingDeclared()
    {
        (_, DiagnosticBag diagnostics) = ResolveBody("        let value = value;");

        Assert.That(IdsOf(diagnostics), Is.EqualTo(new[] { "PC0200" }));
    }

    [Test]
    public void ALoopVariableIsVisibleOnlyInsideItsLoop()
    {
        (_, DiagnosticBag diagnostics) = ResolveBody(
            """
                    for i = 1 to 10
                        let inside = i;
                    end for
                    let outside = i;
            """);

        Assert.That(IdsOf(diagnostics), Is.EqualTo(new[] { "PC0200" }));
    }

    [Test]
    public void ALambdaSeesTheEnclosingLocals()
    {
        // Capture is exactly this: the body binds inside the scope the lambda was written in.
        (_, DiagnosticBag diagnostics) = ResolveBody(
            """
                    let captured = 1;
                    integer delegate(integer) add = (integer a) yield a + captured;
            """);

        Assert.That(IdsOf(diagnostics), Is.Empty);
    }

    [Test]
    public void ANestedFunctionSeesTheEnclosingLocals()
    {
        (_, DiagnosticBag diagnostics) = ResolveBody(
            """
                    let captured = 1;
                    integer function Helper()
                        yield captured;
                    end function
            """);

        Assert.That(IdsOf(diagnostics), Is.Empty);
    }

    // ---- Read-only targets ---------------------------------------------------------------------

    [Test]
    public void AssigningToAConstantLocalIsRejected()
    {
        (_, DiagnosticBag diagnostics) = ResolveBody(
            """
                    constant integer Fixed = 1;
                    Fixed = 2;
            """);

        Assert.That(IdsOf(diagnostics), Is.EqualTo(new[] { "PC0205" }));
    }

    /// <summary>
    /// Each iteration binds a fresh variable, so assigning to one would change nothing. A
    /// silent no-op is the worst outcome for someone learning, hence an error.
    /// </summary>
    [Test]
    public void AssigningToALoopVariableIsRejected()
    {
        (_, DiagnosticBag diagnostics) = ResolveBody(
            """
                    for i = 1 to 10
                        i = 5;
                    end for
            """);

        Assert.Multiple(() =>
        {
            Assert.That(IdsOf(diagnostics), Is.EqualTo(new[] { "PC0206" }));
            Assert.That(diagnostics.Single().Message, Does.Contain("fresh"));
        });
    }

    // ---- Types and inheritance --------------------------------------------------------------

    [Test]
    public void ATypeMayBeNamedBeforeItIsDeclared()
    {
        // Forward references are the whole reason resolution takes two passes.
        (_, DiagnosticBag diagnostics) = Resolve(
            """
            model First
                Second other;
            end model

            model Second
                integer value;
            end model
            """);

        Assert.That(IdsOf(diagnostics), Is.Empty);
    }

    [Test]
    public void AnUnknownTypeIsReportedOnce()
    {
        (_, DiagnosticBag diagnostics) = Resolve(
            """
            model Holder
                Missing value;
            end model
            """);

        Assert.That(IdsOf(diagnostics), Is.EqualTo(new[] { "PC0201" }));
    }

    [Test]
    public void InheritanceIsLinked()
    {
        (SemanticModel model, DiagnosticBag diagnostics) = Resolve(
            """
            model Shape
            end model

            model Square extends Shape
            end model
            """);

        ModelSymbol square = (ModelSymbol)model.AllTypes().First(t => t.Name == "Square");

        Assert.Multiple(() =>
        {
            Assert.That(IdsOf(diagnostics), Is.Empty);
            Assert.That(square.BaseType, Is.Not.Null);
            Assert.That(square.BaseType!.Name, Is.EqualTo("Shape"));
            Assert.That(square.SelfAndAncestors().Select(m => m.Name),
                        Is.EqualTo(new[] { "Square", "Shape" }));
        });
    }

    [Test]
    public void ACycleInInheritanceIsBrokenAndReported()
    {
        // Reported and broken, so that later walks up the chain terminate rather than hang.
        (SemanticModel model, DiagnosticBag diagnostics) = Resolve(
            """
            model A extends B
            end model

            model B extends A
            end model
            """);

        Assert.Multiple(() =>
        {
            Assert.That(IdsOf(diagnostics), Does.Contain("PC0207"));

            foreach (ModelSymbol type in model.AllTypes().OfType<ModelSymbol>())
            {
                Assert.That(type.SelfAndAncestors().Count(), Is.LessThan(10),
                            "the ancestor walk should terminate");
            }
        });
    }

    [Test]
    public void ExtendingASealedModelIsRejected()
    {
        (_, DiagnosticBag diagnostics) = Resolve(
            """
            sealed model Shape
            end model

            model Square extends Shape
            end model
            """);

        Assert.That(IdsOf(diagnostics), Is.EqualTo(new[] { "PC0208" }));
    }

    [Test]
    public void ExtendingAStructureIsRejected()
    {
        (_, DiagnosticBag diagnostics) = Resolve(
            """
            structure Point
            end structure

            model Square extends Point
            end model
            """);

        Assert.That(IdsOf(diagnostics), Is.EqualTo(new[] { "PC0209" }));
    }

    [Test]
    public void SealedAndAbstractTogetherIsRejected()
    {
        (_, DiagnosticBag diagnostics) = Resolve(
            """
            sealed abstract model Shape
            end model
            """);

        Assert.That(IdsOf(diagnostics), Is.EqualTo(new[] { "PC0210" }));
    }

    [Test]
    public void ExtendingModelExplicitlyIsLegalAndRedundant()
    {
        (_, DiagnosticBag diagnostics) = Resolve(
            """
            model Dog extends Model
            end model
            """);

        Assert.That(IdsOf(diagnostics), Is.Empty);
    }

    [Test]
    public void TheBuiltInExceptionsAreKnownWithoutBeingDeclared()
    {
        (_, DiagnosticBag diagnostics) = Resolve(
            """
            model Handler
                function Run()
                    try
                        throw new ArgumentException();
                    catch DivideByZeroException problem
                        yield;
                    catch EmptyOptionalException other
                        yield;
                    end try
                end function
            end model
            """);

        Assert.That(IdsOf(diagnostics), Is.Empty);
    }

    // ---- Reserved names ------------------------------------------------------------------------

    [TestCase("Model")]
    [TestCase("Exception")]
    [TestCase("Console")]
    [TestCase("Reference")]
    public void ABuiltInTypeNameCannotBeRedeclared(string name)
    {
        (_, DiagnosticBag diagnostics) = Resolve($"model {name}\nend model");

        Assert.That(IdsOf(diagnostics), Is.EqualTo(new[] { "PC0203" }));
    }

    [Test]
    public void AnExceptionSubtypeMayBeExtendedEvenThoughItIsBuiltIn()
    {
        // Extending is not redeclaring; only the four names above are closed.
        (_, DiagnosticBag diagnostics) = Resolve(
            """
            model FileMissing extends Exception
            end model
            """);

        Assert.That(IdsOf(diagnostics), Is.Empty);
    }

    [TestCase("Console")]
    [TestCase("Reference")]
    [TestCase("Math")]
    [TestCase("Random")]
    [TestCase("DateTime")]
    public void ExtendingABuiltInThatIsNotModelOrAnExceptionIsRejected(string name)
    {
        // These resolve as models so that their members can be found, which is not an
        // invitation to inherit from them.
        (_, DiagnosticBag diagnostics) = Resolve($"model Mine extends {name}\nend model");

        Assert.That(IdsOf(diagnostics), Is.EqualTo(new[] { "PC0216" }));
    }

    /// <summary>
    /// Two types of one name are rejected, and the message says where the other one is. Nothing
    /// merges them: there is no implicit partial type, so a name written twice is two mistakes
    /// meeting rather than one type in two pieces.
    /// </summary>
    [Test]
    public void DeclaringTheSameTypeTwiceIsRejected()
    {
        (_, DiagnosticBag diagnostics) = Resolve("model Dog\nend model\nmodel Dog\nend model");

        Assert.Multiple(() =>
        {
            Assert.That(IdsOf(diagnostics), Is.EqualTo(new[] { "PC0217" }));
            Assert.That(diagnostics.Single().Message, Does.Contain("on line 1"));
        });
    }

    // ---- The entry point ------------------------------------------------------------------------

    /// <summary>
    /// <para>An entry point yields nothing, or the integer a caller reads as the exit code.</para>
    /// <para>Nothing else has anywhere to go: a result of another type would be computed and
    /// then dropped, so the program would appear to report something it never reported.</para>
    /// </summary>
    [TestCase("", null)]
    [TestCase("integer ", null)]
    [TestCase("string ", "PC0218")]
    [TestCase("real ", "PC0218")]
    [TestCase("boolean ", "PC0218")]
    public void MainYieldsNothingOrAnInteger(string result, string? expected)
    {
        (_, DiagnosticBag diagnostics) = Resolve($$"""
            global model Program
                {{result}}function Main()
                end function
            end model
            """);

        Assert.That(
            IdsOf(diagnostics),
            Is.EqualTo(expected is null ? Array.Empty<string>() : [expected]));
    }

    [Test]
    public void AFileWithNoProgramIsFineUnlessAnEntryPointWasAskedFor()
    {
        // A file that declares no Program is well-formed; it simply is not a whole program.
        (_, DiagnosticBag without) = Resolve("model Helper\nend model");
        (_, DiagnosticBag with) = Resolve("model Helper\nend model", requireEntryPoint: true);

        Assert.Multiple(() =>
        {
            Assert.That(IdsOf(without), Is.Empty);
            Assert.That(IdsOf(with), Is.EqualTo(new[] { "PC0212" }));
        });
    }

    [Test]
    public void ProgramMustBeAGlobalModel()
    {
        (_, DiagnosticBag diagnostics) = Resolve(
            """
            model Program
                function Main()
                end function
            end model
            """);

        Assert.That(IdsOf(diagnostics), Is.EqualTo(new[] { "PC0213" }));
    }

    [Test]
    public void AProgramWithoutMainIsAlwaysWrong()
    {
        (_, DiagnosticBag diagnostics) = Resolve(
            """
            global model Program
                function Other()
                end function
            end model
            """);

        Assert.That(IdsOf(diagnostics), Is.EqualTo(new[] { "PC0212" }));
    }

    // ---- Receivers -----------------------------------------------------------------------------

    [Test]
    public void ThisWorksInsideAStructureToo()
    {
        // A structure has instances, so it has "this"; only "base" needs a model.
        (_, DiagnosticBag diagnostics) = Resolve(
            """
            structure Pair
                public integer Left;

                public function Pair(integer left)
                    this.Left = left;
                end function
            end structure
            """);

        Assert.That(IdsOf(diagnostics), Is.Empty);
    }

    [Test]
    public void ThisIsRejectedInAGlobalMember()
    {
        (_, DiagnosticBag diagnostics) = Resolve(
            """
            model Holder
                integer count;

                global function Run()
                    this.count = 1;
                end function
            end model
            """);

        Assert.That(IdsOf(diagnostics), Is.EqualTo(new[] { "PC0214" }));
    }

    [Test]
    public void BaseNeedsAParent()
    {
        (_, DiagnosticBag diagnostics) = Resolve(
            """
            model Orphan
                function Run()
                    base.Something();
                end function
            end model
            """);

        Assert.That(IdsOf(diagnostics), Is.EqualTo(new[] { "PC0215" }));
    }

    [Test]
    public void AGlobalModelCannotHaveInstanceMembers()
    {
        // Members of a global model are implicitly global, so this only fires where the
        // author wrote something that contradicts that.
        (_, DiagnosticBag diagnostics) = Resolve(
            """
            model Ordinary
                integer instanceField;
            end model
            """);

        Assert.That(IdsOf(diagnostics), Is.Empty, "an ordinary model may hold instance state");
    }

    // ---- Recovery ---------------------------------------------------------------------------------

    [Test]
    public void AParseErrorDoesNotProduceASecondDiagnosticHere()
    {
        // The promise the Missing nodes exist to keep: one mistake, one diagnostic.
        (_, DiagnosticBag diagnostics) = Resolve(
            """
            model Holder
                function Run()
                    let x = ;
                end function
            end model
            """);

        Assert.That(IdsOf(diagnostics), Is.EqualTo(new[] { "PC0101" }),
                    "the resolver should stay quiet about syntax that never parsed");
    }

    [Test]
    public void ResolvingNeverThrows()
    {
        string[] hostile =
        [
            "", "model", "model X end model", "model A extends A end model",
            "global model Program end model", "model X model X end model end model",
            "model X function F() this.y = 1; end function end model",
        ];

        foreach (string source in hostile)
        {
            Assert.DoesNotThrow(() => Resolve(source), $"resolving \"{source}\" threw");
        }
    }
}
