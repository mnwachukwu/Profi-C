using System.Text;

namespace ProfiC.Runtime;

/// <summary>
/// <para>Files and folders.</para>
/// <para><b>A file that is not there is an absence, not a failure.</b> Reading one answers with
/// an empty optional rather than raising, because a program asking for a file it did not write is
/// the ordinary case — and because the alternative teaches a reader to wrap every read in a
/// <c>try</c>. What does raise is a genuine fault: no permission, a path that is not a path, a
/// disk that has gone. Those are <c>IOException</c> and are catchable by name.</para>
/// <para><b>UTF-8 without a byte-order mark, everywhere.</b> A mark is invisible, travels into
/// the first string a program reads, and turns an equality that should hold into one that does
/// not — which is a very long afternoon for a beginner.</para>
/// <para><b>A listing is sorted before it is answered.</b> A file system offers its own order,
/// which differs between machines and sometimes between two runs on one machine; a program that
/// prints a folder should print the same thing twice.</para>
/// </summary>
public static class ProfiCFiles
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    // ---- Reading ----------------------------------------------------------------------------

    public static Optional<string> Read(string path) =>
        File.Exists(path) ? Optional<string>.Of(File.ReadAllText(path, Utf8)) : default;

    public static Optional<ProfiCSet<string>> ReadLines(string path) =>
        File.Exists(path)
            ? Optional<ProfiCSet<string>>.Of(new ProfiCSet<string>(File.ReadAllLines(path, Utf8)))
            : default;

    // ---- Writing ----------------------------------------------------------------------------

    public static void Write(string path, string text) => File.WriteAllText(path, text, Utf8);

    /// <summary>
    /// <para>Each line followed by a newline, including the last.</para>
    /// <para>So that writing lines and reading them back gives what was written, and appending to
    /// the file afterwards starts on a line of its own rather than joining the end of the last
    /// one.</para>
    /// </summary>
    public static void WriteLines(string path, IProfiCSet lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        StringBuilder written = new();

        for (int at = 0; at < lines.Count; at++)
        {
            written.Append(ModelOperations.ToDisplayString(lines.GetElement(at))).Append('\n');
        }

        File.WriteAllText(path, written.ToString(), Utf8);
    }

    public static void Append(string path, string text) => File.AppendAllText(path, text, Utf8);

    // ---- Asking about one --------------------------------------------------------------------

    public static bool Exists(string path) => File.Exists(path);

    /// <summary>
    /// Whether there was one to remove. False rather than a failure for a file that was already
    /// gone, since a program deleting what it may not have written wants to say so once.
    /// </summary>
    public static bool Delete(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);

        return true;
    }

    public static void Copy(string from, string to) => File.Copy(from, to, overwrite: true);

    public static void Move(string from, string to) => File.Move(from, to, overwrite: true);

    public static Optional<long> Size(string path) =>
        File.Exists(path) ? Optional<long>.Of(new FileInfo(path).Length) : default;

    public static Optional<DateTime> Changed(string path) =>
        File.Exists(path) ? Optional<DateTime>.Of(File.GetLastWriteTime(path)) : default;

    // ---- Folders -----------------------------------------------------------------------------

    public static string Current() => Directory.GetCurrentDirectory();

    public static bool FolderExists(string path) => Directory.Exists(path);

    public static void CreateFolder(string path) => Directory.CreateDirectory(path);

    /// <inheritdoc cref="Delete"/>
    public static bool DeleteFolder(string path)
    {
        if (!Directory.Exists(path))
        {
            return false;
        }

        Directory.Delete(path, recursive: true);

        return true;
    }

    public static Optional<ProfiCSet<string>> Files(string path) =>
        Directory.Exists(path)
            ? Optional<ProfiCSet<string>>.Of(new ProfiCSet<string>(Sorted(Directory.GetFiles(path))))
            : default;

    public static Optional<ProfiCSet<string>> Folders(string path) =>
        Directory.Exists(path)
            ? Optional<ProfiCSet<string>>.Of(
                new ProfiCSet<string>(Sorted(Directory.GetDirectories(path))))
            : default;

    private static IEnumerable<string> Sorted(string[] paths) =>
        paths.OrderBy(path => path, StringComparer.Ordinal);

    // ---- The same, for the interpreter -------------------------------------------------------

    /// <summary>
    /// <para>What the interpreter asks for, which is every set held as one of objects and every
    /// absence held as null.</para>
    /// <para><inheritdoc cref="ProfiCText.ToCharactersUntyped" path="/summary/para[2]"/></para>
    /// </summary>
    public static object? ReadUntyped(string path) =>
        File.Exists(path) ? File.ReadAllText(path, Utf8) : null;

    /// <inheritdoc cref="ReadUntyped"/>
    public static object? ReadLinesUntyped(string path) =>
        File.Exists(path) ? Untyped(File.ReadAllLines(path, Utf8)) : null;

    /// <inheritdoc cref="ReadUntyped"/>
    public static object? SizeUntyped(string path) =>
        File.Exists(path) ? new FileInfo(path).Length : null;

    /// <inheritdoc cref="ReadUntyped"/>
    public static object? ChangedUntyped(string path) =>
        File.Exists(path) ? File.GetLastWriteTime(path) : null;

    /// <inheritdoc cref="ReadUntyped"/>
    public static object? FilesUntyped(string path) =>
        Directory.Exists(path) ? Untyped(Sorted(Directory.GetFiles(path))) : null;

    /// <inheritdoc cref="ReadUntyped"/>
    public static object? FoldersUntyped(string path) =>
        Directory.Exists(path) ? Untyped(Sorted(Directory.GetDirectories(path))) : null;

    private static ProfiCSet<object?> Untyped(IEnumerable<string> lines) =>
        new(lines.Select(line => (object?)line));
}
