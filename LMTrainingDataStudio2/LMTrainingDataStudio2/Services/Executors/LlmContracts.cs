using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Services.Executors;

/// <summary>
/// One outbound chat turn. Assistant turns can carry tool calls; tool turns carry the
/// id of the call they answer. Providers serialize these into their native tool formats.
/// </summary>
public sealed record LlmChatTurn(
    string Role,
    string? Content,
    string? ToolCallId = null,
    IReadOnlyList<LlmToolCall>? ToolCalls = null);

public sealed record LlmToolCall(string Id, string Name, string ArgumentsJson);

public sealed record LlmTokenUsage(long PromptTokens, long CompletionTokens)
{
    public long TotalTokens => PromptTokens + CompletionTokens;
}

/// <summary>
/// A single LLM completion request: chat turns, optional tool definitions and an optional
/// JSON Schema used for structured output.
/// </summary>
public sealed record LlmCompletionRequest(
    IReadOnlyList<LlmChatTurn> Messages,
    IReadOnlyList<ToolDefinition>? Tools = null,
    string? ResponseSchema = null);

public sealed record LlmCompletionResult(
    string Content,
    IReadOnlyList<LlmToolCall>? ToolCalls = null,
    LlmTokenUsage? Usage = null);
