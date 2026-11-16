using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class BlockConfigTests
{
    [Test]
    public void CreateDefault_ReturnsConfigMatchingBlockType()
    {
        foreach (var type in Enum.GetValues<BlockType>())
        {
            Assert.That(BlockConfig.CreateDefault(type).GetBlockType(), Is.EqualTo(type));
        }
    }

    [Test]
    public void Clone_ExpressionConfig_DoesNotShareFields()
    {
        var original = new ExpressionBlockConfig("{{ prompt }}", new Dictionary<string, string> { ["prompt"] = "content" });

        var clone = (ExpressionBlockConfig)original.DeepClone();
        clone.Fields["prompt"] = "changed";

        Assert.That(original.Fields["prompt"], Is.EqualTo("content"));
    }

    [Test]
    public void Clone_ToolProfileConfig_DoesNotShareTools()
    {
        var original = new ToolProfileBlockConfig(new ToolProfile
        {
            Id = "tools1",
            Name = "Default tools",
            Tools = { new ToolDefinition { Function = new ToolFunction { Name = "search" } } }
        });

        var clone = (ToolProfileBlockConfig)original.DeepClone();
        clone.Profile.Tools[0].Function.Name = "lookup";

        Assert.That(original.Profile.Tools[0].Function.Name, Is.EqualTo("search"));
        Assert.That(clone.Profile.Id, Is.EqualTo(original.Profile.Id));
    }
}