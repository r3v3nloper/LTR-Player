namespace LTR.Player.Wpf;

/// <summary>
/// Keeps track of work started from a property change, so that it can be waited for.
/// </summary>
/// <remarks>
/// <para>
/// The shell answers a selection or a search by starting a task it does not keep: each reloads a list,
/// handles its own failures and is cancelled by the shell lifetime, so production code has nothing to wait
/// on. A test does — and the version of this that spun on <c>Task.Yield()</c> eight times was
/// timing-dependent by construction, which is a flaky test waiting for a loaded build machine.
/// </para>
/// <para>
/// Not thread-safe beyond the lock it holds, and not meant to be: everything it tracks starts on the UI
/// thread. The lock exists because a task's continuation may complete on another.
/// </para>
/// </remarks>
internal sealed class PendingWork
{
    private readonly object _gate = new();

    private int _outstanding;
    private TaskCompletionSource _idle = CreateSignalled();

    /// <summary>
    /// Completes once nothing started through <see cref="Add"/> is still running.
    /// </summary>
    /// <remarks>
    /// Already completed when nothing is outstanding, so awaiting it costs nothing and reading
    /// <see cref="Task.IsCompleted"/> is a truthful "is the shell idle".
    /// </remarks>
    public Task Completion
    {
        get
        {
            lock (_gate)
            {
                return _idle.Task;
            }
        }
    }

    /// <summary>
    /// Follows a task to its end, whether it succeeds, fails or is cancelled.
    /// </summary>
    /// <remarks>
    /// The task's own outcome is deliberately ignored. Each of these reports its own failure; this only has
    /// to know when the shell has stopped working, and a faulted task that nobody observed here would be
    /// re-raised as an unobserved exception on the finaliser thread.
    /// </remarks>
    public void Add(Task task)
    {
        ArgumentNullException.ThrowIfNull(task);

        lock (_gate)
        {
            if (_outstanding++ == 0)
            {
                _idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        _ = task.ContinueWith(
            static (_, state) => ((PendingWork)state!).Finish(),
            this,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void Finish()
    {
        lock (_gate)
        {
            if (--_outstanding == 0)
            {
                _idle.TrySetResult();
            }
        }
    }

    private static TaskCompletionSource CreateSignalled()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();

        return source;
    }
}
