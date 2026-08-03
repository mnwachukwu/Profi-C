using System.Text;
using System.Text.Json.Nodes;
using ProfiC.Cli.Protocol;

namespace ProfiC.Tests.Protocol;

/// <summary>
/// <para>The framing two protocols share, held to what they specify rather than to what an
/// editor happens to send.</para>
/// <para>Worth testing on its own because it is the one layer with a specification: everything
/// above it is a choice about how to debug or describe Profi-C, and this is a format somebody
/// else defined. A reader that works against one editor's exact traffic and nothing else is a
/// reader that breaks the first time another client sends the same thing differently.</para>
/// <para>Tested here rather than through the debug adapter, which is what used to hold it. The
/// language server frames the same way, and framing covered only by one protocol's tests is
/// framing the other is free to break.</para>
/// </summary>
[TestFixture]
public sealed class FramedConnectionTests
{
    private static byte[] Framed(string json) =>
        Encoding.UTF8.GetBytes(
            $"Content-Length: {Encoding.UTF8.GetByteCount(json)}\r\n\r\n{json}");

    private static Wire Reading(byte[] bytes, out MemoryStream written)
    {
        written = new MemoryStream();
        return new Wire(new MemoryStream(bytes), written);
    }

    private static string Text(MemoryStream written) =>
        Encoding.UTF8.GetString(written.ToArray());

    [Test]
    public void AFramedMessageIsRead()
    {
        Wire connection = Reading(
            Framed("""{"seq":1,"type":"request","command":"initialize"}"""), out _);

        JsonObject? message = connection.Read();

        Assert.Multiple(() =>
        {
            Assert.That(message, Is.Not.Null);
            Assert.That((string?)message!["command"], Is.EqualTo("initialize"));
            Assert.That((int?)message["seq"], Is.EqualTo(1));
        });
    }

    /// <summary>
    /// Messages arrive back to back with nothing between them, which is why the length matters.
    /// </summary>
    [Test]
    public void MessagesArriveBackToBack()
    {
        byte[] both =
        [
            .. Framed("""{"seq":1,"command":"initialize"}"""),
            .. Framed("""{"seq":2,"command":"configurationDone"}"""),
        ];

        Wire connection = Reading(both, out _);

        Assert.Multiple(() =>
        {
            Assert.That((string?)connection.Read()!["command"], Is.EqualTo("initialize"));
            Assert.That((string?)connection.Read()!["command"], Is.EqualTo("configurationDone"));
            Assert.That(connection.Read(), Is.Null, "and then the stream has ended");
        });
    }

    /// <summary>
    /// <para>A stream that hands over its bytes a few at a time is still read whole.</para>
    /// <para>The failure this guards is one that only shows on a slow pipe or a large message —
    /// a stack trace of any size arriving truncated — and never on a memory stream that answers
    /// every read in full. So the stream here answers in threes.</para>
    /// </summary>
    [Test]
    public void AMessageArrivingInPiecesIsStillReadWhole()
    {
        string json = $$"""{"seq":1,"command":"stackTrace","padding":"{{new string('x', 400)}}"}""";

        Wire connection = new(new Trickle(Framed(json), 3), new MemoryStream());

        JsonObject? message = connection.Read();

        Assert.Multiple(() =>
        {
            Assert.That(message, Is.Not.Null);
            Assert.That((string?)message!["command"], Is.EqualTo("stackTrace"));
            Assert.That(((string?)message["padding"])?.Length, Is.EqualTo(400));
        });
    }

    /// <summary>Non-ASCII text is counted in bytes, not characters, as the protocols say.</summary>
    [Test]
    public void TheLengthIsBytesRatherThanCharacters()
    {
        Wire connection = Reading(
            Framed("""{"seq":1,"command":"output","text":"scored — exactly 1|1"}"""), out _);

        Assert.That((string?)connection.Read()!["text"], Is.EqualTo("scored — exactly 1|1"));
    }

    [Test]
    public void AnEndedStreamReadsAsNull() =>
        Assert.That(Reading([], out _).Read(), Is.Null);

