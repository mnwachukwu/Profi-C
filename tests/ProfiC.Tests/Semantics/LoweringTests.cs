using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Semantics;
using ProfiC.Compiler.Text;

namespace ProfiC.Tests.Semantics;

/// <summary>
/// Lowering: conversions made explicit, and iteration rewritten so that neither the
/// interpreter nor the emitter implements it separately.
/// </summary>
[TestFixture]
public sealed class LoweringTests
{
    private static (CompilationUnit Lowered, SemanticModel Model, DiagnosticBag Diagnostics)
        Lower(string source)
    {
        DiagnosticBag diagnostics = new();
        CompilationUnit unit = Parser.Parse(new SourceText(source, "<test>"), diagnostics);
        SemanticModel model = Resolver.Resolve(unit, diagnostics);
        TypeChecker.Check(unit, model, diagnostics);
        DefiniteAssignment.Analyze(unit, model, diagnostics);

        return (Lowering.Lower(unit, model), model, diagnostics);
    }

    private static CompilationUnit LowerBody(string body)
    {
        (CompilationUnit lowered, _, DiagnosticBag diagnostics) = Lower($$"""
            global model Program
                function Main()
            {{body}}
                end function
            end model
            """);

        Assert.That(diagnostics.Select(d => d.Message), Is.Empty, "the snippet should check cleanly");
        return lowered;
    }

    private static IReadOnlyList<ConversionExpr> ConversionsIn(SyntaxNode tree) =>
        [.. tree.Descendants().OfType<ConversionExpr>()];

    // ---- Conversions ------------------------------------------------------------------------

    [TestCase("        real r = 1;", ConversionOperation.IntegerToReal)]
    [TestCase("        fraction f = 2;", ConversionOperation.IntegerToFraction)]
    [TestCase("        integer? maybe = 5;", ConversionOperation.WrapOptional)]
    [TestCase("        character[] letters = \"abc\";", ConversionOperation.StringToCharacters)]
    [TestCase("        string s = \"a\";", null)]
    public void AnImplicitConversionBecomesARealNode(string body, ConversionOperation? expected)
    {
        IReadOnlyList<ConversionExpr> conversions = ConversionsIn(LowerBody(body));

        if (expected is null)
        {
            Assert.That(conversions, Is.Empty, "nothing needed converting");
            return;
        }

        Assert.That(conversions, Has.Count.EqualTo(1));
        Assert.That(conversions[0].Operation, Is.EqualTo(expected));
    }

    [Test]
    public void ACharacterSetBecomingAStringIsRecordedToo()
    {
        CompilationUnit lowered = LowerBody(
            """
                    character[] letters = {'a', 'b'};
                    string rebuilt = letters;
            """);

        Assert.That(ConversionsIn(lowered).Select(c => c.Operation),
                    Is.EqualTo(new[] { ConversionOperation.CharactersToString }));
    }

    [Test]
    public void AConversionKeepsTheTypeItProduces()
    {
        (CompilationUnit lowered, SemanticModel model, _) = Lower(
            """
            global model Program
                function Main()
                    real r = 1;
                end function
            end model
            """);

        ConversionExpr conversion = ConversionsIn(lowered).Single();

        Assert.That(model.GetType(conversion), Is.SameAs(PrimitiveType.Real));
    }

    [Test]
    public void ReachingAnAncestorNeedsNoConversionAtRunTime()
    {
        // An upcast changes nothing about the value, so nothing is inserted for it.
        (CompilationUnit lowered, _, _) = Lower(
            """
            model Shape
            end model

            model Square extends Shape
            end model

            global model Program
                function Main()
                    Shape s = new Square();
                end function
            end model
            """);

        Assert.That(ConversionsIn(lowered), Is.Empty);
    }

    [Test]
    public void ArgumentsAreConvertedToo()
    {
        (CompilationUnit lowered, _, _) = Lower(
            """
            global model Program
                function Take(real value)
                end function

                function Main()
                    Program.Take(1);
                end function
            end model
            """);

        Assert.That(ConversionsIn(lowered).Select(c => c.Operation),
                    Is.EqualTo(new[] { ConversionOperation.IntegerToReal }));
    }

