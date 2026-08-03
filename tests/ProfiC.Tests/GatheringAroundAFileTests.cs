using ProfiC.Cli;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Semantics;

namespace ProfiC.Tests;

/// <summary>
/// <para>Which files are compiled when a command is pointed at one of them.</para>
/// <para><b>A file in a project cannot be compiled as though it were alone.</b> Its folder is not
/// the program: a project gathers files from wherever it lists them, so a type declared in a
/// folder beside this one is part of the same program and invisible from the folder. Compiled by
/// folder, every name that crosses the boundary is reported missing — a wall of errors about code
/// that is correct, in the editor before anything is run, and a refused run afterwards.</para>
/// <para>Checked against the corpus rather than a fixture, because the corpus already holds a
/// project laid out this way and a fixture would only prove that the one I wrote agrees with
/// itself.</para>
/// </summary>
[TestFixture]
public sealed class GatheringAroundAFileTests : LexerTestBase
{
    private static string Sample(params string[] parts) =>
        Path.Combine([RepositoryRoot, "samples", .. parts]);

    private static DiagnosticBag Checked(string path)
    {
        DiagnosticBag diagnostics = new();

        if (SourceDiscovery.Gather(path, diagnostics) is not { } gathered)
        {
            return diagnostics;
        }

        SemanticModel model = Resolver.Resolve(
            gathered.Units,
            diagnostics,
            projects: gathered.Projects,
            entryPoint: gathered.EntryPoint);

        TypeChecker.Check(gathered.Units, model, diagnostics);

        return diagnostics;
    }

    /// <summary>
    /// <para>A file a project claims compiles as part of that project.</para>
    /// <para><c>library</c> builds on <c>books</c>, which sits in a folder of its own — so
    /// <c>Book</c> is used in <c>Program.pc</c> and declared nowhere near it.</para>
    /// </summary>
    [Test]
    public void AFileInAProjectIsCompiledWithTheProject()
    {
        DiagnosticBag said = Checked(Sample("library", "Program.pc"));

        Assert.That(
            said.Sorted().Select(d => $"{d.Id}: {d.Message}"),
            Is.Empty,
            "a file of a project, compiled on its own, has to see the rest of it");
    }

    /// <summary>
    /// <para>A file the project reaches by reference is compiled with it too, from either end.
    /// </para>
    /// <para>Opening the library's own <c>Book.pc</c> is the same program as opening the file
    /// that uses it. Which of them a reader happened to click on is not a fact about the
    /// program.</para>
    /// </summary>
    [Test]
    public void AFileReachedByReferenceIsCompiledWithItToo()
    {
        DiagnosticBag said = Checked(Sample("library", "books", "Book.pc"));

        Assert.That(said.Sorted().Select(d => $"{d.Id}: {d.Message}"), Is.Empty);
    }

    /// <summary>
    /// <para>A file no project claims still compiles with the folder around it.</para>
    /// <para>The other half, and the one that must not regress: most of the corpus is a folder of
    /// programs with no project file anywhere near them.</para>
    /// </summary>
    [Test]
    public void AFileNoProjectClaimsIsCompiledWithItsFolder()
    {
        DiagnosticBag said = Checked(Sample("hello.pc"));

        Assert.That(said.Sorted().Select(d => $"{d.Id}: {d.Message}"), Is.Empty);
    }
}
