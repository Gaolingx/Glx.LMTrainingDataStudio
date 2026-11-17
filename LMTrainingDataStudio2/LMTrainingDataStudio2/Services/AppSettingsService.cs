using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Services;

public sealed class SettingsChangedEventArgs : EventArgs
{
    public SettingsChangedEventArgs(AppSettings previous, AppSettings current)
    {
        Previous = previous;
        Current = current;
    }

    public AppSettings Previous { get; }

    public AppSettings Current { get; }
}

/// <summary>
/// Read-only view of the settings currently in effect. Long-lived services subscribe to
/// <see cref="Changed"/> instead of capturing a snapshot at construction time.
/// </summary>
public interface IAppSettingsService
{
    AppSettings Current { get; }

    event EventHandler<SettingsChangedEventArgs>? Changed;
}

public sealed class AppSettingsService : IAppSettingsService
{
    public AppSettingsService(AppSettings initial)
    {
        Current = initial ?? throw new ArgumentNullException(nameof(initial));
    }

    public AppSettings Current { get; private set; }

    public event EventHandler<SettingsChangedEventArgs>? Changed;

    public void Update(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (ReferenceEquals(settings, Current))
        {
            return;
        }

        var previous = Current;
        Current = settings;
        Changed?.Invoke(this, new SettingsChangedEventArgs(previous, settings));
    }
}
