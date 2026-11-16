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

    private sealed class OaiSample
    {
        [JsonPropertyName("messages")]
        public required List<ChatMessage> Messages { get; init; }

        [JsonPropertyName("tools")]
        public List<ToolDefinition>? Tools { get; init; }
    }
}
