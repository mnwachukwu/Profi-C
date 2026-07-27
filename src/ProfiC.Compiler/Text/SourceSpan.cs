namespace ProfiC.Compiler.Text;

/// <summary>
/// A half-open range of source text, anchored at a start position and measured in characters.
/// </summary>
public readonly record struct SourceSpan(SourcePosition Start, int Length)
{
    /// <summary>The offset one past the last character of this span.</summary>
    public int EndOffset => Start.Offset + Length;

    /// <summary>A span that covers nothing.</summary>
    public static readonly SourceSpan None = new(SourcePosition.None, 0);

    public override string ToString() => $"{Start}+{Length}";
}
