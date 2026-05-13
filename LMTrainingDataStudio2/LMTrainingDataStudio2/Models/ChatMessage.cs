using System.Text.Json.Serialization;

namespace LMTrainingDataStudio2.Models;

/// <summary>
/// Canonical chat message used across all format conversions.
/// </summary>
public sealed class ChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public object? Content { get; set; }

    [JsonPropertyName("reasoning_content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReasoningContent { get; set; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    [JsonPropertyName("tool_call_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolCallId { get; set; }

    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ToolDefinition>? Tools { get; set; }

    [JsonPropertyName("tool_calls")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ToolCall>? ToolCalls { get; set; }
}

/// <summary>
/// OpenAI-compatible tool definition with nested function schema.
/// </summary>
public sealed class ToolDefinition
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public ToolFunction Function { get; set; } = new();
}

/// <summary>
/// Function schema within a tool definition.
/// </summary>
public sealed class ToolFunction
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonPropertyName("parameters")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, object>? Parameters { get; set; }
}

/// <summary>
/// Assistant tool-call payload.
/// </summary>
public sealed class ToolCall
{
    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public ToolCallFunction Function { get; set; } = new();
}

/// <summary>
/// Function call details within a tool call.
/// </summary>
public sealed class ToolCallFunction
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("arguments")]
    public object? Arguments { get; set; }
}

/// <summary>
/// Canonical SFT sample after format conversion.
/// </summary>
public sealed class SftSample
{
    [JsonPropertyName("messages")]
    public List<ChatMessage> Messages { get; set; } = new();

    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ToolDefinition>? Tools { get; set; }
}
