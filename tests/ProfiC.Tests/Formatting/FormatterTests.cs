using ProfiC.Compiler.Formatting;
using ProfiC.Compiler.Text;

namespace ProfiC.Tests.Formatting;

/// <summary>
/// <para>Lining a program up.</para>
/// <para><b>The corpus is the real test, and it is the reason the rules can be trusted.</b> Every
/// sample is laid out by hand and read by somebody learning the language, so formatting one and
/// getting it back unchanged is a claim about every construct at once — and one that fails
/// loudly, against a file a reader can look at, rather than against a fixture written to match
/// whatever the code happened to do.</para>
/// <para>The cases here are the ones the corpus cannot make: a file that is already wrong, and a
/// file that does not parse.</para>
/// </summary>
[TestFixture]
public sealed class FormatterTests : LexerTestBase
{
    private static string Formatted(string text) =>
        Formatter.Format(new SourceText(text.ReplaceLineEndings("\n"), "<test>"))
            .ReplaceLineEndings("\n");

    /// <summary>
    /// <para>Every sample comes back exactly as it was written.</para>
    /// <para>This is what says the rules are right. A sample holds every construct the language
    /// has, laid out the way the language means them to be, so a rule that indented a
    /// <c>case</c> wrongly or missed that <c>until</c> can close a loop shows up here as a diff
    /// somebody can read.</para>
    /// </summary>
    [TestCaseSource(nameof(SampleNames))]
    public void Sample_IsAlreadyFormatted(string name)
    {
        SourceText source = LoadSample(name);

        Assert.That(
            Formatter.Format(source).ReplaceLineEndings("\n"),
            Is.EqualTo(source.Text.ReplaceLineEndings("\n")),
            $"{name} is laid out by hand; formatting it should change nothing");
    }

    /// <summary>Formatting what is already formatted changes nothing, whatever it was.</summary>
    [TestCaseSource(nameof(SampleNames))]
    public void Sample_FormattingIsSettledAfterOnce(string name)
    {
        string once = Formatter.Format(LoadSample(name));

        Assert.That(Formatter.Format(new SourceText(once, "<once>")), Is.EqualTo(once));
    }

    /// <summary>A file with every line flush left is put back where it belongs.</summary>
    [Test]
    public void AFlattenedFileIsLinedUp() =>
        Assert.That(
            Formatted("""
                shared model Program
                function Main()
                integer counted = 0;
                if counted > 0
                Console.WriteLine(counted);
                else
                Console.WriteLine("none");
                end if
                end function
                end model
                """),
            Is.EqualTo("""
                shared model Program
                    function Main()
                        integer counted = 0;
                        if counted > 0
                            Console.WriteLine(counted);
                        else
                            Console.WriteLine("none");
                        end if
                    end function
                end model
                """));

    /// <summary>
    /// <para>Every closer takes its line back out a level, and each is spelled differently.</para>
    /// <para>Worth doing all of them at once: the rule is one rule, and a construct left off the
    /// list does not fail — it indents its body forever, taking everything below it along.</para>
    /// </summary>
    [Test]
    public void EveryKindOfBodyIsClosedProperly() =>
        Assert.That(
            Formatted("""
                namespace Shapes
                enumeration Color
                Red,
                Green
                end enumeration
                structure Point
                public integer X;
                end structure
                shared model Program
                function Main()
                switch 1
                case 1:
                Console.WriteLine("one");
                default:
                Console.WriteLine("more");
                end switch
                try
                Console.WriteLine("trying");
                catch Exception caught
                Console.WriteLine("caught");
                finally
                Console.WriteLine("done");
                end try
                begin
                Console.WriteLine("apart");
                end
                end function
                end model
                end namespace
                """),
            Is.EqualTo("""
                namespace Shapes
                    enumeration Color
                        Red,
                        Green
                    end enumeration
                    structure Point
                        public integer X;
                    end structure
                    shared model Program
                        function Main()
                            switch 1
                                case 1:
                                    Console.WriteLine("one");
                                default:
                                    Console.WriteLine("more");
                            end switch
                            try
                                Console.WriteLine("trying");
                            catch Exception caught
                                Console.WriteLine("caught");
                            finally
                                Console.WriteLine("done");
                            end try
                            begin
                                Console.WriteLine("apart");
                            end
                        end function
                    end model
                end namespace
                """));

