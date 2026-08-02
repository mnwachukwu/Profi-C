using System.Text;
using System.Text.Json.Nodes;
using ProfiC.Cli.Debugging;

namespace ProfiC.Tests.Debugging;

/// <summary>
/// <para>The Debug Adapter Protocol's framing, held to what the protocol says rather than to
/// what an editor happens to send.</para>
/// <para>Worth testing on its own because it is the one layer with a specification: everything
/// above it is a choice about how to debug Profi-C, and this is a format somebody else defined.
/// A reader that works against VS Code's exact traffic and nothing else is a reader that breaks
/// the first time another client sends the same thing differently.</para>
/// </summary>
[TestFixture]
public sealed class DapConnectionTests
{
    private static byte[] Framed(string json) =>
        Encoding.UTF8.GetBytes(
            $"Content-Length: {Encoding.UTF8.GetByteCount(json)}\r\n\r\n{json}");

    private static DapConnection Reading(byte[] bytes, out MemoryStream written)
    {
        written = new MemoryStream();
        return new DapConnection(new MemoryStream(bytes), written);
    }

    private static string Text(MemoryStream written) =>
        Encoding.UTF8.GetString(written.ToArray());

    [Test]
    public void AFramedMessageIsRead()
    {
        DapConnection connection = Reading(
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

        DapConnection connection = Reading(both, out _);

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

        DapConnection connection = new(new Trickle(Framed(json), 3), new MemoryStream());

        JsonObject? message = connection.Read();

        Assert.Multiple(() =>
        {
            Assert.That(message, Is.Not.Null);
            Assert.That((string?)message!["command"], Is.EqualTo("stackTrace"));
            Assert.That(((string?)message["padding"])?.Length, Is.EqualTo(400));
        });
    }

    /// <summary>Non-ASCII text is counted in bytes, not characters, as the protocol says.</summary>
    [Test]
    public void TheLengthIsBytesRatherThanCharacters()
    {
        DapConnection connection = Reading(
            Framed("""{"seq":1,"command":"output","text":"scored — exactly 1|1"}"""), out _);

        Assert.That((string?)connection.Read()!["text"], Is.EqualTo("scored — exactly 1|1"));
    }

    [Test]
    public void AnEndedStreamReadsAsNull() =>
        Assert.That(Reading([], out _).Read(), Is.Null);

    /// <summary>
    /// <para>A message that will not parse is passed over, and the next one is read.</para>
    /// <para>The length has already said where the bad message ends, so nothing is lost and the
    /// stream is still aligned. Throwing instead would take down a whole debugging session over
    /// one malformed message, and from the reader's side a debugger that vanishes says nothing
    /// about why it went.</para>
    /// <para>It is said out loud rather than skipped in silence. A request that gets no answer
    /// and no explanation is the hardest kind of fault to chase in something whose whole job is
    /// to be talked to.</para>
    /// </summary>
    [Test]
    public void AMessageThatWillNotParseIsPassedOverAndSaidOutLoud()
    {
        byte[] stream =
        [
            .. Framed("""{"seq":1,"command":"initialize" """),
            .. Framed("""{"seq":2,"command":"configurationDone"}"""),
        ];

        DapConnection connection = Reading(stream, out MemoryStream written);

        Assert.Multiple(() =>
        {
            Assert.That((string?)connection.Read()!["command"], Is.EqualTo("configurationDone"),
                        "the broken one is skipped and the good one still arrives");

            Assert.That(connection.Read(), Is.Null);

            Assert.That(Text(written), Does.Contain("could not be read"),
                        "and the editor is told, rather than left waiting on an answer");
        });
    }

    /// <summary>
    /// A response carries the request's sequence number, which is what lets an editor match a
    /// reply to what it asked — several may be in flight.
    /// </summary>
    [Test]
    public void AResponseCarriesTheRequestsSequenceNumber()
    {
        DapConnection connection = Reading([], out MemoryStream written);

        connection.Respond(
            new JsonObject { ["seq"] = 7, ["command"] = "threads" },
            new JsonObject { ["threads"] = new JsonArray() });

        string sent = Text(written);

        Assert.Multiple(() =>
        {
            Assert.That(sent, Does.StartWith("Content-Length: "));
            Assert.That(sent, Does.Contain("\"request_seq\":7"));
            Assert.That(sent, Does.Contain("\"command\":\"threads\""));
            Assert.That(sent, Does.Contain("\"success\":true"));
        });
    }

    [Test]
    public void ARefusalSaysWhy()
    {
        DapConnection connection = Reading([], out MemoryStream written);

        connection.Refuse(
            new JsonObject { ["seq"] = 3, ["command"] = "setExpression" },
            "changing a value while stopped is not supported");

        Assert.Multiple(() =>
        {
            Assert.That(Text(written), Does.Contain("\"success\":false"));
            Assert.That(Text(written), Does.Contain("not supported"));
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
        DapConnection writer = Reading([], out MemoryStream written);

        writer.Event("stopped", new JsonObject
        {
            ["reason"] = "breakpoint",
            ["threadId"] = 1,
            ["line"] = 42,
        });

        JsonObject? read = new DapConnection(
            new MemoryStream(written.ToArray()), new MemoryStream()).Read();

        Assert.Multiple(() =>
        {
            Assert.That((string?)read!["type"], Is.EqualTo("event"));
            Assert.That((string?)read["event"], Is.EqualTo("stopped"));
            Assert.That((int?)read["body"]!["line"], Is.EqualTo(42));
        });
    }

    /// <summary>Every message written carries the next sequence number, as the protocol requires.</summary>
    [Test]
    public void EachMessageWrittenIsNumberedInTurn()
    {
        DapConnection connection = Reading([], out MemoryStream written);

        connection.Event("initialized");
        connection.Event("terminated");

        Assert.Multiple(() =>
        {
            Assert.That(Text(written), Does.Contain("\"seq\":1"));
            Assert.That(Text(written), Does.Contain("\"seq\":2"));
        });
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
