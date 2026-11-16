using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Services.Executors;

/// <summary>
/// Supplies API keys and optional base URLs for LLM providers.
/// </summary>
public interface ILlmCredentials
{
    string? GetApiKey(string provider);

    string? GetBaseUrl(string provider);
}

/// <summary>
/// Reads credentials from environment variables so keys never enter recipe files.
/// </summary>
public sealed class EnvironmentLlmCredentials : ILlmCredentials
{
    public string? GetApiKey(string provider) => LlmProviderIds.Normalize(provider) switch
    {
        LlmProviderIds.OpenAiChat or LlmProviderIds.OpenAiResponses => Read("OPENAI_API_KEY"),
        LlmProviderIds.Anthropic => Read("ANTHROPIC_API_KEY"),
        _ => null
    };

    public string? GetBaseUrl(string provider) => LlmProviderIds.Normalize(provider) switch
    {
        LlmProviderIds.OpenAiChat or LlmProviderIds.OpenAiResponses => Read("OPENAI_BASE_URL"),
        LlmProviderIds.Anthropic => Read("ANTHROPIC_BASE_URL"),
        _ => null
    };

    private static string? Read(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}