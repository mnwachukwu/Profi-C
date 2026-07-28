using ProfiC.Compiler.Ast;

namespace ProfiC.Tests.Parsing;

/// <summary>
/// <para>Expression parsing, asserted on tree <em>shape</em> rather than on parsing having
/// succeeded.</para>
/// <para>That distinction is the point: a precedence bug parses perfectly and produces the
/// wrong answer, so a test that only checks for absence of errors would not notice.</para>
/// </summary>
[TestFixture]
public sealed class ExpressionParsingTests : ParserTestBase
{
    /// <summary>Renders an expression as fully parenthesized source, so shape is visible.</summary>
    private static string Shape(Expression expression) => expression switch
    {
        LiteralExpr n => n.Text,
        IdentifierExpr n => n.Name,
        ParenthesizedExpr n => Shape(n.Inner),
        UnaryExpr n => $"({n.Operator.Spelling()} {Shape(n.Operand)})",
        BinaryExpr n => $"({Shape(n.Left)} {n.Operator.Spelling()} {Shape(n.Right)})",
        TypeTestExpr n => $"({Shape(n.Operand)} is {TypeName(n.TargetType)})",
        TypeCastExpr n => $"({Shape(n.Operand)} as {TypeName(n.TargetType)})",
        CallExpr n => $"{Shape(n.Callee)}({string.Join(", ", n.Arguments.Select(Shape))})",
        IndexExpr n => $"{Shape(n.Receiver)}[{Shape(n.Index)}]",
        MemberExpr n => $"{Shape(n.Receiver)}.{n.MemberName}",
        IfExpr n => $"(if {Shape(n.Condition)} then {Shape(n.ThenValue)} else {Shape(n.ElseValue)})",
        ReceiverExpr n => n.Receiver.ToString().ToLowerInvariant(),
        CollectionExpr n => $"{{{string.Join(", ", n.Elements.Select(Shape))}}}",
        NewExpr n => $"new {n.TypeName}({string.Join(", ", n.Arguments.Select(Shape))})",
        _ => expression.NodeKind,
    };

    private static string TypeName(TypeSyntax type) => type switch
    {
        NamedTypeSyntax n => n.Name,
        SetTypeSyntax n => TypeName(n.ElementType) + "[]",
        OptionalTypeSyntax n => TypeName(n.UnderlyingType) + "?",
        _ => type.NodeKind,
    };

    [TestCase("1 + 2 * 3", "(1 + (2 * 3))")]
    [TestCase("1 * 2 + 3", "((1 * 2) + 3)")]
    [TestCase("1 + 2 - 3", "((1 + 2) - 3)")]
    [TestCase("1 - 2 - 3", "((1 - 2) - 3)")]
    [TestCase("1 * 2 / 3 % 4", "(((1 * 2) / 3) % 4)")]
    [TestCase("1 + 2 - 3 * 4 / 5 % 6", "((1 + 2) - (((3 * 4) / 5) % 6))")]
    public void ArithmeticFollowsPrecedenceAndAssociatesLeft(string source, string expected)
    {
        Assert.That(Shape(ParseExpression(source)), Is.EqualTo(expected));
    }

    [TestCase("a or b and c", "(a or (b and c))")]
    [TestCase("a and b or c", "((a and b) or c)")]
    [TestCase("a == b and c != d", "((a == b) and (c != d))")]
    [TestCase("a < b == c", "((a < b) == c)")]
    public void LogicalOperatorsBindLooserThanComparison(string source, string expected)
    {
        Assert.That(Shape(ParseExpression(source)), Is.EqualTo(expected));
    }

    /// <summary>
    /// The one place the precedence table deliberately departs from C. Python groups it this
    /// way; the C reading would make "not a == b" mean "(not a) == b", which is nearly always
    /// a mistake.
    /// </summary>
    [TestCase("not a == b", "(not (a == b))")]
    [TestCase("not a and b", "((not a) and b)")]
    [TestCase("not a or not b", "((not a) or (not b))")]
    public void NotBindsLooserThanComparisonButTighterThanAnd(string source, string expected)
    {
        Assert.That(Shape(ParseExpression(source)), Is.EqualTo(expected));
    }

    /// <summary>
    /// Exponentiation is the one right-associative infix operator, and the one that binds
    /// tighter than a leading minus. Both match how the notation is read on paper.
    /// </summary>
    [TestCase("2 ^ 3", "(2 ^ 3)")]
    [TestCase("2 ^ 3 ^ 2", "(2 ^ (3 ^ 2))")]
    [TestCase("2 ^ 3 ^ 2 ^ 1", "(2 ^ (3 ^ (2 ^ 1)))")]
    [TestCase("-2 ^ 2", "(- (2 ^ 2))")]
    [TestCase("2 * 3 ^ 2", "(2 * (3 ^ 2))")]
    [TestCase("3 ^ 2 * 2", "((3 ^ 2) * 2)")]
    [TestCase("1 + 2 ^ 3", "(1 + (2 ^ 3))")]
    [TestCase("2 ^ -1", "(2 ^ (- 1))")]
    [TestCase("a.b ^ c", "(a.b ^ c)")]
    [TestCase("(-2) ^ 2", "((- 2) ^ 2)")]
    public void ExponentiationIsRightAssociativeAndBindsTighterThanUnaryMinus(
        string source, string expected)
    {
        Assert.That(Shape(ParseExpression(source)), Is.EqualTo(expected));
    }

