using System.Net;
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
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public LlmApiClient(
        HttpClient httpClient,
        ILlmCredentials credentials,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _httpClient = httpClient;
        _credentials = credentials;
        _delay = delay ?? ((duration, token) => Task.Delay(duration, token));
    }

    public async Task<string> CompleteAsync(
        string prompt, LlmBlockConfig config, CancellationToken cancellationToken = default) =>
        (await CompleteAsync(
            new LlmCompletionRequest([new LlmChatTurn("user", prompt)]),
            config,
            cancellationToken)).Content;

    public async Task<LlmCompletionResult> CompleteAsync(
        LlmCompletionRequest request,
        LlmBlockConfig config,
        CancellationToken cancellationToken = default)
    {
        if (config.TimeoutSeconds <= 0)
        {
            throw new InvalidOperationException(
                $"LLM timeout must be greater than 0 seconds; got {config.TimeoutSeconds}.");
        }

        if (config.MaxRetries < 0)
        {
            throw new InvalidOperationException(
                $"LLM max retries must not be negative; got {config.MaxRetries}.");
        }

        var provider = LlmProviderIds.Normalize(config.Provider)
            ?? throw new InvalidOperationException($"LLM provider '{config.Provider}' is not supported.");

        return provider switch
        {
            LlmProviderIds.OpenAiChat => await CompleteOpenAiChatAsync(request, config, cancellationToken),
            LlmProviderIds.OpenAiResponses => await CompleteOpenAiResponsesAsync(request, config, cancellationToken),
            LlmProviderIds.Anthropic => await CompleteAnthropicAsync(request, config, cancellationToken),
            _ => throw new InvalidOperationException($"LLM provider '{provider}' is not implemented.")
        };
    }

    private async Task<LlmCompletionResult> CompleteOpenAiChatAsync(
        LlmCompletionRequest request, LlmBlockConfig config, CancellationToken ct)
    {
        var apiKey = RequireApiKey(LlmProviderIds.OpenAiChat, "OPENAI_API_KEY");
        var baseUrl = ResolveBaseUrl(config, LlmProviderIds.OpenAiChat, DefaultOpenAiBaseUrl);
        var tools = BuildOpenAiChatTools(request.Tools);
        var payload = new
        {
            model = config.ModelAlias,
            messages = request.Messages
                .Select(message => new { role = message.Role, content = message.Content ?? string.Empty })
                .ToArray(),
            temperature = config.Temperature,
            max_tokens = config.MaxTokens,
            tools,
            tool_choice = tools is null ? null : "auto",
            response_format = BuildOpenAiChatResponseFormat(request.ResponseSchema)
        };

        var body = await SendWithRetryAsync(
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions")
                {
                    Content = JsonContent.Create(payload, options: JsonOptions)
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                return request;
            },
            "OpenAI chat completion",
            config,
            ct);

        using var document = JsonDocument.Parse(body);
        var choice = document.RootElement.GetProperty("choices")[0];
        var message = choice.GetProperty("message");
        var content = message.TryGetProperty("content", out var contentElement) &&
                      contentElement.ValueKind != JsonValueKind.Null
            ? contentElement.ValueKind == JsonValueKind.String
                ? contentElement.GetString() ?? string.Empty
                : contentElement.GetRawText()
            : string.Empty;

        return new LlmCompletionResult(
            content,
            ParseOpenAiChatToolCalls(message),
            ParseUsage(document.RootElement, "prompt_tokens", "completion_tokens"));
    }

    private async Task<LlmCompletionResult> CompleteOpenAiResponsesAsync(
        LlmCompletionRequest request, LlmBlockConfig config, CancellationToken ct)
    {
        var apiKey = RequireApiKey(LlmProviderIds.OpenAiResponses, "OPENAI_API_KEY");
        var baseUrl = ResolveBaseUrl(config, LlmProviderIds.OpenAiResponses, DefaultOpenAiBaseUrl);
        var instructions = string.Join(
            "\n\n",
            request.Messages
                .Where(message => message.Role is "system" or "developer")
                .Select(message => message.Content)
                .Where(content => !string.IsNullOrWhiteSpace(content)));
        var input = request.Messages
            .Where(message => message.Role is not ("system" or "developer"))
            .Select(message => new { role = message.Role, content = message.Content ?? string.Empty })
            .ToArray();
        var tools = request.Tools?.Select(tool => new
        {
            type = "function",
            name = tool.Function.Name,
            description = tool.Function.Description,
            parameters = tool.Function.Parameters
        }).ToArray();
        var format = string.IsNullOrWhiteSpace(request.ResponseSchema)
            ? null
            : new
            {
                type = "json_schema",
                name = "response",
                strict = true,
                schema = ParseSchema(request.ResponseSchema!)
            };
        var payload = new
        {
            model = config.ModelAlias,
            instructions = string.IsNullOrWhiteSpace(instructions) ? null : instructions,
            input,
            temperature = config.Temperature,
            max_output_tokens = config.MaxTokens,
            tools,
            text = format is null ? null : new { format }
        };

        var body = await SendWithRetryAsync(
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/responses")
                {
                    Content = JsonContent.Create(payload, options: JsonOptions)
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                return request;
            },
            "OpenAI response",
            config,
            ct);

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        var text = new List<string>();
        var toolCalls = new List<LlmToolCall>();
        if (root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in output.EnumerateArray())
            {
                var type = item.TryGetProperty("type", out var typeElement)
                    ? typeElement.GetString()
                    : null;
                if (type == "function_call")
                {
                    toolCalls.Add(new LlmToolCall(
                        item.TryGetProperty("call_id", out var callId) ? callId.GetString() ?? string.Empty : string.Empty,
                        item.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
                        item.TryGetProperty("arguments", out var arguments) ? arguments.GetRawText() : "{}"));
                    continue;
                }

                if (!item.TryGetProperty("content", out var content) ||
                    content.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var part in content.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var partText) &&
                        partText.ValueKind == JsonValueKind.String)
                    {
                        text.Add(partText.GetString() ?? string.Empty);
                    }
                }
            }
        }

        if (text.Count == 0 &&
            root.TryGetProperty("output_text", out var outputText) &&
            outputText.ValueKind == JsonValueKind.String)
        {
            text.Add(outputText.GetString() ?? string.Empty);
        }

        if (text.Count == 0 && toolCalls.Count == 0)
        {
            throw new InvalidOperationException("OpenAI Responses API returned no output text.");
        }

        return new LlmCompletionResult(
            string.Concat(text),
            toolCalls.Count == 0 ? null : toolCalls,
            ParseUsage(root, "input_tokens", "output_tokens"));
    }

    private async Task<LlmCompletionResult> CompleteAnthropicAsync(
        LlmCompletionRequest request, LlmBlockConfig config, CancellationToken ct)
    {
        var apiKey = RequireApiKey(LlmProviderIds.Anthropic, "ANTHROPIC_API_KEY");
        var baseUrl = ResolveBaseUrl(config, LlmProviderIds.Anthropic, DefaultAnthropicBaseUrl);
        var systemText = string.Join(
            "\n\n",
            request.Messages
                .Where(message => message.Role == "system")
                .Select(message => message.Content)
                .Where(content => !string.IsNullOrWhiteSpace(content)));
        var messages = request.Messages
            .Where(message => message.Role != "system")
            .Select(message => new
            {
                role = message.Role == "assistant" ? "assistant" : "user",
                content = message.Content ?? string.Empty
            })
            .ToArray();
        var tools = request.Tools?.Select(tool => new
        {
            name = tool.Function.Name,
            description = tool.Function.Description,
            input_schema = tool.Function.Parameters
        }).ToArray();
        var payload = new
        {
            model = config.ModelAlias,
            max_tokens = config.MaxTokens,
            temperature = config.Temperature,
            system = string.IsNullOrWhiteSpace(systemText) ? null : systemText,
            messages,
            tools
        };

        var body = await SendWithRetryAsync(
            () =>
            {
                var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v1/messages")
                {
                    Content = JsonContent.Create(payload, options: JsonOptions)
                };
                httpRequest.Headers.Add("x-api-key", apiKey);
                httpRequest.Headers.Add("anthropic-version", "2023-06-01");
                return httpRequest;
            },
            "Anthropic message",
            config,
            ct);

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var text = new List<string>();
        var toolCalls = new List<LlmToolCall>();

        foreach (var block in root.GetProperty("content").EnumerateArray())
        {
            var type = block.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
            switch (type)
            {
                case "text":
                    if (block.TryGetProperty("text", out var value) &&
                        value.ValueKind == JsonValueKind.String)
                    {
                        text.Add(value.GetString() ?? string.Empty);
                    }

                    break;

                case "tool_use":
                    toolCalls.Add(new LlmToolCall(
                        block.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty,
                        block.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
                        block.TryGetProperty("input", out var input) ? input.GetRawText() : "{}"));
                    break;
            }
        }

        if (text.Count == 0 && toolCalls.Count == 0)
        {
            throw new InvalidOperationException("Anthropic Messages API returned no text content.");
        }

        return new LlmCompletionResult(
            string.Concat(text),
            toolCalls.Count == 0 ? null : toolCalls,
            ParseUsage(root, "input_tokens", "output_tokens"));
    }

    private static object[]? BuildOpenAiChatTools(IReadOnlyList<ToolDefinition>? tools) =>
        tools is { Count: > 0 }
            ? tools.Select(tool => (object)new
            {
                type = "function",
                function = new
                {
                    name = tool.Function.Name,
                    description = tool.Function.Description,
                    parameters = tool.Function.Parameters
                }
            }).ToArray()
            : null;

    private static object? BuildOpenAiChatResponseFormat(string? schema) =>
        string.IsNullOrWhiteSpace(schema)
            ? null
            : new
            {
                type = "json_schema",
                json_schema = new
                {
                    name = "response",
                    strict = true,
                    schema = ParseSchema(schema)
                }
            };

    private static JsonElement ParseSchema(string schema)
    {
        using var document = JsonDocument.Parse(schema);
        return document.RootElement.Clone();
    }

    private static IReadOnlyList<LlmToolCall>? ParseOpenAiChatToolCalls(JsonElement message)
    {
        if (!message.TryGetProperty("tool_calls", out var toolCalls) ||
            toolCalls.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var calls = new List<LlmToolCall>();
        foreach (var call in toolCalls.EnumerateArray())
        {
            if (!call.TryGetProperty("function", out var function))
            {
                continue;
            }

            var arguments = function.TryGetProperty("arguments", out var args)
                ? args.ValueKind == JsonValueKind.String
                    ? args.GetString() ?? "{}"
                    : args.GetRawText()
                : "{}";
            calls.Add(new LlmToolCall(
                call.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty,
                function.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
                arguments));
        }

        return calls.Count == 0 ? null : calls;
    }

    private static LlmTokenUsage? ParseUsage(JsonElement root, string promptName, string completionName)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var prompt = usage.TryGetProperty(promptName, out var promptValue) && promptValue.TryGetInt64(out var p)
            ? p
            : 0;
        var completion = usage.TryGetProperty(completionName, out var completionValue) &&
                         completionValue.TryGetInt64(out var c)
            ? c
            : 0;
        return new LlmTokenUsage(prompt, completion);
    }

    private async Task<string> SendWithRetryAsync(
        Func<HttpRequestMessage> requestFactory,
        string operation,
        LlmBlockConfig config,
        CancellationToken ct)
    {
        var attempts = config.MaxRetries + 1;
        var timeout = TimeSpan.FromSeconds(config.TimeoutSeconds);
        Exception? lastFailure = null;

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            using var request = requestFactory();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);

            try
            {
                using var response = await _httpClient.SendAsync(request, timeoutCts.Token);
                var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);

                if (response.IsSuccessStatusCode)
                {
                    return body;
                }

                var error = CreateHttpError(operation, response.StatusCode, body);
                if (!IsRetryable(response.StatusCode))
                {
                    throw error;
                }

                lastFailure = error;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                lastFailure = new TimeoutException(
                    $"{operation} timed out after {timeout.TotalSeconds:0.#}s.");
            }
            catch (HttpRequestException ex)
            {
                lastFailure = ex;
            }

            if (attempt < attempts - 1)
            {
                await _delay(ComputeBackoff(attempt), ct);
            }
        }

        throw new InvalidOperationException(
            $"{operation} failed after {attempts} attempt(s): {lastFailure?.Message ?? "unknown error"}",
            lastFailure);
    }

    private static bool IsRetryable(HttpStatusCode status) =>
        status == HttpStatusCode.RequestTimeout ||
        (int)status == 429 ||
        (int)status >= 500;

    private static InvalidOperationException CreateHttpError(
        string operation, HttpStatusCode status, string body)
    {
        var snippet = body.Length > 500 ? body[..500] : body;
        return new InvalidOperationException(
            $"{operation} failed with HTTP {(int)status} ({status}): {snippet}");
    }

    private static TimeSpan ComputeBackoff(int attempt) =>
        TimeSpan.FromMilliseconds(Math.Min(500 * Math.Pow(2, attempt), 5000));

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

}