    // ---- Iteration ----------------------------------------------------------------------------

    [Test]
    public void ForEachIsRewrittenAsAnIndexLoop()
    {
        CompilationUnit lowered = LowerBody(
            """
                    integer[] numbers = {1, 2};
                    for each n in numbers
                        let copy = n;
                    end for
            """);

        Assert.Multiple(() =>
        {
            Assert.That(lowered.Descendants().OfType<ForEachStmt>(), Is.Empty,
                        "no 'for each' should survive lowering");
            Assert.That(lowered.Descendants().OfType<ForStmt>().Count(), Is.EqualTo(1));
        });
    }

    [Test]
    public void TheSequenceIsEvaluatedOnceIntoATemporary()
    {
        // The expression may have effects, so it must not run once per iteration.
        CompilationUnit lowered = LowerBody(
            """
                    integer[] numbers = {1, 2};
                    for each n in numbers
                        let copy = n;
                    end for
            """);

        ForStmt loop = lowered.Descendants().OfType<ForStmt>().Single();
        BlockStmt wrapper = lowered.Descendants().OfType<BlockStmt>().Single();

        Assert.Multiple(() =>
        {
            // A block holding the temporary, then the loop, so neither escapes.
            Assert.That(wrapper.Statements, Has.Count.EqualTo(2));
            Assert.That(wrapper.Statements[0], Is.TypeOf<VarDeclStmt>());
            Assert.That(wrapper.Statements[1], Is.SameAs(loop));
        });
    }

    /// <summary>
    /// The element is declared inside the body, so each iteration binds a new one. That is
    /// what removes the capture trap a shared loop variable creates.
    /// </summary>
    [Test]
    public void TheElementIsBoundFreshInsideEachIteration()
    {
        CompilationUnit lowered = LowerBody(
            """
                    integer[] numbers = {1, 2};
                    for each n in numbers
                        let copy = n;
                    end for
            """);

        ForStmt loop = lowered.Descendants().OfType<ForStmt>().Single();
        VarDeclStmt first = (VarDeclStmt)loop.Body[0];

        Assert.Multiple(() =>
        {
            Assert.That(first.Name, Is.EqualTo("n"), "the element is declared inside the body");
            Assert.That(first.Initializer, Is.TypeOf<IndexExpr>());
        });
    }

