using System.Text.Json.Nodes;
using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Semantics;
using ProfiC.Compiler.Text;
using ProfiC.Services;

namespace ProfiC.Tests.LanguageServer;

/// <summary>
/// <para>The language services, asked about a program that is not on a disk.</para>
/// <para><b>This is the shape a browser asks in, written out here where it can be run.</b> A page
/// holds one editor and no file system: there is no folder to gather, no project to read, and the
/// program is whatever is in the box. Everything else an editor asks — what could be written here,
/// what is this, what does this call take — is a question about a tree and a model, and none of it
/// should need a disk to answer.</para>
/// <para>Every other test of these reaches for a real folder, which is the right thing for the
/// command line and proves nothing about this: a service that quietly opened a file would pass all
/// of them and fail the moment a page asked it anything. So the gatherer here refuses to touch
/// anything, and the program is a string.</para>
/// </summary>
[TestFixture]
public sealed class WithoutADiskTests
{
    /// <summary>What a page calls the program in it, since it has no name of its own.</summary>
    private const string Named = "playground.pc";

    /// <summary>
    /// The whole program, which is the one file — and nothing is read to find that out. Resolved
    /// and checked, which is what the questions below are about; the passes after it report what
    /// is unassigned and what is unused, and a file being typed into trips both constantly.
    /// </summary>
    private static Around OnAPage(string path, SourceText text, CancellationToken cancellation)
    {
        DiagnosticBag aside = new();
        CompilationUnit unit = Parser.Parse(text, aside);

        SemanticModel model = Resolver.Resolve(
            [unit], aside, requireEntryPoint: false, cancellation: cancellation);

        TypeChecker.Check([unit], model, aside, cancellation);

        return new Around(model, unit);
    }

    /// <summary>
    /// A program with the caret written into it as <c>|</c>, which is how every case below says
    /// where somebody is typing. Answered as the program without the mark and the place it was.
    /// </summary>
    private static (SourceText Source, int At) Typed(string program)
    {
        int at = program.IndexOf('|', StringComparison.Ordinal);

        return (new SourceText(program.Remove(at, 1), Named), at);
    }

    private static IReadOnlyList<string> Offered(string program)
    {
        (SourceText source, int at) = Typed(program);

        JsonArray? offered = Completion.After(Named, source, at, OnAPage)
            ?? Completion.Bare(Named, source, at, OnAPage);

        return [.. (offered ?? []).Select(item => (string)item!["label"]!)];
    }

    /// <summary>
    /// <para>A member of a type the language provides, offered after a dot.</para>
    /// <para>The case a page is for. Nothing here exists on a disk, and the answer comes from the
    /// same catalog the command line reads.</para>
    /// </summary>
    [Test]
    public void AProvidedMemberIsOfferedAfterADot()
    {
        Assert.That(
            Offered("""
                model Program
                    shared function Main()
                        Console.|
                    end function
                end model
                """),
            Does.Contain("WriteLine"));
    }

    /// <summary>A local declared in the file is offered where a bare name could be written.</summary>
    [Test]
    public void ALocalIsOfferedWhereANameGoes()
    {
        Assert.That(
            Offered("""
                model Program
                    shared function Main()
                        let greeting = "hello";
                        Console.WriteLine(gre|);
                    end function
                end model
                """),
            Does.Contain("greeting"));
    }

    /// <summary>
    /// A type the program declares is offered too, which is what proves the model was built from
    /// this text rather than from a catalog of things the language already knew.
    /// </summary>
    [Test]
    public void ATypeTheProgramDeclaresIsOffered()
    {
        Assert.That(
            Offered("""
                model Counter
                    public integer Total;
                end model

                model Program
                    shared function Main()
                        let seen = new Coun|
                    end function
                end model
                """),
            Does.Contain("Counter"));
    }

    /// <summary>
    /// <para>What is under the cursor, said as a line of Profi-C.</para>
    /// <para>Asserted on the type rather than on the whole answer, since the wording around it is
    /// markdown a page lays out and is not this test's business.</para>
    /// </summary>
    [Test]
    public void HoverSaysWhatALocalIs()
    {
        (SourceText source, int at) = Typed("""
            model Program
                shared function Main()
                    let total = 41 + 1;
                    Console.WriteLine(to|tal);
                end function
            end model
            """);

        Around around = OnAPage(Named, source, CancellationToken.None);

        JsonObject? said = Answers.Hover(
            [around.Unit], around.Unit, around.Model, source, at);

        Assert.That((string?)said?["contents"]?["value"], Does.Contain("integer"));
    }

    /// <summary>
    /// <para>The call the caret is inside, with the argument being written marked.</para>
    /// <para>The parameter arrives as a place in the signature's own text rather than as a copy of
    /// it, which is what lets a page color the whole label once and embolden the stretch. So that
    /// is what is asserted: that the offsets name the parameter they claim to.</para>
    /// </summary>
    [Test]
    public void ASignatureMarksTheArgumentBeingWritten()
    {
        (SourceText source, int at) = Typed("""
            model Program
                shared function Greet(string name, integer times)
                end function

                shared function Main()
                    Program.Greet("hello", 3|);
                end function
            end model
            """);

        Around around = OnAPage(Named, source, CancellationToken.None);

        JsonObject said = Answers.Signature(
            [around.Unit], around.Unit, around.Model, source, at)!;

        string label = (string)said["signatures"]![0]!["label"]!;
        JsonArray parameters = (JsonArray)said["signatures"]![0]!["parameters"]!;
        JsonArray where = (JsonArray)parameters[(int)said["activeParameter"]!]!["label"]!;

        int from = (int)where[0]!;
        int to = (int)where[1]!;

        Assert.Multiple(() =>
        {
            Assert.That((int)said["activeParameter"]!, Is.EqualTo(1));
            Assert.That(label[from..to], Is.EqualTo("integer times"));
        });
    }

    /// <summary>
    /// <para>A program halfway through being typed is still answered about.</para>
    /// <para>The ordinary case rather than an edge one: a reader asking what goes here has, by
    /// definition, not finished the line. A service that needed a program that parses would answer
    /// nothing exactly when it was wanted.</para>
    /// </summary>
    [Test]
    public void AHalfWrittenProgramIsStillAnswered()
    {
        Assert.That(
            Offered("""
                model Program
                    shared function Main()
                        let count = 3;
                        Console.
                        cou|
                    end function
                end model
                """),
            Does.Contain("count"));
    }
}
