using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProfiC.Cli.Protocol;

/// <summary>
/// <para>JSON messages framed by a byte count, which is how an editor and a tool beside it talk
/// over a pipe.</para>
/// <para>Each message is a header, a blank line, and exactly that many bytes of JSON:</para>
/// <code>
/// Content-Length: 91\r\n
/// \r\n
/// {"seq":1,"type":"request","command":"initialize","arguments":{}}
/// </code>
/// <para>The count is what makes the stream readable at all. The traffic is messages with
/// nothing between them, so without a length there is no way to know where one ends — and JSON
/// cannot be scanned for a closing brace, because braces occur inside strings.</para>
/// <para><b>Shared, because two protocols frame this way.</b> The Language Server Protocol
/// defines it as its base protocol and the Debug Adapter Protocol borrowed it. What they carry
/// is not shared at all: a language server speaks JSON-RPC, and a debug adapter has an envelope
/// of its own with <c>seq</c> and <c>request_seq</c> in it. So only the framing is here, and
/// what a message means belongs to the protocol that means it.</para>
/// <para>Worth having apart for the reason it was worth having apart from the debug session: it
/// is the one layer with a specification, so it can be held to that rather than to what one
/// editor happens to send.</para>
/// </summary>
public abstract class FramedConnection(Stream input, Stream output)
{
    private const string Header = "Content-Length: ";

    private readonly Stream _input = input;
    private readonly Stream _output = output;
    private readonly Lock _writing = new();

    /// <summary>
    /// <para>Reads one message, or null where the stream has ended.</para>
    /// <para>Ending mid-message is an ended stream rather than an error: one of these is closed
    /// by the editor going away, and that is what going away looks like from here.</para>
    /// <para>A message that will not parse is passed over and the next one read. The count has
    /// already said where it ends, so the stream is still aligned and there is nothing to
    /// recover — where throwing would take the whole session down over one bad message, and a
    /// tool that vanishes tells the reader nothing about why.</para>
    /// <para>Passed over, but said out loud, which is <see cref="ReportUnreadable"/>'s job.
    /// Skipping in silence means a request that never gets an answer and no sign of why, which
    /// is the hardest kind of fault to chase in something whose whole job is to be talked
    /// to.</para>
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

            ReportUnreadable(why!);
        }
    }

    /// <summary>
    /// Says that a message could not be read, in whatever way this protocol has of saying
    /// things. There is no shared way: a debug adapter writes an output event and a language
    /// server logs to the window, and neither is a shape the other understands.
    /// </summary>
    protected abstract void ReportUnreadable(string why);

    /// <summary>
    /// <para>Marks a message as this protocol marks them, just before it is framed.</para>
    /// <para>Called holding the write lock, so that whatever it assigns is assigned in the order
    /// messages reach the wire. For a running count — which is what a debug adapter needs — that
    /// ordering is the whole point of the hook.</para>
    /// <para>Nothing by default, since a protocol may have nothing to add.</para>
    /// </summary>
    protected virtual void Stamp(JsonObject message)
    {
    }

    /// <summary>
    /// <para>Frames and writes one message.</para>
    /// <para>Locked because messages come from more than one thread — a debug adapter's events
    /// come from the program being debugged while its responses come from the thread reading
    /// requests. Two writers interleaving would not corrupt the JSON, since each is built whole,
    /// but would interleave the bytes of two framed messages, which no reader can recover
    /// from.</para>
    /// </summary>
    protected void Send(JsonObject message)
    {
        ArgumentNullException.ThrowIfNull(message);

        lock (_writing)
        {
            Stamp(message);

            byte[] payload = Encoding.UTF8.GetBytes(message.ToJsonString());
            byte[] header = Encoding.UTF8.GetBytes($"{Header}{payload.Length}\r\n\r\n");

            _output.Write(header);
            _output.Write(payload);
            _output.Flush();
        }
    }

    /// <summary>
    /// One message's bytes as an object, or null with the reason where they are not one. A
    /// well-formed payload that is not an object counts as neither, since every message either
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
                    CultureInfo.InvariantCulture,
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
