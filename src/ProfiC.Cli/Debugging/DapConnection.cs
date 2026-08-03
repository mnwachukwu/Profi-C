using System.Text.Json.Nodes;
using ProfiC.Cli.Protocol;

namespace ProfiC.Cli.Debugging;

/// <summary>
/// <para>The Debug Adapter Protocol's messages: what a request, a response and an event are
/// shaped like, and how each is numbered.</para>
/// <para>The framing underneath — <c>Content-Length</c> and the bytes after it — is
/// <see cref="FramedConnection"/>'s, and is shared with the language server because both
/// protocols frame the same way. What is here is only what makes a message a DAP message: an
/// envelope of <c>type</c>, <c>seq</c> and <c>request_seq</c>, which is the adapter's own and
/// nothing like JSON-RPC.</para>
/// <para>Kept apart from the session for the same reason the framing is kept apart from this:
/// the part with a specification to follow can be tested against that specification rather than
/// against a debugger.</para>
/// </summary>
public sealed class DapConnection(Stream input, Stream output) : FramedConnection(input, output)
{
    private int _seq;

    /// <summary>
    /// Every message the protocol carries is numbered in turn, including the ones nobody asked
    /// for. It is what lets an editor tell one from another where several are in flight.
    /// </summary>
    protected override void Stamp(JsonObject message)
    {
        ArgumentNullException.ThrowIfNull(message);

        message["seq"] = ++_seq;
    }

    /// <summary>
    /// An output event on the error stream, which is where an adapter puts anything a reader
    /// might need and nobody asked for.
    /// </summary>
    protected override void ReportUnreadable(string why) =>
        Event("output", new JsonObject
        {
            ["category"] = "stderr",
            ["output"] = $"A message could not be read and was passed over: {why}\n",
        });

    /// <summary>Answers a request, carrying its sequence number so a reply cannot be mistaken.</summary>
    public void Respond(JsonObject request, JsonObject? body = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        JsonObject message = new()
        {
            ["type"] = "response",
            ["request_seq"] = (int?)request["seq"] ?? 0,
            ["success"] = true,
            ["command"] = (string?)request["command"] ?? string.Empty,
        };

        if (body is not null)
        {
            message["body"] = body;
        }

        Send(message);
    }

    /// <summary>
    /// Refuses a request, saying why. A failed response is part of the protocol rather than a
    /// fault: an editor may ask for something an adapter does not do, and being told so is how
    /// it finds out.
    /// </summary>
    public void Refuse(JsonObject request, string why)
    {
        ArgumentNullException.ThrowIfNull(request);

        Send(new JsonObject
        {
            ["type"] = "response",
            ["request_seq"] = (int?)request["seq"] ?? 0,
            ["success"] = false,
            ["command"] = (string?)request["command"] ?? string.Empty,
            ["message"] = why,
        });
    }

    /// <summary>
    /// Tells the editor something it did not ask about — that the program stopped, printed, or
    /// ended. Events are the half of the protocol that makes a debugger feel live.
    /// </summary>
    public void Event(string name, JsonObject? body = null)
    {
        JsonObject message = new()
        {
            ["type"] = "event",
            ["event"] = name,
        };

        if (body is not null)
        {
            message["body"] = body;
        }

        Send(message);
    }
}
