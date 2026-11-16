using System.Net.Http;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services.Executors;
using LMTrainingDataStudio2.Services.Simulation;

namespace LMTrainingDataStudio2.Tests;

/// <summary>
/// Live provider smoke tests. They are skipped unless the following environment variables are set:
/// LMTDS_PROVIDER_SMOKE_KEY, LMTDS_PROVIDER_SMOKE_BASE_URL, LMTDS_PROVIDER_SMOKE_MODEL.
/// Never put API keys in source control.
/// </summary>
[TestFixture]
public sealed class ProviderSmokeTests
{
    [Test]
    public async Task OpenAiChat_RealProvider_ReturnsToolCallAndUsage()
    {
        if (!TryReadSettings(out var settings))
        {
            Assert.Ignore(
                "Set LMTDS_PROVIDER_SMOKE_KEY, LMTDS_PROVIDER_SMOKE_BASE_URL and " +
                "LMTDS_PROVIDER_SMOKE_MODEL to run the live provider smoke test.");
            return;
        }

        var config = CreateConfig(settings);
        var client = new ProviderChatCompletionClient(
            new HttpClient(),
            new EnvCredentials(settings.Key),
            config);
        var request = new ChatCompletionRequest(
            settings.Model,
            new[]
            {
                new ChatMessage { Role = "system", Content = "You are a tool-using agent." },
                new ChatMessage
                {
                    Role = "user",
                    Content = "Use the python tool to calculate 6*7, then report the result."
                }
            },
            new[] { PythonToolDefinition() },
            Temperature: 0.2,
            MaxTokens: 300,
            Round: 1);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var response = await client.CompleteAsync(request, cancellation.Token);

        TestContext.Progress.WriteLine(
            $"provider={settings.BaseUrl} model={settings.Model} " +
            $"finish={response.FinishReason} " +
            $"usage={response.Usage?.PromptTokens}+{response.Usage?.CompletionTokens} " +
            $"text={response.Message.Content} " +
            $"calls={response.Message.ToolCalls?.Count ?? 0}");

        Assert.That(response.Message.ToolCalls, Is.Not.Null.And.Count.GreaterThan(0));
        Assert.That(response.Message.ToolCalls![0].Function.Name, Is.EqualTo("python"));
        Assert.That(response.Message.ToolCalls[0].Function.Arguments, Does.Contain("print"));
        Assert.That(response.Usage, Is.Not.Null);
        Assert.That(response.Usage!.TotalTokens, Is.GreaterThan(0));
    }

    [Test]
    public async Task SimulationHarness_RealProvider_CompletesToolRoundTrip()
    {
        if (!TryReadSettings(out var settings))
        {
            Assert.Ignore(
                "Set LMTDS_PROVIDER_SMOKE_KEY, LMTDS_PROVIDER_SMOKE_BASE_URL and " +
                "LMTDS_PROVIDER_SMOKE_MODEL to run the live provider smoke test.");
            return;
        }

        var config = CreateConfig(settings);
        var harness = new SimulationHarness(
            new ProviderChatCompletionClient(
                new HttpClient(),
                new EnvCredentials(settings.Key),
                config),
            new SimulatedToolRuntime((_, _) => Task.CompletedTask),
            (_, _) => Task.CompletedTask);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var trajectory = await harness.RunAsync(
            "Use the python tool to calculate 6*7, then report the result.",
            config,
            log: message => TestContext.Progress.WriteLine(message),
            cancellationToken: cancellation.Token);

        TestContext.Progress.WriteLine(
            $"status={trajectory.Status} rounds={trajectory.Rounds} " +
            $"toolCalls={trajectory.ToolCallCount} modelCalls={trajectory.ModelCalls} " +
            $"tokens={trajectory.PromptTokens}+{trajectory.CompletionTokens} " +
            $"latency={trajectory.ModelLatency.TotalSeconds:F2}s");

        Assert.That(trajectory.Status, Is.EqualTo(TrajectoryStatus.Completed));
        Assert.That(trajectory.ToolCallCount, Is.GreaterThan(0));
        Assert.That(trajectory.Messages.Any(message => message.Role == "tool"), Is.True);
        Assert.That(trajectory.Messages[^1].Role, Is.EqualTo("assistant"));
        Assert.That(trajectory.Messages[^1].Content?.ToString(), Is.Not.Empty);
        Assert.That(trajectory.ModelCalls, Is.GreaterThanOrEqualTo(2));
        Assert.That(trajectory.PromptTokens + trajectory.CompletionTokens, Is.GreaterThan(0));

        using var document = System.Text.Json.JsonDocument.Parse(
            TrajectorySerializer.ToOaiJson(trajectory));
        Assert.That(document.RootElement.GetProperty("tools")[0]
            .GetProperty("function").GetProperty("name").GetString(), Is.EqualTo("python"));
    }

    private static SimulationBlockConfig CreateConfig(SmokeSettings settings) => new(
        settings.Model,
        MaxRounds: 4,
        SystemPrompt: "You are a tool-using agent.",
        Tools: new[]
        {
            new SimulatedToolRule
            {
                Name = "python",
                Description = "Execute Python source code and return its output.",
                ParametersJson =
                    """{"type":"object","properties":{"code":{"type":"string"}},"required":["code"]}""",
                ResponseTemplate = "[simulated python] executed: {{arguments}}"
            }
        },
        Rules: new ToolHarnessRules { DefaultTimeoutMs = 60_000, MaxRetries = 0 },
        FinalContent: "Task completed.",
        Provider: LlmProviderIds.OpenAiChat,
        Temperature: 0.2,
        MaxTokens: 300,
        TimeoutSeconds: 60,
        ModelMode: SimulationModelMode.Provider,
        Endpoint: settings.BaseUrl);

    private static ToolDefinition PythonToolDefinition() => new()
    {
        Function = new ToolFunction
        {
            Name = "python",
            Description = "Execute Python source code and return its output.",
            Parameters = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["code"] = new Dictionary<string, object> { ["type"] = "string" }
                },
                ["required"] = new[] { "code" }
            }
        }
    };

    private static bool TryReadSettings(out SmokeSettings settings)
    {
        var key = Environment.GetEnvironmentVariable("LMTDS_PROVIDER_SMOKE_KEY");
        var baseUrl = Environment.GetEnvironmentVariable("LMTDS_PROVIDER_SMOKE_BASE_URL");
        var model = Environment.GetEnvironmentVariable("LMTDS_PROVIDER_SMOKE_MODEL");

        if (string.IsNullOrWhiteSpace(key) ||
            string.IsNullOrWhiteSpace(baseUrl) ||
            string.IsNullOrWhiteSpace(model))
        {
            settings = default;
            return false;
        }

        settings = new SmokeSettings(key.Trim(), baseUrl.Trim().TrimEnd('/'), model.Trim());
        return true;
    }

    private readonly record struct SmokeSettings(string Key, string BaseUrl, string Model);

    private sealed class EnvCredentials(string key) : ILlmCredentials
    {
        public string? GetApiKey(string provider) => key;

        public string? GetBaseUrl(string provider) => null;
    }
}
