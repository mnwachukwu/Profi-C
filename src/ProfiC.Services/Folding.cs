using ProfiC.Compiler.Ast;
using ProfiC.Compiler.Documentation;
using ProfiC.Compiler.Text;

namespace ProfiC.Services;

/// <summary>
/// <para>Which stretches of a file can be folded away, and what each one holds.</para>
/// <para>Answered from the compiler rather than from indentation, which is what an editor falls
/// back to when nothing tells it otherwise. Indentation is a good guess about a language it does
/// not know and a guess this language does not need to be made about: a block opens with a word
/// and closes with <c>end</c>, and the parser has already read both.</para>
/// <para>Built from the parse alone, with nothing resolved — the same choice <see cref="Outline"/>
/// makes and for the same reason. Folding is wanted most in a long file being worked on, which is
/// exactly when the file does not compile. The parser recovers, so the blocks around a mistake
/// still fold.</para>
/// <para>Comments come from the scanner rather than the tree, since none of them is syntax and no
/// node holds one.</para>
/// </summary>
public static class Folding
{
    /// <summary>
    /// <para>One foldable stretch, with the line it opens on and the line it closes on.</para>
    /// <para>Lines are one-based, as everything a reader sees in Profi-C is.</para>
    /// <para><see cref="Held"/> is what an editor can show in place of what it hid, and is empty
    /// where there is nothing worth saying. <b>Only a comment carries one.</b> A folded block
    /// leaves the line that opens it on screen, and that line names what went away — a second
    /// label beside <c>function TwiceTheFirstEven(integer[] values)</c> tells a reader nothing
    /// they cannot already see. A folded comment leaves the mark that opens it and nothing
    /// else.</para>
    /// </summary>
    public sealed record Range(int Line, int EndLine, string Kind, string Held);

    /// <summary>A block of code, which is what almost everything here is.</summary>
    public const string Block = "region";

    /// <summary>A documentation comment, which an editor may fold apart from code.</summary>
    public const string Comment = "comment";

    /// <summary>Every foldable stretch of one file, in the order they open.</summary>
    public static IReadOnlyList<Range> Of(CompilationUnit unit, SourceText source)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(source);

        List<Range> found = [];

        // Every comment, not only the ones that document something. What a comment says decides
        // what a reader is shown in place of it; it does not decide whether it folds.
        foreach (SourceSpan comment in unit.Comments)
        {
            Add(found, comment, source, Comment, Says(source, comment));
        }

        foreach (SyntaxNode node in Everything(unit))
        {
            if (Folds(node))
            {
                Add(found, node.Span, source, Block, string.Empty);
            }
        }

        found.Sort((left, right) => left.Line == right.Line
            ? right.EndLine.CompareTo(left.EndLine)
            : left.Line.CompareTo(right.Line));

        return found;
    }

    /// <summary>
    /// <para>Whether a node is a block somebody would fold.</para>
    /// <para>Named one at a time rather than taken from "spans more than one line", which is true
    /// of a set literal written down the page and of a call with an argument on each line. Neither
    /// is a block, and an editor offering to fold them puts a control beside lines that are not
    /// worth hiding.</para>
    /// <para><see cref="WalkStmt"/> is absent because lowering makes it and the parser never does,
    /// and nothing here has been lowered.</para>
    /// </summary>
    private static bool Folds(SyntaxNode node) => node switch
    {
        // The file-scoped form takes everything after it and closes at the end of the file, so
        // folding it would fold the file.
        NamespaceDecl inner => !inner.IsFileScoped,

        ModelDecl or StructureDecl or EnumerationDecl => true,

        // One left for a descendant to write ends at its semicolon and holds nothing.
        FunctionDecl function => !function.IsBodiless,

        BlockStmt or IfStmt or ElseIfClause or WhileStmt or LoopForeverStmt or LoopUntilStmt
            or ForStmt or ForEachStmt or SwitchStmt or CaseGroup or TryStmt or CatchClause => true,

        _ => false,
    };

    /// <summary>
    /// Records a span as a range, where it covers more than one line. A block written on one line
    /// has nothing to hide, and an editor given a range that opens and closes on the same line
    /// draws a control that does nothing.
    /// </summary>
    private static void Add(
        List<Range> found, SourceSpan span, SourceText source, string kind, string held)
    {
        int line = span.Start.Line;
        int end = source
            .PositionAt(Math.Min(span.Start.Offset + span.Length, source.Text.Length))
            .Line;

        if (end <= line)
        {
            return;
        }

        found.Add(new Range(line, end, kind, held));
    }

    /// <summary>
    /// <para>What a comment says, in one line, or nothing where a reader can already read it.</para>
    /// <para><b>Only a comment that opens on a bare line has anything to say here.</b> Folding
    /// leaves the opening line on screen, and a run of line comments folds onto its own first
    /// sentence — repeating it beside itself says nothing. A block comment opens with its marks
    /// and closes with them, so folding one hides every word in it.</para>
    /// <para>What it says is its <c>@summary:</c> where it documents something, and its opening
    /// prose otherwise. That prose is joined to the first blank line before it is shortened, so a
    /// sentence that wrapped is cut where it ends rather than where it happened to break.</para>
    /// </summary>
    private static string Says(SourceText source, SourceSpan span)
    {
        int start = span.Start.Offset;
        int end = Math.Min(start + span.Length, source.Text.Length);

        string[] lines = source.Text[start..end].Split('\n');

        if (DocComment.Bare(lines[0]).Length > 0)
        {
            return string.Empty;
        }

        if (DocComment.TryRead(source, start, end, out DocComment? documented))
        {
            return Shortened(documented.Summary);
        }

        List<string> opening = [];

        foreach (string line in lines)
        {
            string bare = DocComment.Bare(line);

            if (bare.Length == 0)
            {
                if (opening.Count > 0)
                {
                    break;
                }

                continue;
            }

            opening.Add(bare);
        }

        return Shortened(string.Join(' ', opening));
    }

    /// <summary>
    /// <para>A summary as one line, short enough to sit at the end of the line it labels.</para>
    /// <para>A summary runs as long as it needs to and may be several sentences. What an editor
    /// has room for is the first of them, so this takes that far and stops.</para>
    /// </summary>
    private static string Shortened(string summary)
    {
        string first = summary.ReplaceLineEndings(" ").Trim();
        int stop = first.IndexOf(". ", StringComparison.Ordinal);

        if (stop > 0)
        {
            first = first[..(stop + 1)];
        }

        return first.Length > 80 ? $"{first[..79].TrimEnd()}…" : first;
    }

    private static IEnumerable<SyntaxNode> Everything(SyntaxNode node)
    {
        yield return node;

        foreach (SyntaxNode child in node.Children)
        {
            foreach (SyntaxNode inside in Everything(child))
            {
                yield return inside;
            }
        }
    }
}
