using ProfiC.Compiler.Ast;

namespace ProfiC.Tests.Ast;

/// <summary>
/// Type suffix nesting. The distinction between a set of optionals and an optional set is
/// the one thing in the type grammar that is easy to get backwards, so it gets its own
/// fixture.
/// </summary>
[TestFixture]
public sealed class TypeSyntaxTests : AstTestBase
{
    [Test]
    public void SetOfOptionals_AndOptionalSet_AreDifferentTrees()
    {
        // Node?[] is a set of optionals: the set is outermost.
        TypeSyntax setOfOptionals = SetOf(OptionalOf(Named("Node")));

        // Node[]? is an optional set: the optional is outermost.
        TypeSyntax optionalSet = OptionalOf(SetOf(Named("Node")));

        Assert.Multiple(() =>
        {
            Assert.That(setOfOptionals, Is.TypeOf<SetTypeSyntax>());
            Assert.That(((SetTypeSyntax)setOfOptionals).ElementType,
                        Is.TypeOf<OptionalTypeSyntax>());

            Assert.That(optionalSet, Is.TypeOf<OptionalTypeSyntax>());
            Assert.That(((OptionalTypeSyntax)optionalSet).UnderlyingType,
                        Is.TypeOf<SetTypeSyntax>());
        });
    }

    [Test]
    public void SuffixesNestToArbitraryDepth()
    {
        // Node?[]?[] — read left to right: optional, set, optional, set.
        TypeSyntax type = SetOf(OptionalOf(SetOf(OptionalOf(Named("Node")))));

        List<string> shape = [];

        for (TypeSyntax? current = type; current is not null;)
        {
            switch (current)
            {
                case SetTypeSyntax s:
                    shape.Add("set");
                    current = s.ElementType;
                    break;
                case OptionalTypeSyntax o:
                    shape.Add("optional");
                    current = o.UnderlyingType;
                    break;
                case NamedTypeSyntax n:
                    shape.Add(n.Name);
                    current = null;
                    break;
                default:
                    current = null;
                    break;
            }
        }

        Assert.That(shape, Is.EqualTo(new[] { "set", "optional", "set", "optional", "Node" }));
    }

    [Test]
    public void FunctionType_MayHaveNoReturnType()
    {
        FunctionTypeSyntax voidFunction = new(NextSpan(), null, [Named("string")]);

        Assert.Multiple(() =>
        {
            Assert.That(voidFunction.ReturnType, Is.Null);
            Assert.That(voidFunction.ParameterTypes, Has.Count.EqualTo(1));
            Assert.That(voidFunction.Children.Count(), Is.EqualTo(1));
        });
    }

    [Test]
    public void FunctionType_ChildrenIncludeReturnTypeThenParameters()
    {
        FunctionTypeSyntax comparator =
            new(NextSpan(), Named("integer"), [Named("integer"), Named("integer")]);

        Assert.That(comparator.Children.Cast<NamedTypeSyntax>().Select(t => t.Name),
                    Is.EqualTo(new[] { "integer", "integer", "integer" }));
    }

    [Test]
    public void FunctionType_MayItselfBeOptional()
    {
        // integer function(integer, integer)?
        TypeSyntax type = OptionalOf(
            new FunctionTypeSyntax(NextSpan(), Named("integer"), [Named("integer")]));

        Assert.That(((OptionalTypeSyntax)type).UnderlyingType, Is.TypeOf<FunctionTypeSyntax>());
    }

    [Test]
    public void ModelArrivesAsAnOrdinaryNamedType()
    {
        // "Model" is a reserved type name, not a keyword, so it reaches the tree as a name.
        // Rejecting a redeclaration of it belongs to the resolver.
        Assert.That(Named("Model").Name, Is.EqualTo("Model"));
    }
}