    /// <summary>
    /// <para>The loop <c>end</c> does not close is closed by <c>until</c>.</para>
    /// <para>And the same word is the bound of a counted loop, where it closes nothing. Told
    /// apart by the word after <c>loop</c>, which is the only place the difference is
    /// written.</para>
    /// </summary>
    [Test]
    public void UntilClosesOneLoopAndBoundsAnother() =>
        Assert.That(
            Formatted("""
                shared model Program
                function Main()
                loop
                Console.WriteLine("once at least");
                until true
                loop for i = 1 until 3
                Console.WriteLine(i);
                end loop
                end function
                end model
                """),
            Is.EqualTo("""
                shared model Program
                    function Main()
                        loop
                            Console.WriteLine("once at least");
                        until true
                        loop for i = 1 until 3
                            Console.WriteLine(i);
                        end loop
                    end function
                end model
                """));

    /// <summary>
    /// <para>An <c>if</c> written as an expression opens nothing.</para>
    /// <para>The case a formatter counting keywords gets wrong, and gets wrong invisibly: it has
    /// no <c>end if</c>, so everything below it would be indented one level too far, all the way
    /// to the end of the file. <c>then</c> is what tells the two apart.</para>
    /// </summary>
    [Test]
    public void AnIfExpressionIsNotABody() =>
        Assert.That(
            Formatted("""
                shared model Program
                function Main()
                integer counted = if true then 1 else 2;
                Console.WriteLine(counted);
                end function
                end model
                """),
            Is.EqualTo("""
                shared model Program
                    function Main()
                        integer counted = if true then 1 else 2;
                        Console.WriteLine(counted);
                    end function
                end model
                """));

    /// <summary>
    /// A function declared without a body closes nothing, since there is nothing to close. The
    /// semicolon where the body would have been is what says so.
    /// </summary>
    [Test]
    public void AFunctionWithNoBodyOpensNothing() =>
        Assert.That(
            Formatted("""
                abstract model Shape
                public abstract real function Area();
                public real function Twice()
                yield this.Area() * 2.0;
                end function
                end model
                """),
            Is.EqualTo("""
                abstract model Shape
                    public abstract real function Area();
                    public real function Twice()
                        yield this.Area() * 2.0;
                    end function
                end model
                """));

    /// <summary>
    /// <para>What is inside a block string is the string, and is left exactly as it is.</para>
    /// <para><b>The one case where getting this wrong changes what a program prints.</b> The
    /// spaces in there are characters the program holds, not layout.</para>
    /// </summary>
    [Test]
    public void TheInsideOfABlockStringIsUntouched()
    {
        string held = "shared model Program\n"
            + "function Main()\n"
            + "string held = \"\"\"\n"
            + "      kept exactly\n"
            + "   as written\n"
            + "\"\"\";\n"
            + "Console.WriteLine(held);\n"
            + "end function\n"
            + "end model";

        Assert.That(
            Formatted(held),
            Does.Contain("      kept exactly\n   as written"));
    }

    /// <summary>
    /// <para>A comment on its own line is indented; the middle of a block comment is not.</para>
    /// <para>Two halves of one rule. A line comment marks the code below it and belongs at that
    /// code's level. The inside of a block comment is prose somebody laid out, and re-flowing it
    /// is the thing a formatter has no business doing.</para>
    /// </summary>
    [Test]
    public void ACommentIsIndentedButItsInsidesAreNot() =>
        Assert.That(
            Formatted("""
                shared model Program
                ##
                    Laid out
                        by hand
                ##
                function Main()
                # says what the next line does
                Console.WriteLine(1);
                end function
                end model
                """),
            Is.EqualTo("""
                shared model Program
                    ##
                    Laid out
                        by hand
                ##
                    function Main()
                        # says what the next line does
                        Console.WriteLine(1);
                    end function
                end model
                """));

