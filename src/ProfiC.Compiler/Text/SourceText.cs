namespace ProfiC.Compiler.Text;

/// <summary>
/// <para>A source file's text, together with the line table needed to turn any character
/// offset into a line and column.</para>
/// <para>The line table is built once, in a single pass, when the text is loaded. Callers
/// that walk the text therefore never have to track line numbers themselves, which is the
/// point: a scanner that advances its index in many places would otherwise have to funnel
/// every one of those advances through a newline-counting helper, and missing a single site
/// corrupts every position downstream without any visible symptom.</para>
/// </summary>
public sealed class SourceText
{
    /// <summary>Offset at which each line begins. Always contains at least one entry.</summary>
    private readonly int[] _lineStarts;

    /// <summary>The name reported in diagnostics. Not necessarily a real path.</summary>
    public string FileName { get; }

    /// <summary>The full text of the source.</summary>
    public string Text { get; }

    public SourceText(string text, string fileName = "<input>")
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(fileName);

        Text = text;
        FileName = fileName;
        _lineStarts = BuildLineStarts(text);
    }

    /// <summary>Reads a source file from disk.</summary>
    public static SourceText FromFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return new SourceText(File.ReadAllText(path), path);
    }

    /// <summary>The number of characters in the source.</summary>
    public int Length => Text.Length;

    /// <summary>The number of lines. A file with no line breaks has one line.</summary>
    public int LineCount => _lineStarts.Length;

    /// <summary>
    /// <para>Builds the table of line-start offsets.</para>
    /// <para>A carriage return followed by a line feed counts as one terminator, and a
    /// lone line feed counts as one. A lone carriage return is whitespace but does not
    /// begin a new line.</para>
    /// </summary>
    private static int[] BuildLineStarts(string text)
    {
        List<int> starts = [0];

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                i++;
                starts.Add(i + 1);
            }
            else if (c == '\n')
            {
                starts.Add(i + 1);
            }
        }

        return [.. starts];
    }

    /// <summary>
    /// <para>Maps a zero-based character offset to its one-based line and column.</para>
    /// <para>Offsets past the end of the text map to the position just past the last
    /// character, so that an end-of-file token has somewhere real to point.</para>
    /// </summary>
    public SourcePosition PositionAt(int offset)
    {
        if (offset < 0)
        {
            return SourcePosition.None;
        }

        if (offset > Text.Length)
        {
            offset = Text.Length;
        }

        int lineIndex = FindLineIndex(offset);
        int column = offset - _lineStarts[lineIndex] + 1;

        return new SourcePosition(lineIndex + 1, column, offset);
    }

    /// <summary>Creates a span beginning at the given offset and covering the given length.</summary>
    public SourceSpan SpanAt(int offset, int length) => new(PositionAt(offset), length);

    /// <summary>
    /// Returns the index into <see cref="_lineStarts"/> of the line containing an offset.
    /// </summary>
    private int FindLineIndex(int offset)
    {
        int index = Array.BinarySearch(_lineStarts, offset);

        // An exact hit means the offset is the first character of that line. Otherwise
        // BinarySearch returns the bitwise complement of the insertion point, and the
        // offset belongs to the line before it.
        return index >= 0 ? index : ~index - 1;
    }

    /// <summary>
    /// <para>Returns the text of a one-based line number, excluding its terminator.</para>
    /// <para>Used to render the source line beneath a diagnostic.</para>
    /// </summary>
    public ReadOnlySpan<char> GetLine(int lineNumber)
    {
        if (lineNumber < 1 || lineNumber > _lineStarts.Length)
        {
            return default;
        }

        int start = _lineStarts[lineNumber - 1];
        int end = lineNumber < _lineStarts.Length ? _lineStarts[lineNumber] : Text.Length;

        // Trim whichever terminator ended the line.
        while (end > start && (Text[end - 1] == '\n' || Text[end - 1] == '\r'))
        {
            end--;
        }

        return Text.AsSpan(start, end - start);
    }

    /// <summary>
    /// Returns the offset of the first character of a one-based line number, or -1 if there is
    /// no such line. This is what turns a line number back into a span.
    /// </summary>
    public int OffsetOfLine(int lineNumber) =>
        lineNumber < 1 || lineNumber > _lineStarts.Length ? -1 : _lineStarts[lineNumber - 1];

    /// <summary>Returns the text covered by a span.</summary>
    public ReadOnlySpan<char> GetText(SourceSpan span) =>
        Text.AsSpan(span.Start.Offset, span.Length);

    public override string ToString() => FileName;
}
