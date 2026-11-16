using System.Text.Json;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services;
using LMTrainingDataStudio2.ViewModels;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class BlockNodeViewModelTests
{
    [Test]
    public void Config_WhenSet_UpdatesConfigJson()
    {
        var block = CreateBlock(BlockType.Simulation);

        Assert.That(block.ConfigJson, Does.Contain("\"kind\": \"simulation\""));
        Assert.That(block.ConfigJsonError, Is.Null);
    }

    [Test]
    public void TryApplyConfigJson_ValidJson_ReplacesConfig()
    {
        var block = CreateBlock(BlockType.Simulation);
        var json = JsonSerializer.Serialize<BlockConfig>(
            new SimulationBlockConfig("sim-model", MaxRounds: 2, FinalContent: "done"),
            RecipeStore.SerializerOptions);

        var applied = block.TryApplyConfigJson(json);

        Assert.That(applied, Is.True);
        Assert.That(block.Config, Is.TypeOf<SimulationBlockConfig>());
        Assert.That(((SimulationBlockConfig)block.Config).MaxRounds, Is.EqualTo(2));
        Assert.That(((SimulationBlockConfig)block.Config).FinalContent, Is.EqualTo("done"));
        Assert.That(block.ConfigJsonError, Is.Null);
    }

    [Test]
    public void TryApplyConfigJson_WrongKind_ReturnsFalse()
    {
        var block = CreateBlock(BlockType.Simulation);
        var json = JsonSerializer.Serialize<BlockConfig>(
            new QuerySourceBlockConfig(new[] { "q" }),
            RecipeStore.SerializerOptions);

        var applied = block.TryApplyConfigJson(json);

        Assert.That(applied, Is.False);
        Assert.That(block.ConfigJsonError, Does.Contain("does not match block type"));
        Assert.That(block.Config, Is.TypeOf<SimulationBlockConfig>());
    }

    [Test]
    public void TryApplyConfigJson_InvalidJson_ReturnsFalse()
    {
        var block = CreateBlock(BlockType.Cleaner);

        var applied = block.TryApplyConfigJson("{not-json");

        Assert.That(applied, Is.False);
        Assert.That(block.ConfigJsonError, Does.Contain("Invalid config JSON"));
    }

    [Test]
    public void TryApplyConfigJson_EmptyInput_ReturnsFalse()
    {
        var block = CreateBlock(BlockType.QuerySource);

        var applied = block.TryApplyConfigJson("   ");

        Assert.That(applied, Is.False);
        Assert.That(block.ConfigJsonError, Is.EqualTo("Config JSON is empty."));
    }

    [Test]
    public void Config_NewNodeTypes_CreateDedicatedEditors()
    {
        var query = CreateBlock(BlockType.QuerySource);
        var simulation = CreateBlock(BlockType.Simulation);
        var cleaner = CreateBlock(BlockType.Cleaner);
        var export = CreateBlock(BlockType.SftExport);

        Assert.That(query.QuerySourceEditor, Is.Not.Null);
        Assert.That(query.SimulationEditor, Is.Null);
        Assert.That(simulation.SimulationEditor, Is.Not.Null);
        Assert.That(simulation.QuerySourceEditor, Is.Null);
        Assert.That(cleaner.CleanerEditor, Is.Not.Null);
        Assert.That(export.SftExportEditor, Is.Not.Null);
    }

    [Test]
    public void QuerySourceEditor_UpdatesConfiguration()
    {
        var block = CreateBlock(BlockType.QuerySource);
        var editor = block.QuerySourceEditor!;

        editor.QueriesText = "q1" + Environment.NewLine + "  q2  " + Environment.NewLine;
        editor.Repeat = 3;
        editor.SystemPrompt = "be brief";
        editor.SelectedMode = editor.Modes.Single(option => option.Mode == QuerySetMode.SameQuery);

        var config = (QuerySourceBlockConfig)block.Config;
        Assert.That(config.Queries, Is.EqualTo(new[] { "q1", "q2" }));
        Assert.That(config.Repeat, Is.EqualTo(3));
        Assert.That(config.SystemPrompt, Is.EqualTo("be brief"));
        Assert.That(config.Mode, Is.EqualTo(QuerySetMode.SameQuery));
        Assert.That(block.ConfigJson, Does.Contain("\"repeat\": 3"));
    }

    [Test]
    public void SimulationEditor_UpdatesCoreAndSandboxSettings_PreservesToolsAndScript()
    {
        var tools = new[] { new SimulatedToolRule { Name = "python" } };
        var script = new[] { new SimulatedTurn(null, new[] { new SimulatedToolCallSpec("python") }) };
        var block = CreateBlock(BlockType.Simulation);
        block.Config = new SimulationBlockConfig(
            "sim-model", Tools: tools, Script: script, FinalContent: "done");
        var editor = block.SimulationEditor!;

        editor.SelectedModelMode = editor.ModelModes.Single(option =>
            option.Mode == SimulationModelMode.Provider);
        editor.SelectedToolMode = editor.ToolModes.Single(option =>
            option.Mode == ToolExecutionMode.Real);
        editor.ModelAlias = "gpt-4o-mini";
        editor.MaxRounds = 6;
        editor.MaxConcurrency = 4;
        editor.Endpoint = "https://proxy.example/v1";
        editor.FailureOutputPath = "out/failed.jsonl";
        editor.MaxFailureSamples = 5;
        editor.InputCostPerMillionTokens = 1.5;
        editor.OutputCostPerMillionTokens = 3;
        editor.SandboxAllowNetwork = true;
        editor.SandboxTimeoutMs = 1234;
        editor.SandboxMaxMemoryMb = 256;
        editor.PythonExecutable = "python3";

        var config = (SimulationBlockConfig)block.Config;
        Assert.That(config.ModelMode, Is.EqualTo(SimulationModelMode.Provider));
        Assert.That(config.ToolMode, Is.EqualTo(ToolExecutionMode.Real));
        Assert.That(config.ModelAlias, Is.EqualTo("gpt-4o-mini"));
        Assert.That(config.MaxRounds, Is.EqualTo(6));
        Assert.That(config.MaxConcurrency, Is.EqualTo(4));
        Assert.That(config.Endpoint, Is.EqualTo("https://proxy.example/v1"));
        Assert.That(config.FailureOutputPath, Is.EqualTo("out/failed.jsonl"));
        Assert.That(config.MaxFailureSamples, Is.EqualTo(5));
        Assert.That(config.InputCostPerMillionTokens, Is.EqualTo(1.5));
        Assert.That(config.OutputCostPerMillionTokens, Is.EqualTo(3));
        Assert.That(config.Sandbox, Is.Not.Null);
        Assert.That(config.Sandbox!.AllowNetwork, Is.True);
        Assert.That(config.Sandbox.TimeoutMs, Is.EqualTo(1234));
        Assert.That(config.Sandbox.MaxMemoryMb, Is.EqualTo(256));
        Assert.That(config.Sandbox.PythonExecutable, Is.EqualTo("python3"));
        Assert.That(config.Tools, Is.SameAs(tools));
        Assert.That(config.Script, Is.SameAs(script));
        Assert.That(config.FinalContent, Is.EqualTo("done"));
    }

    [Test]
    public void CleanerEditor_UpdatesToggles_PreservesRegexRules()
    {
        var block = CreateBlock(BlockType.Cleaner);
        var rules = new DatasetCleanupRules
        {
            RegexReplacements = new[] { new RegexReplacementRule("secret", "[redacted]") },
            NormalizeNewlines = true
        };
        block.Config = new CleanerBlockConfig(rules);
        var editor = block.CleanerEditor!;

        editor.StripAnsi = true;
        editor.RemoveReasoningContent = true;
        editor.DropEmptyMessages = true;
        editor.NormalizeWhitespace = true;

        var config = (CleanerBlockConfig)block.Config;
        Assert.That(config.Rules.StripAnsi, Is.True);
        Assert.That(config.Rules.RemoveReasoningContent, Is.True);
        Assert.That(config.Rules.DropEmptyMessages, Is.True);
        Assert.That(config.Rules.NormalizeWhitespace, Is.True);
        Assert.That(config.Rules.NormalizeNewlines, Is.True);
        Assert.That(config.Rules.RegexReplacements, Has.Length.EqualTo(1));
        Assert.That(config.Rules.RegexReplacements![0].Pattern, Is.EqualTo("secret"));
    }

    [Test]
    public void SftExportEditor_UpdatesOutputPath()
    {
        var block = CreateBlock(BlockType.SftExport);
        block.SftExportEditor!.OutputPath = "out/sft.jsonl";

        Assert.That(((SftExportBlockConfig)block.Config).OutputPath, Is.EqualTo("out/sft.jsonl"));
        Assert.That(block.ConfigJson, Does.Contain("out/sft.jsonl"));
    }

    private static BlockNodeViewModel CreateBlock(BlockType type) => new()
    {
        Type = type,
        Name = "block",
        DisplayName = "Block",
        Config = BlockConfig.CreateDefault(type)
    };
}
