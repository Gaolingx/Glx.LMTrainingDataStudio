namespace LMTrainingDataStudio2.Services;

/// <summary>
/// Cooperative pause gate checked before each block starts. Blocks already running
/// are allowed to finish; new blocks wait until <see cref="Resume"/> is called.
/// </summary>
public sealed class ExecutionPauseController
{
    private readonly object _gate = new();
    private TaskCompletionSource? _paused;

    public bool IsPaused
    {
        get
        {
            lock (_gate)
            {
                return _paused is not null;
            }
        }
    }

    public void Pause()
    {
        lock (_gate)
        {
            _paused ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public void Resume()
    {
        TaskCompletionSource? source;
        lock (_gate)
        {
            source = _paused;
            _paused = null;
        }

        source?.TrySetResult();
    }

    public async Task WaitIfPausedAsync(CancellationToken cancellationToken)
    {
        Task? wait;
        lock (_gate)
        {
            wait = _paused?.Task;
        }

        if (wait is not null)
        {
            await wait.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
