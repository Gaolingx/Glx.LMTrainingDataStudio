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
/// Resolves API keys from application settings first, then environment variables.
/// Keys are stored in settings.json on this machine and never in recipe files.
/// </summary>
public sealed class LlmCredentials : ILlmCredentials
{
    private readonly AppSettings _settings;
    private readonly Func<string, string?> _readEnvironment;

    public LlmCredentials(AppSettings settings, Func<string, string?>? readEnvironment = null)
    {
        _settings = settings;
        _readEnvironment = readEnvironment ?? Environment.GetEnvironmentVariable;
    }

    public string? GetApiKey(string provider) => LlmProviderIds.Normalize(provider) switch
    {
        LlmProviderIds.OpenAiChat or LlmProviderIds.OpenAiResponses =>
            FirstNonEmpty(_settings.OpenAiApiKey, Read("OPENAI_API_KEY")),
        LlmProviderIds.Anthropic =>
            FirstNonEmpty(_settings.AnthropicApiKey, Read("ANTHROPIC_API_KEY")),
        _ => null
    };

    public string? GetBaseUrl(string provider) => LlmProviderIds.Normalize(provider) switch
    {
        LlmProviderIds.OpenAiChat or LlmProviderIds.OpenAiResponses => Read("OPENAI_BASE_URL"),
        LlmProviderIds.Anthropic => Read("ANTHROPIC_BASE_URL"),
        _ => null
    };

    private string? Read(string name)
    {
        var value = _readEnvironment(name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? FirstNonEmpty(string? preferred, string? fallback) =>
        string.IsNullOrWhiteSpace(preferred) ? fallback : preferred.Trim();
}
