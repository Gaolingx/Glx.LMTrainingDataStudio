using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Services.Executors;

/// <summary>
/// HTTP client for OpenAI Chat Completions, OpenAI Responses and Anthropic Messages APIs.
/// API keys come from <see cref="ILlmCredentials"/> and never from the recipe.
/// </summary>
public sealed class LlmApiClient : ILlmClient
{
    private const string DefaultOpenAiBaseUrl = "https://api.openai.com/v1";
    private const string DefaultAnthropicBaseUrl = "https://api.anthropic.com";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly ILlmCredentials _credentials;

    public LlmApiClient(HttpClient httpClient, ILlmCredentials credentials)
    {
        _httpClient = httpClient;
        _credentials = credentials;
    }

    public async Task<string> CompleteAsync(
        string prompt, LlmBlockConfig config, CancellationToken cancellationToken = default)
    {
        var provider = LlmProviderIds.Normalize(config.Provider)
            ?? throw new InvalidOperationException($"LLM provider '{config.Provider}' is not supported.");

        return provider switch
        {
            LlmProviderIds.OpenAiChat => await CompleteOpenAiChatAsync(prompt, config, cancellationToken),
            LlmProviderIds.OpenAiResponses => await CompleteOpenAiResponsesAsync(prompt, config, cancellationToken),
            LlmProviderIds.Anthropic => await CompleteAnthropicAsync(prompt, config, cancellationToken),
            _ => throw new InvalidOperationException($"LLM provider '{provider}' is not implemented.")
        };
    }

    private async Task<string> CompleteOpenAiChatAsync(
        string prompt, LlmBlockConfig config, CancellationToken ct)
    {
        var apiKey = RequireApiKey(LlmProviderIds.OpenAiChat, "OPENAI_API_KEY");
        var baseUrl = ResolveBaseUrl(config, LlmProviderIds.OpenAiChat, DefaultOpenAiBaseUrl);
        var payload = new ChatCompletionRequest(
            config.ModelAlias,
            new[] { new ChatMessage("user", prompt) },
            config.Temperature,
            config.MaxTokens);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions")
        {
            Content = JsonContent.Create(payload, options: JsonOptions)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var body = await SendAsync(request, "OpenAI chat completion", ct);
        using var document = JsonDocument.Parse(body);

        var content = document.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content");

        return content.ValueKind == JsonValueKind.String
            ? content.GetString() ?? string.Empty
            : content.GetRawText();
    }

    private async Task<string> CompleteOpenAiResponsesAsync(
        string prompt, LlmBlockConfig config, CancellationToken ct)
    {
        var apiKey = RequireApiKey(LlmProviderIds.OpenAiResponses, "OPENAI_API_KEY");
        var baseUrl = ResolveBaseUrl(config, LlmProviderIds.OpenAiResponses, DefaultOpenAiBaseUrl);
        var payload = new ResponsesRequest(
            config.ModelAlias,
            prompt,
            config.Temperature,
            config.MaxTokens);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/responses")
        {
            Content = JsonContent.Create(payload, options: JsonOptions)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var body = await SendAsync(request, "OpenAI response", ct);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        if (root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in output.EnumerateArray())
            {
                if (!item.TryGetProperty("content", out var content) ||
                    content.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var part in content.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var text) &&
                        text.ValueKind == JsonValueKind.String)
                    {
                        return text.GetString() ?? string.Empty;
                    }
                }
            }
        }

        if (root.TryGetProperty("output_text", out var outputText) &&
            outputText.ValueKind == JsonValueKind.String)
        {
            return outputText.GetString() ?? string.Empty;
        }

        throw new InvalidOperationException("OpenAI Responses API returned no output text.");
    }

    private async Task<string> CompleteAnthropicAsync(
        string prompt, LlmBlockConfig config, CancellationToken ct)
    {
        var apiKey = RequireApiKey(LlmProviderIds.Anthropic, "ANTHROPIC_API_KEY");
        var baseUrl = ResolveBaseUrl(config, LlmProviderIds.Anthropic, DefaultAnthropicBaseUrl);
        var payload = new AnthropicRequest(
            config.ModelAlias,
            config.MaxTokens,
            config.Temperature,
            new[] { new AnthropicMessage("user", prompt) });

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v1/messages")
        {
            Content = JsonContent.Create(payload, options: JsonOptions)
        };
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");

        var body = await SendAsync(request, "Anthropic message", ct);
        using var document = JsonDocument.Parse(body);
        var content = document.RootElement.GetProperty("content");

        foreach (var block in content.EnumerateArray())
        {
            if (block.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            {
                return text.GetString() ?? string.Empty;
            }
        }

        throw new InvalidOperationException("Anthropic Messages API returned no text content.");
    }

    private async Task<string> SendAsync(
        HttpRequestMessage request, string operation, CancellationToken ct)
    {
        using var response = await _httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            var snippet = body.Length > 500 ? body[..500] : body;
            throw new InvalidOperationException(
                $"{operation} failed with HTTP {(int)response.StatusCode} ({response.StatusCode}): {snippet}");
        }

        return body;
    }

    private string RequireApiKey(string provider, string environmentVariable)
    {
        var apiKey = _credentials.GetApiKey(provider);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                $"{environmentVariable} is not set. Export it before running LLM blocks.");
        }

        return apiKey;
    }

    private string ResolveBaseUrl(LlmBlockConfig config, string provider, string defaultBaseUrl)
    {
        var baseUrl = !string.IsNullOrWhiteSpace(config.Endpoint)
            ? config.Endpoint.Trim()
            : _credentials.GetBaseUrl(provider) ?? defaultBaseUrl;

        return baseUrl.TrimEnd('/');
    }

    private sealed record ChatMessage(string Role, string Content);

    private sealed record ChatCompletionRequest(
        string Model,
        ChatMessage[] Messages,
        double Temperature,
        [property: JsonPropertyName("max_tokens")] int MaxTokens);

    private sealed record ResponsesRequest(
        string Model,
        string Input,
        double Temperature,
        [property: JsonPropertyName("max_output_tokens")] int MaxOutputTokens);

    private sealed record AnthropicMessage(string Role, string Content);

    private sealed record AnthropicRequest(
        string Model,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        double Temperature,
        AnthropicMessage[] Messages);
}