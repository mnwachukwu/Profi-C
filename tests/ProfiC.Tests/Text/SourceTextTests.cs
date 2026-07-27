using ProfiC.Compiler.Text;

namespace ProfiC.Tests.Text;

/// <summary>
/// The line table, which every source position depends on.
/// </summary>
[TestFixture]
public sealed class SourceTextTests
{
    [Test]
    public void EmptyText_HasOneLine()
    {
        Assert.That(new SourceText(string.Empty).LineCount, Is.EqualTo(1));
    }

    [Test]
    public void TextWithNoBreaks_HasOneLine()
    {
        Assert.That(new SourceText("one line").LineCount, Is.EqualTo(1));
    }

    [TestCase("a\nb", 2)]
    [TestCase("a\nb\nc", 3)]
    [TestCase("a\r\nb", 2)]
    [TestCase("a\r\nb\r\nc", 3)]
    [TestCase("a\n", 2)]
    public void LineBreaks_AreCounted(string text, int expected)
    {
        Assert.That(new SourceText(text).LineCount, Is.EqualTo(expected));
    }

    [Test]
    public void LoneCarriageReturn_IsWhitespaceButNotALineBreak()
    {
        Assert.That(new SourceText("a\rb").LineCount, Is.EqualTo(1));
    }

    [Test]
    public void PositionAt_MapsOffsetsToLineAndColumn()
    {
        SourceText source = new("ab\ncd\nef");

        Assert.Multiple(() =>
        {
            Assert.That(source.PositionAt(0), Is.EqualTo(new SourcePosition(1, 1, 0)));
            Assert.That(source.PositionAt(1), Is.EqualTo(new SourcePosition(1, 2, 1)));
            Assert.That(source.PositionAt(3), Is.EqualTo(new SourcePosition(2, 1, 3)));
            Assert.That(source.PositionAt(4), Is.EqualTo(new SourcePosition(2, 2, 4)));
            Assert.That(source.PositionAt(6), Is.EqualTo(new SourcePosition(3, 1, 6)));
        });
    }

    [Test]
    public void PositionAt_HandlesCarriageReturnLineFeed()
    {
        SourceText source = new("ab\r\ncd");

        Assert.Multiple(() =>
        {
            Assert.That(source.PositionAt(4).Line, Is.EqualTo(2));
            Assert.That(source.PositionAt(4).Column, Is.EqualTo(1));
        });
    }

    [Test]
    public void PositionAt_PastTheEnd_ClampsToJustAfterTheLastCharacter()
    {
        SourceText source = new("abc");

        Assert.Multiple(() =>
        {
            Assert.That(source.PositionAt(3), Is.EqualTo(new SourcePosition(1, 4, 3)));
            Assert.That(source.PositionAt(999).Offset, Is.EqualTo(3));
        });
    }

    [Test]
    public void PositionAt_NegativeOffset_IsNone()
    {
        Assert.That(new SourceText("abc").PositionAt(-1), Is.EqualTo(SourcePosition.None));
    }

    [Test]
    public void PositionAt_RoundTripsForEveryOffset()
    {
        SourceText source = new("model P\n  let x = 1;\n\nend model\n");

        for (int offset = 0; offset <= source.Length; offset++)
        {
            Assert.That(source.PositionAt(offset).Offset, Is.EqualTo(offset));
        }
    }

    [TestCase(1, "first")]
    [TestCase(2, "second")]
    [TestCase(3, "third")]
    public void GetLine_ReturnsTheLineWithoutItsTerminator(int line, string expected)
    {
        SourceText source = new("first\r\nsecond\nthird");
        Assert.That(source.GetLine(line).ToString(), Is.EqualTo(expected));
    }

    [TestCase(0)]
    [TestCase(99)]
    public void GetLine_OutOfRange_IsEmpty(int line)
    {
        Assert.That(new SourceText("abc").GetLine(line).IsEmpty, Is.True);
    }

    [Test]
    public void SpanAt_CoversTheRequestedText()
    {
        SourceText source = new("model Program");
        SourceSpan span = source.SpanAt(6, 7);

        Assert.Multiple(() =>
        {
            Assert.That(source.GetText(span).ToString(), Is.EqualTo("Program"));
            Assert.That(span.EndOffset, Is.EqualTo(13));
        });
    }

    [Test]
    public void FileName_DefaultsWhenNotSupplied()
    {
        Assert.That(new SourceText("x").FileName, Is.EqualTo("<input>"));
    }
}
