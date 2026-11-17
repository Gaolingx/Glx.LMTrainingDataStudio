using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services.Executors;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class LlmApiClientTests
{
    [Test]
    public async Task OpenAiChat_SendsExpectedRequestAndParsesContent()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        var client = CreateClient((request, content) =>
        {
            captured = request;
            body = content;
            return JsonResponse(HttpStatusCode.OK, """{"choices":[{"message":{"content":"hello"}}]}""");
        });

        var result = await client.CompleteAsync(
            "Say hi", CreateConfig(LlmProviderIds.OpenAiChat, "gpt-4o"));

        Assert.That(result, Is.EqualTo("hello"));
        Assert.That(captured!.RequestUri!.ToString(), Is.EqualTo("https://api.openai.com/v1/chat/completions"));
        Assert.That(captured.Headers.Authorization!.Scheme, Is.EqualTo("Bearer"));
        Assert.That(captured.Headers.Authorization.Parameter, Is.EqualTo("test-key"));

        using var document = JsonDocument.Parse(body!);
        var root = document.RootElement;
        Assert.That(root.GetProperty("model").GetString(), Is.EqualTo("gpt-4o"));
        Assert.That(root.GetProperty("max_tokens").GetInt32(), Is.EqualTo(2048));
        Assert.That(root.GetProperty("temperature").GetDouble(), Is.EqualTo(0.2));
        Assert.That(root.GetProperty("messages")[0].GetProperty("role").GetString(), Is.EqualTo("user"));
        Assert.That(root.GetProperty("messages")[0].GetProperty("content").GetString(), Is.EqualTo("Say hi"));
    }

    [Test]
    public async Task OpenAiChat_LegacyOpenAiAlias_UsesChatCompletions()
    {
        HttpRequestMessage? captured = null;
        var client = CreateClient((request, _) =>
        {
            captured = request;
            return JsonResponse(HttpStatusCode.OK, """{"choices":[{"message":{"content":"ok"}}]}""");
        });

        await client.CompleteAsync("hi", CreateConfig("openai"));

        Assert.That(captured!.RequestUri!.ToString(), Is.EqualTo("https://api.openai.com/v1/chat/completions"));
    }

    [Test]
    public async Task OpenAiChat_UsesConfiguredEndpoint()
    {
        HttpRequestMessage? captured = null;
        var client = CreateClient((request, _) =>
        {
            captured = request;
            return JsonResponse(HttpStatusCode.OK, """{"choices":[{"message":{"content":"ok"}}]}""");
        });
        var config = CreateConfig(LlmProviderIds.OpenAiChat) with { Endpoint = "https://proxy.example/v1/" };

        await client.CompleteAsync("hi", config);

        Assert.That(captured!.RequestUri!.ToString(), Is.EqualTo("https://proxy.example/v1/chat/completions"));
    }

    [Test]
    public async Task OpenAiResponses_SendsExpectedRequestAndParsesOutput()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        var client = CreateClient((request, content) =>
        {
            captured = request;
            body = content;
            return JsonResponse(
                HttpStatusCode.OK,
                """{"output":[{"content":[{"type":"output_text","text":"answer"}]}]}""");
        });

        var result = await client.CompleteAsync(
            "Explain", CreateConfig(LlmProviderIds.OpenAiResponses, "gpt-4o-mini"));

        Assert.That(result, Is.EqualTo("answer"));
        Assert.That(captured!.RequestUri!.ToString(), Is.EqualTo("https://api.openai.com/v1/responses"));
        using var document = JsonDocument.Parse(body!);
        Assert.That(document.RootElement.GetProperty("model").GetString(), Is.EqualTo("gpt-4o-mini"));
        Assert.That(document.RootElement.GetProperty("input")[0].GetProperty("role").GetString(), Is.EqualTo("user"));
        Assert.That(document.RootElement.GetProperty("input")[0].GetProperty("content").GetString(), Is.EqualTo("Explain"));
        Assert.That(document.RootElement.GetProperty("max_output_tokens").GetInt32(), Is.EqualTo(2048));
    }

    [Test]
    public async Task Anthropic_SendsExpectedRequestAndParsesText()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        var client = CreateClient((request, content) =>
        {
            captured = request;
            body = content;
            return JsonResponse(
                HttpStatusCode.OK,
                """{"content":[{"type":"text","text":"claude answer"}]}""");
        });

        var result = await client.CompleteAsync(
            "Hello", CreateConfig(LlmProviderIds.Anthropic, "claude-sonnet-4-5"));

        Assert.That(result, Is.EqualTo("claude answer"));
        Assert.That(captured!.RequestUri!.ToString(), Is.EqualTo("https://api.anthropic.com/v1/messages"));
        Assert.That(captured.Headers.GetValues("x-api-key").Single(), Is.EqualTo("test-key"));
        Assert.That(captured.Headers.GetValues("anthropic-version").Single(), Is.EqualTo("2023-06-01"));

        using var document = JsonDocument.Parse(body!);
        var root = document.RootElement;
        Assert.That(root.GetProperty("model").GetString(), Is.EqualTo("claude-sonnet-4-5"));
        Assert.That(root.GetProperty("max_tokens").GetInt32(), Is.EqualTo(2048));
        Assert.That(root.GetProperty("messages")[0].GetProperty("content").GetString(), Is.EqualTo("Hello"));
    }

    [Test]
    public async Task OpenAiChat_SerializesToolCallsAndToolResults()
    {
        string? body = null;
        var client = CreateClient((_, content) =>
        {
            body = content;
            return JsonResponse(HttpStatusCode.OK, """{"choices":[{"message":{"content":"ok"}}]}""");
        });

        await client.CompleteAsync(
            CreateToolConversation(),
            CreateConfig(LlmProviderIds.OpenAiChat));

        using var document = JsonDocument.Parse(body!);
        var messages = document.RootElement.GetProperty("messages");
        Assert.That(messages[1].GetProperty("role").GetString(), Is.EqualTo("assistant"));
        var call = messages[1].GetProperty("tool_calls")[0];
        Assert.That(call.GetProperty("id").GetString(), Is.EqualTo("call_1"));
        Assert.That(call.GetProperty("function").GetProperty("name").GetString(), Is.EqualTo("python"));
        Assert.That(call.GetProperty("function").GetProperty("arguments").GetString(),
            Is.EqualTo("""{"code":"1"}"""));
        Assert.That(messages[2].GetProperty("role").GetString(), Is.EqualTo("tool"));
        Assert.That(messages[2].GetProperty("tool_call_id").GetString(), Is.EqualTo("call_1"));
        Assert.That(messages[2].GetProperty("content").GetString(), Is.EqualTo("done"));
    }

    [Test]
    public async Task OpenAiResponses_SerializesFunctionCallItemsAndOutputs()
    {
        string? body = null;
        var client = CreateClient((_, content) =>
        {
            body = content;
            return JsonResponse(
                HttpStatusCode.OK,
                """{"output":[{"content":[{"type":"output_text","text":"done"}]}]}""");
        });

        await client.CompleteAsync(
            CreateToolConversation(),
            CreateConfig(LlmProviderIds.OpenAiResponses));

        using var document = JsonDocument.Parse(body!);
        var input = document.RootElement.GetProperty("input");
        Assert.That(input[0].GetProperty("role").GetString(), Is.EqualTo("user"));
        Assert.That(input[1].GetProperty("type").GetString(), Is.EqualTo("function_call"));
        Assert.That(input[1].GetProperty("call_id").GetString(), Is.EqualTo("call_1"));
        Assert.That(input[1].GetProperty("name").GetString(), Is.EqualTo("python"));
        Assert.That(input[2].GetProperty("type").GetString(), Is.EqualTo("function_call_output"));
        Assert.That(input[2].GetProperty("call_id").GetString(), Is.EqualTo("call_1"));
        Assert.That(input[2].GetProperty("output").GetString(), Is.EqualTo("done"));
    }

    [Test]
    public async Task Anthropic_SerializesToolUseAndToolResultBlocks()
    {
        string? body = null;
        var client = CreateClient((_, content) =>
        {
            body = content;
            return JsonResponse(
                HttpStatusCode.OK,
                """{"content":[{"type":"text","text":"done"}]}""");
        });

        await client.CompleteAsync(
            CreateToolConversation(),
            CreateConfig(LlmProviderIds.Anthropic));

        using var document = JsonDocument.Parse(body!);
        var messages = document.RootElement.GetProperty("messages");
        Assert.That(messages[0].GetProperty("role").GetString(), Is.EqualTo("user"));
        Assert.That(messages[1].GetProperty("role").GetString(), Is.EqualTo("assistant"));
        var toolUse = messages[1].GetProperty("content")[0];
        Assert.That(toolUse.GetProperty("type").GetString(), Is.EqualTo("tool_use"));
        Assert.That(toolUse.GetProperty("id").GetString(), Is.EqualTo("call_1"));
        Assert.That(toolUse.GetProperty("name").GetString(), Is.EqualTo("python"));
        Assert.That(toolUse.GetProperty("input").GetProperty("code").GetString(), Is.EqualTo("1"));

        Assert.That(messages[2].GetProperty("role").GetString(), Is.EqualTo("user"));
        var toolResult = messages[2].GetProperty("content")[0];
        Assert.That(toolResult.GetProperty("type").GetString(), Is.EqualTo("tool_result"));
        Assert.That(toolResult.GetProperty("tool_use_id").GetString(), Is.EqualTo("call_1"));
        Assert.That(toolResult.GetProperty("content").GetString(), Is.EqualTo("done"));
    }

    [Test]
    public async Task Anthropic_StructuredOutput_UsesForcedToolAndReturnsInputJson()
    {
        string? body = null;
        var client = CreateClient((_, content) =>
        {
            body = content;
            return JsonResponse(
                HttpStatusCode.OK,
                """
                {"content":[{"type":"tool_use","id":"toolu_1","name":"structured_output","input":{"ok":true}}],
                 "usage":{"input_tokens":3,"output_tokens":5}}
                """);
        });
        var request = new LlmCompletionRequest(
            [new LlmChatTurn("user", "Return JSON")],
            Tools: null,
            ResponseSchema: """{"type":"object","required":["ok"]}""");

        var result = await client.CompleteAsync(
            request, CreateConfig(LlmProviderIds.Anthropic));

        using (var document = JsonDocument.Parse(body!))
        {
            var root = document.RootElement;
            var tool = root.GetProperty("tools")[0];
            Assert.That(tool.GetProperty("name").GetString(), Is.EqualTo("structured_output"));
            Assert.That(tool.GetProperty("input_schema").GetProperty("type").GetString(),
                Is.EqualTo("object"));
            Assert.That(root.GetProperty("tool_choice").GetProperty("type").GetString(), Is.EqualTo("tool"));
            Assert.That(root.GetProperty("tool_choice").GetProperty("name").GetString(),
                Is.EqualTo("structured_output"));
        }

        using var resultDocument = JsonDocument.Parse(result.Content);
        Assert.That(resultDocument.RootElement.GetProperty("ok").GetBoolean(), Is.True);
        Assert.That(result.ToolCalls, Is.Null);
        Assert.That(result.Usage!.PromptTokens, Is.EqualTo(3));
        Assert.That(result.Usage.CompletionTokens, Is.EqualTo(5));
    }

    private static LlmCompletionRequest CreateToolConversation() => new(
    [
        new LlmChatTurn("user", "hi"),
        new LlmChatTurn("assistant", string.Empty, ToolCalls:
            [new LlmToolCall("call_1", "python", """{"code":"1"}""")]),
        new LlmChatTurn("tool", "done", ToolCallId: "call_1")
    ]);

    [Test]
    public void MissingApiKey_ThrowsWithEnvironmentVariableName()
    {
        var client = CreateClient(
            (_, _) => JsonResponse(HttpStatusCode.OK, "{}"),
            apiKey: null);

        var ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await client.CompleteAsync("hi", CreateConfig(LlmProviderIds.OpenAiChat)));

        Assert.That(ex!.Message, Does.Contain("OPENAI_API_KEY"));
    }

    [Test]
    public void ErrorResponse_ThrowsWithStatusAndBody()
    {
        var client = CreateClient((_, _) =>
            JsonResponse(HttpStatusCode.TooManyRequests, """{"error":{"message":"rate limited"}}"""));

        var ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await client.CompleteAsync("hi", CreateConfig(LlmProviderIds.OpenAiChat, maxRetries: 0)));

        Assert.That(ex!.Message, Does.Contain("429"));
        Assert.That(ex.Message, Does.Contain("rate limited"));
    }

    [Test]
    public async Task TransientServerError_IsRetried()
    {
        var calls = 0;
        var delays = new List<TimeSpan>();
        var client = CreateClient((_, _) =>
        {
            calls++;
            return calls == 1
                ? JsonResponse(HttpStatusCode.InternalServerError, "boom")
                : JsonResponse(HttpStatusCode.OK, """{"choices":[{"message":{"content":"ok"}}]}""");
        }, delays: delays);

        var result = await client.CompleteAsync(
            "hi", CreateConfig(LlmProviderIds.OpenAiChat, maxRetries: 2));

        Assert.That(result, Is.EqualTo("ok"));
        Assert.That(calls, Is.EqualTo(2));
        Assert.That(delays, Has.Count.EqualTo(1));
        Assert.That(delays[0], Is.EqualTo(TimeSpan.FromMilliseconds(500)));
    }

    [Test]
    public void RetriesExhausted_ThrowsAfterConfiguredAttempts()
    {
        var calls = 0;
        var client = CreateClient((_, _) =>
        {
            calls++;
            return JsonResponse(HttpStatusCode.InternalServerError, "boom");
        }, delays: new List<TimeSpan>());

        var ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await client.CompleteAsync(
                "hi", CreateConfig(LlmProviderIds.OpenAiChat, maxRetries: 1)));

        Assert.That(calls, Is.EqualTo(2));
        Assert.That(ex!.Message, Does.Contain("failed after 2 attempt(s)"));
        Assert.That(ex.Message, Does.Contain("500"));
    }

    [Test]
    public void ClientError_IsNotRetried()
    {
        var calls = 0;
        var client = CreateClient((_, _) =>
        {
            calls++;
            return JsonResponse(HttpStatusCode.BadRequest, "bad request");
        });

        var ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await client.CompleteAsync(
                "hi", CreateConfig(LlmProviderIds.OpenAiChat, maxRetries: 3)));

        Assert.That(calls, Is.EqualTo(1));
        Assert.That(ex!.Message, Does.Contain("400"));
    }

    [Test]
    public void Timeout_ThrowsAfterConfiguredTimeout()
    {
        var client = new LlmApiClient(new HttpClient(new HangingHandler()), new FakeCredentials("test-key"));

        var ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await client.CompleteAsync(
                "hi", CreateConfig(LlmProviderIds.OpenAiChat, timeoutSeconds: 1, maxRetries: 0)));

        Assert.That(ex!.Message, Does.Contain("timed out after 1s"));
    }

    [Test]
    public void UnsupportedProvider_Throws()
    {
        var client = CreateClient((_, _) => JsonResponse(HttpStatusCode.OK, "{}"));

        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await client.CompleteAsync("hi", CreateConfig("mystery")));
    }

    private static LlmApiClient CreateClient(
        Func<HttpRequestMessage, string, HttpResponseMessage> responder,
        string? apiKey = "test-key",
        List<TimeSpan>? delays = null)
        => new(
            new HttpClient(new StubHandler(responder)),
            new FakeCredentials(apiKey),
            delays is null
                ? null
                : (duration, _) =>
                {
                    delays.Add(duration);
                    return Task.CompletedTask;
                });

    private static LlmBlockConfig CreateConfig(
        string provider,
        string model = "gpt-4o",
        int timeoutSeconds = 100,
        int maxRetries = 2) =>
        new(provider, model, Temperature: 0.2, MaxTokens: 2048, Prompt: "ignored",
            Endpoint: null, TimeoutSeconds: timeoutSeconds, MaxRetries: maxRetries);

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

    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }

    private sealed class FakeCredentials(string? apiKey) : ILlmCredentials
    {
        public string? GetApiKey(string provider) => apiKey;

        public string? GetBaseUrl(string provider) => null;
    }
}
