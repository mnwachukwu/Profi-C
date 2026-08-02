namespace ProfiC.Interpreter;

/// <summary>
/// <para>A program being debugged: the policy, and the waiting the policy implies.</para>
/// <para>Two threads meet here. The program runs on one and calls <see cref="Reached"/> before
/// every statement; whatever is driving the debugger — a protocol loop, a test — runs on the
/// other and calls <see cref="Continue"/>, <see cref="StepOver"/> and the rest. A stop is
/// simply <see cref="Reached"/> not returning yet.</para>
/// <para>Nothing here knows the Debug Adapter Protocol. What it offers is the vocabulary that
/// protocol needs — set breakpoints, run, step, ask where we are — so that the wire format is a
/// translation rather than a design.</para>
/// </summary>
public sealed class DebugSession : IDebugHost, IDisposable
{
    private readonly StopPolicy _policy = new();

    /// <summary>Raised while the program is stopped, before anyone is told it may go on.</summary>
    private readonly Action<ExecutionPoint, StopReason>? _stopped;

    /// <summary>Held by the program while it waits, released by whoever lets it go on.</summary>
    private readonly SemaphoreSlim _mayContinue = new(0, 1);

    /// <summary>Guards the handover, which two threads reach from opposite sides.</summary>
    private readonly Lock _gate = new();

    private ExecutionPoint? _where;
    private bool _waiting;
    private bool _finished;

    /// <summary>
    /// <param name="stopped">
    /// Called on the program's own thread each time it stops, with the point it stopped at and
    /// why it stopped there. Called before the program is released, so the point's locals are
    /// still readable — they are a live view of a scope rather than a copy, and afterwards they
    /// answer about a program that has moved on.
    /// </param>
    /// </summary>
    public DebugSession(Action<ExecutionPoint, StopReason>? stopped = null) => _stopped = stopped;

    /// <summary>Where the program is stopped, or null while it is running or finished.</summary>
    public ExecutionPoint? Where => _where;

    /// <summary>
    /// The lines a breakpoint sits on in one file. May be set at any time, including while the
    /// program runs, since that is when an editor sends them.
    /// </summary>
    public void BreakpointsAt(string file, IEnumerable<int> lines) =>
        _policy.BreakpointsAt(file, lines);

    /// <summary>Let the program run on, stopping only at breakpoints.</summary>
    public void Continue() => Release(StepMode.Run);

    /// <summary>Run to the next line, following any call made.</summary>
    public void StepInto() => Release(StepMode.Into);

    /// <summary>Run to the next line in this call or above it, stepping past any call made.</summary>
    public void StepOver() => Release(StepMode.Over);

    /// <summary>Run until this call returns.</summary>
    public void StepOut() => Release(StepMode.Out);

    /// <summary>
    /// <para>Called by the interpreter before each statement. Returns when the program may go
    /// on, which is what makes a stop a stop.</para>
    /// <para>A session begins running rather than stopped: breakpoints are sent before the
    /// program is started, so there is nothing to be gained by holding it at the first line and
    /// a reader who asked to run would be surprised to be stopped.</para>
    /// </summary>
    public void Reached(ExecutionPoint point)
    {
        if (_finished || _policy.WhyStopAt(point) is not { } why)
        {
            return;
        }

        lock (_gate)
        {
            _where = point;
            _waiting = true;
        }

        // Announced before waiting, and on this thread, so that whoever is told can read the
        // point's locals while the scope it views is still the one in force.
        _stopped?.Invoke(point, why);

        _mayContinue.Wait();

        lock (_gate)
        {
            _where = null;
        }
    }

    /// <summary>
    /// <para>Stops answering, and lets the program run to its end.</para>
    /// <para>What disconnecting means: the reader has closed the session but the program is
    /// mid-statement on another thread, and killing it there would leave whatever it was doing
    /// half-done. Letting it finish unwatched is the only ending that is not a mess.</para>
    /// </summary>
    public void Detach()
    {
        _finished = true;
        Release(StepMode.Run);
    }

    /// <summary>
    /// <para>Sets what to do next and lets the program go, at most once per stop.</para>
    /// <para>Locked, and counted rather than asked. <c>SemaphoreSlim</c> holds at most one here,
    /// so releasing one that is already free throws — and testing <c>CurrentCount</c> first does
    /// not fix it, because two callers can both read zero before either releases. The throw
    /// lands on whichever thread called, which for a protocol loop means the debugger falling
    /// over rather than the program.</para>
    /// <para>Two calls between one stop and the next is not a misuse to refuse: an editor may
    /// send a continue and a disconnect close together, and the second should be a no-op rather
    /// than an error.</para>
    /// </summary>
    private void Release(StepMode mode)
    {
        lock (_gate)
        {
            _policy.Resume(mode, _where?.Depth ?? 0);

            if (!_waiting)
            {
                return;
            }

            _waiting = false;
            _mayContinue.Release();
        }
    }

    public void Dispose() => _mayContinue.Dispose();
}
