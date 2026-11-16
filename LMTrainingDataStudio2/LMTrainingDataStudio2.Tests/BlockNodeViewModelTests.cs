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

    private static BlockNodeViewModel CreateBlock(BlockType type) => new()
    {
        Type = type,
        Name = "block",
        DisplayName = "Block",
        Config = BlockConfig.CreateDefault(type)
    };
}
