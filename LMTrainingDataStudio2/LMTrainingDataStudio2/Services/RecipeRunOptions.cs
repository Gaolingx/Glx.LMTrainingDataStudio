namespace LMTrainingDataStudio2.Services;

/// <summary>
/// Execution policy: layer parallelism, per-node retries and optional run output directory.
/// </summary>
public sealed record RecipeRunOptions(
    int MaxParallelism = 4,
    int NodeRetryCount = 0,
    string? OutputDirectory = null)
{
    public int EffectiveMaxParallelism => Math.Clamp(MaxParallelism, 1, 64);

    public int EffectiveRetryCount => Math.Max(0, NodeRetryCount);
}