using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Parsing;
using ProfiC.Compiler.Semantics;
using ProfiC.Compiler.Text;

namespace ProfiC.Tests.Semantics;

/// <summary>
/// <para>What <c>File</c> and <c>Directory</c> do, against a real file system.</para>
/// <para>Held apart from the rest of the built-in catalog because these are the only members
/// that touch anything outside the process. Each test runs in a folder of its own, made before
/// and removed after, so one cannot see what another left behind and none can reach the
/// repository it is being run from.</para>
/// <para>Against the real thing rather than against a stand-in: what is being asserted is that
/// a program reading a file gets what is in it, and a stand-in would only assert that the
/// stand-in was called.</para>
/// </summary>
/// <remarks>
/// Never alongside anything else. The current directory belongs to the process rather than to
/// a test, so moving it moves it for whatever else is running — and these tests move it. NUnit
/// runs one at a time unless told otherwise, so this changes nothing today; it is here so that
/// turning parallelism on later fails loudly here instead of quietly everywhere else.
/// </remarks>
[TestFixture]
[NonParallelizable]
public sealed class FileMemberTests
{
    private string _folder = string.Empty;
    private string _previous = string.Empty;

    [SetUp]
    public void MakeAFolderOfMyOwn()
    {
        _folder = Path.Combine(
            Path.GetTempPath(), "profi-c-files", TestContext.CurrentContext.Test.ID);

        Directory.CreateDirectory(_folder);

        // Run in it, so a program under test may write "notes.txt" and mean a file here.
        _previous = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_folder);
    }

    [TearDown]
    public void PutItBack()
    {
        Directory.SetCurrentDirectory(_previous);

        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A file still held open somewhere is not this test's failure to report.
        }
    }

    private static string Run(string body)
    {
        string source = $$"""
            global model Program
                function Main()
            {{body}}
                end function
            end model
            """;

        DiagnosticBag diagnostics = new();
        CompilationUnit unit = Parser.Parse(new SourceText(source, "<test>"), diagnostics);
        SemanticModel model = Resolver.Resolve(unit, diagnostics, requireEntryPoint: true);
        TypeChecker.Check(unit, model, diagnostics);
        DefiniteAssignment.Analyze(unit, model, diagnostics);

        Assert.That(
            diagnostics.Select(d => $"{d.Descriptor.Id}: {d.Message}"),
            Is.Empty,
            "the program should check cleanly before it is run");

        StringWriter output = new();
        ProfiC.Interpreter.Interpreter.Run(
            Lowering.Lower(unit, model), model, output, TextReader.Null);

        return output.ToString().ReplaceLineEndings("\n");
    }

    // ---- Reading what was written -------------------------------------------------------

    [Test]
    public void WhatIsWrittenIsWhatIsRead() => Assert.That(
        Run("""
                    File.Write("notes.txt", "hello");
                    Console.WriteLine(File.Read("notes.txt").Or("nothing"));
            """),
        Is.EqualTo("hello\n"));

    [Test]
    public void WritingReplacesAndAppendingAdds() => Assert.That(
        Run("""
                    File.Write("notes.txt", "one");
                    File.Write("notes.txt", "two");
                    File.Append("notes.txt", " three");
                    Console.WriteLine(File.Read("notes.txt").Or("nothing"));
            """),
        Is.EqualTo("two three\n"));

    /// <summary>
    /// Lines come back without their endings, and go out with one each. A file written here
    /// and read on another machine holds the same lines, which is why the ending is not the
    /// platform's.
    /// </summary>
    [Test]
    public void LinesGoOutAndComeBackWithoutTheirEndings() => Assert.That(
        Run("""
                    File.WriteLines("notes.txt", {"first", "second", "third"});

                    string[] read = File.ReadLines("notes.txt").Or({});

                    Console.WriteLine(read.Count());
                    Console.WriteLine(read.Join("|"));
            """),
        Is.EqualTo("3\nfirst|second|third\n"));

    [Test]
    public void ACarriageReturnIsNotPartOfTheLine()
    {
        File.WriteAllText("windows.txt", "first\r\nsecond\r\n");

        Assert.That(
            Run("""
                        Console.WriteLine(File.ReadLines("windows.txt").Or({}).Join("|"));
                """),
            Is.EqualTo("first|second\n"));
    }

    /// <summary>Text goes out as UTF-8 with nothing in front of it.</summary>
    [Test]
    public void TextIsWrittenAsUtf8WithNoMark()
    {
        Run("""
                    File.Write("accented.txt", "café");
            """);

        byte[] written = File.ReadAllBytes("accented.txt");

        Assert.Multiple(() =>
        {
            Assert.That(written[0], Is.Not.EqualTo(0xEF), "a byte-order mark was written");
            Assert.That(File.ReadAllText("accented.txt"), Is.EqualTo("café"));
        });
    }

    // ---- What is not there --------------------------------------------------------------

    /// <summary>
    /// The whole reason reading yields an optional: asking for a file that is not there is an
    /// ordinary thing to do, and needs no guard around it.
    /// </summary>
    [Test]
    public void ReadingWhatIsNotThereGivesNothing() => Assert.That(
        Run("""
                    Console.WriteLine(File.Read("absent.txt").HasValue());
                    Console.WriteLine(File.ReadLines("absent.txt").HasValue());
                    Console.WriteLine(File.Size("absent.txt").HasValue());
                    Console.WriteLine(File.Changed("absent.txt").HasValue());
                    Console.WriteLine(File.Exists("absent.txt"));
            """),
        Is.EqualTo("false\nfalse\nfalse\nfalse\nfalse\n"));

    /// <summary>A folder is not a file, so both answers say so and they agree.</summary>
    [Test]
    public void AFolderIsNotAFile() => Assert.That(
        Run("""
                    Directory.Create("somewhere");
                    Console.WriteLine(File.Exists("somewhere"));
                    Console.WriteLine(File.Read("somewhere").HasValue());
            """),
        Is.EqualTo("false\nfalse\n"));

    // ---- What goes wrong ------------------------------------------------------------------

    /// <summary>
    /// Everything that is not "there is no such file" raises, because absence cannot say which
    /// of them happened. Writing into a folder that does not exist is the common one.
    /// </summary>
    [Test]
    public void WritingWhereThereIsNoFolderRaises() => Assert.That(
        Run("""
                    try
                        File.Write("nowhere/deep/notes.txt", "text");
                        Console.WriteLine("wrote it");
                    catch IOException problem
                        Console.WriteLine("raised");
                    end try
            """),
        Is.EqualTo("raised\n"));

    [Test]
    public void CopyingWhatIsNotThereRaises() => Assert.That(
        Run("""
                    try
                        File.Copy("absent.txt", "copy.txt");
                        Console.WriteLine("copied it");
                    catch IOException problem
                        Console.WriteLine("raised");
                    end try
            """),
        Is.EqualTo("raised\n"));

    // ---- Moving things about --------------------------------------------------------------

    [Test]
    public void CopyingAndMovingAndDeleting() => Assert.That(
        Run("""
                    File.Write("first.txt", "text");
                    File.Copy("first.txt", "second.txt");
                    File.Move("second.txt", "third.txt");

                    Console.WriteLine(File.Exists("first.txt"));
                    Console.WriteLine(File.Exists("second.txt"));
                    Console.WriteLine(File.Read("third.txt").Or("nothing"));

                    Console.WriteLine(File.Delete("third.txt"));
                    Console.WriteLine(File.Delete("third.txt"));
            """),
        Is.EqualTo("true\nfalse\ntext\ntrue\nfalse\n"));

    [Test]
    public void SizeAndChangedDescribeTheFile() => Assert.That(
        Run("""
                    File.Write("notes.txt", "12345");

                    Console.WriteLine(File.Size("notes.txt").Or(0));
                    Console.WriteLine(File.Changed("notes.txt").Or(new DateTime(1, 1, 1)).Year > 2000);
            """),
        Is.EqualTo("5\ntrue\n"));

    // ---- Folders ----------------------------------------------------------------------------

    /// <summary>
    /// Creating makes every folder on the way, since making one inside another that is not
    /// there yet is the ordinary reason to ask. Deleting takes what is inside with it.
    /// </summary>
    [Test]
    public void FoldersAreMadeAndRemovedWholeAndDeep() => Assert.That(
        Run("""
                    Directory.Create("one/two/three");
                    Console.WriteLine(Directory.Exists("one/two/three"));

                    File.Write("one/two/three/notes.txt", "text");

                    Console.WriteLine(Directory.Delete("one"));
                    Console.WriteLine(Directory.Exists("one"));
                    Console.WriteLine(Directory.Delete("one"));
            """),
        Is.EqualTo("true\ntrue\nfalse\nfalse\n"));

    /// <summary>
    /// Listed in a settled order rather than whatever the file system offers, so that a
    /// program prints the same twice and on two machines.
    /// </summary>
    [Test]
    public void WhatIsInAFolderIsListedInOrder() => Assert.That(
        Run("""
                    File.Write("gamma.txt", "");
                    File.Write("alpha.txt", "");
                    File.Write("beta.txt", "");
                    Directory.Create("zeta");
                    Directory.Create("delta");

                    Console.WriteLine(Directory.Files(".").Or({}).Count());
                    Console.WriteLine(Directory.Folders(".").Or({}).Count());
            """),
        Is.EqualTo("3\n2\n"));

    [Test]
    public void AFolderThatIsNotThereListsNothing() => Assert.That(
        Run("""
                    Console.WriteLine(Directory.Exists("nowhere"));
                    Console.WriteLine(Directory.Files("nowhere").HasValue());
                    Console.WriteLine(Directory.Folders("nowhere").HasValue());
            """),
        Is.EqualTo("false\nfalse\nfalse\n"));

    [Test]
    public void TheCurrentFolderIsWhereTheProgramIs() => Assert.That(
        Run("""
                    Console.WriteLine(Directory.Exists(Directory.Current));
            """),
        Is.EqualTo("true\n"));

    /// <summary>
    /// What the tests above exercise. Listed rather than inferred, so that a member added to
    /// File or Directory without a test here fails the catalog's coverage check.
    /// </summary>
    public static readonly BuiltInId[] Covered =
    [
        BuiltInId.FileRead, BuiltInId.FileReadLines, BuiltInId.FileWrite,
        BuiltInId.FileWriteLines, BuiltInId.FileAppend, BuiltInId.FileExists,
        BuiltInId.FileDelete, BuiltInId.FileCopy, BuiltInId.FileMove,
        BuiltInId.FileSize, BuiltInId.FileChanged,
        BuiltInId.DirectoryExists, BuiltInId.DirectoryCreate, BuiltInId.DirectoryDelete,
        BuiltInId.DirectoryFiles, BuiltInId.DirectoryFolders, BuiltInId.DirectoryCurrent,
    ];
}
