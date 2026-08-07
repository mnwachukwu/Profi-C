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
/// </summary>
public static class Folding
{
    /// <summary>
    /// <para>One foldable stretch, with the line it opens on and the line it closes on.</para>
    /// <para>Lines are one-based, as everything a reader sees in Profi-C is.</para>
    /// <para><see cref="Held"/> is what an editor can show in place of what it hid. A block whose
    /// declaration is documented says what its documentation says; anything else says how much
    /// there is. Both beat the bare mark an editor draws on its own, which says only that
    /// something is there.</para>
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

        // What each documented line says about itself, so that a block can be labeled with its
        // author's own words rather than with a count of what is inside it.
        Dictionary<int, string> summaries = [];

        foreach (DocComment comment in unit.Documentation)
        {
            if (comment.Summary.Length > 0)
            {
                summaries[comment.Documents] = Shortened(comment.Summary);
            }
        }

        List<Range> found = [];

        foreach (DocComment comment in unit.Documentation)
        {
            Add(found, comment.Span, source, Comment, comment.Summary.Length > 0
                ? Shortened(comment.Summary)
                : string.Empty);
        }

        foreach (SyntaxNode node in Everything(unit))
        {
            if (Folds(node))
            {
                Add(found, node.Span, source, Block,
                    summaries.GetValueOrDefault(node.Span.Start.Line, string.Empty));
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
        List<Range> found, SourceSpan span, SourceText source, string kind, string summary)
    {
        int line = span.Start.Line;
        int end = source
            .PositionAt(Math.Min(span.Start.Offset + span.Length, source.Text.Length))
            .Line;

        if (end <= line)
        {
            return;
        }

        found.Add(new Range(
            line,
            end,
            kind,
            summary.Length > 0 ? summary : Counted(end - line)));
    }

    /// <summary>How much a fold hides, for a block whose declaration says nothing about itself.</summary>
    private static string Counted(int lines) => lines == 1 ? "1 line" : $"{lines} lines";

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
