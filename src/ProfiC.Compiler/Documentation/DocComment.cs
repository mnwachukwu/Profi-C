using System.Diagnostics.CodeAnalysis;
using ProfiC.Compiler.Text;

namespace ProfiC.Compiler.Documentation;

/// <summary>One labeled part of a documentation comment, and the text it carries.</summary>
/// <param name="Name">
/// The word between the mark and the colon. <c>summary</c> for the opening part,
/// <c>remarks</c>, <c>yields</c> and <c>throws</c> for those, and otherwise a parameter's name.
/// </param>
/// <param name="Text">Everything after the colon, with continuation lines joined on.</param>
/// <param name="Span">The label's own line, so what is reported points at it.</param>
public sealed record DocLabel(string Name, string Text, SourceSpan Span);

/// <summary>
/// <para>What a reader wrote about a declaration, ready for anything that shows it.</para>
/// <para>Nothing here is syntax. A documentation comment is recognized while scanning and
/// carried beside the tree rather than in it, so the grammar has no idea it exists and neither
/// does the parser.</para>
/// </summary>
public sealed class DocComment
{
    /// <summary>The mark that opens every label.</summary>
    public const char Mark = '@';

    /// <summary>The label that opens one, and carries its first part.</summary>
    public const string Opening = "summary";

    /// <summary>
    /// Everything worth saying that the summary is too short to hold. The split is C#'s, and
    /// it is what lets one line stand in a list of completions while the whole explanation
    /// waits behind a hover.
    /// </summary>
    public const string Remarks = "remarks";

    /// <summary>What a function gives back.</summary>
    public const string Yields = "yields";

    /// <summary>What a function can raise.</summary>
    public const string Throws = "throws";

    /// <summary>The labels that mean something fixed rather than naming a parameter.</summary>
    private static readonly string[] Fixed = [Opening, Remarks, Yields, Throws];

    private DocComment(SourceSpan span, int documents, IReadOnlyList<DocLabel> labels)
    {
        Span = span;
        Documents = documents;
        Labels = labels;
    }

    /// <summary>Where the comment itself sits.</summary>
    public SourceSpan Span { get; }

    /// <summary>
    /// The line this documents: the first line carrying code below it. Zero where nothing
    /// follows, which documents nothing.
    /// </summary>
    public int Documents { get; }

    /// <summary>Every labeled part, in the order written, beginning with the summary.</summary>
    public IReadOnlyList<DocLabel> Labels { get; }

    /// <summary>The opening part, which every documentation comment has.</summary>
    public string Summary => Text(Opening);

    /// <summary>The fuller explanation, where one was written.</summary>
    public string Remark => Text(Remarks);

    /// <summary>Every label naming a parameter rather than a fixed part.</summary>
    public IEnumerable<DocLabel> Parameters =>
        Labels.Where(l => !Fixed.Contains(l.Name, StringComparer.Ordinal));

    /// <summary>
    /// <para>Reads a documentation comment from the text between two offsets, or reports that
    /// the comment is an ordinary remark.</para>
    /// <para>One is recognized by opening with <c>@summary:</c>, not by where it sits. A block
    /// above a declaration is prose unless it says otherwise, which is the same rule an
    /// <c>ignore</c> directive follows and holds for the same reason: promoting a remark to
    /// something the tooling acts on, purely because of where a reader put it, acts on
    /// sentences nobody meant that way.</para>
    /// </summary>
    public static bool TryRead(
        SourceText source,
        int start,
        int end,
        [NotNullWhen(true)] out DocComment? comment)
    {
        ArgumentNullException.ThrowIfNull(source);

        comment = null;

        List<DocLabel> labels = [];

        // Offsets are tracked alongside the text so that each label can be reported at the
        // line it was written on rather than at the comment as a whole.
        int offset = start;
        bool blankSince = false;
        DocLabel? open = null;

        foreach (string line in source.Text[start..end].Split('\n'))
        {
            string text = line.TrimEnd('\r');
            string bare = Bare(text);

            if (bare.Length == 0)
            {
                blankSince = open is not null;
                offset += line.Length + 1;
                continue;
            }

            if (Split(bare) is var (name, rest))
            {
                if (labels.Count == 0 && name != Opening)
                {
                    return false;
                }

                // From the mark to the end of the line, so the caret sits on the label rather
                // than on whatever indentation precedes it.
                int mark = text.IndexOf(Mark, StringComparison.Ordinal);

                open = new DocLabel(
                    name,
                    rest,
                    new SourceSpan(source.PositionAt(offset + mark), text.Length - mark));

                labels.Add(open);
            }
            else if (open is not null)
            {
                // A line carrying no label continues the one above it, which is what lets a
                // paragraph wrap. A blank line between them is kept as a break rather than
                // closing the label, so a summary may run to several paragraphs and still be
                // one summary. Nothing written is discarded.
                labels[^1] = open = open with
                {
                    Text = open.Text.Length == 0
                        ? bare
                        : open.Text + (blankSince ? "\n\n" : " ") + bare,
                };
            }
            else
            {
                return false;
            }

            blankSince = false;
            offset += line.Length + 1;
        }

        if (labels.Count == 0)
        {
            return false;
        }

        comment = new DocComment(
            new SourceSpan(source.PositionAt(start), end - start), 0, labels);

        return true;
    }

    /// <summary>
    /// What one line of a comment says, with its marks and indentation taken off. The marks are
    /// stripped rather than required, so the block and line forms read the same way. A line
    /// carrying nothing but marks comes back empty.
    /// </summary>
    public static string Bare(string line) =>
        line is null ? string.Empty : line.Trim().TrimStart('#').Trim();

    /// <summary>
    /// <para>Splits a label from its text, or answers that the line carries none.</para>
    /// <para><b><c>@</c> is what makes a label a label</b>, and it is not decoration. Prose
    /// wraps, and a wrapped line often begins with a word and a colon — "that is why it yields
    /// an / optional: ..." — which is what every such line in the corpus turned out to be.
    /// Without the mark, telling those from a label needs a rule about where a line sits, and
    /// no such rule survives ordinary formatting: one that reads a label only at the start of
    /// a paragraph refuses labels written on consecutive lines, and one that also accepts a
    /// line after a label refuses the label following a wrapped one. The mark costs a
    /// character and removes the question.</para>
    /// </summary>
    private static (string Name, string Text)? Split(string line)
    {
        if (line[0] != Mark)
        {
            return null;
        }

        int colon = line.IndexOf(':', StringComparison.Ordinal);

        if (colon <= 1)
        {
            return null;
        }

        string name = line[1..colon];

        return name.All(c => char.IsLetterOrDigit(c) || c == '_') && !char.IsDigit(name[0])
            ? (name, line[(colon + 1)..].Trim())
            : null;
    }

    /// <summary>What one fixed label carries, or nothing where it was not written.</summary>
    private string Text(string name) =>
        Labels.FirstOrDefault(l => l.Name == name)?.Text ?? string.Empty;

    /// <summary>The same comment, told which line it documents.</summary>
    public DocComment Documenting(int line) => new(Span, line, Labels);
}
