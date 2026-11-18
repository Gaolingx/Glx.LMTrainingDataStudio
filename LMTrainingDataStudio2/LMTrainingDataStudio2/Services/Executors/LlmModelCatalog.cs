using System.Net.Http.Headers;
using System.Text.Json;
using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Services.Executors;

/// <summary>
/// Lists the models a provider exposes to the configured API key.
/// </summary>
public interface ILlmModelCatalog
{
    Task<IReadOnlyList<string>> ListModelsAsync(
        string provider,
        string? endpoint,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads model ids from the OpenAI-compatible <c>/models</c> endpoint or the
/// Anthropic <c>/v1/models</c> endpoint. Credentials are resolved per call so
/// settings changes take effect without recreating the catalog.
/// </summary>
public sealed class LlmModelCatalog : ILlmModelCatalog
{
    private const string DefaultOpenAiBaseUrl = "https://api.openai.com/v1";
    private const string DefaultAnthropicBaseUrl = "https://api.anthropic.com";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private readonly HttpClient _httpClient;
    private readonly Func<ILlmCredentials> _credentials;

    public LlmModelCatalog(HttpClient httpClient, Func<ILlmCredentials> credentials)
    {
        _httpClient = httpClient;
        _credentials = credentials;
    }

    public async Task<IReadOnlyList<string>> ListModelsAsync(
        string provider,
        string? endpoint,
        CancellationToken cancellationToken = default)
    {
        var normalized = LlmProviderIds.Normalize(provider)
            ?? throw new InvalidOperationException($"LLM provider '{provider}' is not supported.");
        var credentials = _credentials();
        var apiKey = credentials.GetApiKey(normalized);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                normalized == LlmProviderIds.Anthropic
                    ? "ANTHROPIC_API_KEY is not set. Configure it in Settings before listing models."
                    : "OPENAI_API_KEY is not set. Configure it in Settings before listing models.");
        }

        var baseUrl = !string.IsNullOrWhiteSpace(endpoint)
            ? endpoint.Trim().TrimEnd('/')
            : (credentials.GetBaseUrl(normalized)
               ?? (normalized == LlmProviderIds.Anthropic ? DefaultAnthropicBaseUrl : DefaultOpenAiBaseUrl))
              .TrimEnd('/');
        var url = normalized == LlmProviderIds.Anthropic
            ? $"{baseUrl}/v1/models?limit=1000"
            : $"{baseUrl}/models";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (normalized == LlmProviderIds.Anthropic)
        {
            request.Headers.Add("x-api-key", apiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");
        }
        else
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(RequestTimeout);

        string body;
        try
        {
            using var response = await _httpClient.SendAsync(request, timeoutCts.Token);
            body = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                var snippet = body.Length > 500 ? body[..500] : body;
                throw new InvalidOperationException(
                    $"Listing models failed with HTTP {(int)response.StatusCode} ({response.StatusCode}): {snippet}");
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                $"Listing models timed out after {RequestTimeout.TotalSeconds:0}s.");
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Listing models failed: {ex.Message}", ex);
        }

        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return data.EnumerateArray()
            .Select(item => item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
                ? id.GetString()
                : null)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
