using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Text;
using ProfiC.Services;

namespace ProfiC.Tests.LanguageServer;

/// <summary>
/// <para>Which stretches of a file fold, and what each says it holds.</para>
/// <para>The last fixture is the one that carries the weight: an editor given ranges that
/// half-overlap discards the ones it cannot make sense of, silently, so a file folds in some
/// places and not others and nothing anywhere says why. Held against the whole corpus, since
/// that is where a construct nobody thought of shows up.</para>
/// </summary>
[TestFixture]
public sealed class FoldingTests : LexerTestBase
{
    private static IReadOnlyList<Folding.Range> Fold(string text)
    {
        SourceText source = new(text, "Program.pc");
        DiagnosticBag diagnostics = new();

        return Folding.Of(Parser.Parse(source, diagnostics), source);
    }

    private static Folding.Range At(IReadOnlyList<Folding.Range> found, int line) =>
        found.Single(range => range.Line == line);

    private const string Counting = """
        shared model Program

            ##
                @summary: Counts up to a limit, and says so.
                @limit: how far to count.
            ##
            function CountTo(integer limit)
                loop for at = 1 to limit
                    Console.WriteLine(at);
                end loop
            end function

            function Main()
                Program.CountTo(3);
            end function

        end model
        """;

    [Test]
    public void EveryBlockFolds()
    {
        IReadOnlyList<Folding.Range> found = Fold(Counting);

        Assert.Multiple(() =>
        {
            // The model, the documentation, both functions, and the loop inside one of them.
            Assert.That(found.Select(range => range.Line), Is.EqualTo(new[] { 1, 3, 7, 8, 13 }));
            Assert.That(At(found, 1).EndLine, Is.EqualTo(17));
            Assert.That(At(found, 7).EndLine, Is.EqualTo(11));
            Assert.That(At(found, 8).EndLine, Is.EqualTo(10));
        });
    }

    [Test]
    public void ADocumentationCommentFoldsAsAComment()
    {
        Assert.That(At(Fold(Counting), 3).Kind, Is.EqualTo(Folding.Comment));
    }

    [Test]
    public void ADocumentedBlockHoldsWhatItsSummarySays()
    {
        Assert.That(At(Fold(Counting), 7).Held, Is.EqualTo("Counts up to a limit, and says so."));
    }

    [Test]
    public void AnythingElseHoldsHowMuchThereIs()
    {
        Assert.Multiple(() =>
        {
            Assert.That(At(Fold(Counting), 13).Held, Is.EqualTo("2 lines"));
            Assert.That(At(Fold(Counting), 8).Held, Is.EqualTo("2 lines"));
        });
    }

    [Test]
    public void ASummaryIsCutToItsFirstSentence()
    {
        const string ThreeSentences = """
            shared model Program

                ##
                    @summary: Says hello. Then says nothing else at all. And stops.
                ##
                function Main()
                    Console.WriteLine("hi");
                end function

            end model
            """;

        Assert.That(At(Fold(ThreeSentences), 6).Held, Is.EqualTo("Says hello."));
    }

    [Test]
    public void ABlockOnOneLineDoesNotFold()
    {
        const string Inline = """
            shared model Program

                function Main()
                    if true Console.WriteLine("hi"); end if
                end function

            end model
            """;

        Assert.Multiple(() =>
        {
            // The if opens and closes on line 4, so there is nothing between to hide. Asserted
            // beside the function around it, since a file that failed to parse would also fold
            // nothing at line 4 and would prove nothing.
            Assert.That(Fold(Inline).Select(range => range.Line), Does.Not.Contain(4));
            Assert.That(Fold(Inline).Select(range => range.Line), Does.Contain(3));
        });
    }

