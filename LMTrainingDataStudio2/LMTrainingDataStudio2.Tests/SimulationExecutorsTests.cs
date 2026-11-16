using System.Text.Json;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services;
using LMTrainingDataStudio2.Services.Executors;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class SimulationExecutorsTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "lmtds-sim-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_directory, true); } catch { /* best effort */ }
    }

    [Test]
    public async Task QuerySource_SameQuery_RepeatsFirstQuery()
    {
        var config = new QuerySourceBlockConfig(
            new[] { "q1", "q2" },
            QuerySetMode.SameQuery,
            Repeat: 3,
            SystemPrompt: "sys");
        var block = CreateBlock(BlockType.QuerySource, "queries", config);

        var rows = await CollectAsync(
            (await new QuerySourceBlockExecutor().ExecuteAsync(CreateContext(block), CancellationToken.None)).Output);

        Assert.That(rows, Has.Count.EqualTo(3));
        foreach (var row in rows)
        {
            using var document = JsonDocument.Parse(row.Json);
            Assert.That(document.RootElement.GetProperty("query").GetString(), Is.EqualTo("q1"));
            Assert.That(document.RootElement.GetProperty("system").GetString(), Is.EqualTo("sys"));
        }
    }

    [Test]
    public async Task QuerySource_MultipleQueries_EmitsEachQueryPerRepeat()
    {
        var config = new QuerySourceBlockConfig(
            new[] { "q1", "q2" },
            QuerySetMode.MultipleQueries,
            Repeat: 2);
        var block = CreateBlock(BlockType.QuerySource, "queries", config);

        var rows = await CollectAsync(
            (await new QuerySourceBlockExecutor().ExecuteAsync(CreateContext(block), CancellationToken.None)).Output);

        Assert.That(rows.Select(row =>
        {
            using var document = JsonDocument.Parse(row.Json);
            return document.RootElement.GetProperty("query").GetString();
        }), Is.EqualTo(new[] { "q1", "q1", "q2", "q2" }));
    }

    [Test]
    public void QuerySource_WithoutQueries_Throws()
    {
        var block = CreateBlock(BlockType.QuerySource, "queries",
            new QuerySourceBlockConfig(Array.Empty<string>()));

        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await new QuerySourceBlockExecutor()
                .ExecuteAsync(CreateContext(block), CancellationToken.None));
    }

    [Test]
    public async Task Simulation_ProducesOaiMessagesWithTools()
    {
        var config = CreateSimulationConfig();
        var block = CreateBlock(BlockType.Simulation, "sim", config);
        var input = new[] { new DatasetRow(0, """{"query":"run python"}""") };

        var rows = await CollectAsync(
            (await new SimulationBlockExecutor().ExecuteAsync(CreateContext(block, input), CancellationToken.None)).Output);

        Assert.That(rows, Has.Count.EqualTo(1));
        using var document = JsonDocument.Parse(rows[0].Json);
        var root = document.RootElement;
        Assert.That(root.GetProperty("messages")[0].GetProperty("role").GetString(), Is.EqualTo("user"));
        Assert.That(root.GetProperty("messages")[1].GetProperty("role").GetString(), Is.EqualTo("assistant"));
        Assert.That(root.GetProperty("messages")[1].GetProperty("tool_calls").GetArrayLength(), Is.EqualTo(1));
        Assert.That(root.GetProperty("messages")[2].GetProperty("role").GetString(), Is.EqualTo("tool"));
        Assert.That(root.GetProperty("messages")[3].GetProperty("content").GetString(), Is.EqualTo("final"));
        Assert.That(root.GetProperty("tools")[0].GetProperty("function").GetProperty("name").GetString(),
            Is.EqualTo("python"));
    }

    [Test]
    public async Task Simulation_RowSystemPrompt_OverridesConfig()
    {
        var config = CreateSimulationConfig() with { SystemPrompt = "configured" };
        var block = CreateBlock(BlockType.Simulation, "sim", config);
        var input = new[] { new DatasetRow(0, """{"query":"hi","system":"from row"}""") };

        var rows = await CollectAsync(
            (await new SimulationBlockExecutor().ExecuteAsync(CreateContext(block, input), CancellationToken.None)).Output);

        using var document = JsonDocument.Parse(rows[0].Json);
        var system = document.RootElement.GetProperty("messages")[0];
        Assert.That(system.GetProperty("role").GetString(), Is.EqualTo("system"));
        Assert.That(system.GetProperty("content").GetString(), Is.EqualTo("from row"));
    }

    [Test]
    public async Task Simulation_IncompleteTrajectory_IsSkippedByDefault()
    {
        var config = CreateSimulationConfig() with { MaxRounds = 1 };
        var block = CreateBlock(BlockType.Simulation, "sim", config);
        var input = new[] { new DatasetRow(0, """{"query":"loop"}""") };
        var logs = new List<string>();

        var rows = await CollectAsync(
            (await new SimulationBlockExecutor().ExecuteAsync(
                CreateContext(block, input, logs), CancellationToken.None)).Output);

        Assert.That(rows, Is.Empty);
        Assert.That(logs, Has.Some.Contains("skipped MaxRoundsExceeded"));
    }

    [Test]
    public async Task Simulation_IncompleteTrajectory_IsEmittedWhenConfigured()
    {
        var config = CreateSimulationConfig() with { MaxRounds = 1, EmitIncompleteTrajectories = true };
        var block = CreateBlock(BlockType.Simulation, "sim", config);
        var input = new[] { new DatasetRow(0, """{"query":"loop"}""") };

        var rows = await CollectAsync(
            (await new SimulationBlockExecutor().ExecuteAsync(CreateContext(block, input), CancellationToken.None)).Output);

        Assert.That(rows, Has.Count.EqualTo(1));
    }

    [Test]
    public void Simulation_WithoutQueryField_Throws()
    {
        var block = CreateBlock(BlockType.Simulation, "sim", CreateSimulationConfig());
        var input = new[] { new DatasetRow(0, """{"other":"x"}""") };

        var exception = Assert.ThrowsAsync<InvalidDataException>(
            async () => await CollectAsync(
                (await new SimulationBlockExecutor().ExecuteAsync(
                    CreateContext(block, input), CancellationToken.None)).Output));

        Assert.That(exception!.Message, Does.Contain("query"));
    }

    [Test]
    public async Task Cleaner_AppliesRegexAndWhitespaceRules()
    {
        var block = CreateBlock(BlockType.Cleaner, "clean",
            new CleanerBlockConfig(new DatasetCleanupRules
            {
                RegexReplacements = new[] { new RegexReplacementRule("WORLD", "world") },
                NormalizeWhitespace = true
            }));
        var input = new[] { new DatasetRow(0, """{"messages":[{"role":"assistant","content":"  Hello   WORLD  "}]}""") };

        var rows = await CollectAsync(
            (await new CleanerBlockExecutor().ExecuteAsync(CreateContext(block, input), CancellationToken.None)).Output);

        using var document = JsonDocument.Parse(rows[0].Json);
        Assert.That(
            document.RootElement.GetProperty("messages")[0].GetProperty("content").GetString(),
            Is.EqualTo("Hello   world"));
    }

    [Test]
    public void Cleaner_InvalidRegex_ThrowsWithBlockAndLine()
    {
        var block = CreateBlock(BlockType.Cleaner, "clean",
            new CleanerBlockConfig(new DatasetCleanupRules
            {
                RegexReplacements = new[] { new RegexReplacementRule("[", "x") }
            }));
        var input = new[] { new DatasetRow(7, """{"messages":[{"role":"assistant","content":"x"}]}""") };

        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await CollectAsync(
                (await new CleanerBlockExecutor().ExecuteAsync(
                    CreateContext(block, input), CancellationToken.None)).Output));

        Assert.That(exception!.Message, Does.Contain("line 7"));
    }

    [Test]
    public async Task Validator_AppliesCleanupToPassingRows()
    {
        var block = CreateBlock(BlockType.Validator, "check",
            new ValidatorBlockConfig(
                "expression",
                "messages.length > 0",
                RejectOnFailure: true,
                Cleanup: new DatasetCleanupRules
                {
                    RegexReplacements = new[] { new RegexReplacementRule("secret", "[redacted]") }
                }));
        var input = new[] { new DatasetRow(0, """{"messages":[{"role":"assistant","content":"my secret"}]}""") };

        var rows = await CollectAsync(
            (await new ValidatorBlockExecutor().ExecuteAsync(CreateContext(block, input), CancellationToken.None)).Output);

        using var document = JsonDocument.Parse(rows[0].Json);
        Assert.That(
            document.RootElement.GetProperty("messages")[0].GetProperty("content").GetString(),
            Is.EqualTo("my [redacted]"));
    }

    [Test]
    public async Task SftExport_WritesJsonlAndPassesRowsThrough()
    {
        var outputPath = Path.Combine(_directory, "out", "sft.jsonl");
        var block = CreateBlock(BlockType.SftExport, "export", new SftExportBlockConfig(outputPath));
        var input = new[]
        {
            new DatasetRow(0, """{"messages":[{"role":"user","content":"a"}]}"""),
            new DatasetRow(1, """{"messages":[{"role":"assistant","content":"b"}]}""")
        };

        var rows = await CollectAsync(
            (await new SftExportBlockExecutor().ExecuteAsync(CreateContext(block, input), CancellationToken.None)).Output);

        Assert.That(rows, Has.Count.EqualTo(2));
        var lines = await File.ReadAllLinesAsync(outputPath);
        Assert.That(lines, Is.EqualTo(input.Select(row => row.Json)));
        Assert.That(File.Exists(outputPath + ".tmp"), Is.False);
    }

    [Test]
    public async Task SftExport_OverwritesExistingFile()
    {
        var outputPath = Path.Combine(_directory, "sft.jsonl");
        await File.WriteAllTextAsync(outputPath, "old content\n");
        var block = CreateBlock(BlockType.SftExport, "export", new SftExportBlockConfig(outputPath));

        await CollectAsync(
            (await new SftExportBlockExecutor().ExecuteAsync(
                CreateContext(block, new[] { new DatasetRow(0, """{"messages":[]}""") }),
                CancellationToken.None)).Output);

        Assert.That(await File.ReadAllTextAsync(outputPath), Is.EqualTo("{\"messages\":[]}" + Environment.NewLine));
    }

    [Test]
    public void SftExport_WithoutPath_Throws()
    {
        var block = CreateBlock(BlockType.SftExport, "export", new SftExportBlockConfig(string.Empty));

        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await new SftExportBlockExecutor()
                .ExecuteAsync(CreateContext(block, Array.Empty<DatasetRow>()), CancellationToken.None));
    }

    [Test]
    public async Task RecipeRunner_SimulationPipeline_ExportsSftJsonl()
    {
        var outputPath = Path.Combine(_directory, "pipeline", "sft.jsonl");

        var queryBlock = CreateBlock(
            BlockType.QuerySource, "queries",
            new QuerySourceBlockConfig(new[] { "Explain TCP" }, QuerySetMode.SameQuery, Repeat: 1));
        queryBlock.OutputPorts.Add(Port("q_out", PortDirection.Output));

        var simulationBlock = CreateBlock(BlockType.Simulation, "sim", CreateSimulationConfig() with
        {
            Tools = new[]
            {
                new SimulatedToolRule
                {
                    Name = "python",
                    Description = "Run Python",
                    ResponseTemplate = "{{arguments}}"
                }
            }
        });
        simulationBlock.InputPorts.Add(Port("s_in", PortDirection.Input));
        simulationBlock.OutputPorts.Add(Port("s_out", PortDirection.Output));

        var cleanerBlock = CreateBlock(BlockType.Cleaner, "clean",
            new CleanerBlockConfig(new DatasetCleanupRules
            {
                RegexReplacements = new[] { new RegexReplacementRule("print", "PRINT") }
            }));
        cleanerBlock.InputPorts.Add(Port("c_in", PortDirection.Input));
        cleanerBlock.OutputPorts.Add(Port("c_out", PortDirection.Output));

        var exportBlock = CreateBlock(BlockType.SftExport, "export", new SftExportBlockConfig(outputPath));
        exportBlock.InputPorts.Add(Port("e_in", PortDirection.Input));

        var recipe = new Recipe
        {
            Name = "simulation pipeline",
            Blocks = { queryBlock, simulationBlock, cleanerBlock, exportBlock },
            Edges =
            {
                Edge("q_out", "s_in", queryBlock, simulationBlock),
                Edge("s_out", "c_in", simulationBlock, cleanerBlock),
                Edge("c_out", "e_in", cleanerBlock, exportBlock)
            }
        };

        var runner = new RecipeRunner(
            new RecipeValidationService(),
            new IBlockExecutor[]
            {
                new QuerySourceBlockExecutor(),
                new SimulationBlockExecutor(),
                new CleanerBlockExecutor(),
                new SftExportBlockExecutor()
            });

        var run = await runner.RunAsync(recipe);

        Assert.That(run.Status, Is.EqualTo(RunStatus.Succeeded), string.Join('\n', run.Logs));
        Assert.That(File.Exists(outputPath), Is.True);

        var lines = await File.ReadAllLinesAsync(outputPath);
        Assert.That(lines, Has.Length.EqualTo(1));

        using var document = JsonDocument.Parse(lines[0]);
        var messages = document.RootElement.GetProperty("messages");
        Assert.That(messages.GetArrayLength(), Is.EqualTo(4));
        Assert.That(messages[2].GetProperty("content").GetString(), Does.Contain("PRINT(1)"));
        Assert.That(document.RootElement.GetProperty("tools")[0]
            .GetProperty("function").GetProperty("name").GetString(), Is.EqualTo("python"));
    }

    private static SimulationBlockConfig CreateSimulationConfig() => new(
        "sim-model",
        MaxRounds: 4,
        SystemPrompt: null,
        Tools: new[]
        {
            new SimulatedToolRule { Name = "python", Description = "Run Python" }
        },
        Rules: new ToolHarnessRules { MaxRetries = 0 },
        Script: new[]
        {
            new SimulatedTurn(null, new[] { new SimulatedToolCallSpec("python", """{"code":"print(1)"}""") })
        },
        FinalContent: "final",
        TimeoutSeconds: 5);

    private static RecipeBlock CreateBlock(BlockType type, string name, BlockConfig config) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Type = type,
        Name = name,
        DisplayName = name,
        Config = config
    };

    private static BlockPort Port(string id, PortDirection direction) => new()
    {
        Id = id,
        Name = id,
        DataType = "any",
        Direction = direction
    };

    private static RecipeEdge Edge(
        string sourcePort, string targetPort, RecipeBlock source, RecipeBlock target) => new()
    {
        SourceBlockId = source.Id,
        SourcePortId = sourcePort,
        TargetBlockId = target.Id,
        TargetPortId = targetPort
    };

    private static BlockExecutionContext CreateContext(
        RecipeBlock block,
        IEnumerable<DatasetRow>? input = null,
        List<string>? logs = null) => new()
    {
        Block = block,
        Input = input is null ? null : ToAsync(input),
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
}
