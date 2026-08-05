using System.Text.Json.Nodes;
using ProfiC.Compiler.Diagnostics;
using ProfiC.Compiler.Text;

namespace ProfiC.Cli.LanguageServer;

/// <summary>
/// <para>Where the compiler's idea of a place and the protocol's meet.</para>
/// <para>Kept in one file because there are exactly two disagreements and both are the kind that
/// is wrong by one forever once it is wrong anywhere: the compiler counts lines and columns from
/// one, as every Profi-C diagnostic prints them, and the protocol counts from zero. Doing the
/// arithmetic at each call site is how one of them ends up off by one and the others do not.
/// </para>
/// </summary>
public static class Conversions
{
    /// <summary>
    /// <para>The path a <c>file:</c> URI names, or null where it names something else.</para>
    /// <para>An editor speaks URIs and the compiler speaks paths, and the gap between them is
    /// wider than it looks: a space arrives as <c>%20</c>, a Windows drive as <c>/d%3A/</c>.
    /// <see cref="Uri.LocalPath"/> answers most of it, and answers it per platform.</para>
    /// <para><b>It does not answer the escaped drive.</b> <c>file:///D:/x</c> comes back as
    /// <c>D:\x</c> and <c>file:///d%3A/x</c> as <c>/d:/x</c> — the same file written two ways,
    /// answered two ways, and the second is the one VS Code sends. Left as it is, the leading
    /// slash makes the drive read as a folder under the current drive, so a file in
    /// <c>D:\Repos</c> is looked for in <c>D:\d:\Repos</c> and every question about it fails.
    /// </para>
    /// <para><b>Answered in the form the rest of the compiler writes paths in</b>, which means
    /// normalized rather than merely correct. An editor's URI yields forward slashes even on
    /// Windows, and everything that matches a document against a compilation compares it to a
    /// full path — case-insensitively, but not separator-insensitively. Two spellings of one file
    /// never match, so every question about a place answers null while diagnostics, which match
    /// nothing, go on working.</para>
    /// <para>Null for anything not on disk — an editor's untitled buffer arrives as
    /// <c>untitled:Untitled-1</c>, which names no file and cannot be compiled. Saying so here
    /// keeps every caller from inventing a path for it.</para>
    /// </summary>
    public static string? PathOf(string? uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsed) || !parsed.IsFile)
        {
            return null;
        }

        string local = parsed.LocalPath;

        if (local.Length >= 3
            && local[0] is '/' or '\\'
            && char.IsAsciiLetter(local[1])
            && local[2] == ':')
        {
            local = local[1..];
        }

        try
        {
            return Path.GetFullPath(local);
        }
        catch (ArgumentException)
        {
            // A URI naming something no file system could hold. It arrived from outside, so
            // answering "not a file" is the same thing this says about an unsaved buffer.
            return null;
        }
    }

    /// <summary>The URI naming a file, which is how the protocol asks about one.</summary>
    public static string UriOf(string path) => new Uri(Path.GetFullPath(path)).AbsoluteUri;

    /// <summary>
    /// The offset a protocol position names, or null where the file has no such place — which an
    /// editor can ask for, since its idea of the document and this one's may differ by a keystroke.
    /// </summary>
    public static int? OffsetOf(JsonObject? position, SourceText source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if ((int?)position?["line"] is not { } line || (int?)position?["character"] is not { } character)
        {
            return null;
        }

        // From zero in the protocol, from one in the compiler.
        if (line < 0 || line + 1 > source.LineCount)
        {
            return null;
        }

        int start = source.OffsetOfLine(line + 1);
        int offset = start + Math.Max(0, character);

        return offset <= source.Text.Length ? offset : null;
    }

    /// <summary>
    /// <para>The protocol's kind for one of the outline's, so an editor draws the right icon.
    /// </para>
    /// <para>The numbers are the protocol's own <c>SymbolKind</c>. A structure is Struct because
    /// that is what the word means here; a constructor is told apart from a function by the
    /// outline, which knows the rule that a function named for its model is one.</para>
    /// </summary>
    public static int SymbolKindOf(string kind) => kind switch
    {
        "namespace" => 3,
        "model" => 5,
        "structure" => 23,
        "enumeration" => 10,
        "enumMember" => 22,
        "constructor" => 9,
        "field" => 8,
        _ => 12,
    };

    /// <summary>
    /// <para>Where a diagnostic is, as a range the editor can underline.</para>
    /// <para>A span carries an offset and a length, so the end is worked out from the file
    /// rather than guessed: a length that ran past the end of a line would otherwise underline
    /// into the next one.</para>
    /// </summary>
    public static JsonObject RangeOf(SourceSpan span, SourceText? source)
    {
        SourcePosition start = span.Start;

        SourcePosition end = source is null
            ? start
            : source.PositionAt(Math.Min(span.EndOffset, source.Text.Length));

        return new JsonObject
        {
            ["start"] = PositionOf(start),
            ["end"] = PositionOf(end),
        };
    }

    /// <summary>One place in a file, counted the protocol's way.</summary>
    public static JsonObject PositionOf(SourcePosition position) => new()
    {
        // From one in the compiler, from zero here. Guarded rather than subtracted blindly,
        // since a synthesized span may carry no position at all and a line of -1 is a message
        // the editor silently drops.
        ["line"] = Math.Max(0, position.Line - 1),
        ["character"] = Math.Max(0, position.Column - 1),
    };

    /// <summary>
    /// <para>One diagnostic, as the protocol writes one.</para>
    /// <para>The identifier travels as <c>code</c> rather than being folded into the message,
    /// which is what lets an editor group by it, let a reader silence one, and later offer a fix
    /// for the ones that carry their own replacement.</para>
    /// </summary>
    public static JsonObject DiagnosticOf(Diagnostic diagnostic, SourceText? source)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        JsonObject written = new()
        {
            ["range"] = RangeOf(diagnostic.Span, source),
            ["severity"] = SeverityOf(diagnostic.Severity),
            ["code"] = diagnostic.Id,
            ["source"] = "profi-c",
            ["message"] = diagnostic.Message,
        };

        // Tag 1 is the protocol's "unnecessary", which an editor shows by fading the span
        // instead of underlining it — keeping whatever color the name already has, so it still
        // reads as the field or the local it is and reads as one nothing reaches. Sent only
        // where it is true; a tag on everything would fade the whole file.
        if (diagnostic.Descriptor.Unused)
        {
            written["tags"] = new JsonArray(1);
        }

        return written;
    }

    /// <summary>
    /// <para>The protocol's severity for one of the language's three.</para>
    /// <para>An opinion is not a warning. It says a program does what its author meant and says
    /// it a way the language would rather it were not, so it arrives as Information — the
    /// nearest thing the protocol has that does not read as "something may be wrong". The same
    /// mapping the build's problem matcher makes, and it has to be: one severity meaning two
    /// things depending on how the compiler was reached would be worse than either.</para>
    /// </summary>
    public static int SeverityOf(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Error => 1,
        DiagnosticSeverity.Warning => 2,
        _ => 3,
    };
}
