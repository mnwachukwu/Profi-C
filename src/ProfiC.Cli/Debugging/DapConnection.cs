using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProfiC.Cli.Debugging;

/// <summary>
/// <para>The Debug Adapter Protocol's wire format: JSON messages framed by a byte count.</para>
/// <para>Each message is a header, a blank line, and exactly that many bytes of JSON:</para>
/// <code>
/// Content-Length: 91\r\n
/// \r\n
/// {"seq":1,"type":"request","command":"initialize","arguments":{}}
/// </code>
/// <para>The count is what makes the stream readable at all. A debugger's traffic is a stream of
/// messages with nothing between them, so without a length there is no way to know where one
/// ends — and JSON cannot be scanned for a closing brace, because braces occur inside strings.
/// </para>
/// <para>This is only the framing. What the messages mean is the session's business, kept apart
/// so that the part with a specification to follow can be tested against that specification
/// rather than against a debugger.</para>
/// </summary>
public sealed class DapConnection(Stream input, Stream output)
{
    private const string Header = "Content-Length: ";

    private readonly Stream _input = input;
    private readonly Stream _output = output;
    private readonly Lock _writing = new();

    private int _seq;

    /// <summary>
    /// <para>Reads one message, or null where the stream has ended.</para>
    /// <para>Ending mid-message is an ended stream rather than an error: a debugger is closed by
    /// the editor going away, and that is what going away looks like from here.</para>
    /// <para>A message that will not parse is passed over and the next one read. The count has
    /// already said where it ends, so the stream is still aligned and there is nothing to
    /// recover — where throwing would take the whole session down over one bad message, and a
    /// debugger that vanishes tells the reader nothing about why.</para>
    /// <para>Passed over, but said out loud. Skipping in silence means a request that never gets
    /// an answer and no sign of why, which is the hardest kind of fault to chase in something
    /// whose whole job is to be talked to.</para>
    /// </summary>
    public JsonObject? Read()
    {
        while (true)
        {
            if (ReadContentLength() is not { } length)
            {
                return null;
            }

            byte[] payload = new byte[length];
            int filled = 0;

            while (filled < length)
            {
                // A stream hands over what it has, not what was asked for. Reading once and
                // trusting the count is the classic way to lose the tail of a large message —
                // which here means a stack trace of any size failing on a slow pipe and not a
                // fast one.
                int read = _input.Read(payload, filled, length - filled);

                if (read == 0)
                {
                    return null;
                }

                filled += read;
            }

            if (Parse(payload, out string? why) is { } message)
            {
                return message;
            }

            Event("output", new JsonObject
            {
                ["category"] = "stderr",
                ["output"] = $"A message could not be read and was passed over: {why}\n",
            });
        }
    }

    /// <summary>
    /// One message's bytes as an object, or null with the reason where they are not one. A
    /// well-formed payload that is not an object counts as neither, since every message the
    /// protocol defines is one.
    /// </summary>
    private static JsonObject? Parse(byte[] payload, out string? why)
    {
        try
        {
            if (JsonNode.Parse(Encoding.UTF8.GetString(payload)) is JsonObject message)
            {
                why = null;
                return message;
            }

            why = "it is not a JSON object";
            return null;
        }
        catch (JsonException failure)
        {
            why = failure.Message;
            return null;
        }
    }

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

    /// <summary>
    /// <para>Frames and writes one message.</para>
    /// <para>Locked because events come from the program's thread and responses from the one
    /// reading requests. Two writers interleaving would not corrupt the JSON — each is built
    /// whole — but would interleave the bytes of two framed messages, which no reader can
    /// recover from.</para>
    /// </summary>
    private void Send(JsonObject message)
    {
        lock (_writing)
        {
            message["seq"] = ++_seq;

            byte[] payload = Encoding.UTF8.GetBytes(message.ToJsonString());
            byte[] header = Encoding.UTF8.GetBytes($"{Header}{payload.Length}\r\n\r\n");

            _output.Write(header);
            _output.Write(payload);
            _output.Flush();
        }
    }

    /// <summary>
    /// <para>Reads headers to the blank line and answers with the content length.</para>
    /// <para>Read a byte at a time, which is not a performance question: the payload must not be
    /// touched, and a buffered read would swallow the front of it. Headers are short and there
    /// are two of them at most.</para>
    /// </summary>
    private int? ReadContentLength()
    {
        int? length = null;

        while (true)
        {
            if (ReadLine() is not { } line)
            {
                return null;
            }

            if (line.Length == 0)
            {
                return length;
            }

            if (line.StartsWith(Header, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(
                    line[Header.Length..].Trim(),
                    System.Globalization.CultureInfo.InvariantCulture,
                    out int found))
            {
                length = found;
            }
        }
    }

    private string? ReadLine()
    {
        StringBuilder line = new();

        while (true)
        {
            int next = _input.ReadByte();

            if (next < 0)
            {
                return line.Length > 0 ? line.ToString() : null;
            }

            if (next == '\n')
            {
                return line.ToString().TrimEnd('\r');
            }

            line.Append((char)next);
        }
    }
}
