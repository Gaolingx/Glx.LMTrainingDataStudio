using System.Text.Json;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services.Executors;
using LMTrainingDataStudio2.Services.Simulation;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class LlmToolLoopTests
{
    [Test]
    public async Task ToolCalls_AreExecutedAndFedBack_ThenFinalAnswerIsEmitted()
    {
        var toolCalls = new[] { new LlmToolCall("call_1", "echo", """{"text":"hi"}""") };
        var client = new ScriptedLlmClient(
            new LlmCompletionResult(string.Empty, toolCalls, new LlmTokenUsage(10, 2)),
            new LlmCompletionResult("final answer", null, new LlmTokenUsage(20, 4)));
        var logs = new List<string>();
        var block = CreateLlmBlock(new LlmBlockConfig(
            "openai_chat", "gpt-4o", Temperature: 0.2, MaxTokens: 128,
            Prompt: "Rewrite: {{ content }}", MaxToolRounds: 2));

        var executor = new LlmBlockExecutor(client);
        var result = await executor.ExecuteAsync(
            CreateContext(block, CreateProfile("echo"), new[] { new DatasetRow(0, """{"content":"hello"}""") }, logs),
            CancellationToken.None);
        var rows = await CollectAsync(result.Output);

        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(client.Requests, Has.Count.EqualTo(2));

        var followUp = client.Requests[1].Messages;
        Assert.That(followUp[^1].Role, Is.EqualTo("tool"));
        Assert.That(followUp[^1].ToolCallId, Is.EqualTo("call_1"));
        Assert.That(followUp[^1].Content, Does.Contain("echo"));
        Assert.That(followUp[^2].Role, Is.EqualTo("assistant"));
        Assert.That(followUp[^2].ToolCalls![0].Name, Is.EqualTo("echo"));

        using var document = JsonDocument.Parse(rows[0].Json);
        var messages = document.RootElement.GetProperty("messages");
        Assert.That(
            messages.EnumerateArray().Select(message => message.GetProperty("role").GetString()).ToArray(),
            Is.EqualTo(new[] { "user", "assistant", "tool", "assistant" }));
        Assert.That(messages[2].GetProperty("tool_call_id").GetString(), Is.EqualTo("call_1"));
        Assert.That(messages[3].GetProperty("content").GetString(), Is.EqualTo("final answer"));
        Assert.That(document.RootElement.GetProperty("tools")[0]
            .GetProperty("function").GetProperty("name").GetString(), Is.EqualTo("echo"));

        Assert.That(logs.Any(log => log.Contains("tool call(s)")), Is.True);
        Assert.That(logs.Any(log => log.Contains("Tokens: 30+6")), Is.True);
    }

    [Test]
    public async Task MaxToolRoundsZero_RecordsToolCallsWithoutExecuting()
    {
        var toolCalls = new[] { new LlmToolCall("call_1", "echo", "{}") };
        var client = new ScriptedLlmClient(
            new LlmCompletionResult("done", toolCalls, null));
        var block = CreateLlmBlock(new LlmBlockConfig(
            "openai_chat", "gpt-4o", Temperature: 0.2, MaxTokens: 128, Prompt: "hi"));

        var executor = new LlmBlockExecutor(client);
        var result = await executor.ExecuteAsync(
            CreateContext(block, CreateProfile("echo"), new[] { new DatasetRow(0, "{}") }),
            CancellationToken.None);
        var rows = await CollectAsync(result.Output);

        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(client.Requests, Has.Count.EqualTo(1));
        using var document = JsonDocument.Parse(rows[0].Json);
        var messages = document.RootElement.GetProperty("messages");
        Assert.That(messages.GetArrayLength(), Is.EqualTo(1));
        Assert.That(messages[0].GetProperty("tool_calls").GetArrayLength(), Is.EqualTo(1));
    }

    [Test]
    public void ExceedingMaxToolRounds_FailsTheRow()
    {
        var toolCalls = new[] { new LlmToolCall("call_1", "echo", "{}") };
        var client = new ScriptedLlmClient(
            new LlmCompletionResult(string.Empty, toolCalls, null),
            new LlmCompletionResult(string.Empty, toolCalls, null));
        var block = CreateLlmBlock(new LlmBlockConfig(
            "openai_chat", "gpt-4o", Temperature: 0.2, MaxTokens: 128, Prompt: "hi",
            MaxToolRounds: 1));

        var executor = new LlmBlockExecutor(client);

        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            var result = await executor.ExecuteAsync(
                CreateContext(block, CreateProfile("echo"), new[] { new DatasetRow(0, "{}") }),
                CancellationToken.None);
            await CollectAsync(result.Output);
        });

        Assert.That(ex!.Message, Does.Contain("tool round"));
    }

    [Test]
    public async Task ToolFailure_IsFedBackToTheModel()
    {
        var toolCalls = new[] { new LlmToolCall("call_1", "broken", "{}") };
        var client = new ScriptedLlmClient(
            new LlmCompletionResult(string.Empty, toolCalls, null),
            new LlmCompletionResult("recovered", null, null));
        var block = CreateLlmBlock(new LlmBlockConfig(
            "openai_chat", "gpt-4o", Temperature: 0.2, MaxTokens: 128, Prompt: "hi",
            MaxToolRounds: 2));

        var executor = new LlmBlockExecutor(
            client,
            toolRuntimeFactory: _ => new ThrowingToolRuntime());
        var result = await executor.ExecuteAsync(
            CreateContext(block, CreateProfile("broken"), new[] { new DatasetRow(0, "{}") }),
            CancellationToken.None);
        var rows = await CollectAsync(result.Output);

        Assert.That(rows, Has.Count.EqualTo(1));
        var toolTurn = client.Requests[1].Messages[^1];
        Assert.That(toolTurn.Role, Is.EqualTo("tool"));
        Assert.That(toolTurn.Content, Does.Contain("boom"));
    }

    [Test]
    public async Task UnknownTool_IsFedBackAsAnError()
    {
        var toolCalls = new[] { new LlmToolCall("call_1", "missing", "{}") };
        var client = new ScriptedLlmClient(
            new LlmCompletionResult(string.Empty, toolCalls, null),
            new LlmCompletionResult("recovered", null, null));
        var block = CreateLlmBlock(new LlmBlockConfig(
            "openai_chat", "gpt-4o", Temperature: 0.2, MaxTokens: 128, Prompt: "hi",
            MaxToolRounds: 2));

        var executor = new LlmBlockExecutor(client);
        var result = await executor.ExecuteAsync(
            CreateContext(block, CreateProfile("echo"), new[] { new DatasetRow(0, "{}") }),
            CancellationToken.None);
        await CollectAsync(result.Output);

        var toolTurn = client.Requests[1].Messages[^1];
        Assert.That(toolTurn.Content, Does.Contain("not available"));
    }

    private static RecipeBlock CreateLlmBlock(LlmBlockConfig config) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Type = BlockType.Llm,
        Name = "llm",
        DisplayName = "LLM Generator",
        Config = config
    };

    private static ToolProfile CreateProfile(string toolName) => new()
    {
        Id = "tools",
        Name = "tools",
        Tools =
        {
            new ToolDefinition
            {
                Function = new ToolFunction
                {
                    Name = toolName,
                    Description = "test tool",
                    Parameters = new Dictionary<string, object>
                    {
                        ["type"] = "object",
                        ["properties"] = new Dictionary<string, object>()
                    }
                }
            }
        }
    };

    private static BlockExecutionContext CreateContext(
        RecipeBlock block,
        ToolProfile profile,
        IEnumerable<DatasetRow> rows,
        List<string>? logs = null) => new()
        {
            Block = block,
            Input = ToAsync(rows),
            ToolProfile = profile,
            Log = message => logs?.Add(message)
        };

    private static async IAsyncEnumerable<DatasetRow> ToAsync(IEnumerable<DatasetRow> rows)
    {
        await Task.Yield();
        foreach (var row in rows)
        {
            yield return row;
        }
    }

    private static async Task<List<DatasetRow>> CollectAsync(IAsyncEnumerable<DatasetRow> rows)
    {
        var result = new List<DatasetRow>();
        await foreach (var row in rows)
        {
            result.Add(row);
        }

        return result;
    }

    private sealed class ScriptedLlmClient : ILlmClient
    {
        private readonly Queue<LlmCompletionResult> _responses;

        public ScriptedLlmClient(params LlmCompletionResult[] responses) =>
            _responses = new Queue<LlmCompletionResult>(responses);

        public List<LlmCompletionRequest> Requests { get; } = new();

        public Task<string> CompleteAsync(
            string prompt, LlmBlockConfig config, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<LlmCompletionResult> CompleteAsync(
            LlmCompletionRequest request,
            LlmBlockConfig config,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(_responses.Dequeue());
        }
    }

    private sealed class ThrowingToolRuntime : IToolRuntime
    {
        public Task<string> ExecuteAsync(
            SimulatedToolRule rule,
            ToolCall toolCall,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("boom");
    }
}
