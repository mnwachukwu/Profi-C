using System.Reflection;
using ProfiC.Compiler.Ast;

namespace ProfiC.Tests.Ast;

/// <summary>
/// Structural properties of the node hierarchy itself. These are the checks that catch a node
/// added later without being wired in properly.
/// </summary>
[TestFixture]
public sealed class SyntaxNodeTests : AstTestBase
{
    /// <summary>Every concrete node type in the compiler assembly.</summary>
    public static IEnumerable<Type> NodeTypes =>
        typeof(SyntaxNode).Assembly
            .GetTypes()
            .Where(t => t.IsSubclassOf(typeof(SyntaxNode)) && !t.IsAbstract)
            .OrderBy(t => t.Name, StringComparer.Ordinal);

    public static IEnumerable<string> NodeTypeNames => NodeTypes.Select(t => t.Name);

    [Test]
    public void EveryNodeType_IsSealed()
    {
        // An unsealed node would let someone subclass it and silently miss the visitor
        // dispatch, since Accept is what routes to the right method.
        Assert.That(NodeTypes.Where(t => !t.IsSealed), Is.Empty);
    }

    [Test]
    public void EveryNodeType_HasAVisitorMethod()
    {
        // The failure this catches: adding a node class and forgetting to add its visit
        // method, which would compile but leave the node unreachable by any pass.
        MethodInfo[] methods = typeof(SyntaxVisitor).GetMethods();

        List<string> unreachable = [];

        foreach (Type type in NodeTypes)
        {
            bool hasMethod = methods.Any(m =>
                m.Name.StartsWith("Visit", StringComparison.Ordinal)
                && m.GetParameters() is [ParameterInfo p] && p.ParameterType == type);

            if (!hasMethod)
            {
                unreachable.Add(type.Name);
            }
        }

        Assert.That(unreachable, Is.Empty, "these node types have no visitor method");
    }

    [Test]
    public void BothVisitors_ExposeTheSameMethodNames()
    {
        static IEnumerable<string> VisitMethods(Type t) =>
            t.GetMethods()
             .Where(m => m.Name.StartsWith("Visit", StringComparison.Ordinal) && m.Name != "Visit")
             .Select(m => m.Name)
             .OrderBy(n => n, StringComparer.Ordinal);

        Assert.That(VisitMethods(typeof(SyntaxVisitor<int>)),
                    Is.EqualTo(VisitMethods(typeof(SyntaxVisitor))));
    }

    [Test]
    public void NodeKind_DefaultsToTheClassName()
    {
        Assert.That(Id("x").NodeKind, Is.EqualTo("IdentifierExpr"));
        Assert.That(Int("1").NodeKind, Is.EqualTo("LiteralExpr"));
    }

    [Test]
    public void ReceiverExpr_ReportsWhichReceiverItIs()
    {
        // There are only two. A nested model holds no reference to the model it sits inside,
        // so there is no third receiver to name.
        Assert.Multiple(() =>
        {
            Assert.That(new ReceiverExpr(NextSpan(), ReceiverKind.This).NodeKind,
                        Is.EqualTo("ThisExpr"));
            Assert.That(new ReceiverExpr(NextSpan(), ReceiverKind.Base).NodeKind,
                        Is.EqualTo("BaseExpr"));
            Assert.That(Enum.GetValues<ReceiverKind>(), Has.Length.EqualTo(2));
        });
    }

    [Test]
    public void EveryNode_ReportsItsSpanAsLineAndColumn()
    {
        IdentifierExpr node = new(SpanAt(7, 13, 100, 3), "value");

        Assert.Multiple(() =>
        {
            Assert.That(node.Line, Is.EqualTo(7));
            Assert.That(node.Column, Is.EqualTo(13));
            Assert.That(node.Span.Length, Is.EqualTo(3));
            Assert.That(node.Span.EndOffset, Is.EqualTo(103));
        });
    }

    [Test]
    public void Children_AreReturnedInSourceOrder()
    {
        BinaryExpr sum = Binary(Id("a"), BinaryOperator.Add, Id("b"));

        Assert.That(sum.Children.Cast<IdentifierExpr>().Select(n => n.Name),
                    Is.EqualTo(new[] { "a", "b" }));
    }

    [Test]
    public void Children_OmitAbsentOptionalParts()
    {
        YieldStmt bare = new(NextSpan(), null);
        YieldStmt valued = new(NextSpan(), Int("1"));

        Assert.Multiple(() =>
        {
            Assert.That(bare.Children, Is.Empty);
            Assert.That(valued.Children.Count(), Is.EqualTo(1));
        });
    }

    [Test]
    public void Descendants_ReachesEveryNodeDepthFirst()
    {
        // (a + b) * c
        BinaryExpr tree = Binary(
            new ParenthesizedExpr(NextSpan(), Binary(Id("a"), BinaryOperator.Add, Id("b"))),
            BinaryOperator.Multiply,
            Id("c"));

        Assert.That(tree.Descendants().OfType<IdentifierExpr>().Select(n => n.Name),
                    Is.EqualTo(new[] { "a", "b", "c" }));
    }

    [Test]
    public void Descendants_SurvivesDeepNestingWithoutExhaustingTheStack()
    {
        // A recursive walk would overflow here. Ten thousand levels is well beyond any real
        // program, but an expression tree is exactly where an attacker or a generator would
        // find the limit.
        Expression deep = Id("x");

        for (int i = 0; i < 10_000; i++)
        {
            deep = new ParenthesizedExpr(NextSpan(), deep);
        }

        Assert.That(deep.Descendants().Count(), Is.EqualTo(10_000));
    }
}
