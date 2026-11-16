using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Services.Simulation;

/// <summary>
/// A single completion request. Tool definitions are already filtered by the harness whitelist.
/// </summary>
public sealed record ChatCompletionRequest(
    string Model,
    IReadOnlyList<ChatMessage> Messages,
    IReadOnlyList<ToolDefinition> Tools,
    double Temperature,
    int MaxTokens,
    int Round);

/// <summary>
/// Token usage reported by a provider. Scripted clients report none.
/// </summary>
public sealed record ChatCompletionUsage(int PromptTokens, int CompletionTokens)
{
    public int TotalTokens => PromptTokens + CompletionTokens;
}

public sealed record ChatCompletionResponse(
    ChatMessage Message,
    string? FinishReason = null,
    ChatCompletionUsage? Usage = null);

/// <summary>
/// Chat-completion transport used by the mini harness. Implementations can call a real
/// provider or return deterministic scripted turns.
/// </summary>
public interface IChatCompletionClient
{
    Task<ChatCompletionResponse> CompleteAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken);
}

public enum TrajectoryStatus
{
    Completed,
    MaxRoundsExceeded,
    Aborted
}

/// <summary>
/// Full recorded trajectory, including metadata for logging and diagnostics.
/// Only <see cref="Messages"/> and <see cref="Tools"/> are exported for SFT.
/// </summary>
public sealed class TrajectoryResult
{
    public List<ChatMessage> Messages { get; } = new();
    public List<ToolDefinition> Tools { get; } = new();
    public TrajectoryStatus Status { get; set; }
    public int Rounds { get; set; }
    public int ToolCallCount { get; set; }
    public int ToolRetryCount { get; set; }
    public int ModelCalls { get; set; }
    public long PromptTokens { get; set; }
    public long CompletionTokens { get; set; }
    public TimeSpan ModelLatency { get; set; }
    public decimal EstimatedCost { get; set; }
    public string? Error { get; set; }
}
