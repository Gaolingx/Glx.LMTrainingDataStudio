namespace LMTrainingDataStudio2.Models;

/// <summary>
/// Application-wide settings persisted to disk.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Maximum index cache size in megabytes.</summary>
    public long MaxCacheSizeMb { get; set; } = 512;

    /// <summary>Path to the temporary index cache directory.</summary>
    public string CacheDirectory { get; set; } = Path.Combine(Path.GetTempPath(), "LMTrainingDataStudio", "index-cache");

    /// <summary>Whether to use dark theme.</summary>
    public bool IsDarkTheme { get; set; } = true;

    /// <summary>Canvas grid snap size in pixels.</summary>
    public int GridSnapSize { get; set; } = 16;

    /// <summary>Whether to enable grid snapping.</summary>
    public bool EnableGridSnap { get; set; } = true;
}
