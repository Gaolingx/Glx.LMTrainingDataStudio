using System.Text.Json.Serialization;

namespace LMTrainingDataStudio2.Models;

public enum RunStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Cancelled
}

public enum ExecutionStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Skipped,
    Cancelled
}

/// <summary>
/// Execution outcome for a single recipe block.
/// </summary>
public sealed class NodeExecutionResult
{
    public required string BlockId { get; init; }
    public required string BlockName { get; init; }
    public ExecutionStatus Status { get; set; } = ExecutionStatus.Pending;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public long InputRows { get; set; }
    public long OutputRows { get; set; }
    public string? Error { get; set; }

    [JsonIgnore]
    public TimeSpan? Duration => StartedAt is null || FinishedAt is null ? null : FinishedAt - StartedAt;
}

/// <summary>
/// A single recipe execution with per-node results and logs.
/// </summary>
public sealed class ExecutionRun
{
    public string RunId { get; init; } = Guid.NewGuid().ToString("N");
    public string RecipeId { get; init; } = string.Empty;
    public RunStatus Status { get; set; } = RunStatus.Pending;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string? OutputDirectory { get; set; }
    public List<NodeExecutionResult> Nodes { get; set; } = new();

    /// <summary>
    /// Logs are persisted to a sidecar file instead of the run JSON.
    /// </summary>
    [JsonIgnore]
    public List<string> Logs { get; set; } = new();
}

/// <summary>
/// Lightweight entry for execution history lists.
/// </summary>
public sealed record ExecutionRunSummary(
    string RunId,
    string RecipeId,
    RunStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt);

/// <summary>
/// Progress snapshot reported while a recipe runs.
/// </summary>
public sealed record ExecutionProgress(
    string? CurrentBlockId,
    string? CurrentBlockName,
    int CompletedBlocks,
    int TotalBlocks,
    long ProcessedRows);