    /// <summary>
    /// <para>A file that does not parse is formatted anyway.</para>
    /// <para>Which is most of the time it is being formatted. Nothing here needs a tree, so a
    /// half-written line is lined up like any other and the lines around it are unaffected.
    /// </para>
    /// <para>The bracket left open on <c>Console.WriteLine(</c> is closed by the <c>end</c>
    /// below it, since a bracket cannot outlive the body it was opened in. Without that rule one
    /// unclosed paren carries the rest of the file off to the right — and a file being written
    /// has one in it almost constantly.</para>
    /// </summary>
    [Test]
    public void AFileThatWillNotParseIsStillLinedUp() =>
        Assert.That(
            Formatted("""
                shared model Program
                function Main()
                integer counted =
                Console.WriteLine(
                end function
                end model
                """),
            Is.EqualTo("""
                shared model Program
                    function Main()
                        integer counted =
                        Console.WriteLine(
                    end function
                end model
                """));

    /// <summary>
    /// <para>A wrapped line is placed against the bracket it is inside, and what the line above
    /// ended on says how.</para>
    /// <para>A comma — or the bracket itself — means something new begins, and it lines up with
    /// the first thing in the bracket: the rows of a matrix line up with the first row.
    /// Anything else carries the line above on, and takes one indent from the bracket, so it
    /// does not read as another item when it is the rest of one.</para>
    /// <para>An item is placed by alignment and a continuation by a tab stop, which is what
    /// keeps them apart even where a bracket happens to sit on one.</para>
    /// </summary>
    [Test]
    public void AWrappedLineIsPlacedAgainstItsBracket() =>
        Assert.That(
            Formatted("""
                shared model Program
                    function Main()
                        integer[][] square = {{1, 2, 3},
                        {4, 5, 6}};

                        Console.WriteLine("a long piece of text "
                        + square.Count);

                        Console.WriteLine(
                        "the bracket ended the line",
                        square.Count);
                    end function
                end model
                """),
            Is.EqualTo("""
                shared model Program
                    function Main()
                        integer[][] square = {{1, 2, 3},
                                              {4, 5, 6}};

                        Console.WriteLine("a long piece of text "
                                            + square.Count);

                        Console.WriteLine(
                            "the bracket ended the line",
                            square.Count);
                    end function
                end model
                """));

    /// <summary>
    /// <para>A line carrying another one on lands on a tab stop, whatever column its bracket
    /// fell at.</para>
    /// <para>The property behind the rule, rather than one arrangement of it. An editor indents
    /// a wrapped line to a whole number of units, so a continuation placed anywhere else is one
    /// the editor argues with on every newline typed inside a call — which is how this was found
    /// in the first place, a space or two adrift and impossible to leave alone.</para>
    /// <para>Every prefix length is tried, so a bracket landing on a stop, one short of it, and
    /// one past it are all covered — the three cases a formula off by one gets wrong.</para>
    /// </summary>
    [TestCase("Ab")]
    [TestCase("Abc")]
    [TestCase("Abcd")]
    [TestCase("Abcde")]
    [TestCase("Abcdef")]
    public void ACarriedOnLineLandsOnATabStop(string name)
    {
        string formatted = Formatted($$"""
            shared model Program
                function Main()
                    Program.{{name}}("text "
                    + "carried on");
                end function

                function {{name}}(string given)
                    Console.WriteLine(given);
                end function
            end model
            """);

        string carried = formatted.ReplaceLineEndings("\n")
                                  .Split('\n')
                                  .Single(line => line.TrimStart().StartsWith('+'));

        int indent = carried.Length - carried.TrimStart(' ').Length;

        Assert.That(indent % Formatter.IndentWidth, Is.Zero,
                    $"'{carried.Trim()}' sits at {indent}, which no editor would indent to");
    }

