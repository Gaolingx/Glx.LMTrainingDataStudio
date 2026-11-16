using System.Text.Json;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services.Simulation;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class SimulationHarnessTests
{
    private static Task NoDelay(TimeSpan duration, CancellationToken cancellationToken) => Task.CompletedTask;

    [Test]
    public async Task RunAsync_NoTools_CompletesWithSystemUserAssistant()
    {
        var config = CreateConfig();

        var result = await CreateHarness().RunAsync("hello", config);

        Assert.That(result.Status, Is.EqualTo(TrajectoryStatus.Completed));
        Assert.That(result.Rounds, Is.EqualTo(1));
        Assert.That(result.Messages.Select(message => message.Role),
            Is.EqualTo(new[] { "system", "user", "assistant" }));
        Assert.That(result.Messages[2].Content, Is.EqualTo("done"));
        Assert.That(result.Tools, Is.Empty);
    }

    [Test]
    public async Task RunAsync_ScriptedToolCall_RecordsAssistantAndToolMessages()
    {
        var tools = new[]
        {
            new SimulatedToolRule
            {
                Name = "python",
                Description = "Run Python",
                ResponseTemplate = "{{name}} -> {{arguments}}"
            }
        };
        var script = new[]
        {
            new SimulatedTurn("running", new[] { new SimulatedToolCallSpec("python", """{"code":"print(1)"}""") })
        };
        var config = CreateConfig(tools: tools, script: script);

        var result = await CreateHarness(script).RunAsync("run it", config);

        Assert.That(result.Status, Is.EqualTo(TrajectoryStatus.Completed));
        Assert.That(result.Rounds, Is.EqualTo(2));
        Assert.That(result.ToolCallCount, Is.EqualTo(1));
        Assert.That(result.ToolRetryCount, Is.EqualTo(0));
        Assert.That(result.Messages.Select(message => message.Role),
            Is.EqualTo(new[] { "system", "user", "assistant", "tool", "assistant" }));

        var assistant = result.Messages[2];
        Assert.That(assistant.ToolCalls, Has.Count.EqualTo(1));
        Assert.That(assistant.ToolCalls![0].Id, Is.EqualTo("call_0_0"));

        var toolMessage = result.Messages[3];
        Assert.That(toolMessage.ToolCallId, Is.EqualTo("call_0_0"));
        Assert.That(toolMessage.Content, Is.EqualTo("""python -> {"code":"print(1)"}"""));
        Assert.That(result.Messages[4].Content, Is.EqualTo("done"));
    }

    [Test]
    public async Task RunAsync_ToolFailure_RetriesThenSucceeds()
    {
        var tools = new[]
        {
            new SimulatedToolRule { Name = "python", FailuresBeforeSuccess = 1 }
        };
        var script = new[]
        {
            new SimulatedTurn(null, new[] { new SimulatedToolCallSpec("python") })
        };
        var config = CreateConfig(
            tools: tools,
            script: script,
            rules: new ToolHarnessRules { MaxRetries = 1 });
        var logs = new List<string>();

        var result = await CreateHarness(script)
            .RunAsync("run", config, log: logs.Add);

        Assert.That(result.Status, Is.EqualTo(TrajectoryStatus.Completed));
        Assert.That(result.ToolRetryCount, Is.EqualTo(1));

        var toolMessage = result.Messages.Single(message => message.Role == "tool");
        Assert.That(toolMessage.Content, Is.EqualTo("ok"));
        Assert.That(logs, Has.Some.Contains("succeeded on attempt 2"));
    }

    [Test]
    public async Task RunAsync_ToolFailureAfterRetries_FeedsErrorBackAndContinues()
    {
        var tools = new[]
        {
            new SimulatedToolRule
            {
                Name = "python",
                Behavior = SimulatedToolBehavior.Failure,
                ErrorMessage = "sandbox exploded"
            }
        };
        var script = new[]
        {
            new SimulatedTurn(null, new[] { new SimulatedToolCallSpec("python") })
        };
        var config = CreateConfig(
            tools: tools,
            script: script,
            rules: new ToolHarnessRules { MaxRetries = 1 });

        var result = await CreateHarness(script).RunAsync("run", config);

        Assert.That(result.Status, Is.EqualTo(TrajectoryStatus.Completed));
        Assert.That(result.ToolRetryCount, Is.EqualTo(1));

        var toolMessage = result.Messages.Single(message => message.Role == "tool");
        Assert.That(toolMessage.Content, Does.Contain("sandbox exploded"));
        Assert.That(toolMessage.Content, Does.Contain("\"attempts\":2"));
        Assert.That(result.Messages[^1].Content, Is.EqualTo("done"));
    }

    [Test]
    public async Task RunAsync_ToolTimeout_IsRecordedAsFailure()
    {
        var tools = new[]
        {
            new SimulatedToolRule
            {
                Name = "web_fetch",
                Behavior = SimulatedToolBehavior.Timeout
            }
        };
        var script = new[]
        {
            new SimulatedTurn(null, new[] { new SimulatedToolCallSpec("web_fetch") })
        };
        var config = CreateConfig(
            tools: tools,
            script: script,
            rules: new ToolHarnessRules { MaxRetries = 0 });

        var result = await CreateHarness(script).RunAsync("fetch", config);

        var toolMessage = result.Messages.Single(message => message.Role == "tool");
        Assert.That(toolMessage.Content, Does.Contain("timed out"));
        Assert.That(result.Status, Is.EqualTo(TrajectoryStatus.Completed));
    }

    [Test]
    public async Task RunAsync_DisallowedTool_IsRejectedWithoutExecution()
    {
        var tools = new[]
        {
            new SimulatedToolRule { Name = "python" },
            new SimulatedToolRule { Name = "web_fetch" }
        };
        var script = new[]
        {
            new SimulatedTurn(null, new[] { new SimulatedToolCallSpec("web_fetch") })
        };
        var config = CreateConfig(
            tools: tools,
            script: script,
            rules: new ToolHarnessRules { AllowedTools = new[] { "python" } });

        var result = await CreateHarness(script).RunAsync("fetch", config);

        Assert.That(result.Tools.Select(tool => tool.Function.Name), Is.EqualTo(new[] { "python" }));
        var toolMessage = result.Messages.Single(message => message.Role == "tool");
        Assert.That(toolMessage.Content, Does.Contain("not available"));
        Assert.That(result.Status, Is.EqualTo(TrajectoryStatus.Completed));
    }

    [Test]
    public async Task RunAsync_ExceedsMaxRounds_ReturnsMaxRoundsExceeded()
    {
        var tools = new[]
        {
            new SimulatedToolRule { Name = "python", FailuresBeforeSuccess = int.MaxValue }
        };
        var script = new[]
        {
            new SimulatedTurn(null, new[] { new SimulatedToolCallSpec("python") })
        };
        var config = CreateConfig(
            tools: tools,
            script: script,
            rules: new ToolHarnessRules { MaxRetries = 0 },
            maxRounds: 1);

        var result = await CreateHarness(script).RunAsync("loop", config);

        Assert.That(result.Status, Is.EqualTo(TrajectoryStatus.MaxRoundsExceeded));
        Assert.That(result.Rounds, Is.EqualTo(1));
        Assert.That(result.Error, Does.Contain("maximum of 1 round"));
    }

    [Test]
    public async Task RunAsync_AbortOnToolFailure_ReturnsAborted()
    {
        var tools = new[]
        {
            new SimulatedToolRule { Name = "python", Behavior = SimulatedToolBehavior.Failure }
        };
        var script = new[]
        {
            new SimulatedTurn(null, new[] { new SimulatedToolCallSpec("python") })
        };
        var config = CreateConfig(
            tools: tools,
            script: script,
            rules: new ToolHarnessRules { MaxRetries = 0, AbortOnToolFailure = true });

        var result = await CreateHarness(script).RunAsync("run", config);

        Assert.That(result.Status, Is.EqualTo(TrajectoryStatus.Aborted));
        Assert.That(result.Error, Does.Contain("python"));
        Assert.That(result.Messages.Any(message => message.Role == "tool"), Is.False);
    }

    [Test]
    public async Task RunAsync_WithoutScript_CallsEveryExposedToolOnce()
    {
        var tools = new[]
        {
            new SimulatedToolRule { Name = "python" },
            new SimulatedToolRule { Name = "web_fetch" }
        };
        var config = CreateConfig(
            tools: tools,
            rules: new ToolHarnessRules { AllowedTools = new[] { "python" } });

        var result = await CreateHarness().RunAsync("go", config);

        Assert.That(result.Status, Is.EqualTo(TrajectoryStatus.Completed));
        Assert.That(result.Rounds, Is.EqualTo(2));
        Assert.That(result.Messages.Count(message => message.Role == "tool"), Is.EqualTo(1));
        Assert.That(result.Tools.Select(tool => tool.Function.Name), Is.EqualTo(new[] { "python" }));
    }

    [Test]
    public void RunAsync_Cancelled_Throws()
    {
        var config = CreateConfig();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(
            async () => await CreateHarness().RunAsync("hello", config, cancellationToken: cancellation.Token));
    }

    [Test]
    public void RunAsync_ModelTimeout_ThrowsWithAttemptCount()
    {
        var config = CreateConfig(timeoutSeconds: 1);
        var harness = new SimulationHarness(new HangingChatClient(), new SimulatedToolRuntime(NoDelay), NoDelay);

        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await harness.RunAsync("hello", config));

        Assert.That(exception!.Message, Does.Contain("failed after 1 attempt"));
        Assert.That(exception.Message, Does.Contain("timed out"));
    }

    [Test]
    public void RunAsync_InvalidToolParametersJson_Throws()
    {
        var tools = new[] { new SimulatedToolRule { Name = "python", ParametersJson = "{not-json" } };
        var config = CreateConfig(tools: tools);

        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await CreateHarness().RunAsync("hello", config));

        Assert.That(exception!.Message, Does.Contain("invalid parameters JSON"));
    }

    [Test]
    public async Task TrajectorySerializer_WritesOpenAiMessagesAndTools()
    {
        var tools = new[] { new SimulatedToolRule { Name = "python", Description = "Run Python" } };
        var script = new[]
        {
            new SimulatedTurn("calling", new[] { new SimulatedToolCallSpec("python", """{"code":"1"}""") })
        };
        var config = CreateConfig(tools: tools, script: script);
        var trajectory = await CreateHarness(script).RunAsync("hello", config);

        var json = TrajectorySerializer.ToOaiJson(trajectory);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.That(root.GetProperty("messages")[0].GetProperty("role").GetString(), Is.EqualTo("system"));
        Assert.That(root.GetProperty("tools")[0].GetProperty("function").GetProperty("name").GetString(),
            Is.EqualTo("python"));

        var toolCall = root.GetProperty("messages")[2].GetProperty("tool_calls")[0];
        Assert.That(toolCall.GetProperty("function").GetProperty("arguments").GetString(),
            Is.EqualTo("""{"code":"1"}"""));
    }

    [Test]
    public async Task TrajectorySerializer_PreservesNonAsciiText()
    {
        var trajectory = await CreateHarness().RunAsync("请分析 TCP 握手", CreateConfig());

        var json = TrajectorySerializer.ToOaiJson(trajectory);

        Assert.That(json, Does.Contain("请分析 TCP 握手"));
    }

    private static SimulationBlockConfig CreateConfig(
        SimulatedToolRule[]? tools = null,
        ToolHarnessRules? rules = null,
        SimulatedTurn[]? script = null,
        int maxRounds = 4,
        int timeoutSeconds = 5) => new(
        "sim-model",
        MaxRounds: maxRounds,
        SystemPrompt: "You are a helpful agent.",
        Tools: tools,
        Rules: rules,
        Script: script,
        FinalContent: "done",
        TimeoutSeconds: timeoutSeconds);

    private static SimulationHarness CreateHarness(IReadOnlyList<SimulatedTurn>? script = null) =>
        new(new ScriptedChatClient(script, "done"), new SimulatedToolRuntime(NoDelay), NoDelay);

    private sealed class HangingChatClient : IChatCompletionClient
    {
        public async Task<ChatCompletionResponse> CompleteAsync(
            ChatCompletionRequest request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }
}
