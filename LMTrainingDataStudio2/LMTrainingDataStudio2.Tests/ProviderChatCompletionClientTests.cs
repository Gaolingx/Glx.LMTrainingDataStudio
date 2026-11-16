using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services.Executors;
using LMTrainingDataStudio2.Services.Simulation;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class ProviderChatCompletionClientTests
{
    [Test]
    public async Task OpenAiChat_WithTools_SendsSchemasAndParsesToolCalls()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        var client = CreateClient((request, content) =>
        {
            captured = request;
            body = content;
            return JsonResponse(HttpStatusCode.OK, """
                {
                  "choices": [
                    {
                      "message": {
                        "role": "assistant",
                        "content": null,
                        "tool_calls": [
                          {
                            "id": "call_1",
                            "type": "function",
                            "function": { "name": "python", "arguments": "{\"code\":\"print(1)\"}" }
                          }
                        ]
                      },
                      "finish_reason": "tool_calls"
                    }
                  ],
                  "usage": { "prompt_tokens": 10, "completion_tokens": 4 }
                }
                """);
        });

        var response = await client.CompleteAsync(CreateRequest(tools: PythonTools()), CancellationToken.None);

        Assert.That(captured!.RequestUri!.ToString(), Is.EqualTo("https://api.openai.com/v1/chat/completions"));
        Assert.That(captured.Headers.Authorization!.Parameter, Is.EqualTo("test-key"));

        using var document = JsonDocument.Parse(body!);
        var root = document.RootElement;
        Assert.That(root.GetProperty("model").GetString(), Is.EqualTo("gpt-test"));
        Assert.That(root.GetProperty("max_tokens").GetInt32(), Is.EqualTo(512));
        Assert.That(root.GetProperty("tool_choice").GetString(), Is.EqualTo("auto"));
        Assert.That(root.GetProperty("tools")[0].GetProperty("type").GetString(), Is.EqualTo("function"));
        Assert.That(
            root.GetProperty("tools")[0].GetProperty("function").GetProperty("name").GetString(),
            Is.EqualTo("python"));
        Assert.That(root.GetProperty("messages")[0].GetProperty("role").GetString(), Is.EqualTo("system"));
        Assert.That(root.GetProperty("messages")[1].GetProperty("content").GetString(), Is.EqualTo("hello"));

        Assert.That(response.Message.Role, Is.EqualTo("assistant"));
        Assert.That(response.Message.Content, Is.Null);
        Assert.That(response.Message.ToolCalls, Has.Count.EqualTo(1));
        Assert.That(response.Message.ToolCalls![0].Id, Is.EqualTo("call_1"));
        Assert.That(response.Message.ToolCalls[0].Function.Name, Is.EqualTo("python"));
        Assert.That(response.Message.ToolCalls[0].Function.Arguments,
            Is.EqualTo("""{"code":"print(1)"}"""));
        Assert.That(response.FinishReason, Is.EqualTo("tool_calls"));
        Assert.That(response.Usage!.PromptTokens, Is.EqualTo(10));
        Assert.That(response.Usage.CompletionTokens, Is.EqualTo(4));
        Assert.That(response.Usage.TotalTokens, Is.EqualTo(14));
    }

    [Test]
    public async Task OpenAiChat_WithoutTools_OmitsToolPayload()
    {
        string? body = null;
        var client = CreateClient((_, content) =>
        {
            body = content;
            return JsonResponse(HttpStatusCode.OK, """{"choices":[{"message":{"content":"plain"}}]}""");
        });

        var response = await client.CompleteAsync(CreateRequest(), CancellationToken.None);

        using var document = JsonDocument.Parse(body!);
        Assert.That(document.RootElement.TryGetProperty("tools", out _), Is.False);
        Assert.That(document.RootElement.TryGetProperty("tool_choice", out _), Is.False);
        Assert.That(response.Message.Content, Is.EqualTo("plain"));
        Assert.That(response.Message.ToolCalls, Is.Null);
    }

    [Test]
    public async Task OpenAiResponses_SendsFunctionItemsAndParsesFunctionCall()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        var client = CreateClient(
            (request, content) =>
            {
                captured = request;
                body = content;
                return JsonResponse(HttpStatusCode.OK, """
                    {
                      "status": "completed",
                      "output": [
                        {
                          "type": "message",
                          "role": "assistant",
                          "content": [ { "type": "output_text", "text": "final answer" } ]
                        },
                        {
                          "type": "function_call",
                          "call_id": "call_9",
                          "name": "web_fetch",
                          "arguments": "{\"url\":\"https://example.com\"}"
                        }
                      ],
                      "usage": { "input_tokens": 20, "output_tokens": 8 }
                    }
                    """);
            },
            provider: LlmProviderIds.OpenAiResponses);

        var messages = new List<ChatMessage>
        {
            new() { Role = "system", Content = "rules" },
            new() { Role = "user", Content = "go" },
            new()
            {
                Role = "assistant",
                Content = "working",
                ToolCalls = new List<ToolCall>
                {
                    Call("call_9", "web_fetch", """{"url":"https://example.com"}""")
                }
            },
            new() { Role = "tool", ToolCallId = "call_9", Content = "page" }
        };
        var request = new ChatCompletionRequest(
            "gpt-test", messages, PythonTools(), 0.2, 512, 2);

        var response = await client.CompleteAsync(request, CancellationToken.None);

        Assert.That(captured!.RequestUri!.ToString(), Is.EqualTo("https://api.openai.com/v1/responses"));
        using var document = JsonDocument.Parse(body!);
        var root = document.RootElement;
        Assert.That(root.GetProperty("instructions").GetString(), Is.EqualTo("rules"));
        Assert.That(root.GetProperty("max_output_tokens").GetInt32(), Is.EqualTo(512));
        Assert.That(root.GetProperty("tools")[0].GetProperty("name").GetString(), Is.EqualTo("python"));
        Assert.That(root.GetProperty("tools")[0].TryGetProperty("function", out _), Is.False);

        var input = root.GetProperty("input");
        Assert.That(input.EnumerateArray().Any(item =>
            item.GetProperty("type").GetString() == "function_call"), Is.True);
        Assert.That(input.EnumerateArray().Any(item =>
            item.GetProperty("type").GetString() == "function_call_output"), Is.True);

        Assert.That(response.Message.Content, Is.EqualTo("final answer"));
        Assert.That(response.Message.ToolCalls, Has.Count.EqualTo(1));
        Assert.That(response.Message.ToolCalls![0].Id, Is.EqualTo("call_9"));
        Assert.That(response.Message.ToolCalls[0].Function.Arguments,
            Is.EqualTo("""{"url":"https://example.com"}"""));
        Assert.That(response.FinishReason, Is.EqualTo("completed"));
        Assert.That(response.Usage!.PromptTokens, Is.EqualTo(20));
        Assert.That(response.Usage.CompletionTokens, Is.EqualTo(8));
    }

    [Test]
    public async Task Anthropic_SendsToolUseAndMergesToolResults()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        var client = CreateClient(
            (request, content) =>
            {
                captured = request;
                body = content;
                return JsonResponse(HttpStatusCode.OK, """
                    {
                      "stop_reason": "tool_use",
                      "content": [
                        { "type": "text", "text": "calling" },
                        { "type": "tool_use", "id": "toolu_1", "name": "bash", "input": { "command": "ls" } }
                      ],
                      "usage": { "input_tokens": 30, "output_tokens": 12 }
                    }
                    """);
            },
            provider: LlmProviderIds.Anthropic);

        var messages = new List<ChatMessage>
        {
            new() { Role = "system", Content = "be careful" },
            new() { Role = "user", Content = "list files" },
            new()
            {
                Role = "assistant",
                Content = string.Empty,
                ToolCalls = new List<ToolCall>
                {
                    Call("call_1", "bash", """{"command":"ls"}"""),
                    Call("call_2", "bash", """{"command":"pwd"}""")
                }
            },
            new() { Role = "tool", ToolCallId = "call_1", Content = "a.txt" },
            new() { Role = "tool", ToolCallId = "call_2", Content = "/tmp" }
        };
        var request = new ChatCompletionRequest(
            "claude-test", messages, Tools("bash"), 0.3, 1024, 1);

        var response = await client.CompleteAsync(request, CancellationToken.None);

        Assert.That(captured!.RequestUri!.ToString(), Is.EqualTo("https://api.anthropic.com/v1/messages"));
        Assert.That(captured.Headers.GetValues("x-api-key").Single(), Is.EqualTo("test-key"));
        Assert.That(captured.Headers.GetValues("anthropic-version").Single(), Is.EqualTo("2023-06-01"));

        using var document = JsonDocument.Parse(body!);
        var root = document.RootElement;
        Assert.That(root.GetProperty("system").GetString(), Is.EqualTo("be careful"));
        Assert.That(root.GetProperty("max_tokens").GetInt32(), Is.EqualTo(1024));
        Assert.That(root.GetProperty("tools")[0].GetProperty("name").GetString(), Is.EqualTo("bash"));
        Assert.That(
            root.GetProperty("tools")[0].GetProperty("input_schema").GetProperty("type").GetString(),
            Is.EqualTo("object"));

        var outbound = root.GetProperty("messages");
        Assert.That(outbound.GetArrayLength(), Is.EqualTo(3));
        Assert.That(outbound[1].GetProperty("role").GetString(), Is.EqualTo("assistant"));
        var assistantBlocks = outbound[1].GetProperty("content");
        Assert.That(assistantBlocks.GetArrayLength(), Is.EqualTo(2));
        Assert.That(assistantBlocks[0].GetProperty("type").GetString(), Is.EqualTo("tool_use"));
        Assert.That(
            assistantBlocks[0].GetProperty("input").GetProperty("command").GetString(),
            Is.EqualTo("ls"));
        Assert.That(
            assistantBlocks[1].GetProperty("input").GetProperty("command").GetString(),
            Is.EqualTo("pwd"));

        var toolResultMessage = outbound[2];
        Assert.That(toolResultMessage.GetProperty("role").GetString(), Is.EqualTo("user"));
        var toolResults = toolResultMessage.GetProperty("content");
        Assert.That(toolResults.GetArrayLength(), Is.EqualTo(2));
        Assert.That(toolResults[0].GetProperty("type").GetString(), Is.EqualTo("tool_result"));
        Assert.That(toolResults[1].GetProperty("tool_use_id").GetString(), Is.EqualTo("call_2"));

        Assert.That(response.Message.Content, Is.EqualTo("calling"));
        Assert.That(response.Message.ToolCalls, Has.Count.EqualTo(1));
        Assert.That(response.Message.ToolCalls![0].Id, Is.EqualTo("toolu_1"));
        Assert.That(response.Message.ToolCalls[0].Function.Name, Is.EqualTo("bash"));
        Assert.That(response.Message.ToolCalls[0].Function.Arguments, Does.Contain("\"ls\""));
        Assert.That(response.FinishReason, Is.EqualTo("tool_use"));
        Assert.That(response.Usage!.PromptTokens, Is.EqualTo(30));
        Assert.That(response.Usage.CompletionTokens, Is.EqualTo(12));
    }

    [Test]
    public void MissingApiKey_ThrowsWithEnvironmentVariableName()
    {
        var client = CreateClient(
            (_, _) => JsonResponse(HttpStatusCode.OK, "{}"),
            apiKey: null);

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await client.CompleteAsync(CreateRequest(), CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("OPENAI_API_KEY"));
    }

    [Test]
    public void MissingAnthropicKey_ThrowsWithAnthropicVariable()
    {
        var client = CreateClient(
            (_, _) => JsonResponse(HttpStatusCode.OK, "{}"),
            apiKey: null,
            provider: LlmProviderIds.Anthropic);

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await client.CompleteAsync(CreateRequest(), CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("ANTHROPIC_API_KEY"));
    }

    [Test]
    public void HttpError_IncludesStatusAndBody()
    {
        var client = CreateClient((_, _) =>
            JsonResponse(HttpStatusCode.TooManyRequests, """{"error":{"message":"rate limited"}}"""));

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await client.CompleteAsync(CreateRequest(), CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("429"));
        Assert.That(exception.Message, Does.Contain("rate limited"));
    }

    [Test]
    public void UnknownProvider_ConstructorThrows()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            _ = CreateClient(
                (_, _) => JsonResponse(HttpStatusCode.OK, "{}"),
                provider: "mystery");
        });

        Assert.That(exception!.Message, Does.Contain("not supported"));
    }

    [Test]
    public async Task Harness_WithProviderClient_RecordsToolRoundTripAndUsage()
    {
        var calls = 0;
        var client = CreateClient((_, _) =>
        {
            calls++;
            return calls == 1
                ? JsonResponse(HttpStatusCode.OK, """
                    {
                      "choices": [
                        {
                          "message": {
                            "tool_calls": [
                              {
                                "id": "call_1",
                                "type": "function",
                                "function": { "name": "python", "arguments": "{\"code\":\"1\"}" }
                              }
                            ]
                          }
                        }
                      ],
                      "usage": { "prompt_tokens": 5, "completion_tokens": 2 }
                    }
                    """)
                : JsonResponse(HttpStatusCode.OK, """
                    {
                      "choices": [ { "message": { "content": "done" } } ],
                      "usage": { "prompt_tokens": 7, "completion_tokens": 3 }
                    }
                    """);
        });
        var harness = new SimulationHarness(
            client,
            new SimulatedToolRuntime((_, _) => Task.CompletedTask),
            (_, _) => Task.CompletedTask);
        var config = new SimulationBlockConfig(
            "gpt-test",
            Tools: new[] { new SimulatedToolRule { Name = "python" } },
            FinalContent: "done",
            TimeoutSeconds: 5,
            InputCostPerMillionTokens: 1_000_000,
            OutputCostPerMillionTokens: 2_000_000);

        var trajectory = await harness.RunAsync("run", config);

        Assert.That(trajectory.Status, Is.EqualTo(TrajectoryStatus.Completed));
        Assert.That(trajectory.Messages.Select(message => message.Role),
            Is.EqualTo(new[] { "user", "assistant", "tool", "assistant" }));
        Assert.That(trajectory.Messages[2].ToolCallId, Is.EqualTo("call_1"));
        Assert.That(trajectory.ToolCallCount, Is.EqualTo(1));
        Assert.That(trajectory.ModelCalls, Is.EqualTo(2));
        Assert.That(trajectory.PromptTokens, Is.EqualTo(12));
        Assert.That(trajectory.CompletionTokens, Is.EqualTo(5));
        Assert.That(trajectory.EstimatedCost, Is.EqualTo(12m + 10m));
    }

    private static ProviderChatCompletionClient CreateClient(
        Func<HttpRequestMessage, string, HttpResponseMessage> responder,
        string? apiKey = "test-key",
        string provider = LlmProviderIds.OpenAiChat,
        string? endpoint = null)
    {
        var config = new SimulationBlockConfig(
            "gpt-test",
            Provider: provider,
            Endpoint: endpoint);
        return new ProviderChatCompletionClient(
            new HttpClient(new StubHandler(responder)),
            new FakeCredentials(apiKey),
            config);
    }

    private static ChatCompletionRequest CreateRequest(ToolDefinition[]? tools = null) => new(
        "gpt-test",
        new[]
        {
            new ChatMessage { Role = "system", Content = "You are helpful." },
            new ChatMessage { Role = "user", Content = "hello" }
        },
        tools ?? Array.Empty<ToolDefinition>(),
        0.2,
        512,
        1);

    private static ToolDefinition[] PythonTools() => Tools("python");

    private static ToolDefinition[] Tools(string name) => new[]
    {
        new ToolDefinition
        {
            Function = new ToolFunction
            {
                Name = name,
                Description = "test tool",
                Parameters = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>()
                }
            }
        }
    };

    private static ToolCall Call(string id, string name, string arguments) => new()
    {
        Id = id,
        Type = "function",
        Function = new ToolCallFunction { Name = name, Arguments = arguments }
    };

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, string, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return responder(request, content);
        }
    }

    private sealed class FakeCredentials(string? apiKey) : ILlmCredentials
    {
        public string? GetApiKey(string provider) => apiKey;

        public string? GetBaseUrl(string provider) => null;
    }
}
