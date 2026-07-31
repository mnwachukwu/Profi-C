using System.Diagnostics.CodeAnalysis;
using ProfiC.Compiler.Text;

namespace ProfiC.Compiler.Diagnostics;

/// <summary>How far a suppression reaches.</summary>
public enum SuppressionScope
{
    /// <summary>The next line carrying code below the one the directive was written on.</summary>
    Line,

    /// <summary>Every line of the file the directive was written in.</summary>
    File,

    /// <summary>Every file the compilation reads.</summary>
    Project,
}

/// <summary>
/// <para>What a directive asked to stop hearing: a whole severity, or one diagnostic.</para>
/// <para>Reading a directive and deciding whether the thing it names can be silenced are
/// separate steps, because the second needs the diagnostic table and the first does not.</para>
/// </summary>
/// <param name="Severity">Set when the directive named <c>warning</c> or <c>opinion</c>.</param>
/// <param name="Id">Set when it named an identifier such as <c>PC0340</c>.</param>
/// <param name="WholeFile">Set when <c>in file</c> followed.</param>
public sealed record SuppressionTarget(
    DiagnosticSeverity? Severity,
    string? Id,
    bool WholeFile)
{
    /// <summary>What the directive named, for a message that quotes it back.</summary>
    public string Written => Id ?? Severity.ToString()!.ToLowerInvariant();
}

/// <summary>
/// <para>A request to stop reporting something, and the reach it was asked for.</para>
/// <para>Nothing that stops compilation can be suppressed, so <see cref="Covers"/> refuses an
/// error before it looks at anything else. A writer reaches for this to make the compiler
/// quieter, and a mechanism that could silence a build-stopper would make it lie instead.</para>
/// <para>That refusal is the second of two. A directive naming an error is turned away where
/// it is read, so one never reaches here — and the guard stays anyway, because the rule is
/// worth stating where it applies rather than leaving it to hold by consequence somewhere
/// else. Removing either one alone leaves errors surviving; removing both does not.</para>
/// </summary>
/// <param name="Scope">How far it reaches.</param>
/// <param name="Source">
/// The file the directive was written in, null for one that came from a project file.
/// </param>
/// <param name="Line">
/// The line covered, for <see cref="SuppressionScope.Line"/>. Zero where the directive was
/// written with no code below it, which covers nothing.
/// </param>
/// <param name="Severity">The severity it silences, where it named one.</param>
/// <param name="Id">The one diagnostic it silences, where it named one.</param>
/// <param name="Span">Where the directive itself sits, for a message that points at it.</param>
public sealed record Suppression(
    SuppressionScope Scope,
    SourceText? Source,
    int Line,
    DiagnosticSeverity? Severity,
    string? Id,
    SourceSpan Span)
{
    /// <summary>The file this was written in, matched against the one a diagnostic carries.</summary>
    public string? FileName => Source?.FileName;

    /// <summary>Whether this silences the given diagnostic.</summary>
    public bool Covers(Diagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        if (diagnostic.Severity == DiagnosticSeverity.Error)
        {
            return false;
        }

        if (Scope != SuppressionScope.Project
            && !string.Equals(FileName, diagnostic.FileName, StringComparison.Ordinal))
        {
            return false;
        }

        if (Scope == SuppressionScope.Line && diagnostic.Span.Start.Line != Line)
        {
            return false;
        }

        return Id is null
            ? diagnostic.Severity == Severity
            : string.Equals(Id, diagnostic.Id, StringComparison.Ordinal);
    }
}

/// <summary>
/// <para>Reads the one directive a comment may carry.</para>
/// <para>Three forms, and the word after <c>ignore</c> is never absent:</para>
/// <code>
/// # ignore warning
/// # ignore opinion
/// # ignore PC0340
/// </code>
/// <para>Each takes <c>in file</c>, which widens it from the line below to the whole file.</para>
/// <para><b>Prose beginning with the word "ignore" stays prose.</b> <c>#&#160;ignore the sign
/// for now</c> is a comment a person writes, and turning it into a compiler error would be a
/// worse failure than the one it prevents. So a directive is recognized only once a severity
/// or something shaped like an identifier follows, and anything else is read as prose. The
/// cost is that a near miss such as <c>#&#160;ignore opinions</c> silently does nothing, which
/// is self-correcting: the diagnostic it meant to silence is still there to read.</para>
/// <para>Words after the target are prose too, so a directive may say why it is there.</para>
/// </summary>
public static class SuppressionDirective
{
    /// <summary>The word a directive opens with.</summary>
    public const string Opening = "ignore";

    /// <summary>The two words that widen one to a whole file.</summary>
    private static readonly string[] WholeFile = ["in", "file"];

    /// <summary>Reads a directive from the words of a comment, or reports that it is prose.</summary>
    public static bool TryRead(string text, [NotNullWhen(true)] out SuppressionTarget? target)
    {
        ArgumentNullException.ThrowIfNull(text);

        target = null;

        string[] words = text.Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (words.Length < 2 || !string.Equals(words[0], Opening, StringComparison.Ordinal))
        {
            return false;
        }

        if (!TryReadTarget(words[1], out DiagnosticSeverity? severity, out string? id))
        {
            return false;
        }

        bool wholeFile = words.Length >= 4
            && string.Equals(words[2], WholeFile[0], StringComparison.Ordinal)
            && string.Equals(words[3], WholeFile[1], StringComparison.Ordinal);

        target = new SuppressionTarget(severity, id, wholeFile);
        return true;
    }

    /// <summary>
    /// Reads the word naming what to silence. An identifier is read whatever its case and
    /// written back in the case the compiler reports, since that is the case a reader compares
    /// it against.
    /// </summary>
    public static bool TryReadTarget(
        string word,
        out DiagnosticSeverity? severity,
        out string? id)
    {
        ArgumentNullException.ThrowIfNull(word);

        severity = null;
        id = null;

        switch (word)
        {
            case "warning":
                severity = DiagnosticSeverity.Warning;
                return true;

            case "opinion":
                severity = DiagnosticSeverity.Opinion;
                return true;
        }

        if (word.Length == 6
            && char.ToUpperInvariant(word[0]) == 'P'
            && char.ToUpperInvariant(word[1]) == 'C'
            && word.AsSpan(2).ContainsAnyExceptInRange('0', '9') is false)
        {
            id = string.Concat("PC", word.AsSpan(2));
            return true;
        }

        return false;
    }
}
