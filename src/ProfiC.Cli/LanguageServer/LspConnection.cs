using System.Text.Json.Nodes;
using ProfiC.Cli.Protocol;

namespace ProfiC.Cli.LanguageServer;

/// <summary>
/// <para>The Language Server Protocol's messages, which are JSON-RPC 2.0.</para>
/// <para>The framing underneath — <c>Content-Length</c> and the bytes after it — is
/// <see cref="FramedConnection"/>'s, and is shared with the debug adapter because both protocols
/// frame the same way. What is here is only what makes a message JSON-RPC: a <c>jsonrpc</c>
/// version, an <c>id</c> on anything expecting an answer, and an error shaped the way the
/// specification says errors are shaped.</para>
/// <para><b>Three kinds of message, and the difference matters.</b> A <em>request</em> carries an
/// id and is waiting; a <em>notification</em> carries none and nothing is waiting, which is what
/// lets the server defer the work an edit implies; and a <em>response</em> answers an id. Reading
/// a notification as a request means answering something nobody asked, and an editor will
/// complain about an id it never sent.</para>
/// </summary>
public sealed class LspConnection(Stream input, Stream output) : FramedConnection(input, output)
{
    /// <summary>The version every JSON-RPC message carries, and the only one there is.</summary>
    private const string Version = "2.0";

    /// <summary>
    /// <para>The codes the protocol reserves, of which these are the ones a server sends.</para>
    /// <para>Numbers rather than names on the wire, so they are written down here rather than
    /// left inline where nobody could tell -32601 from a typo.</para>
    /// </summary>
    public static class Fault
    {
        /// <summary>A request naming something this server does not do.</summary>
        public const int MethodNotFound = -32601;

        /// <summary>A request whose parameters are missing or not the shape asked for.</summary>
        public const int InvalidParams = -32602;

        /// <summary>Anything the server got wrong, which is a fault here rather than there.</summary>
        public const int InternalError = -32603;

        /// <summary>
        /// A request abandoned because the answer stopped being wanted. Reserved by the protocol
        /// rather than by JSON-RPC, and the code a client expects when it cancels one.
        /// </summary>
        public const int RequestCancelled = -32800;
    }

    /// <summary>
    /// Nothing is added to an outgoing message. A response carries the id it answers and a
    /// notification carries none, so unlike a debug adapter there is no running count to keep.
    /// </summary>
    protected override void Stamp(JsonObject message)
    {
    }

    /// <summary>
    /// A message that could not be read, logged to the editor's window. The one thing a server
    /// can say when it has no id to answer against.
    /// </summary>
    protected override void ReportUnreadable(string why) =>
        Log($"A message could not be read and was passed over: {why}");

    /// <summary>Answers a request, carrying the id it was asked with.</summary>
    public void Respond(JsonNode? id, JsonNode? result) =>
        Send(new JsonObject
        {
            ["jsonrpc"] = Version,
            ["id"] = id?.DeepClone(),
            ["result"] = result,
        });

    /// <summary>
    /// Refuses a request, with a code from <see cref="Fault"/>. A failed response is part of the
    /// protocol rather than a fault in it: an editor may ask for something this does not do, and
    /// being told so is how it finds out.
    /// </summary>
    public void Refuse(JsonNode? id, int code, string why) =>
        Send(new JsonObject
        {
            ["jsonrpc"] = Version,
            ["id"] = id?.DeepClone(),
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = why,
            },
        });

    /// <summary>
    /// Tells the editor something it did not ask about. Diagnostics arrive this way, which is
    /// what lets them appear when the reader stops typing rather than when something asks.
    /// </summary>
    public void Notify(string method, JsonObject? parameters = null) =>
        Send(new JsonObject
        {
            ["jsonrpc"] = Version,
            ["method"] = method,
            ["params"] = parameters,
        });

    /// <summary>
    /// <para>Writes a line to the editor's Profi-C output channel.</para>
    /// <para>Standard error is not available to say anything in: it belongs to the transport,
    /// and writing a byte to it that is not a framed message desynchronizes the stream for
    /// good.</para>
    /// </summary>
    public void Log(string message) =>
        Notify("window/logMessage", new JsonObject
        {
            // 3 is Info in the protocol's numbering. A server that logged everything as an error
            // would have an editor showing a red badge for ordinary chatter.
            ["type"] = 3,
            ["message"] = message,
        });
}