    /// <summary>
    /// <para>A lambda's body is a body, and nests like one.</para>
    /// <para>The case where aligning is plainly wrong. Its statements are placed from the
    /// statement that holds the call, not from wherever that call's bracket happened to fall —
    /// otherwise a whole function is pushed off to column sixty for no reason a reader could
    /// name, and the nesting inside it flattens on the way.</para>
    /// </summary>
    [Test]
    public void ALambdaBodyNestsRatherThanAligns() =>
        Assert.That(
            Formatted("""
                shared model Program
                    function Main()
                        Console.WriteLine("clamped: " + Program.Apply(numbers, function(n)
                        if n > 3
                        yield 3;
                        end if

                        yield n;
                        end function));
                    end function
                end model
                """),
            Is.EqualTo("""
                shared model Program
                    function Main()
                        Console.WriteLine("clamped: " + Program.Apply(numbers, function(n)
                            if n > 3
                                yield 3;
                            end if

                            yield n;
                        end function));
                    end function
                end model
                """));

    /// <summary>
    /// A lambda written on one line has a body and closes it, even though the line ends with a
    /// semicolon — which is also how a function with no body at all is written.
    /// </summary>
    [Test]
    public void AOneLineLambdaIsNotADeclarationWithoutABody() =>
        Assert.That(
            Formatted("""
                shared model Program
                    function Main()
                        Program.Show(function() let a = 1; end function);
                        Console.WriteLine("still here");
                    end function
                end model
                """),
            Is.EqualTo("""
                shared model Program
                    function Main()
                        Program.Show(function() let a = 1; end function);
                        Console.WriteLine("still here");
                    end function
                end model
                """));

    /// <summary>
    /// <para>A namespace claiming the rest of the file indents none of it.</para>
    /// <para>The other form of the same word closes with <c>end namespace</c> and does indent
    /// what it holds. Reading this one as that one puts every remaining line of the file in a
    /// body that never ends.</para>
    /// </summary>
    [Test]
    public void AFileScopedNamespaceOpensNothing() =>
        Assert.That(
            Formatted("""
                namespace Shapes;

                shared model Program
                    function Main()
                        Console.WriteLine(1);
                    end function
                end model
                """),
            Is.EqualTo("""
                namespace Shapes;

                shared model Program
                    function Main()
                        Console.WriteLine(1);
                    end function
                end model
                """));

    /// <summary>Trailing whitespace goes, and a blank line becomes empty rather than blank.</summary>
    [Test]
    public void TrailingWhitespaceIsRemoved() =>
        Assert.That(
            Formatted("shared model Program   \n    \nend model  "),
            Is.EqualTo("shared model Program\n\nend model"));

    /// <summary>
    /// <para>A file written with one kind of line ending keeps it.</para>
    /// <para>Otherwise formatting a file on Windows rewrites every line of one written on Linux,
    /// and a diff of one changed line becomes a diff of the file.</para>
    /// </summary>
    [Test]
    public void TheFilesOwnLineEndingsAreKept()
    {
        string windows = "shared model Program\r\nfunction Main()\r\nend function\r\nend model";

        Assert.Multiple(() =>
        {
            Assert.That(
                Formatter.Format(new SourceText(windows, "<test>")),
                Does.Contain("\r\n    function Main()"));

            Assert.That(
                Formatter.Format(new SourceText(windows.Replace("\r\n", "\n"), "<test>")),
                Does.Not.Contain("\r"));
        });
    }

    /// <summary>Whether a file ended with a newline is the writer's business, and is kept.</summary>
    [Test]
    public void WhetherTheFileEndsWithANewlineIsKept() =>
        Assert.Multiple(() =>
        {
            Assert.That(Formatted("shared model Program\nend model\n"), Does.EndWith("\n"));
            Assert.That(Formatted("shared model Program\nend model"), Does.Not.EndWith("\n"));
        });
}
