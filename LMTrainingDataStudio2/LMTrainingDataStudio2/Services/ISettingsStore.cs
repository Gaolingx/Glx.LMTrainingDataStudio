using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Services;

/// <summary>
/// Result of loading settings, including warnings for invalid values that fell back to defaults.
/// </summary>
public sealed record SettingsLoadResult(AppSettings Settings, IReadOnlyList<string> Warnings);

/// <summary>
/// Persists application settings.
/// </summary>
public interface ISettingsStore
{
    string FilePath { get; }

    SettingsLoadResult Load();

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
