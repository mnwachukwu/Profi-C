namespace ProfiC.Cli.LanguageServer;

/// <summary>
/// <para>Runs the analysis a change implies, once the reader has stopped changing things.</para>
/// <para><b>The rule: debounce the analysis, never the synchronization.</b> The document store is
/// updated the instant an edit arrives, because every other question — what type is this, where
/// is this declared — must be answered about the text as it is now. What is deferred is only the
/// expensive, unprompted work: parse, resolve, check, publish.</para>
/// <para><b>Why defer it at all is not the cost.</b> A thousand-line file goes through the whole
/// front end in single-digit milliseconds. It is that diagnostics which flicker while somebody
/// types are worse than none: half-written code is full of errors that are not errors, and a
/// panel strobing red teaches a beginner to ignore it. That argument holds however fast the
/// compiler gets.</para>
/// <para>Server side rather than in the editor, for three reasons. The synchronization has to
/// happen immediately regardless, so the split only exists here. Every editor gets it rather than
/// each one solving it again. And the notification carrying an edit expects no answer, so
/// deferring is exactly what it is for.</para>
/// </summary>
public sealed class Analysis : IDisposable
{
    /// <summary>
    /// <para>How long the reader has to stop for before anything is analyzed.</para>
    /// <para>Below about 200 ms it fires in the middle of a word. Above about 500 ms it feels
    /// like something is broken. This is the usual landing place, and what most servers ship.
    /// </para>
    /// </summary>
    public static readonly TimeSpan DefaultQuiet = TimeSpan.FromMilliseconds(300);

    private readonly TimeSpan _quiet;
    private readonly Func<string, CancellationToken, Task> _analyze;
    private readonly Lock _scheduling = new();
    private readonly Dictionary<string, Pending> _pending = new(SourceDiscovery.PathComparer);

    private bool _disposed;

    /// <summary>What is scheduled or running for one file, and how to stop it.</summary>
    private sealed record Pending(CancellationTokenSource Stop, Task Running);

    /// <param name="analyze">
    /// The work itself, given the file to analyze and a token that the next change to it trips.
    /// Passed in rather than done here so that what is scheduled and what is analyzed can be
    /// tested apart — the timing is subtle and the analysis is not.
    /// </param>
    /// <param name="quiet">How long to wait. The default unless a reader has said otherwise.</param>
    public Analysis(Func<string, CancellationToken, Task> analyze, TimeSpan? quiet = null)
    {
        ArgumentNullException.ThrowIfNull(analyze);

        _analyze = analyze;
        _quiet = quiet ?? DefaultQuiet;
    }

    /// <summary>
    /// <para>Says the file changed, and schedules analysis for once the changes stop.</para>
    /// <para>Anything already scheduled or running for that file is cancelled first, so a burst
    /// of keystrokes runs the analysis once and against the last of them. Returns the task so a
    /// test can wait on it; nothing in the server does.</para>
    /// </summary>
    public Task Schedule(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        lock (_scheduling)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            Cancel(path);

            CancellationTokenSource stop = new();
            Task running = Run(path, stop.Token);

            _pending[path] = new Pending(stop, running);
            return running;
        }
    }

    /// <summary>
    /// <para>Analyzes now, without waiting, and cancels anything already scheduled for the
    /// file.</para>
    /// <para>For opening and for saving, which are the two places a reader has said they are
    /// done. Waiting on an open shows a blank panel over a file that is right there, and waiting
    /// on a save defers work the reader explicitly asked to have done.</para>
    /// </summary>
    public Task Now(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        lock (_scheduling)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            Cancel(path);

            CancellationTokenSource stop = new();
            Task running = Run(path, stop.Token, wait: false);

            _pending[path] = new Pending(stop, running);
            return running;
        }
    }

    /// <summary>Stops whatever is pending for a file, for one the editor has closed.</summary>
    public void Forget(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        lock (_scheduling)
        {
            Cancel(path);
        }
    }

    private async Task Run(string path, CancellationToken stop, bool wait = true)
    {
        try
        {
            if (wait)
            {
                await Task.Delay(_quiet, stop).ConfigureAwait(false);
            }

            stop.ThrowIfCancellationRequested();

            await _analyze(path, stop).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The reader typed again, which is the ordinary case rather than a fault. Nothing is
            // published: what this was analyzing is text nobody is looking at any more.
        }
    }

    /// <summary>Cancels what is pending for a file. Called holding the lock.</summary>
    private void Cancel(string path)
    {
        if (_pending.Remove(path, out Pending? pending))
        {
            pending.Stop.Cancel();
            pending.Stop.Dispose();
        }
    }

    public void Dispose()
    {
        lock (_scheduling)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            foreach (Pending pending in _pending.Values)
            {
                pending.Stop.Cancel();
                pending.Stop.Dispose();
            }

            _pending.Clear();
        }
    }
}
