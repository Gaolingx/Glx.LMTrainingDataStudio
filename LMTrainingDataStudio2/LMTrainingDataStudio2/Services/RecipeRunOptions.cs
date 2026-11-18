namespace LMTrainingDataStudio2.Services;

/// <summary>
/// Execution policy: layer parallelism, per-node retries, intermediate-output spilling
/// and the optional run output directory used for summaries and resume checkpoints.
/// </summary>
public sealed record RecipeRunOptions(
    int MaxParallelism = 4,
    int NodeRetryCount = 0,
    string? OutputDirectory = null,
    long SpillThresholdRows = 10_000,
    long SpillThresholdBytes = 64L * 1024 * 1024,
    string? SpillDirectory = null,
    bool PersistNodeOutputs = true)
{
    /// <summary>Spill root used when no directory is configured.</summary>
    public static string DefaultSpillDirectory { get; } =
        Path.Combine(Path.GetTempPath(), "LMTrainingDataStudio", "run-spill");

    public int EffectiveMaxParallelism => Math.Clamp(MaxParallelism, 1, 64);

    public int EffectiveRetryCount => Math.Max(0, NodeRetryCount);

    public long EffectiveSpillThresholdRows => Math.Max(0, SpillThresholdRows);

    /// <summary>Byte budget per buffer; zero disables the byte limit.</summary>
    public long EffectiveSpillThresholdBytes => Math.Max(0, SpillThresholdBytes);

    public string EffectiveSpillDirectory => string.IsNullOrWhiteSpace(SpillDirectory)
        ? DefaultSpillDirectory
        : Path.GetFullPath(SpillDirectory.Trim());
}
