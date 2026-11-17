using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services.Executors;

namespace LMTrainingDataStudio2.Services.Simulation;

/// <summary>
/// Real provider chat-completion transport with tool calling:
/// OpenAI Chat Completions (function calling), OpenAI Responses (function_call) and
/// Anthropic Messages (tool_use). Timeout and retry policy live in the harness.
/// </summary>
public sealed class ProviderChatCompletionClient : IChatCompletionClient
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
    private readonly string _provider;
    private readonly string? _endpoint;
    private readonly bool _streamDeltas;

    public ProviderChatCompletionClient(
        HttpClient httpClient,
        ILlmCredentials credentials,
        SimulationBlockConfig config)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(config);

        _provider = LlmProviderIds.Normalize(config.Provider)
            ?? throw new InvalidOperationException(
                $"Simulation provider '{config.Provider}' is not supported.");
        _endpoint = string.IsNullOrWhiteSpace(config.Endpoint) ? null : config.Endpoint.Trim();
        _streamDeltas = config.StreamDeltas && _provider == LlmProviderIds.OpenAiChat;
        _httpClient = httpClient;
        _credentials = credentials;
    }

    public async Task<ChatCompletionResponse> CompleteAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var apiKey = RequireApiKey();
        var baseUrl = ResolveBaseUrl();

        if (_streamDeltas)
        {
            return await CompleteOpenAiChatStreamAsync(request, apiKey, baseUrl, cancellationToken);
        }

        using var httpRequest = _provider switch
        {
            LlmProviderIds.OpenAiChat => CreateOpenAiChatRequest(request, apiKey, baseUrl),
            LlmProviderIds.OpenAiResponses => CreateOpenAiResponsesRequest(request, apiKey, baseUrl),
            LlmProviderIds.Anthropic => CreateAnthropicRequest(request, apiKey, baseUrl),
            _ => throw new InvalidOperationException(
                $"Simulation provider '{_provider}' is not implemented.")
        };

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"{DisplayName} failed with HTTP {(int)response.StatusCode} ({response.StatusCode}): {Snippet(body)}");
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            return _provider switch
            {
                LlmProviderIds.OpenAiChat => ParseOpenAiChat(document.RootElement),
                LlmProviderIds.OpenAiResponses => ParseOpenAiResponses(document.RootElement),
                LlmProviderIds.Anthropic => ParseAnthropic(document.RootElement),
                _ => throw new InvalidOperationException(
                    $"Simulation provider '{_provider}' is not implemented.")
            };
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"{DisplayName} returned invalid JSON: {ex.Message}. Body: {Snippet(body)}", ex);
        }
    }

    /// <summary>
    /// Sends an OpenAI Chat Completions request with <c>stream=true</c> and accumulates
    /// content/tool-call deltas into one response so the harness can keep its simple contract.
    /// </summary>
    private async Task<ChatCompletionResponse> CompleteOpenAiChatStreamAsync(
        ChatCompletionRequest request,
        string apiKey,
        string baseUrl,
        CancellationToken cancellationToken)
    {
        var tools = request.Tools.Count > 0 ? request.Tools : null;
        var payload = new
        {
            model = request.Model,
            messages = request.Messages,
            tools,
            tool_choice = tools is null ? null : "auto",
            temperature = request.Temperature,
            max_tokens = request.MaxTokens,
            stream = true,
            stream_options = new { include_usage = true }
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions")
        {
            Content = JsonContent.Create(payload, options: JsonOptions)
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await _httpClient.SendAsync(
            httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"{DisplayName} failed with HTTP {(int)response.StatusCode} ({response.StatusCode}): {Snippet(errorBody)}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var content = new StringBuilder();
        var toolCalls = new Dictionary<int, StreamingToolCall>();
        ChatCompletionUsage? usage = null;
        string? finishReason = null;

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
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
            usage = ParseOpenAiUsage(root) ?? usage;

            if (!root.TryGetProperty("choices", out var choices) ||
                choices.ValueKind != JsonValueKind.Array ||
                choices.GetArrayLength() == 0)
            {
                continue;
            }

            var choice = choices[0];
            if (choice.TryGetProperty("delta", out var delta))
            {
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

            if (choice.TryGetProperty("finish_reason", out var finish) &&
                finish.ValueKind == JsonValueKind.String)
            {
                finishReason = finish.GetString();
            }
        }

        List<ToolCall>? calls = null;
        if (toolCalls.Count > 0)
        {
            calls = toolCalls
                .OrderBy(pair => pair.Key)
                .Select(pair => new ToolCall
                {
                    Id = pair.Value.Id ?? $"call_{pair.Key}",
                    Type = "function",
                    Function = new ToolCallFunction
                    {
                        Name = pair.Value.Name ?? string.Empty,
                        Arguments = pair.Value.Arguments.Length == 0 ? "{}" : pair.Value.Arguments.ToString()
                    }
                })
                .ToList();
        }

        return new ChatCompletionResponse(
            new ChatMessage
            {
                Role = "assistant",
                Content = content.Length == 0 ? null : content.ToString(),
                ToolCalls = calls
            },
            finishReason,
            usage);
    }

    private sealed class StreamingToolCall
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public StringBuilder Arguments { get; } = new();
    }

    private HttpRequestMessage CreateOpenAiChatRequest(
        ChatCompletionRequest request,
        string apiKey,
        string baseUrl)
    {
        var tools = request.Tools.Count > 0 ? request.Tools : null;
        var payload = new OpenAiChatPayload(
            request.Model,
            request.Messages,
            tools,
            request.Temperature,
            request.MaxTokens,
            tools is null ? null : "auto");

        var message = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions")
        {
            Content = JsonContent.Create(payload, options: JsonOptions)
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return message;
    }

    private static ChatCompletionResponse ParseOpenAiChat(JsonElement root)
    {
        if (!root.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("OpenAI chat response did not contain any choices.");
        }

        var choice = choices[0];
        if (!choice.TryGetProperty("message", out var messageElement))
        {
            throw new InvalidOperationException("OpenAI chat response did not contain a message.");
        }

        var message = new ChatMessage { Role = "assistant" };
        if (messageElement.TryGetProperty("content", out var content) &&
            content.ValueKind != JsonValueKind.Null)
        {
            message.Content = content.ValueKind == JsonValueKind.String
                ? content.GetString()
                : content.GetRawText();
        }

        if (messageElement.TryGetProperty("tool_calls", out var toolCalls) &&
            toolCalls.ValueKind == JsonValueKind.Array)
        {
            foreach (var call in toolCalls.EnumerateArray())
            {
                if (!call.TryGetProperty("function", out var function))
                {
                    continue;
                }

                message.ToolCalls ??= new List<ToolCall>();
                message.ToolCalls.Add(new ToolCall
                {
                    Id = call.TryGetProperty("id", out var id) ? id.GetString() : null,
                    Type = call.TryGetProperty("type", out var type)
                        ? type.GetString() ?? "function"
                        : "function",
                    Function = new ToolCallFunction
                    {
                        Name = function.TryGetProperty("name", out var name)
                            ? name.GetString() ?? string.Empty
                            : string.Empty,
                        Arguments = function.TryGetProperty("arguments", out var arguments)
                            ? ReadArguments(arguments)
                            : "{}"
                    }
                });
            }
        }

        var finishReason = choice.TryGetProperty("finish_reason", out var finish)
            ? finish.GetString()
            : null;

        return new ChatCompletionResponse(message, finishReason, ParseOpenAiUsage(root));
    }

    private HttpRequestMessage CreateOpenAiResponsesRequest(
        ChatCompletionRequest request,
        string apiKey,
        string baseUrl)
    {
        var instructions = new List<string>();
        var input = new List<object>();

        foreach (var message in request.Messages)
        {
            var text = MessageText(message);
            switch (message.Role)
            {
                case "system":
                case "developer":
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        instructions.Add(text);
                    }

                    break;

                case "user":
                    input.Add(new
                    {
                        type = "message",
                        role = "user",
                        content = new[] { new { type = "input_text", text } }
                    });
                    break;

                case "assistant":
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        input.Add(new
                        {
                            type = "message",
                            role = "assistant",
                            content = new[] { new { type = "output_text", text } }
                        });
                    }

                    if (message.ToolCalls is { Count: > 0 })
                    {
                        foreach (var call in message.ToolCalls)
                        {
                            input.Add(new
                            {
                                type = "function_call",
                                call_id = call.Id ?? string.Empty,
                                name = call.Function.Name,
                                arguments = ArgumentsText(call)
                            });
                        }
                    }

                    break;

                case "tool":
                    input.Add(new
                    {
                        type = "function_call_output",
                        call_id = message.ToolCallId ?? string.Empty,
                        output = text
                    });
                    break;

                default:
                    input.Add(new
                    {
                        type = "message",
                        role = message.Role,
                        content = new[] { new { type = "input_text", text } }
                    });
                    break;
            }
        }

        var tools = request.Tools.Count > 0
            ? request.Tools.Select(tool => new
            {
                type = "function",
                name = tool.Function.Name,
                description = tool.Function.Description,
                parameters = tool.Function.Parameters
            }).ToArray()
            : null;

        var payload = new OpenAiResponsesPayload(
            request.Model,
            instructions.Count > 0 ? string.Join("\n\n", instructions) : null,
            input,
            tools,
            request.Temperature,
            request.MaxTokens);

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/responses")
        {
            Content = JsonContent.Create(payload, options: JsonOptions)
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return httpRequest;
    }

    private static ChatCompletionResponse ParseOpenAiResponses(JsonElement root)
    {
        var text = new List<string>();
        List<ToolCall>? toolCalls = null;

        if (root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in output.EnumerateArray())
            {
                var type = item.TryGetProperty("type", out var typeElement)
                    ? typeElement.GetString()
                    : null;

                switch (type)
                {
                    case "message":
                        if (item.TryGetProperty("content", out var content) &&
                            content.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var part in content.EnumerateArray())
                            {
                                if (part.TryGetProperty("text", out var partText) &&
                                    partText.ValueKind == JsonValueKind.String)
                                {
                                    text.Add(partText.GetString() ?? string.Empty);
                                }
                            }
                        }

                        break;

                    case "function_call":
                        toolCalls ??= new List<ToolCall>();
                        toolCalls.Add(new ToolCall
                        {
                            Id = item.TryGetProperty("call_id", out var callId) ? callId.GetString() : null,
                            Type = "function",
                            Function = new ToolCallFunction
                            {
                                Name = item.TryGetProperty("name", out var name)
                                    ? name.GetString() ?? string.Empty
                                    : string.Empty,
                                Arguments = item.TryGetProperty("arguments", out var arguments)
                                    ? ReadArguments(arguments)
                                    : "{}"
                            }
                        });
                        break;
                }
            }
        }

        if (text.Count == 0 &&
            root.TryGetProperty("output_text", out var outputText) &&
            outputText.ValueKind == JsonValueKind.String)
        {
            text.Add(outputText.GetString() ?? string.Empty);
        }

        if (text.Count == 0 && toolCalls is null)
        {
            throw new InvalidOperationException("OpenAI Responses API returned no text or tool calls.");
        }

        var message = new ChatMessage
        {
            Role = "assistant",
            Content = text.Count == 0 ? null : string.Concat(text),
            ToolCalls = toolCalls
        };

        var finishReason = root.TryGetProperty("status", out var status) ? status.GetString() : null;
        return new ChatCompletionResponse(message, finishReason, ParseOpenAiUsage(root));
    }

    private HttpRequestMessage CreateAnthropicRequest(
        ChatCompletionRequest request,
        string apiKey,
        string baseUrl)
    {
        var systemParts = new List<string>();
        var messages = new List<AnthropicOutboundMessage>();

        foreach (var message in request.Messages)
        {
            var text = MessageText(message);
            switch (message.Role)
            {
                case "system":
                case "developer":
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        systemParts.Add(text);
                    }

                    break;

                case "user":
                    AddAnthropicMessage(messages, "user", text);
                    break;

                case "assistant":
                    var blocks = new List<object>();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        blocks.Add(new { type = "text", text });
                    }

                    if (message.ToolCalls is { Count: > 0 })
                    {
                        foreach (var call in message.ToolCalls)
                        {
                            blocks.Add(new
                            {
                                type = "tool_use",
                                id = call.Id ?? $"toolu_{Guid.NewGuid():N}",
                                name = call.Function.Name,
                                input = ParseArgumentsNode(call)
                            });
                        }
                    }

                    if (blocks.Count > 0)
                    {
                        AddAnthropicMessage(messages, "assistant", blocks);
                    }

                    break;

                case "tool":
                    AddAnthropicMessage(messages, "user", new List<object>
                    {
                        new
                        {
                            type = "tool_result",
                            tool_use_id = message.ToolCallId ?? string.Empty,
                            content = text
                        }
                    });
                    break;
            }
        }

        var tools = request.Tools.Count > 0
            ? request.Tools.Select(tool => new
            {
                name = tool.Function.Name,
                description = tool.Function.Description,
                input_schema = tool.Function.Parameters ?? new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>()
                }
            }).ToArray()
            : null;

        var payload = new AnthropicPayload(
            request.Model,
            request.MaxTokens,
            request.Temperature,
            systemParts.Count > 0 ? string.Join("\n\n", systemParts) : null,
            messages,
            tools);

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v1/messages")
        {
            Content = JsonContent.Create(payload, options: JsonOptions)
        };
        httpRequest.Headers.Add("x-api-key", apiKey);
        httpRequest.Headers.Add("anthropic-version", "2023-06-01");
        return httpRequest;
    }

    private static ChatCompletionResponse ParseAnthropic(JsonElement root)
    {
        if (!root.TryGetProperty("content", out var content) ||
            content.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Anthropic response did not contain a content array.");
        }

        var text = new List<string>();
        List<ToolCall>? toolCalls = null;

        foreach (var block in content.EnumerateArray())
        {
            var type = block.TryGetProperty("type", out var typeElement)
                ? typeElement.GetString()
                : null;

            if (type == "text" &&
                block.TryGetProperty("text", out var textElement) &&
                textElement.ValueKind == JsonValueKind.String)
            {
                text.Add(textElement.GetString() ?? string.Empty);
            }
            else if (type == "tool_use")
            {
                var input = block.TryGetProperty("input", out var inputElement)
                    ? inputElement.GetRawText()
                    : "{}";

                toolCalls ??= new List<ToolCall>();
                toolCalls.Add(new ToolCall
                {
                    Id = block.TryGetProperty("id", out var id) ? id.GetString() : null,
                    Type = "function",
                    Function = new ToolCallFunction
                    {
                        Name = block.TryGetProperty("name", out var name)
                            ? name.GetString() ?? string.Empty
                            : string.Empty,
                        Arguments = input
                    }
                });
            }
        }

        if (text.Count == 0 && toolCalls is null)
        {
            var stopReason = root.TryGetProperty("stop_reason", out var stop)
                ? stop.GetString()
                : null;
            throw new InvalidOperationException(
                $"Anthropic response contained no text or tool calls (stop_reason: {stopReason ?? "none"}).");
        }

        var message = new ChatMessage
        {
            Role = "assistant",
            Content = text.Count == 0 ? null : string.Concat(text),
            ToolCalls = toolCalls
        };

        var finishReason = root.TryGetProperty("stop_reason", out var reason)
            ? reason.GetString()
            : null;

        ChatCompletionUsage? usage = null;
        if (root.TryGetProperty("usage", out var usageElement) &&
            usageElement.ValueKind == JsonValueKind.Object)
        {
            usage = new ChatCompletionUsage(
                ReadInt(usageElement, "input_tokens"),
                ReadInt(usageElement, "output_tokens"));
        }

        return new ChatCompletionResponse(message, finishReason, usage);
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

    private static JsonNode ParseArgumentsNode(ToolCall call)
    {
        var raw = ArgumentsText(call);
        try
        {
            var node = JsonNode.Parse(raw);
            return node switch
            {
                JsonObject => node,
                null => new JsonObject(),
                _ => new JsonObject { ["value"] = node.DeepClone() }
            };
        }
        catch (JsonException)
        {
            return new JsonObject { ["raw"] = raw };
        }
    }

    private static string ArgumentsText(ToolCall call) => call.Function.Arguments switch
    {
        null => "{}",
        string text => string.IsNullOrWhiteSpace(text) ? "{}" : text,
        JsonElement element => element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? "{}"
            : element.GetRawText(),
        _ => JsonSerializer.Serialize(call.Function.Arguments, JsonOptions)
    };

    private static string MessageText(ChatMessage message) => message.Content switch
    {
        null => string.Empty,
        string text => text,
        JsonElement element => element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? string.Empty
            : element.GetRawText(),
        _ => JsonSerializer.Serialize(message.Content, JsonOptions)
    };

    private static string ReadArguments(JsonElement arguments) => arguments.ValueKind switch
    {
        JsonValueKind.String => arguments.GetString() ?? "{}",
        JsonValueKind.Null or JsonValueKind.Undefined => "{}",
        _ => arguments.GetRawText()
    };

    private static ChatCompletionUsage? ParseOpenAiUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var prompt = ReadInt(usage, "prompt_tokens", "input_tokens");
        var completion = ReadInt(usage, "completion_tokens", "output_tokens");
        return new ChatCompletionUsage(prompt, completion);
    }

    private static int ReadInt(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) &&
                value.ValueKind == JsonValueKind.Number &&
                value.TryGetInt32(out var number))
            {
                return number;
            }
        }

        return 0;
    }

    private string RequireApiKey()
    {
        var apiKey = _credentials.GetApiKey(_provider);
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            return apiKey;
        }

        var environmentVariable = _provider == LlmProviderIds.Anthropic
            ? "ANTHROPIC_API_KEY"
            : "OPENAI_API_KEY";
        throw new InvalidOperationException(
            $"{environmentVariable} is not set. Export it before running provider simulation.");
    }

    private string ResolveBaseUrl()
    {
        var baseUrl = _endpoint
            ?? _credentials.GetBaseUrl(_provider)
            ?? (_provider == LlmProviderIds.Anthropic ? DefaultAnthropicBaseUrl : DefaultOpenAiBaseUrl);
        return baseUrl.TrimEnd('/');
    }

    private string DisplayName => _provider switch
    {
        LlmProviderIds.OpenAiChat => "OpenAI chat completion",
        LlmProviderIds.OpenAiResponses => "OpenAI response",
        LlmProviderIds.Anthropic => "Anthropic message",
        _ => _provider
    };

    private static string Snippet(string body) =>
        body.Length <= 500 ? body : body[..500];

    private sealed record OpenAiChatPayload(
        string Model,
        IReadOnlyList<ChatMessage> Messages,
        IReadOnlyList<ToolDefinition>? Tools,
        double Temperature,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("tool_choice")] string? ToolChoice);

    private sealed record OpenAiResponsesPayload(
        string Model,
        string? Instructions,
        List<object> Input,
        object[]? Tools,
        double Temperature,
        [property: JsonPropertyName("max_output_tokens")] int MaxOutputTokens);

    private sealed record AnthropicPayload(
        string Model,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        double Temperature,
        string? System,
        List<AnthropicOutboundMessage> Messages,
        object[]? Tools);

    private sealed record AnthropicOutboundMessage(string Role, object Content);
}
