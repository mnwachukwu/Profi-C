using System.Collections.Concurrent;
using ProfiC.Cli.LanguageServer;

namespace ProfiC.Tests.LanguageServer;

/// <summary>
/// <para>Analysis waits for the reader to stop typing, and runs once against what they stopped
/// at.</para>
/// <para><b>This is the requirement, so it has tests that fail if the debounce is removed.</b>
/// Without it every keystroke starts a whole compilation, each one about text that is already out
/// of date, and they queue — so the editor falls further behind the faster somebody types.</para>
/// <para>The reason to defer is not the cost: a realistic file goes through the front end in
/// single-digit milliseconds. It is that diagnostics flickering while somebody types are worse
/// than none — half-written code is full of errors that are not errors, and a panel strobing red
/// teaches a beginner to ignore it.</para>
/// <para>Timings here are short and generous in the direction that matters: a quiet period of a
/// few tens of milliseconds, waited on for far longer than it needs. A test that fails on a slow
/// machine is a test nobody trusts.</para>
/// </summary>
[TestFixture]
public sealed class AnalysisTests
{
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(40);

    /// <summary>Long enough that a machine under load still gets there.</summary>
    private static readonly TimeSpan LongEnough = TimeSpan.FromSeconds(5);

    /// <summary>What each run was given, in the order the runs happened.</summary>
    private sealed class Ran
    {
        private readonly ConcurrentQueue<string> _paths = new();

        public int Count => _paths.Count;

        public string[] Paths => [.. _paths];

        public Func<string, CancellationToken, Task> Recording => (path, _) =>
        {
            _paths.Enqueue(path);
            return Task.CompletedTask;
        };
    }

    private static async Task Until(Func<bool> settled)
    {
        DateTime giveUp = DateTime.UtcNow + LongEnough;

        while (DateTime.UtcNow < giveUp && !settled())
        {
            await Task.Delay(5);
        }
    }

    /// <summary>
    /// <para>A burst of changes runs the analysis once.</para>
    /// <para>The whole point in one assertion. Ten keystrokes are one analysis, not ten.</para>
    /// </summary>
    [Test]
    public async Task ABurstOfChangesIsAnalyzedOnce()
    {
        Ran ran = new();

        using Analysis analysis = new(ran.Recording, Quiet);

        for (int i = 0; i < 10; i++)
        {
            _ = analysis.Schedule("a.pc");
        }

        await Until(() => ran.Count > 0);
        await Task.Delay(Quiet * 4);

        Assert.That(ran.Count, Is.EqualTo(1), "every keystroke started its own compilation");
    }

    /// <summary>
    /// <para>Two files being edited are two analyses, not one.</para>
    /// <para>The debounce is per file: a change to one says nothing about whether the other is
    /// settled, and coalescing across them would drop an analysis somebody is waiting for.</para>
    /// </summary>
    [Test]
    public async Task TwoFilesAreDebouncedApart()
    {
        Ran ran = new();

        using Analysis analysis = new(ran.Recording, Quiet);

        _ = analysis.Schedule("a.pc");
        _ = analysis.Schedule("b.pc");

        await Until(() => ran.Count >= 2);

        Assert.That(ran.Paths, Is.EquivalentTo(new[] { "a.pc", "b.pc" }));
    }

    /// <summary>
    /// <para>Analysis already running is stopped when the file changes again.</para>
    /// <para>What the token is for. Without it the debounce can decline to start new work but
    /// cannot stop work already going, so a long analysis still finishes and still publishes
    /// against text nobody is looking at.</para>
    /// </summary>
    [Test]
    public async Task WorkAlreadyRunningIsStopped()
    {
        TaskCompletionSource started = new();
        CancellationToken given = default;

        using Analysis analysis = new(
            async (_, stop) =>
            {
                given = stop;
                started.TrySetResult();

                await Task.Delay(LongEnough, stop);
            },
            Quiet);

        _ = analysis.Schedule("a.pc");

        await started.Task.WaitAsync(LongEnough);

        Assert.That(given.IsCancellationRequested, Is.False, "it is running");

        _ = analysis.Schedule("a.pc");

        await Until(() => given.IsCancellationRequested);

        Assert.That(given.IsCancellationRequested, Is.True, "and the next change stopped it");
    }

    /// <summary>
    /// <para>Opening and saving do not wait.</para>
    /// <para>Both are the reader saying they are done — one by arriving at the file, the other by
    /// pressing a key to say so. Waiting on an open shows a blank panel over code that is right
    /// there.</para>
    /// </summary>
    [Test]
    public async Task OpeningAndSavingRunAtOnce()
    {
        Ran ran = new();

        using Analysis analysis = new(ran.Recording, TimeSpan.FromSeconds(30));

        await analysis.Now("a.pc");

        Assert.That(ran.Count, Is.EqualTo(1), "a wait of thirty seconds was applied to an open");
    }

    /// <summary>A file the editor has closed has nothing pending against it.</summary>
    [Test]
    public async Task ClosingStopsWhatWasScheduled()
    {
        Ran ran = new();

        using Analysis analysis = new(ran.Recording, Quiet);

        _ = analysis.Schedule("a.pc");
        analysis.Forget("a.pc");

        await Task.Delay(Quiet * 4);

        Assert.That(ran.Count, Is.Zero, "a file nobody has open was analyzed anyway");
    }

    /// <summary>
    /// The last text is the one analyzed, which is what makes coalescing safe. Held by way of the
    /// file each run was given, since the run reads whatever the store holds by then.
    /// </summary>
    [Test]
    public async Task WhatRunsIsTheLastOneScheduled()
    {
        ConcurrentQueue<string> seen = new();

        using Analysis analysis = new(
            (path, _) =>
            {
                seen.Enqueue(path);
                return Task.CompletedTask;
            },
            Quiet);

        _ = analysis.Schedule("first.pc");
        _ = analysis.Schedule("first.pc");
        _ = analysis.Schedule("first.pc");

        await Until(() => !seen.IsEmpty);
        await Task.Delay(Quiet * 4);

        Assert.That(seen, Is.EqualTo(new[] { "first.pc" }));
    }
}