    /// <summary>
    /// <para>A message that will not parse is passed over, and the next one is read.</para>
    /// <para>The length has already said where the bad message ends, so nothing is lost and the
    /// stream is still aligned. Throwing instead would take down a whole session over one
    /// malformed message, and from the reader's side a tool that vanishes says nothing about why
    /// it went.</para>
    /// <para>It is handed to the protocol above rather than skipped in silence. A request that
    /// gets no answer and no explanation is the hardest kind of fault to chase in something whose
    /// whole job is to be talked to.</para>
    /// </summary>
    [Test]
    public void AMessageThatWillNotParseIsPassedOverAndSaidOutLoud()
    {
        byte[] stream =
        [
            .. Framed("""{"seq":1,"command":"initialize" """),
            .. Framed("""{"seq":2,"command":"configurationDone"}"""),
        ];

        Wire connection = Reading(stream, out _);

        Assert.Multiple(() =>
        {
            Assert.That((string?)connection.Read()!["command"], Is.EqualTo("configurationDone"),
                        "the broken one is skipped and the good one still arrives");

            Assert.That(connection.Read(), Is.Null);
            Assert.That(connection.Unreadable, Has.Count.EqualTo(1), "and it was reported");
        });
    }

    /// <summary>A payload that parses but is not an object is no more a message than one that will not parse.</summary>
    [Test]
    public void APayloadThatIsNotAnObjectIsPassedOverToo()
    {
        Wire connection = Reading(Framed("[1, 2, 3]"), out _);

        Assert.Multiple(() =>
        {
            Assert.That(connection.Read(), Is.Null);
            Assert.That(connection.Unreadable, Does.Contain("it is not a JSON object"));
        });
    }

    /// <summary>
    /// <para>What is written can be read back, header and all.</para>
    /// <para>The strongest check available without an editor: whatever the framing does, it does
    /// consistently, so the pair cannot drift apart in the same direction and still pass.</para>
    /// </summary>
    [Test]
    public void WhatIsWrittenCanBeReadBack()
    {
        Wire writer = Reading([], out MemoryStream written);

        writer.Write(new JsonObject { ["greeting"] = "hello", ["line"] = 42 });

        JsonObject? read = new Wire(
            new MemoryStream(written.ToArray()), new MemoryStream()).Read();

        Assert.Multiple(() =>
        {
            Assert.That((string?)read!["greeting"], Is.EqualTo("hello"));
            Assert.That((int?)read["line"], Is.EqualTo(42));
            Assert.That(Text(written), Does.StartWith("Content-Length: "));
        });
    }

    /// <summary>
    /// <para>A protocol may mark each message on its way out, and every message gets the mark.
    /// </para>
    /// <para>The hook a debug adapter numbers messages with, and the one a language server will
    /// use for something else. Held here so that it stays a thing the framing offers rather than
    /// something one protocol happens to rely on.</para>
    /// </summary>
    [Test]
    public void EachMessageIsMarkedOnItsWayOut()
    {
        Counting connection = new(new MemoryStream(), new MemoryStream());

        connection.Write(new JsonObject { ["first"] = true });
        connection.Write(new JsonObject { ["second"] = true });

        Assert.That(connection.Marked, Is.EqualTo(2));
    }

    /// <summary>The framing with nothing above it: what is unreadable is recorded rather than sent.</summary>
    private sealed class Wire(Stream input, Stream output) : FramedConnection(input, output)
    {
        public List<string> Unreadable { get; } = [];

        public void Write(JsonObject message) => Send(message);

        protected override void ReportUnreadable(string why) => Unreadable.Add(why);
    }

    /// <summary>A protocol that marks every message, as a debug adapter numbers them.</summary>
    private sealed class Counting(Stream input, Stream output) : FramedConnection(input, output)
    {
        public int Marked { get; private set; }

        public void Write(JsonObject message) => Send(message);

        protected override void ReportUnreadable(string why)
        {
        }

        protected override void Stamp(JsonObject message) => Marked++;
    }

    /// <summary>A stream that answers every read with a few bytes, as a pipe does.</summary>
    private sealed class Trickle(byte[] all, int atATime) : Stream
    {
        private int _at;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => all.Length;

        public override long Position
        {
            get => _at;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int giving = Math.Min(Math.Min(atATime, count), all.Length - _at);

            Array.Copy(all, _at, buffer, offset, giving);
            _at += giving;

            return giving;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
