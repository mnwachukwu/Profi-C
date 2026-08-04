using System.Text.Json.Nodes;
using ProfiC.Cli.LanguageServer;
using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Semantics;
using ProfiC.Compiler.Text;

namespace ProfiC.Tests.LanguageServer;

/// <summary>
/// <para>The types a program does not write, written in beside the names that got them.</para>
/// <para>Profi-C writes its types down, and <c>let</c> is the one place it does not — so unlike a
/// language where inference is everywhere, this fills in a small and fixed set of holes rather
/// than annotating the page. That is what makes it worth having on by default, and what these
/// hold: exactly the three constructs that declare a name with no type on it, and nothing beside
/// a type somebody already wrote.</para>
/// </summary>
[TestFixture]
public sealed class HintsTests
{
    private static (CompilationUnit Unit, SemanticModel Model, SourceText Source) Compile(
        string text)
    {
        SourceText source = new(text, "Program.pc");
        DiagnosticBag diagnostics = new();

        CompilationUnit unit = Parser.Parse(source, diagnostics);
        SemanticModel model = Resolver.Resolve([unit], diagnostics, requireEntryPoint: false);

        TypeChecker.Check([unit], model, diagnostics);

        return (unit, model, source);
    }

    /// <summary>Each hint as the line it lands on and what it says.</summary>
    private static IReadOnlyList<(int Line, string Label)> Shown(
        string text, Hints.Wants? wants = null)
    {
        (CompilationUnit unit, SemanticModel model, SourceText source) = Compile(text);

        JsonArray hints = Hints.In(
            unit, model, source, 0, source.Text.Length, wants ?? Hints.Wants.Default);

        return
        [
            .. hints.Select(hint => (
                (int)hint!["position"]!["line"]! + 1, (string)hint["label"]!)),
        ];
    }

    private const string Counting = """
        shared model Program
            function Main()
                let total = 1|3 + 1|6;
                integer counted = 0;
                string[] words = {"one", "two"};

                loop for i = 1 to 10
                    counted = counted + i;
                end loop

                loop each word in words
                    Console.WriteLine(word);
                end loop
            end function
        end model
        """;

    /// <summary>
    /// <para>The three names a program declares without a type, and no others.</para>
    /// <para>The fraction is the case worth having: a reader who wrote <c>1|3 + 1|6</c> and sees
    /// <c>fraction</c> appear has been told what the language did with it, and was not going to
    /// look it up.</para>
    /// </summary>
    [Test]
    public void OnlyTheNamesWithNoTypeWrittenGetOne()
    {
        Assert.That(
            Shown(Counting),
            Is.EqualTo(new[]
            {
                (3, ": fraction"),
                (7, ": integer"),
                (11, ": string"),
            }));
    }

    /// <summary>
    /// Nothing beside a type somebody wrote. A hint there is the editor reading the line back,
    /// which is noise wherever it lands and worst on the line a reader is looking at.
    /// </summary>
    [Test]
    public void ATypeThatIsWrittenIsNotWrittenAgain()
    {
        IReadOnlyList<(int Line, string Label)> shown = Shown(Counting);

        Assert.Multiple(() =>
        {
            Assert.That(shown.Select(hint => hint.Line), Does.Not.Contain(4), "integer counted");
            Assert.That(shown.Select(hint => hint.Line), Does.Not.Contain(5), "string[] words");
        });
    }

    /// <summary>Turned off, there is nothing to see — which is the point of it being a setting.</summary>
    [Test]
    public void TypesCanBeTurnedOff()
    {
        Assert.That(
            Shown(Counting, new Hints.Wants(Types: false, ParameterNames: false)),
            Is.Empty);
    }

    // ---- The names of the parameters being written to -----------------------------------------

    private const string Calling = """
        shared model Program
            function Main()
                integer times = 3;

                Program.Show("counted", times);
                Program.Show("again", 1);
            end function

            function Show(string label, integer times)
                Console.WriteLine(label);
            end function
        end model
        """;

    /// <summary>
    /// <para>Off unless asked for, because most reading is not the reading these help with.</para>
    /// <para>They repeat what one hover away already says, and they land on every argument of
    /// every call — including <c>Console.WriteLine</c>, where the parameter has no name to give
    /// and nothing is written at all.</para>
    /// </summary>
    [Test]
    public void ParameterNamesAreOffUntilAskedFor()
    {
        Assert.That(
            Shown(Calling).Select(hint => hint.Label),
            Has.None.Contains("label"),
            "nothing about parameters until somebody wants it");
    }

    /// <summary>
    /// <para>Asked for, each argument says which parameter it is filling.</para>
    /// <para>Except where the argument is already the parameter's own name: <c>Show(label)</c>
    /// annotated as <c>Show(label: label)</c> tells a reader nothing they cannot see.</para>
    /// </summary>
    [Test]
    public void AskedFor_EachArgumentSaysWhichParameterItFills()
    {
        IReadOnlyList<(int Line, string Label)> shown =
            Shown(Calling, new Hints.Wants(Types: false, ParameterNames: true));

        Assert.That(
            shown,
            Is.EqualTo(new[]
            {
                (5, "label:"),
                (6, "label:"),
                (6, "times:"),
            }),
            "and 'times' passed to 'times' on line 5 is left alone");
    }

    /// <summary>
    /// A member the language provides is a catalog entry with types and no names, so there is
    /// nothing to write. Inventing one would be inventing it.
    /// </summary>
    [Test]
    public void AMemberTheLanguageProvidesHasNoParameterNameToShow()
    {
        Assert.That(
            Shown(
                """
                shared model Program
                    function Main()
                        Console.WriteLine("counted");
                    end function
                end model
                """,
                new Hints.Wants(Types: false, ParameterNames: true)),
            Is.Empty);
    }

    // ---- What the editor asked for -----------------------------------------------------------

    /// <summary>
    /// <para>Settings are read from where the editor puts them, and anything they leave out keeps
    /// its default.</para>
    /// <para>A client that sends none at all is the ordinary case rather than the broken one —
    /// every editor other than the one this ships with will be that client.</para>
    /// </summary>
    [Test]
    public void SettingsFillInWhatTheyDoNotSay()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Hints.Wanted(null), Is.EqualTo(Hints.Wants.Default), "nothing said");

            Assert.That(
                Hints.Wanted(JsonNode.Parse("""
                    {"profi-c": {"inlayHints": {"parameterNames": true}}}
                    """) as JsonObject),
                Is.EqualTo(new Hints.Wants(Types: true, ParameterNames: true)),
                "one of the two said, and the other keeps its default");

            Assert.That(
                Hints.Wanted(JsonNode.Parse("""
                    {"profi-c": {"inlayHints": {"types": false}}}
                    """) as JsonObject),
                Is.EqualTo(new Hints.Wants(Types: false, ParameterNames: false)));
        });
    }
}
