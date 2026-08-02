using ProfiC.Compiler.Ast;

namespace ProfiC.Interpreter;

/// <summary>What the reader last asked for, which decides where the program stops next.</summary>
public enum StepMode
{
    /// <summary>Run on, stopping only where a breakpoint is set.</summary>
    Run,

    /// <summary>Stop at the next line reached, wherever it is — into a call included.</summary>
    Into,

    /// <summary>Stop at the next line no deeper than this one, stepping past any call made.</summary>
    Over,

    /// <summary>Stop at the next line shallower than this one, finishing the call.</summary>
    Out,
}

/// <summary>
/// <para>Decides whether a point the interpreter has reached is somewhere to stop.</para>
/// <para>Separated from the waiting, and from any protocol, so that the rules can be read and
/// tested as rules. Everything hard about stepping is here; everything else in a debug adapter
/// is plumbing.</para>
/// </summary>
public sealed class StopPolicy
{
    /// <summary>
    /// <para>Two paths name the same file when they resolve to the same place, whatever the
    /// spelling. Which matters because the two sides spell it differently: an editor sends an
    /// absolute path, and the compiler carries whatever was typed on the command line.</para>
    /// <para>Case is ignored on Windows because the file system ignores it, so
    /// <c>D:\Repos\Program.pc</c> and <c>d:\repos\program.pc</c> are one file and a breakpoint
    /// set through either has to fire.</para>
    /// </summary>
    private static readonly StringComparer SameFile =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private readonly Dictionary<string, HashSet<int>> _breakpoints = new(SameFile);

    /// <summary>
    /// Resolved paths, kept because <see cref="ShouldStopAt"/> runs on every statement of every
    /// run and resolving a path touches the file system. The set of files a program is written
    /// in is small and fixed, so this fills once and answers from memory thereafter.
    /// </summary>
    private readonly Dictionary<string, string> _resolved = new(StringComparer.Ordinal);

    /// <summary>
    /// Running, because that is what launching means: an editor that wanted the first line
    /// asks for it, and one that set a breakpoint expects to arrive there rather than at the
    /// top of the program.
    /// </summary>
    private StepMode _mode = StepMode.Run;
    private int _depthWhenAsked;
    private Statement? _stoppedAt;
    private int _stoppedLine = -1;
    private int _stoppedDepth = -1;

    /// <summary>
    /// <para>The lines a breakpoint sits on in one file. Replaces what was there for that file
    /// and leaves every other file alone, which is how the protocol reports them — a file's
    /// breakpoints arrive as the whole set each time any one of them changes.</para>
    /// <para>Per file rather than per program because line numbers are not unique across a
    /// project. A breakpoint on line 5 of one file must not stop the program on line 5 of
    /// another, and with several files open that is not a rare arrangement.</para>
    /// </summary>
    public void BreakpointsAt(string file, IEnumerable<int> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        HashSet<int> set = [.. lines];

        if (set.Count == 0)
        {
            _breakpoints.Remove(Resolve(file));
            return;
        }

        _breakpoints[Resolve(file)] = set;
    }

    /// <summary>
    /// <para>A path in the one spelling both sides can be compared in.</para>
    /// <para>An unresolvable path is kept as it was rather than rejected: a program can be run
    /// from a source that is not a file at all — a test writes one in memory — and such a name
    /// still has to compare equal to itself.</para>
    /// </summary>
    private string Resolve(string file)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (_resolved.TryGetValue(file, out string? already))
        {
            return already;
        }

        string full = file;

        try
        {
            full = Path.GetFullPath(file);
        }
        catch (Exception failure) when (failure is ArgumentException or NotSupportedException
                                            or PathTooLongException or IOException
                                            or System.Security.SecurityException)
        {
            // Not a path. Kept verbatim, and so still equal to itself.
        }

        _resolved[file] = full;
        return full;
    }

    /// <summary>Whether a breakpoint sits on this line of this file.</summary>
    private bool BreakpointAt(ExecutionPoint point) =>
        _breakpoints.Count > 0
        && _breakpoints.TryGetValue(Resolve(point.File), out HashSet<int>? lines)
        && lines.Contains(point.Line);

    /// <summary>
    /// <para>What the reader asked for after the last stop, and where they asked it from.</para>
    /// <para>The depth is taken here rather than read later because a step over is a question
    /// about the frame it was asked in, and by the time the next point arrives the program may
    /// be several frames away.</para>
    /// </summary>
    public void Resume(StepMode mode, int depthWhenAsked)
    {
        _mode = mode;
        _depthWhenAsked = depthWhenAsked;
    }

    /// <summary>
    /// <para>Whether to stop here, and if so remembers it as the stop that was made.</para>
    /// <para>Two rules, and the second is the one that took measuring. A construct rewritten by
    /// lowering makes several statements that share one source line — a <c>loop each</c> makes
    /// six — and stopping at each would report the same line six times for one step. So a
    /// <em>different</em> statement on the line just stopped at, at the same depth, is passed
    /// over.</para>
    /// <para>But the <em>same</em> statement again is not: that is a loop coming round, and a
    /// breakpoint in a loop body has to fire on every turn. By line alone the two cases are
    /// identical, which is why the statement is what tells them apart.</para>
    /// </summary>
    public bool ShouldStopAt(ExecutionPoint point)
    {
        ArgumentNullException.ThrowIfNull(point);

        if (StillOnTheLineJustStopped(point))
        {
            return false;
        }

        bool stop = _mode switch
        {
            StepMode.Into => true,
            StepMode.Over => point.Depth <= _depthWhenAsked,
            StepMode.Out => point.Depth < _depthWhenAsked,
            _ => false,
        };

        // A breakpoint is honored whatever was asked for, so stepping over a call that contains
        // one stops inside it — which is what a reader who set it there meant.
        stop = stop || BreakpointAt(point);

        if (stop)
        {
            _stoppedAt = point.Statement;
            _stoppedLine = point.Line;
            _stoppedDepth = point.Depth;
        }

        return stop;
    }

    /// <summary>
    /// Whether this is another of the statements lowering made for the line already stopped at,
    /// rather than a new place. The same statement is not: that is a second turn.
    /// </summary>
    private bool StillOnTheLineJustStopped(ExecutionPoint point) =>
        _stoppedAt is not null
        && point.Line == _stoppedLine
        && point.Depth == _stoppedDepth
        && !ReferenceEquals(point.Statement, _stoppedAt);
}