    [Test]
    public void TheLoopCountsFromZeroUpToTheCountExclusively()
    {
        CompilationUnit lowered = LowerBody(
            """
                    integer[] numbers = {1, 2};
                    for each n in numbers
                        let copy = n;
                    end for
            """);

        ForStmt loop = lowered.Descendants().OfType<ForStmt>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(loop.IsInclusive, Is.False, "an index runs up to the count, not through it");
            Assert.That(((LiteralExpr)loop.Start).Text, Is.EqualTo("0"));
            Assert.That(loop.Bound, Is.TypeOf<CallExpr>());
            Assert.That(((MemberExpr)((CallExpr)loop.Bound).Callee).MemberName, Is.EqualTo("Count"));
        });
    }

    /// <summary>
    /// A string works unchanged, because its members were deliberately made to mirror a
    /// set's: it answers Count() and indexes to characters.
    /// </summary>
    [Test]
    public void IteratingAStringLowersTheSameWay()
    {
        CompilationUnit lowered = LowerBody(
            """
                    for each letter in "abc"
                        let copy = letter;
                    end for
            """);

        Assert.Multiple(() =>
        {
            Assert.That(lowered.Descendants().OfType<ForEachStmt>(), Is.Empty);
            Assert.That(lowered.Descendants().OfType<ForStmt>().Count(), Is.EqualTo(1));
        });
    }

    [Test]
    public void TemporaryNamesCannotCollideWithAnythingWritten()
    {
        CompilationUnit lowered = LowerBody(
            """
                    integer[] numbers = {1};
                    for each n in numbers
                        let copy = n;
                    end for
            """);

        IEnumerable<string> synthesized = lowered.Descendants()
            .OfType<VarDeclStmt>()
            .Select(v => v.Name)
            .Where(n => n.StartsWith('<'));

        // Angle brackets are not identifier characters, so no program can write one of these.
        Assert.That(synthesized, Is.Not.Empty);
        Assert.That(synthesized, Is.All.Matches<string>(n => n.Contains('<') && n.Contains('>')));
    }

    [Test]
    public void NestedIterationLowersBothLoops()
    {
        CompilationUnit lowered = LowerBody(
            """
                    integer[] outer = {1};
                    integer[] inner = {2};
                    for each a in outer
                        for each b in inner
                            let sum = a + b;
                        end for
                    end for
            """);

        Assert.Multiple(() =>
        {
            Assert.That(lowered.Descendants().OfType<ForEachStmt>(), Is.Empty);
            Assert.That(lowered.Descendants().OfType<ForStmt>().Count(), Is.EqualTo(2));
        });
    }

    // ---- Structure --------------------------------------------------------------------------------

    [Test]
    public void ParenthesesAreDroppedSinceTheTreeAlreadyRecordsGrouping()
    {
        CompilationUnit lowered = LowerBody("        let x = (1 + 2) * 3;");

        Assert.That(lowered.Descendants().OfType<ParenthesizedExpr>(), Is.Empty);

        // The grouping itself survives, which is the point: only the punctuation is gone.
        BinaryExpr outerMost = lowered.Descendants().OfType<BinaryExpr>().First();
        Assert.That(outerMost.Operator, Is.EqualTo(BinaryOperator.Multiply));
        Assert.That(outerMost.Left, Is.TypeOf<BinaryExpr>());
    }

    [Test]
    public void LoweringPreservesEverythingElse()
    {
        CompilationUnit lowered = LowerBody(
            """
                    integer x = 1;
                    if x > 0
                        x = 2;
                    else
                        x = 3;
                    end if
                    while x > 0
                        x = x - 1;
                    end while
                    switch x
                        case 0:
                            x = 1;
                        default:
                            x = 2;
                    end switch
                    try
                        throw new ArgumentException();
                    catch Exception problem
                        x = 0;
                    finally
                        x = 9;
                    end try
            """);

        Assert.Multiple(() =>
        {
            Assert.That(lowered.Descendants().OfType<IfStmt>().Count(), Is.EqualTo(1));
            Assert.That(lowered.Descendants().OfType<WhileStmt>().Count(), Is.EqualTo(1));
            Assert.That(lowered.Descendants().OfType<SwitchStmt>().Count(), Is.EqualTo(1));
            Assert.That(lowered.Descendants().OfType<TryStmt>().Count(), Is.EqualTo(1));
        });
    }

    [Test]
    public void LoweringIsDeterministic()
    {
        CompilationUnit first = LowerBody("        integer[] n = {1}; for each x in n\n let c = x;\n end for");
        CompilationUnit second = LowerBody("        integer[] n = {1}; for each x in n\n let c = x;\n end for");

        Assert.That(AstPrinter.Print(second), Is.EqualTo(AstPrinter.Print(first)));
    }

    [Test]
    public void EverySampleLowersWithoutTrouble()
    {
        string root = LexerTestBase.RepositoryRootForTests;

        foreach (string path in Directory.EnumerateFiles(Path.Combine(root, "samples"), "*.pc"))
        {
            SourceText source = SourceText.FromFile(path);
            DiagnosticBag diagnostics = new();

            CompilationUnit unit = Parser.Parse(source, diagnostics);
            SemanticModel model = Resolver.Resolve(unit, diagnostics);
            TypeChecker.Check(unit, model, diagnostics);

            Assert.DoesNotThrow(
                () => Lowering.Lower(unit, model),
                $"lowering {Path.GetFileName(path)} threw");
        }
    }
}
