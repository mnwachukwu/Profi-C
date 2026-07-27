using ProfiC.Compiler.Ast;

namespace ProfiC.Tests.Ast;

/// <summary>Visitor dispatch and traversal.</summary>
[TestFixture]
public sealed class SyntaxVisitorTests : AstTestBase
{
    /// <summary>Records the kind of every node it reaches, in order.</summary>
    private sealed class RecordingVisitor : SyntaxVisitor
    {
        public List<string> Visited { get; } = [];

        protected override void DefaultVisit(SyntaxNode node)
        {
            Visited.Add(node.NodeKind);
            base.DefaultVisit(node);
        }
    }

    /// <summary>Counts identifiers without restating how to walk anything else.</summary>
    private sealed class IdentifierCounter : SyntaxVisitor
    {
        public int Count { get; private set; }

        public override void VisitIdentifierExpr(IdentifierExpr node) => Count++;
    }

    /// <summary>Sums integer literals, showing the result-returning form.</summary>
    private sealed class LiteralSummer : SyntaxVisitor<int>
    {
        protected override int DefaultVisit(SyntaxNode node) =>
            node.Children.Sum(child => child.Accept(this));

        public override int VisitLiteralExpr(LiteralExpr node) =>
            node.Kind == LiteralKind.Integer ? int.Parse(node.Text) : 0;
    }

    [Test]
    public void Visitor_ReachesEveryNodeInTheTree()
    {
        BinaryExpr tree = Binary(Id("a"), BinaryOperator.Add, Int("1"));
        RecordingVisitor visitor = new();

        visitor.Visit(tree);

        Assert.That(visitor.Visited,
                    Is.EqualTo(new[] { "BinaryExpr", "IdentifierExpr", "LiteralExpr" }));
    }

    [Test]
    public void Visitor_DescendsThroughDeclarationsIntoStatements()
    {
        CompilationUnit unit = Unit(
            Model("Program",
                DeclarationModifiers.Global,
                members: [Function("Main", body: [new ExpressionStmt(NextSpan(), Id("x"))])]));

        RecordingVisitor visitor = new();
        visitor.Visit(unit);

        Assert.That(visitor.Visited, Is.EqualTo(new[]
        {
            "CompilationUnit", "ModelDecl", "FunctionDecl", "ExpressionStmt", "IdentifierExpr",
        }));
    }

    [Test]
    public void Visitor_OverridingOneMethod_StopsTraversalThere()
    {
        // IdentifierCounter does not call the base, so it never descends past an identifier.
        // That is the correct default: an override owns its subtree.
        BinaryExpr tree = Binary(
            Binary(Id("a"), BinaryOperator.Add, Id("b")),
            BinaryOperator.Multiply,
            Id("c"));

        IdentifierCounter counter = new();
        counter.Visit(tree);

        Assert.That(counter.Count, Is.EqualTo(3));
    }

    [Test]
    public void ResultVisitor_ComputesOverTheTree()
    {
        // 1 + (2 * 3)
        BinaryExpr tree = Binary(
            Int("1"),
            BinaryOperator.Add,
            new ParenthesizedExpr(NextSpan(), Binary(Int("2"), BinaryOperator.Multiply, Int("3"))));

        Assert.That(new LiteralSummer().Visit(tree), Is.EqualTo(6));
    }

    [Test]
    public void Visitor_HandlesEveryStatementForm()
    {
        List<Statement> statements =
        [
            new BlockStmt(NextSpan(), []),
            new VarDeclStmt(NextSpan(), Named("integer"), "x", Int("1"), isConstant: false),
            new IfStmt(NextSpan(), Id("c"), [], [], null),
            new WhileStmt(NextSpan(), Id("c"), []),
            new ForStmt(NextSpan(), Named("integer"), "i", Int("1"), Int("10"), true, null, []),
            new ForEachStmt(NextSpan(), "item", Id("items"), []),
            new SwitchStmt(NextSpan(), Id("code"), [], null),
            new TryStmt(NextSpan(), [], [], null),
            new ThrowStmt(NextSpan(), Id("problem")),
            new YieldStmt(NextSpan(), null),
            new BreakStmt(NextSpan()),
            new ContinueStmt(NextSpan()),
            new ExpressionStmt(NextSpan(), Id("f")),
            new AssignmentStmt(NextSpan(), Id("x"), Int("1")),
            new LocalDeclStmt(NextSpan(), Function("Nested")),
        ];

        foreach (Statement statement in statements)
        {
            RecordingVisitor visitor = new();
            Assert.DoesNotThrow(() => visitor.Visit(statement), $"visiting {statement.NodeKind}");
            Assert.That(visitor.Visited[0], Is.EqualTo(statement.NodeKind));
        }
    }

    [Test]
    public void Visitor_HandlesBothLambdaForms()
    {
        LambdaExpr arrow = LambdaExpr.Arrow(
            NextSpan(),
            [Param("integer", "a")],
            Binary(Id("a"), BinaryOperator.Add, Int("1")));

        LambdaExpr block = LambdaExpr.Block(
            NextSpan(),
            [Param("integer", "a")],
            [new YieldStmt(NextSpan(), Id("a"))]);

        Assert.Multiple(() =>
        {
            Assert.That(arrow.IsExpressionBodied, Is.True);
            Assert.That(arrow.Body, Is.Null);
            Assert.That(block.IsExpressionBodied, Is.False);
            Assert.That(block.ExpressionBody, Is.Null);

            Assert.That(new IdentifierCounter().Also(v => v.Visit(arrow)).Count, Is.EqualTo(1));
            Assert.That(new IdentifierCounter().Also(v => v.Visit(block)).Count, Is.EqualTo(1));
        });
    }
}

/// <summary>Small helper so an expression can both construct and act.</summary>
internal static class TestExtensions
{
    public static T Also<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }
}
