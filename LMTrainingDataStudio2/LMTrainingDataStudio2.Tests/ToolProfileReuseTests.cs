using System.Text.Json;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services;
using LMTrainingDataStudio2.Services.Executors;
using LMTrainingDataStudio2.ViewModels;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class ToolProfileReuseTests
{
    [Test]
    public void Resolver_MatchesBlockNameOrProfileName()
    {
        var profile = CreateProfile();
        var toolBlock = CreateBlock("tools", BlockType.ToolProfile, new ToolProfileBlockConfig(profile));
        var simulation = CreateBlock("sim", BlockType.Simulation,
            new SimulationBlockConfig("sim-model", ToolProfileName: "tools"));
        var llm = CreateBlock("llm", BlockType.Llm,
            new LlmBlockConfig("openai", "gpt-4o", 0.7, 1024, null, ToolProfileName: "Agent Tools"));

        var recipe = new Recipe { Blocks = { toolBlock, simulation, llm } };

        Assert.That(ToolProfileResolver.Resolve(recipe, simulation), Is.SameAs(profile));
        Assert.That(ToolProfileResolver.Resolve(recipe, llm), Is.SameAs(profile));
    }

    [Test]
    public async Task SimulationExecutor_UsesReferencedToolProfile()
    {
        var config = new SimulationBlockConfig(
            "sim-model",
            MaxRounds: 3,
            Tools: null,
            Rules: new ToolHarnessRules { MaxRetries = 0 },
            Script: new[]
            {
                new SimulatedTurn(null, new[] { new SimulatedToolCallSpec("python", """{"code":"print(1)"}""") }),
                new SimulatedTurn("done", null)
            },
            FinalContent: "final",
            TimeoutSeconds: 5,
            ToolProfileName: "tools");
        var block = CreateBlock("sim", BlockType.Simulation, config);
        var logs = new List<string>();

        var executor = new SimulationBlockExecutor();
        var result = await executor.ExecuteAsync(
            CreateContext(block, new[] { new DatasetRow(0, """{"query":"go"}""") }, CreateProfile(), logs),
            CancellationToken.None);
        var rows = await CollectAsync(result.Output);

        Assert.That(rows, Has.Count.EqualTo(1));
        using var document = JsonDocument.Parse(rows[0].Json);
        Assert.That(document.RootElement.GetProperty("tools")[0]
            .GetProperty("function").GetProperty("name").GetString(), Is.EqualTo("python"));
        Assert.That(logs.Any(log => log.Contains("Using tool profile")), Is.True);
    }

    [Test]
    public async Task LlmExecutor_AddsReferencedToolProfileToOutput()
    {
        var client = new FakeLlmClient("done");
        var config = new LlmBlockConfig(
            "openai",
            "gpt-4o",
            Temperature: 0.7,
            MaxTokens: 1024,
            Prompt: "{{ content }}",
            ToolProfileName: "tools");
        var block = CreateBlock("llm", BlockType.Llm, config);

        var executor = new LlmBlockExecutor(client);
        var result = await executor.ExecuteAsync(
            CreateContext(block, new[] { new DatasetRow(0, """{"content":"hello"}""") }, CreateProfile()),
            CancellationToken.None);
        var rows = await CollectAsync(result.Output);

        using var document = JsonDocument.Parse(rows[0].Json);
        Assert.That(document.RootElement.GetProperty("messages")[0]
            .GetProperty("content").GetString(), Is.EqualTo("done"));
        Assert.That(document.RootElement.GetProperty("tools")[0]
            .GetProperty("function").GetProperty("name").GetString(), Is.EqualTo("python"));
    }

    [Test]
    public void UnknownToolProfileReference_IsRejected()
    {
        var simulationBlock = CreateBlock("sim", BlockType.Simulation,
            new SimulationBlockConfig("sim-model", ToolProfileName: "missing"));
        var simulationExecutor = new SimulationBlockExecutor();

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await simulationExecutor.ExecuteAsync(
                CreateContext(simulationBlock, Array.Empty<DatasetRow>(), profile: null),
                CancellationToken.None));

        var llmBlock = CreateBlock("llm", BlockType.Llm,
            new LlmBlockConfig("openai", "gpt-4o", 0.7, 1024, null, ToolProfileName: "missing"));
        var llmExecutor = new LlmBlockExecutor(new FakeLlmClient("unused"));

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await llmExecutor.ExecuteAsync(
                CreateContext(llmBlock, Array.Empty<DatasetRow>(), profile: null),
                CancellationToken.None));
    }

    [Test]
    public async Task RecipeRunner_ResolvesToolProfileAndRunsConfigOnlyNode()
    {
        var profile = CreateProfile();
        var query = CreateBlock("queries", BlockType.QuerySource,
            new QuerySourceBlockConfig(new[] { "go" }, QuerySetMode.SameQuery));
        query.InputPorts.AddRange(BlockPortSchema.CreateInputPorts(BlockType.QuerySource));
        query.OutputPorts.AddRange(BlockPortSchema.CreateOutputPorts(BlockType.QuerySource));

        var simulation = CreateBlock("sim", BlockType.Simulation,
            new SimulationBlockConfig(
                "sim-model",
                MaxRounds: 2,
                Rules: new ToolHarnessRules { MaxRetries = 0 },
                FinalContent: "done",
                TimeoutSeconds: 5,
                ToolProfileName: "tools"));
        simulation.InputPorts.AddRange(BlockPortSchema.CreateInputPorts(BlockType.Simulation));
        simulation.OutputPorts.AddRange(BlockPortSchema.CreateOutputPorts(BlockType.Simulation));

        var tools = CreateBlock("tools", BlockType.ToolProfile, new ToolProfileBlockConfig(profile));

        var recipe = new Recipe
        {
            Name = "tool profile pipeline",
            Blocks = { query, simulation, tools },
            Edges =
            {
                new RecipeEdge
                {
                    Id = "e1",
                    SourceBlockId = query.Id,
                    SourcePortId = query.OutputPorts[0].Id,
                    TargetBlockId = simulation.Id,
                    TargetPortId = simulation.InputPorts[0].Id
                }
            }
        };

        var runner = new RecipeRunner(
            new RecipeValidationService(),
            new IBlockExecutor[]
            {
                new QuerySourceBlockExecutor(),
                new SimulationBlockExecutor(),
                new ToolProfileBlockExecutor()
            });

        var run = await runner.RunAsync(recipe);

        Assert.That(run.Status, Is.EqualTo(RunStatus.Succeeded), string.Join('\n', run.Logs));
        Assert.That(run.Nodes.Single(node => node.BlockId == simulation.Id).OutputRows, Is.EqualTo(1));
        Assert.That(run.Logs.Any(log => log.Contains("Using tool profile")), Is.True);
    }

    [Test]
    public void ToolProfileEditor_EditsTreeAndImportsExportsJson()
    {
        var applied = new List<ToolProfileBlockConfig>();
        var editor = new ToolProfileEditorViewModel(CreateProfile(), applied.Add);

        Assert.That(editor.Tools, Has.Count.EqualTo(1));
        Assert.That(editor.SelectedTool, Is.Not.Null);
        Assert.That(editor.SelectedTool!.Parameters, Has.Count.EqualTo(1));

        var code = editor.SelectedTool.Parameters[0];
        Assert.That(code.Name, Is.EqualTo("code"));
        Assert.That(code.IsRequired, Is.True);

        code.Name = "source_code";

        Assert.That(editor.SelectedTool.ParametersJson, Does.Contain("source_code"));
        Assert.That(applied[^1].Profile.Tools[0].Function.Parameters, Is.Not.Null);

        editor.AddToolCommand.Execute(null);
        Assert.That(editor.Tools, Has.Count.EqualTo(2));

        editor.ExportJsonCommand.Execute(null);
        Assert.That(editor.ProfileJson, Does.Contain("\"tools\""));

        editor.ProfileJson = """
            {"id":"p2","name":"Imported","tools":[{"type":"function","function":{"name":"bash","description":"run","parameters":{"type":"object","properties":{"cmd":{"type":"string"}},"required":["cmd"]}}}]}
            """;
        editor.ImportJsonCommand.Execute(null);

        Assert.That(editor.ProfileJsonError, Is.Null);
        Assert.That(editor.Name, Is.EqualTo("Imported"));
        Assert.That(editor.Tools, Has.Count.EqualTo(1));
        Assert.That(editor.SelectedTool!.Name, Is.EqualTo("bash"));
        Assert.That(editor.SelectedTool.Parameters, Has.Count.EqualTo(1));
    }

    private static ToolProfile CreateProfile() => new()
    {
        Id = "p1",
        Name = "Agent Tools",
        Tools = new List<ToolDefinition>
        {
            new()
            {
                Function = new ToolFunction
                {
                    Name = "python",
                    Description = "Run Python",
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
            }
        }
    };

    private static RecipeBlock CreateBlock(string id, BlockType type, BlockConfig config) => new()
    {
        Id = id,
        Type = type,
        Name = id,
        DisplayName = id,
        Config = config
    };

    private static BlockExecutionContext CreateContext(
        RecipeBlock block,
        IEnumerable<DatasetRow> input,
        ToolProfile? profile,
        List<string>? logs = null) => new()
        {
            Block = block,
            Input = ToAsync(input),
            Log = message => logs?.Add(message),
            ToolProfile = profile
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

    private sealed class FakeLlmClient(string completion) : ILlmClient
    {
        public Task<string> CompleteAsync(
            string prompt,
            LlmBlockConfig config,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(completion);
    }
}
