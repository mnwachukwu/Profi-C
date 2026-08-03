using System.Text;
using System.Text.Json.Nodes;
using ProfiC.Cli.Debugging;

namespace ProfiC.Tests.Debugging;

/// <summary>
/// <para>The shape of a Debug Adapter Protocol message: what a response and an event look like,
/// and how each is numbered.</para>
/// <para>The framing underneath is <see cref="ProfiC.Tests.Protocol.FramedConnectionTests"/>'s,
/// and is shared with the language server. What is held here is only what makes a message this
/// protocol's rather than the other's — an envelope of <c>type</c>, <c>seq</c> and
/// <c>request_seq</c>, which is nothing like JSON-RPC.</para>
/// </summary>
[TestFixture]
public sealed class DapConnectionTests
{
    private static DapConnection Writing(out MemoryStream written)
    {
        written = new MemoryStream();
        return new DapConnection(new MemoryStream(), written);
    }

    private static string Text(MemoryStream written) =>
        Encoding.UTF8.GetString(written.ToArray());

    /// <summary>
    /// A response carries the request's sequence number, which is what lets an editor match a
    /// reply to what it asked — several may be in flight.
    /// </summary>
    [Test]
    public void AResponseCarriesTheRequestsSequenceNumber()
    {
        DapConnection connection = Writing(out MemoryStream written);

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
        DapConnection connection = Writing(out MemoryStream written);

        connection.Refuse(
            new JsonObject { ["seq"] = 3, ["command"] = "setExpression" },
            "changing a value while stopped is not supported");

        Assert.Multiple(() =>
        {
            Assert.That(Text(written), Does.Contain("\"success\":false"));
            Assert.That(Text(written), Does.Contain("not supported"));
        });
    }

    /// <summary>An event says what happened and carries whatever the editor needs to show it.</summary>
    [Test]
    public void AnEventCarriesItsNameAndBody()
    {
        DapConnection connection = Writing(out MemoryStream written);

        connection.Event("stopped", new JsonObject
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
        DapConnection connection = Writing(out MemoryStream written);

        connection.Event("initialized");
        connection.Event("terminated");

        Assert.Multiple(() =>
        {
            Assert.That(Text(written), Does.Contain("\"seq\":1"));
            Assert.That(Text(written), Does.Contain("\"seq\":2"));
        });
    }

    /// <summary>
    /// <para>A message that will not parse is reported as an output event on the error stream.
    /// </para>
    /// <para>That the framing skips it and carries on is held where the framing is. What this
    /// holds is the half that is the adapter's: an editor is told, in the one shape this protocol
    /// has for saying something nobody asked about.</para>
    /// </summary>
    [Test]
    public void AnUnreadableMessageIsReportedAsOutput()
    {
        // Framed correctly and complete, so the reader gets all of it and then finds it is not
        // JSON. A length longer than what follows is a different fault: the stream ends first.
        const string Json = """{"seq":1,"command":"initialize" """;

        byte[] broken = Encoding.UTF8.GetBytes(
            $"Content-Length: {Encoding.UTF8.GetByteCount(Json)}\r\n\r\n{Json}");

        MemoryStream written = new();

        Assert.That(new DapConnection(new MemoryStream(broken), written).Read(), Is.Null);

        string sent = Text(written);

        Assert.Multiple(() =>
        {
            Assert.That(sent, Does.Contain("\"event\":\"output\""));
            Assert.That(sent, Does.Contain("\"category\":\"stderr\""));
            Assert.That(sent, Does.Contain("could not be read"));
        });
    }
}
