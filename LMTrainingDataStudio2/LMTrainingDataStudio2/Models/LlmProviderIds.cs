namespace LMTrainingDataStudio2.Models;

/// <summary>
/// Canonical LLM provider identifiers stored in recipes.
/// </summary>
public static class LlmProviderIds
{
    public const string OpenAiChat = "openai_chat";
    public const string OpenAiResponses = "openai_responses";
    public const string Anthropic = "anthropic";

    public static string? Normalize(string? provider) => provider?.Trim().ToLowerInvariant() switch
    {
        "openai" or "openai_chat" or "openai_completion" or "completion" => OpenAiChat,
        "openai_responses" or "response" or "responses" => OpenAiResponses,
        "anthropic" or "claude" => Anthropic,
        _ => null
    };
}
