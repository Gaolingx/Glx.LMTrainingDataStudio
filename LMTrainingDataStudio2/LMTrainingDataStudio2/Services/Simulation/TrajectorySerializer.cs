using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Services.Simulation;

/// <summary>
/// Serializes trajectories to the OpenAI chat-completions shape used by SFT trainers.
/// </summary>
public static class TrajectorySerializer
{
    public static readonly JsonSerializerOptions OaiJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string ToOaiJson(TrajectoryResult trajectory)
    {
        ArgumentNullException.ThrowIfNull(trajectory);

        return JsonSerializer.Serialize(
            new OaiSample
            {
                Messages = trajectory.Messages,
                Tools = trajectory.Tools.Count > 0 ? trajectory.Tools : null
            },
            OaiJsonOptions);
    }

    /// <summary>
    /// Serializes a failed trajectory together with its diagnostics for a separate failure file.
    /// </summary>
    public static string ToFailureJson(string query, TrajectoryResult trajectory)
    {
        ArgumentNullException.ThrowIfNull(trajectory);

        return JsonSerializer.Serialize(
            new FailureSample
            {
                Query = query,
                Status = trajectory.Status.ToString(),
                Error = trajectory.Error,
                Rounds = trajectory.Rounds,
                ToolCalls = trajectory.ToolCallCount,
                ToolRetries = trajectory.ToolRetryCount,
                PromptTokens = trajectory.PromptTokens,
                CompletionTokens = trajectory.CompletionTokens,
                Messages = trajectory.Messages,
                Tools = trajectory.Tools.Count > 0 ? trajectory.Tools : null
            },
            OaiJsonOptions);
    }

    private sealed class OaiSample
    {
        [JsonPropertyName("messages")]
        public required List<ChatMessage> Messages { get; init; }

        [JsonPropertyName("tools")]
        public List<ToolDefinition>? Tools { get; init; }
    }

    private sealed class FailureSample
    {
        [JsonPropertyName("query")]
        public required string Query { get; init; }

        [JsonPropertyName("status")]
        public required string Status { get; init; }

        [JsonPropertyName("error")]
        public string? Error { get; init; }

        [JsonPropertyName("rounds")]
        public int Rounds { get; init; }

        [JsonPropertyName("tool_calls")]
        public int ToolCalls { get; init; }

        [JsonPropertyName("tool_retries")]
        public int ToolRetries { get; init; }

        [JsonPropertyName("prompt_tokens")]
        public long PromptTokens { get; init; }

        [JsonPropertyName("completion_tokens")]
        public long CompletionTokens { get; init; }

        [JsonPropertyName("messages")]
        public required List<ChatMessage> Messages { get; init; }

        [JsonPropertyName("tools")]
        public List<ToolDefinition>? Tools { get; init; }
    }
}
