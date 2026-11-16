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

    /// <summary>UI language code; "system" follows the OS UI culture.</summary>
    public string Language { get; set; } = "system";

    /// <summary>Canvas grid snap size in pixels.</summary>
    public int GridSnapSize { get; set; } = 16;

    /// <summary>Whether to enable grid snapping.</summary>
    public bool EnableGridSnap { get; set; } = true;

    /// <summary>Maximum number of blocks executed concurrently inside a layer.</summary>
    public int MaxParallelism { get; set; } = 4;

    /// <summary>Number of retries per block before a run fails.</summary>
    public int NodeRetryCount { get; set; }

    /// <summary>
    /// Base directory for per-run summaries. Each run gets a subdirectory named after its run id.
    /// </summary>
    public string? RunOutputDirectory { get; set; } =
        Path.Combine(Path.GetTempPath(), "LMTrainingDataStudio", "runs");

    /// <summary>Rows kept in memory per inter-block buffer before spilling to disk.</summary>
    public long SpillThresholdRows { get; set; } = 10_000;

    /// <summary>Directory for temporary inter-block spill files.</summary>
    public string RunTempDirectory { get; set; } =
        Path.Combine(Path.GetTempPath(), "LMTrainingDataStudio", "run-temp");

    /// <summary>OpenAI API key; falls back to OPENAI_API_KEY when empty.</summary>
    public string? OpenAiApiKey { get; set; }

    /// <summary>Anthropic API key; falls back to ANTHROPIC_API_KEY when empty.</summary>
    public string? AnthropicApiKey { get; set; }

    /// <summary>Most recently opened recipe files, newest first.</summary>
    public List<string> RecentFiles { get; set; } = new();

    /// <summary>Recipe file restored on the next launch; empty starts a blank document.</summary>
    public string? LastRecipePath { get; set; }
}
