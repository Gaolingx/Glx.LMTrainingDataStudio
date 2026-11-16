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
    string? SpillDirectory = null,
    bool PersistNodeOutputs = true)
{
    public int EffectiveMaxParallelism => Math.Clamp(MaxParallelism, 1, 64);

    public int EffectiveRetryCount => Math.Max(0, NodeRetryCount);

    public long EffectiveSpillThresholdRows => Math.Max(0, SpillThresholdRows);

    public string EffectiveSpillDirectory => string.IsNullOrWhiteSpace(SpillDirectory)
        ? Path.Combine(Path.GetTempPath(), "LMTrainingDataStudio", "run-spill")
        : Path.GetFullPath(SpillDirectory.Trim());
}
