using ProfiC.Compiler.Ast;

namespace ProfiC.Tests.Ast;

/// <summary>The printer, which is how a tree gets read by a person.</summary>
[TestFixture]
public sealed class AstPrinterTests : AstTestBase
{
    private static string Print(SyntaxNode node) =>
        AstPrinter.Print(node).ReplaceLineEndings("\n");

    /// <summary>
    /// <para>The expected tree, with its line endings normalized to match the printer's.</para>
    /// <para>A raw string literal carries whatever endings the source file has, and this
    /// repository is checked out with <c>core.autocrlf=true</c> — so on Windows the literal is
    /// CRLF while the printer emits LF, and the comparison fails for a reason that has nothing
    /// to do with the tree. Normalizing both sides makes these tests independent of how the
    /// file happened to arrive.</para>
    /// </summary>
    private static string Tree(string expected) => expected.ReplaceLineEndings("\n");

    [Test]
    public void PrintsAnIndentedTree()
    {
        BinaryExpr tree = Binary(Id("a"), BinaryOperator.Add, Int("1"));

        Assert.That(Print(tree), Is.EqualTo(Tree(
            """
            BinaryExpr '+'
              IdentifierExpr 'a'
              LiteralExpr 1 [Integer]

            """)));
    }

    [Test]
    public void PrintsModifiersOnDeclarations()
    {
        ModelDecl model = Model(
            "Program",
            DeclarationModifiers.Global | DeclarationModifiers.Public,
            members: [Function("Main")]);

        Assert.That(Print(model), Is.EqualTo(Tree(
            """
            ModelDecl 'Program' [public global]
              FunctionDecl 'Main'

            """)));
    }

    [Test]
    public void PrintsTheBaseTypeOfAModel()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Print(Model("Circle", baseTypeName: "Shape")),
                        Is.EqualTo(Tree("ModelDecl 'Circle' extends Shape\n")));

            // No extends clause means the model extends Model implicitly, and nothing is
            // printed for it.
            Assert.That(Print(Model("Shape")), Is.EqualTo(Tree("ModelDecl 'Shape'\n")));
        });
    }

    [Test]
    public void DistinguishesSetOfOptionalsFromOptionalSet()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Print(SetOf(OptionalOf(Named("Node")))), Is.EqualTo(Tree(
                """
                SetTypeSyntax
                  OptionalTypeSyntax
                    NamedTypeSyntax 'Node'

                """)));

            Assert.That(Print(OptionalOf(SetOf(Named("Node")))), Is.EqualTo(Tree(
                """
                OptionalTypeSyntax
                  SetTypeSyntax
                    NamedTypeSyntax 'Node'

                """)));
        });
    }

    [Test]
    public void MarksConstantAndInferredVariables()
    {
        VarDeclStmt inferred = new(NextSpan(), null, "x", Int("5"), isConstant: false);
        VarDeclStmt constant = new(NextSpan(), Named("integer"), "Limit", Int("10"), isConstant: true);

        Assert.Multiple(() =>
        {
            Assert.That(Print(inferred), Does.Contain("VarDeclStmt 'x' [inferred]"));
            Assert.That(Print(constant), Does.Contain("VarDeclStmt 'Limit' [constant]"));
        });
    }

    [Test]
    public void MarksWhetherARangeLoopIsInclusive()
    {
        ForStmt inclusive =
            new(NextSpan(), "i", Int("1"), Int("10"), true, null, []);
        ForStmt exclusive =
            new(NextSpan(), "i", Int("0"), Int("10"), false, null, []);

        Assert.Multiple(() =>
        {
            Assert.That(Print(inclusive), Does.StartWith("ForStmt 'i' to"));
            Assert.That(Print(exclusive), Does.StartWith("ForStmt 'i' until"));
        });
    }

    [Test]
    public void DistinguishesTheTwoLambdaForms()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Print(LambdaExpr.Arrow(NextSpan(), [], Int("1"))),
                        Does.StartWith("LambdaExpr arrow"));
            Assert.That(Print(LambdaExpr.Block(NextSpan(), [], [])),
                        Does.StartWith("LambdaExpr block"));
        });
    }

    [Test]
    public void NamesTheReceiver()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Print(new ReceiverExpr(NextSpan(), ReceiverKind.This)),
                        Is.EqualTo(Tree("ThisExpr\n")));
            Assert.That(Print(new ReceiverExpr(NextSpan(), ReceiverKind.Base)),
                        Is.EqualTo(Tree("BaseExpr\n")));
        });
    }

    [Test]
    public void OmitsPositionsUnlessAsked()
    {
        IdentifierExpr node = new(SpanAt(4, 9), "value");

        Assert.Multiple(() =>
        {
            Assert.That(AstPrinter.Print(node), Does.Not.Contain("@"));
            Assert.That(AstPrinter.Print(node, includePositions: true), Does.Contain("@4:9"));
        });
    }

    [Test]
    public void PrintsAWholeCompilationUnit()
    {
        CompilationUnit unit = Unit(
            Model("Program",
                DeclarationModifiers.Global,
                members:
                [
                    Function(
                        "Main",
                        body:
                        [
                            new VarDeclStmt(NextSpan(), null, "total", Int("0"), false),
                            new AssignmentStmt(NextSpan(), Id("total"),
                                Binary(Id("total"), BinaryOperator.Add, Int("1"))),
                        ]),
                ]));

        Assert.That(Print(unit), Is.EqualTo(Tree(
            """
            CompilationUnit
              ModelDecl 'Program' [global]
                FunctionDecl 'Main'
                  VarDeclStmt 'total' [inferred]
                    LiteralExpr 0 [Integer]
                  AssignmentStmt
                    IdentifierExpr 'total'
                    BinaryExpr '+'
                      IdentifierExpr 'total'
                      LiteralExpr 1 [Integer]

            """)));
    }
}
