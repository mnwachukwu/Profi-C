using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Text;

namespace ProfiC.Tests.Parsing;

/// <summary>
/// <para>Where a name is written, as against where the thing that has it is written.</para>
/// <para>Two questions that are the same for a literal and quite different for a function, whose
/// span runs from its first modifier to its <c>end function</c> while its name is one identifier
/// somewhere inside. Everything that writes over a name — renaming one, revealing one, selecting
/// one — needs the identifier and not the declaration.</para>
/// <para><b>Asserted against the source text rather than against numbers.</b> A span is three
/// integers, and three integers that are wrong look exactly like three integers that are right.
/// Slicing the source with them and comparing the result to the name says what is meant, and says
/// it in a form that fails readably.</para>
/// </summary>
[TestFixture]
public sealed class NameSpanTests : LexerTestBase
{
    private static readonly string EveryDeclaration = """
        namespace Shapes

            enumeration Color
                Red,
                Green
            end enumeration

            structure Point
                public integer X;
            end structure

            shared model Program
                integer count;

                public shared string label = "n";

                function Main()
                    integer total = 0;
                    Point where;

                    loop each n in {1, 2}
                        total = total + n;
                    end loop

                    loop for i = 1 to 2
                        total = total + i;
                    end loop

                    try
                        Console.WriteLine(total);
                    catch DivisionByZeroException caught
                        Console.WriteLine(caught.Message);
                    end try
                end function

                integer function Twice(integer value)
                    yield value + value;
                end function
            end model

        end namespace
        """;

    private static (SourceText Source, CompilationUnit Unit) Parse(string text)
    {
        SourceText source = new(text, "<test>");
        DiagnosticBag diagnostics = new();
        CompilationUnit unit = Parser.Parse(source, diagnostics);

        Assert.That(
            diagnostics.Sorted().Select(d => $"{d.Id}: {d.Message}"),
            Is.Empty,
            "the fixture should parse cleanly");

        return (source, unit);
    }

    private static string Sliced(SourceText source, SourceSpan span) =>
        source.Text.Substring(span.Start.Offset, span.Length);

    private static IEnumerable<SyntaxNode> Everything(SyntaxNode node) =>
        [node, .. node.Descendants()];

    /// <summary>
    /// <para>Every name a declaration introduces is where the node says it is.</para>
    /// <para>One case per kind of declaration, gathered into one assertion because what is being
    /// checked is the same claim about each: slice the source at the span, get the name back.
    /// </para>
    /// </summary>
    [Test]
    public void EveryDeclarationPointsAtItsOwnName()
    {
        (SourceText source, CompilationUnit unit) = Parse(EveryDeclaration);

        List<string> found = [];

        foreach (SyntaxNode node in Everything(unit))
        {
            string? expected = node switch
            {
                EnumerationDecl enumeration => enumeration.Name,
                EnumMemberDecl member => member.Name,
                StructureDecl structure => structure.Name,
                ModelDecl model => model.Name,
                FieldDecl field => field.Name,
                FunctionDecl function => function.Name,
                ParameterDecl parameter => parameter.Name,
                VarDeclStmt local => local.Name,
                CatchClause clause => clause.VariableName,
                _ => null,
            };

            if (expected is null)
            {
                continue;
            }

            found.Add(expected);

            Assert.That(
                Sliced(source, node.NameSpan),
                Is.EqualTo(expected),
                $"{node.NodeKind} on line {node.Line}");
        }

        // That the walk reached what it was written to reach, rather than passing because it
        // found nothing to check.
        Assert.That(
            found,
            Is.SupersetOf(new[]
            {
                "Color", "Red", "Point", "X", "Program", "count", "label",
                "Main", "Twice", "value", "total", "where", "caught",
            }));
    }

    /// <summary>A member access names the member, not the receiver and not the whole access.</summary>
    [Test]
    public void AMemberAccessPointsAtTheMember()
    {
        (SourceText source, CompilationUnit unit) = Parse(EveryDeclaration);

        IEnumerable<string> named = Everything(unit)
            .OfType<MemberExpr>()
            .Select(member => Sliced(source, member.NameSpan));

        Assert.That(named, Is.EquivalentTo(new[] { "WriteLine", "WriteLine", "Message" }));
    }

    /// <summary>
    /// <para>A qualified type names the type, not what qualified it.</para>
    /// <para>The one case where the name is neither the start of the node nor the whole of it:
    /// <c>Shapes.Point</c> is one node whose name is its last five characters.</para>
    /// </summary>
    [Test]
    public void AQualifiedTypePointsAtTheLastPart()
    {
        (SourceText source, CompilationUnit unit) = Parse("""
            shared model Program
                function Main()
                    Shapes.Point where;
                end function
            end model
            """);

        NamedTypeSyntax named = Everything(unit)
            .OfType<NamedTypeSyntax>()
            .Single(type => type.IsQualified);

        Assert.Multiple(() =>
        {
            Assert.That(Sliced(source, named.Span), Is.EqualTo("Shapes.Point"));
            Assert.That(Sliced(source, named.NameSpan), Is.EqualTo("Point"));
        });
    }

    /// <summary>
    /// <para>Across every sample: a recorded name is inside its node and is spelled like one.
    /// </para>
    /// <para>The property that holds for all of them rather than for the cases somebody thought
    /// to write down. A name that ran past its node, or that covered a comma or a whole
    /// declaration, would be caught here without anyone having anticipated the construct it
    /// happened in.</para>
    /// </summary>
    [TestCaseSource(nameof(SampleNames))]
    public void Sample_EveryRecordedNameIsAName(string name)
    {
        SourceText source = LoadSample(name);
        DiagnosticBag diagnostics = new();

        foreach (SyntaxNode node in Everything(Parser.Parse(source, diagnostics)))
        {
            if (!node.HasName)
            {
                continue;
            }

            string named = Sliced(source, node.NameSpan);

            Assert.Multiple(() =>
            {
                Assert.That(
                    node.NameSpan.Start.Offset,
                    Is.GreaterThanOrEqualTo(node.Span.Start.Offset),
                    $"{node.NodeKind} on line {node.Line} begins before the node it is in");

                Assert.That(
                    node.NameSpan.EndOffset,
                    Is.LessThanOrEqualTo(node.Span.EndOffset),
                    $"{node.NodeKind} on line {node.Line} runs past the node it is in");

                Assert.That(
                    named,
                    Is.Not.Empty,
                    $"{node.NodeKind} on line {node.Line} recorded an empty name");

                Assert.That(
                    named.All(c => char.IsLetterOrDigit(c) || c == '_'),
                    Is.True,
                    $"{node.NodeKind} on line {node.Line} named '{named}'");
            });
        }
    }
}
