using System.Diagnostics;

namespace NetworkProbe;

/// <summary>
/// One call under test, running. It makes the call on a DEDICATED thread and records two moments separately:
///
/// <list type="bullet">
/// <item><b>call-return</b>: how long the call itself took to hand back its <see cref="Task"/> (spec section 5.6).
/// An <c>async</c> method runs synchronously up to its first <c>await</c>, so a long call-return means the call can
/// block whatever thread called it (the UI thread, for some TermPoint callers).</item>
/// <item><b>completion</b>: when that task finished, and with what.</item>
/// </list>
///
/// Running the call on its own thread also means a call that blocks its caller for a minute cannot stop the probe from
/// restoring the network on time.
/// </summary>
internal sealed class OperationRun
{
    /// <summary>Makes the call under test (the case's <see cref="CaseDefinition.Execute"/> bound to its files).</summary>
    private readonly Func<Task<OpResult>> _operation;

    /// <summary>Guards the result fields, which are written from the call thread and a thread-pool thread.</summary>
    private readonly object _gate = new();

    /// <summary>Completes when the call has handed back its task (or thrown).</summary>
    private readonly TaskCompletionSource _callReturned = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes when the task has finished (or the call threw).</summary>
    private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>How long the call took to hand back its task; null while it has not.</summary>
    private TimeSpan? _callReturn;

    /// <summary>Probe-clock time the task finished; null while it has not.</summary>
    private TimeSpan? _completedAt;

    /// <summary>What the task returned; null if it has not finished or failed.</summary>
    private OpResult? _result;

    /// <summary>The exception the call or its task failed with, if any.</summary>
    private Exception? _error;

    /// <summary>Creates a run; nothing is started yet.</summary>
    /// <param name="operation">Makes the call under test and returns its task.</param>
    public OperationRun(Func<Task<OpResult>> operation) => _operation = operation;

    /// <summary>Probe-clock time <see cref="Start"/> was called.</summary>
    public TimeSpan StartedAt { get; private set; }

    /// <summary>How long the call took to hand back its task; null while it has not (it may be blocking its caller).</summary>
    public TimeSpan? CallReturn { get { lock (_gate) return _callReturn; } }

    /// <summary>Probe-clock time the task finished; null while it has not.</summary>
    public TimeSpan? CompletedAt { get { lock (_gate) return _completedAt; } }

    /// <summary>What the task returned; null while it has not finished, or if it failed.</summary>
    public OpResult? Result { get { lock (_gate) return _result; } }

    /// <summary>The exception the call or its task failed with, or null.</summary>
    public Exception? Error { get { lock (_gate) return _error; } }

    /// <summary>True once the task has finished (successfully or not).</summary>
    public bool IsCompleted => _completed.Task.IsCompleted;

    /// <summary>A task that completes when the call has finished; it never faults.</summary>
    public Task Completion => _completed.Task;

    /// <summary>A task that completes when the call has handed back its task; it never faults.</summary>
    public Task CallReturned => _callReturned.Task;

    /// <summary>
    /// Starts the call on a dedicated background thread and returns at once.
    /// </summary>
    public void Start()
    {
        StartedAt = ProbeClock.Now;
        new Thread(Body) { IsBackground = true, Name = "probe-call-under-test" }.Start();
    }

    /// <summary>
    /// The thread body: makes the call, notes how long that took, and arranges to note when its task finishes.
    /// </summary>
    private void Body()
    {
        var clock = Stopwatch.StartNew();
        Task<OpResult> task;
        try
        {
            task = _operation();
        }
        catch (Exception ex)
        {
            // The call threw before handing back a task: it has both returned and completed.
            lock (_gate) _callReturn = clock.Elapsed;
            _callReturned.TrySetResult();
            Finish(null, ex);
            return;
        }

        lock (_gate) _callReturn = clock.Elapsed;
        _callReturned.TrySetResult();

        // The continuation runs on a thread-pool thread, so this thread is free to end; the task's outcome is always
        // observed here, whatever happens to it later.
        task.ContinueWith(
            t => Finish(t.IsCompletedSuccessfully ? t.Result : null,
                        t.IsCompletedSuccessfully ? null : (t.Exception?.GetBaseException() ?? new TaskCanceledException())),
            CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    /// <summary>
    /// Records how the call ended.
    /// </summary>
    /// <param name="result">What the task returned, or null if it failed.</param>
    /// <param name="error">The failure, or null.</param>
    private void Finish(OpResult? result, Exception? error)
    {
        lock (_gate)
        {
            _completedAt = ProbeClock.Now;
            _result = result;
            _error = error;
        }
        _completed.TrySetResult();
    }
}
