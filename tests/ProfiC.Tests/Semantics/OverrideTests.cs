using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Semantics;
using ProfiC.Compiler.Text;

namespace ProfiC.Tests.Semantics;

/// <summary>
/// <para>What <c>override</c> claims, and whether the claim holds.</para>
/// <para>The word names a function in a base type. Left unchecked it is a claim nothing
/// verifies, and the way it fails is quiet: a renamed base function, or a parameter list that
/// drifted by one type, leaves a function still marked <c>override</c> and now overriding
/// nothing — compiling, running, and never being the one that gets called.</para>
/// </summary>
[TestFixture]
public sealed class OverrideTests
{
    private static string[] Check(string source)
    {
        DiagnosticBag diagnostics = new();
        CompilationUnit unit = Parser.Parse(new SourceText(source, "<test>"), diagnostics);
        Resolver.Resolve(unit, diagnostics);

        return [.. diagnostics.Sorted().Select(d => d.Id)];
    }

    private const string Base = """
        model Shape
            public virtual string function Describe()
                yield "shape";
            end function

            public string function Name()
                yield "fixed";
            end function
        end model
        """;

    [Test]
    public void AnOverrideOfAVirtualFunctionIsAccepted() => Assert.That(
        Check(Base + """

            model Square extends Shape
                public override string function Describe()
                    yield "square";
                end function
            end model
            """),
        Is.Empty);

    [Test]
    public void OverridingNothingIsRejected() => Assert.That(
        Check(Base + """

            model Square extends Shape
                public override string function Describ()
                    yield "a name that drifted";
                end function
            end model
            """),
        Is.EqualTo(new[] { "PC0222" }));

    /// <summary>
    /// A parameter list that differs makes an overload rather than an override, so a type that
    /// meant to override and mistyped one type is told rather than quietly given a new function.
    /// </summary>
    [Test]
    public void OverridingWithDifferentParametersIsRejected() => Assert.That(
        Check("""
            model Shape
                public virtual string function Describe(integer detail)
                    yield "shape";
                end function
            end model

            model Square extends Shape
                public override string function Describe(real detail)
                    yield "square";
                end function
            end model
            """),
        Is.EqualTo(new[] { "PC0222" }));

    /// <summary>Overriding is offered by a base rather than taken by a derived type.</summary>
    [Test]
    public void OverridingAFunctionThatIsNotVirtualIsRejected() => Assert.That(
        Check(Base + """

            model Square extends Shape
                public override string function Name()
                    yield "square";
                end function
            end model
            """),
        Is.EqualTo(new[] { "PC0223" }));

    /// <summary>An override may itself be overridden, so the word carries down a chain.</summary>
    [Test]
    public void OverridingAnOverrideIsAccepted() => Assert.That(
        Check(Base + """

            model Square extends Shape
                public override string function Describe()
                    yield "square";
                end function
            end model

            model Tile extends Square
                public override string function Describe()
                    yield "tile";
                end function
            end model
            """),
        Is.Empty);

    /// <summary>
    /// Nothing spells "hide the base one on purpose", so a function redeclaring one is either
    /// an override that forgot to say so or a collision. Both want reporting.
    /// </summary>
    [Test]
    public void RedeclaringABaseFunctionWithoutTheWordIsRejected() => Assert.That(
        Check(Base + """

            model Square extends Shape
                public string function Describe()
                    yield "square";
                end function
            end model
            """),
        Is.EqualTo(new[] { "PC0224" }));

    /// <summary>An overload across a base and a derived model is ordinary and stays silent.</summary>
    [Test]
    public void OverloadingAcrossTheBaseIsAccepted() => Assert.That(
        Check(Base + """

            model Square extends Shape
                public string function Describe(integer detail)
                    yield "square";
                end function
            end model
            """),
        Is.Empty);

    [Test]
    public void AnOverrideYieldingSomethingElseIsRejected() => Assert.That(
        Check(Base + """

            model Square extends Shape
                public override integer function Describe()
                    yield 1;
                end function
            end model
            """),
        Is.EqualTo(new[] { "PC0225" }));

    // ---- What every model inherits ----------------------------------------------------------

    /// <summary>
    /// <c>ToString</c> and <c>Equals</c> come from <c>Model</c>, which every model extends
    /// whether or not it wrote <c>extends</c>. Overriding one needs no base to be named.
    /// </summary>
    [TestCase("public override string function ToString()\n        yield \"x\";\n    end function")]
    [TestCase("public override boolean function Equals(Model other)\n        yield true;\n    end function")]
    public void OverridingWhatEveryModelInheritsIsAccepted(string member) => Assert.That(
        Check($$"""
            model Tag
                {{member}}
            end model
            """),
        Is.Empty);

    /// <summary>
    /// And a model with no <c>extends</c> is held to the rule all the same. Skipping one
    /// because it named no base would leave the commonest shape of model unchecked.
    /// </summary>
    [Test]
    public void OverridingNothingWithNoBaseIsRejected() => Assert.That(
        Check("""
            model Tag
                public override string function Nonsense()
                    yield "overrides nothing";
                end function
            end model
            """),
        Is.EqualTo(new[] { "PC0222" }));
}