    [TestCase("-a * b", "((- a) * b)")]
    [TestCase("-a + b", "((- a) + b)")]
    [TestCase("a - -b", "(a - (- b))")]
    [TestCase("a--b", "(a - (- b))")]
    [TestCase("- - a", "(- (- a))")]
    public void UnaryMinusBindsTighterThanAnyInfixOperator(string source, string expected)
    {
        Assert.That(Shape(ParseExpression(source)), Is.EqualTo(expected));
    }

    [TestCase("a.b.c", "a.b.c")]
    [TestCase("a.b()", "a.b()")]
    [TestCase("a()[0]", "a()[0]")]
    [TestCase("a[0].b(1, 2)", "a[0].b(1, 2)")]
    [TestCase("-a.b", "(- a.b)")]
    public void PostfixBindsTightest(string source, string expected)
    {
        Assert.That(Shape(ParseExpression(source)), Is.EqualTo(expected));
    }

    [TestCase("x is Dog", "(x is Dog)")]
    [TestCase("x as Dog", "(x as Dog)")]
    [TestCase("x is Dog and y", "((x is Dog) and y)")]
    [TestCase("a + b is Dog", "((a + b) is Dog)")]
    [TestCase("x as Node?", "(x as Node?)")]
    public void TypeTestsSitAtRelationalPrecedenceAndTakeATypeOnTheRight(
        string source, string expected)
    {
        Assert.That(Shape(ParseExpression(source)), Is.EqualTo(expected));
    }

    [TestCase("(a + b) * c", "((a + b) * c)")]
    [TestCase("(a)", "a")]
    [TestCase("((a))", "a")]
    public void ParenthesesOverridePrecedence(string source, string expected)
    {
        Assert.That(Shape(ParseExpression(source)), Is.EqualTo(expected));
    }

    [Test]
    public void ConditionalExpressionParses()
    {
        Assert.That(Shape(ParseExpression("if a then b else c")),
                    Is.EqualTo("(if a then b else c)"));
    }

    [Test]
    public void ConditionalExpressionsChain()
    {
        Assert.That(Shape(ParseExpression("if a then b else if c then d else e")),
                    Is.EqualTo("(if a then b else (if c then d else e))"));
    }

    [TestCase("this", "this")]
    [TestCase("base", "base")]
    [TestCase("this.field", "this.field")]
    [TestCase("base.Area()", "base.Area()")]
    public void ReceiversParse(string source, string expected)
    {
        Assert.That(Shape(ParseExpression(source)), Is.EqualTo(expected));
    }

    [Test]
    public void OuterIsNoLongerAReceiverAndScansAsAnOrdinaryName()
    {
        // Nested models hold no reference to their enclosing instance, so the word has
        // nothing left to name and is available as an identifier again.
        Assert.That(Shape(ParseExpression("outer")), Is.EqualTo("outer"));
        Assert.That(ParseExpression("outer"), Is.TypeOf<IdentifierExpr>());
    }

    [TestCase("{}", "{}")]
    [TestCase("{1}", "{1}")]
    [TestCase("{1, 2, 3}", "{1, 2, 3}")]
    [TestCase("{{1}, {2}}", "{{1}, {2}}")]
    public void CollectionLiteralsParse(string source, string expected)
    {
        Assert.That(Shape(ParseExpression(source)), Is.EqualTo(expected));
    }

    [TestCase("new Point()", "new Point()")]
    [TestCase("new Point(1, 2)", "new Point(1, 2)")]
    public void ConstructionParses(string source, string expected)
    {
        Assert.That(Shape(ParseExpression(source)), Is.EqualTo(expected));
    }

    // ---- Lambda versus parenthesized expression -----------------------------------------

    [Test]
    public void ArrowLambdaIsToldApartFromAParenthesizedExpression()
    {
        Expression lambda = ParseExpression("(integer a, integer b) => a + b");
        Expression grouped = ParseExpression("(a + b)");

        Assert.Multiple(() =>
        {
            Assert.That(lambda, Is.TypeOf<LambdaExpr>());
            Assert.That(((LambdaExpr)lambda).IsExpressionBodied, Is.True);
            Assert.That(((LambdaExpr)lambda).Parameters, Has.Count.EqualTo(2));

            Assert.That(grouped, Is.TypeOf<ParenthesizedExpr>());
        });
    }

    [Test]
    public void ArrowLambdaWithNoParametersParses()
    {
        Expression lambda = ParseExpression("() => 1");

        Assert.That(lambda, Is.TypeOf<LambdaExpr>());
        Assert.That(((LambdaExpr)lambda).Parameters, Is.Empty);
    }

    [Test]
    public void NestedParenthesesDoNotConfuseTheLambdaLookahead()
    {
        // The scan must match parentheses rather than stopping at the first ")".
        Assert.Multiple(() =>
        {
            Assert.That(ParseExpression("((a + b) * c)"), Is.TypeOf<ParenthesizedExpr>());
            Assert.That(ParseExpression("(integer a) => (a + 1)"), Is.TypeOf<LambdaExpr>());
        });
    }

    [Test]
    public void BlockLambdaParses()
    {
        Expression lambda = ParseExpression(
            """
            function(integer a, integer b)
                        yield a - b;
                    end function
            """);

        Assert.Multiple(() =>
        {
            Assert.That(lambda, Is.TypeOf<LambdaExpr>());
            Assert.That(((LambdaExpr)lambda).IsExpressionBodied, Is.False);
            Assert.That(((LambdaExpr)lambda).Body, Has.Count.EqualTo(1));
        });
    }
}
