using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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

        if (config.Streaming && provider == LlmProviderIds.OpenAiChat)
        {
            return await CompleteOpenAiChatStreamAsync(request, config, cancellationToken);
        }

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
                .Select(message => new
                {
                    role = message.Role,
                    content = message.Content ?? string.Empty,
                    tool_calls = message.ToolCalls?.Select(call => new
                    {
                        id = call.Id,
                        type = "function",
                        function = new { name = call.Name, arguments = call.ArgumentsJson }
                    }).ToArray(),
                    tool_call_id = message.ToolCallId
                })
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

    /// <summary>
    /// OpenAI Chat Completions with <c>stream=true</c>. Deltas are accumulated into a
    /// normal completion result so the executor does not need a streaming contract.
    /// </summary>
    private async Task<LlmCompletionResult> CompleteOpenAiChatStreamAsync(
        LlmCompletionRequest request, LlmBlockConfig config, CancellationToken ct)
    {
        var apiKey = RequireApiKey(LlmProviderIds.OpenAiChat, "OPENAI_API_KEY");
        var baseUrl = ResolveBaseUrl(config, LlmProviderIds.OpenAiChat, DefaultOpenAiBaseUrl);
        var tools = BuildOpenAiChatTools(request.Tools);
        var payload = new
        {
            model = config.ModelAlias,
            messages = request.Messages
                .Select(message => new
                {
                    role = message.Role,
                    content = message.Content ?? string.Empty,
                    tool_calls = message.ToolCalls?.Select(call => new
                    {
                        id = call.Id,
                        type = "function",
                        function = new { name = call.Name, arguments = call.ArgumentsJson }
                    }).ToArray(),
                    tool_call_id = message.ToolCallId
                })
                .ToArray(),
            temperature = config.Temperature,
            max_tokens = config.MaxTokens,
            tools,
            tool_choice = tools is null ? null : "auto",
            response_format = BuildOpenAiChatResponseFormat(request.ResponseSchema),
            stream = true,
            stream_options = new { include_usage = true }
        };

        return await SendStreamingWithRetryAsync(
            () =>
            {
                var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions")
                {
                    Content = JsonContent.Create(payload, options: JsonOptions)
                };
                httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                return httpRequest;
            },
            ReadOpenAiChatStreamAsync,
            "OpenAI chat streaming completion",
            config,
            ct);
    }

    private static async Task<LlmCompletionResult> ReadOpenAiChatStreamAsync(
        HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var content = new StringBuilder();
        var toolCalls = new Dictionary<int, StreamingToolCall>();
        LlmTokenUsage? usage = null;

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var data = line[5..].Trim();
            if (data.Length == 0)
            {
                continue;
            }

            if (data == "[DONE]")
            {
                break;
            }

            using var document = JsonDocument.Parse(data);
            var root = document.RootElement;
            usage = ParseUsage(root, "prompt_tokens", "completion_tokens") ?? usage;

            if (!root.TryGetProperty("choices", out var choices) ||
                choices.ValueKind != JsonValueKind.Array ||
                choices.GetArrayLength() == 0)
            {
                continue;
            }

            var choice = choices[0];
            if (!choice.TryGetProperty("delta", out var delta))
            {
                continue;
            }

            if (delta.TryGetProperty("content", out var deltaContent) &&
                deltaContent.ValueKind == JsonValueKind.String)
            {
                content.Append(deltaContent.GetString());
            }

            if (delta.TryGetProperty("tool_calls", out var deltaCalls) &&
                deltaCalls.ValueKind == JsonValueKind.Array)
            {
                foreach (var call in deltaCalls.EnumerateArray())
                {
                    var index = call.TryGetProperty("index", out var indexElement) &&
                                indexElement.TryGetInt32(out var parsedIndex)
                        ? parsedIndex
                        : 0;
                    if (!toolCalls.TryGetValue(index, out var builder))
                    {
                        builder = new StreamingToolCall();
                        toolCalls[index] = builder;
                    }

                    if (call.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                    {
                        builder.Id = id.GetString();
                    }

                    if (call.TryGetProperty("function", out var function))
                    {
                        if (function.TryGetProperty("name", out var name) &&
                            name.ValueKind == JsonValueKind.String)
                        {
                            builder.Name = name.GetString();
                        }

                        if (function.TryGetProperty("arguments", out var arguments) &&
                            arguments.ValueKind == JsonValueKind.String)
                        {
                            builder.Arguments.Append(arguments.GetString());
                        }
                    }
                }
            }
        }

        List<LlmToolCall>? calls = null;
        if (toolCalls.Count > 0)
        {
            calls = toolCalls
                .OrderBy(pair => pair.Key)
                .Select(pair => new LlmToolCall(
                    pair.Value.Id ?? $"call_{pair.Key}",
                    pair.Value.Name ?? string.Empty,
                    pair.Value.Arguments.Length == 0 ? "{}" : pair.Value.Arguments.ToString()))
                .ToList();
        }

        return new LlmCompletionResult(content.ToString(), calls, usage);
    }

    private sealed class StreamingToolCall
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public StringBuilder Arguments { get; } = new();
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
        var input = BuildOpenAiResponsesInput(request.Messages);
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

    private const string AnthropicStructuredOutputTool = "structured_output";

    private async Task<LlmCompletionResult> CompleteAnthropicAsync(
        LlmCompletionRequest request, LlmBlockConfig config, CancellationToken ct)
    {
        var apiKey = RequireApiKey(LlmProviderIds.Anthropic, "ANTHROPIC_API_KEY");
        var baseUrl = ResolveBaseUrl(config, LlmProviderIds.Anthropic, DefaultAnthropicBaseUrl);
        var systemText = string.Join(
            "\n\n",
            request.Messages
                .Where(message => message.Role is "system" or "developer")
                .Select(message => message.Content)
                .Where(content => !string.IsNullOrWhiteSpace(content)));
        var messages = BuildAnthropicMessages(request.Messages);
        var tools = request.Tools?.Select(tool => (object)new
        {
            name = tool.Function.Name,
            description = tool.Function.Description,
            input_schema = tool.Function.Parameters ?? new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>()
            }
        }).ToList() ?? new List<object>();

        object? toolChoice = null;
        if (!string.IsNullOrWhiteSpace(request.ResponseSchema))
        {
            tools.Add(new
            {
                name = AnthropicStructuredOutputTool,
                description = "Return the answer as one JSON object that matches the response schema.",
                input_schema = ParseSchema(request.ResponseSchema!)
            });
            toolChoice = new { type = "tool", name = AnthropicStructuredOutputTool };
        }

        var payload = new
        {
            model = config.ModelAlias,
            max_tokens = config.MaxTokens,
            temperature = config.Temperature,
            system = string.IsNullOrWhiteSpace(systemText) ? null : systemText,
            messages,
            tools = tools.Count > 0 ? tools : null,
            tool_choice = toolChoice
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
        string? structuredOutput = null;

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
                    var name = block.TryGetProperty("name", out var nameElement)
                        ? nameElement.GetString() ?? string.Empty
                        : string.Empty;
                    var input = block.TryGetProperty("input", out var inputElement)
                        ? inputElement.GetRawText()
                        : "{}";

                    if (string.Equals(name, AnthropicStructuredOutputTool, StringComparison.Ordinal))
                    {
                        structuredOutput = input;
                        break;
                    }

                    toolCalls.Add(new LlmToolCall(
                        block.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty,
                        name,
                        input));
                    break;
            }
        }

        var content = structuredOutput ?? (text.Count == 0 ? null : string.Concat(text));
        if (content is null && toolCalls.Count == 0)
        {
            var stopReason = root.TryGetProperty("stop_reason", out var stop)
                ? stop.GetString()
                : null;
            throw new InvalidOperationException(
                $"Anthropic Messages API returned no text content (stop_reason: {stopReason ?? "none"}).");
        }

        return new LlmCompletionResult(
            content ?? string.Empty,
            toolCalls.Count == 0 ? null : toolCalls,
            ParseUsage(root, "input_tokens", "output_tokens"));
    }

    private static List<AnthropicOutboundMessage> BuildAnthropicMessages(
        IReadOnlyList<LlmChatTurn> messages)
    {
        var result = new List<AnthropicOutboundMessage>();
        foreach (var message in messages)
        {
            switch (message.Role)
            {
                case "system":
                case "developer":
                    continue;

                case "assistant":
                    var blocks = new List<object>();
                    if (!string.IsNullOrWhiteSpace(message.Content))
                    {
                        blocks.Add(new { type = "text", text = message.Content });
                    }

                    if (message.ToolCalls is { Count: > 0 } toolCalls)
                    {
                        foreach (var call in toolCalls)
                        {
                            blocks.Add(new
                            {
                                type = "tool_use",
                                id = call.Id,
                                name = call.Name,
                                input = ParseArgumentsNode(call.ArgumentsJson)
                            });
                        }
                    }

                    if (blocks.Count > 0)
                    {
                        AddAnthropicMessage(result, "assistant", blocks);
                    }

                    break;

                case "tool":
                    AddAnthropicMessage(result, "user", new List<object>
                    {
                        new
                        {
                            type = "tool_result",
                            tool_use_id = message.ToolCallId ?? string.Empty,
                            content = message.Content ?? string.Empty
                        }
                    });
                    break;

                default:
                    AddAnthropicMessage(result, "user", message.Content ?? string.Empty);
                    break;
            }
        }

        return result;
    }

    private static void AddAnthropicMessage(
        List<AnthropicOutboundMessage> messages,
        string role,
        object content)
    {
        if (messages.Count > 0 && messages[^1].Role == role)
        {
            var merged = ToAnthropicBlocks(messages[^1].Content);
            merged.AddRange(ToAnthropicBlocks(content));
            messages[^1] = new AnthropicOutboundMessage(role, merged);
            return;
        }

        messages.Add(new AnthropicOutboundMessage(role, content));
    }

    private static List<object> ToAnthropicBlocks(object content) => content switch
    {
        List<object> blocks => [.. blocks],
        string text => new List<object> { new { type = "text", text } },
        _ => new List<object> { content }
    };

    private static JsonNode ParseArgumentsNode(string argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
        {
            return new JsonObject();
        }

        try
        {
            return JsonNode.Parse(argumentsJson) ?? new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject { ["raw"] = argumentsJson };
        }
    }

    private static object[] BuildOpenAiResponsesInput(IReadOnlyList<LlmChatTurn> messages)
    {
        var items = new List<object>();
        foreach (var message in messages)
        {
            switch (message.Role)
            {
                case "system":
                case "developer":
                    continue;

                case "tool":
                    items.Add(new
                    {
                        type = "function_call_output",
                        call_id = message.ToolCallId ?? string.Empty,
                        output = message.Content ?? string.Empty
                    });
                    break;

                case "assistant" when message.ToolCalls is { Count: > 0 } toolCalls:
                    if (!string.IsNullOrWhiteSpace(message.Content))
                    {
                        items.Add(new { role = "assistant", content = message.Content });
                    }

                    foreach (var call in toolCalls)
                    {
                        items.Add(new
                        {
                            type = "function_call",
                            call_id = call.Id,
                            name = call.Name,
                            arguments = call.ArgumentsJson
                        });
                    }

                    break;

                default:
                    items.Add(new { role = message.Role, content = message.Content ?? string.Empty });
                    break;
            }
        }

        return items.ToArray();
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

    /// <summary>
    /// Streaming variant of <see cref="SendWithRetryAsync"/>. Each attempt rebuilds the
    /// request and re-reads the stream, so a retry restarts from a clean accumulator.
    /// </summary>
    private async Task<LlmCompletionResult> SendStreamingWithRetryAsync(
        Func<HttpRequestMessage> requestFactory,
        Func<HttpResponseMessage, CancellationToken, Task<LlmCompletionResult>> readResponse,
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
                using var response = await _httpClient.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);

                if (response.IsSuccessStatusCode)
                {
                    return await readResponse(response, timeoutCts.Token);
                }

                var errorBody = await response.Content.ReadAsStringAsync(timeoutCts.Token);
                var error = CreateHttpError(operation, response.StatusCode, errorBody);
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
            catch (IOException ex)
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

    private sealed record AnthropicOutboundMessage(string Role, object Content);
}