    [Test]
    public void AFileScopedNamespaceDoesNotFoldAndABlockOneDoes()
    {
        const string Scoped = """
            namespace Counting;

            shared model Program

                function Main()
                    Console.WriteLine("hi");
                end function

            end model
            """;

        const string Blocked = """
            namespace Counting

                shared model Program

                    function Main()
                        Console.WriteLine("hi");
                    end function

                end model

            end namespace
            """;

        Assert.Multiple(() =>
        {
            Assert.That(Fold(Scoped).Select(range => range.Line), Does.Not.Contain(1));
            Assert.That(Fold(Blocked).Select(range => range.Line), Does.Contain(1));
        });
    }

    [Test]
    public void AFunctionLeftForADescendantDoesNotFold()
    {
        const string Abstract = """
            abstract model Shape

                abstract real function Area();

            end model
            """;

        Assert.That(Fold(Abstract).Select(range => range.Line), Is.EqualTo(new[] { 1 }));
    }

    [Test]
    public void ASwitchFoldsAndSoDoesEachOfItsGroups()
    {
        const string Choosing = """
            shared model Program

                function Main()
                    switch 1
                        case 1:
                            Console.WriteLine("one");
                            Console.WriteLine("still one");
                        default:
                            Console.WriteLine("other");
                    end switch
                end function

            end model
            """;

        IReadOnlyList<int> opened = [.. Fold(Choosing).Select(range => range.Line)];

        Assert.Multiple(() =>
        {
            Assert.That(opened, Does.Contain(4), "the switch");
            Assert.That(opened, Does.Contain(5), "the case group");
        });
    }

    [Test]
    public void ATryFoldsAndSoDoesEachOfItsClauses()
    {
        const string Guarding = """
            shared model Program

                function Main()
                    try
                        Console.WriteLine("go");
                        Console.WriteLine("on");
                    catch Exception problem
                        Console.WriteLine(problem.Message());
                        Console.WriteLine("caught");
                    end try
                end function

            end model
            """;

        IReadOnlyList<int> opened = [.. Fold(Guarding).Select(range => range.Line)];

        Assert.Multiple(() =>
        {
            Assert.That(opened, Does.Contain(4), "the try");
            Assert.That(opened, Does.Contain(7), "the catch");
        });
    }

    /// <summary>
    /// A file being written is the one folding is most wanted in, and it is the one that does not
    /// compile. The parser recovers, so the blocks around the mistake still fold.
    /// </summary>
    [Test]
    public void AFileThatDoesNotCompileStillFolds()
    {
        const string Broken = """
            shared model Program

                function Main()
                    let x = ;
                    Console.WriteLine("after");
                end function

            end model
            """;

        Assert.That(Fold(Broken).Select(range => range.Line), Does.Contain(3));
    }

    [Test]
    [TestCaseSource(nameof(SampleNames))]
    public void ASampleFoldsIntoRangesThatNest(string name)
    {
        SourceText source = LoadSample(name);
        DiagnosticBag diagnostics = new();
        CompilationUnit unit = Parser.Parse(source, diagnostics);

        IReadOnlyList<Folding.Range> found = Folding.Of(unit, source);

        Assert.Multiple(() =>
        {
            foreach (Folding.Range range in found)
            {
                Assert.That(range.Line, Is.GreaterThanOrEqualTo(1));
                Assert.That(range.EndLine, Is.GreaterThan(range.Line));
                Assert.That(range.EndLine, Is.LessThanOrEqualTo(source.LineCount));
                Assert.That(range.Held, Is.Not.Empty);
            }

            // Every pair is one inside the other or clear of it. An editor cannot draw a control
            // for a range that starts inside another and ends outside it, and drops it instead.
            foreach (Folding.Range outer in found)
            {
                foreach (Folding.Range inner in found)
                {
                    bool crosses =
                        inner.Line > outer.Line
                        && inner.Line <= outer.EndLine
                        && inner.EndLine > outer.EndLine;

                    Assert.That(crosses, Is.False,
                                $"{name}: {inner.Line}-{inner.EndLine} straddles the end of "
                                + $"{outer.Line}-{outer.EndLine}");
                }
            }
        });
    }
}
