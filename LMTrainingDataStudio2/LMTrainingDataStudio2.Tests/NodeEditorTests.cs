using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.ViewModels;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class NodeEditorTests
{
    [Test]
    public void SeedEditor_UpdatesStreaming()
    {
        var block = CreateBlock(BlockType.Seed);

        block.SeedEditor!.Streaming = true;

        Assert.That(((SeedBlockConfig)block.Config).Streaming, Is.True);
        Assert.That(block.ConfigJson, Does.Contain("\"streaming\": true"));
    }

    [Test]
    public void CleanerEditor_EditsRegexRulesThroughSharedCleanupEditor()
    {
        var block = CreateBlock(BlockType.Cleaner);
        var editor = block.CleanerEditor!;

        editor.Cleanup.AddRegexCommand.Execute(null);
        var rule = editor.Cleanup.RegexReplacements.Single();
        rule.Pattern = "secret";
        rule.Replacement = "[redacted]";
        rule.IgnoreCase = true;

        var rules = ((CleanerBlockConfig)block.Config).Rules;
        Assert.That(rules.RegexReplacements, Has.Length.EqualTo(1));
        Assert.That(rules.RegexReplacements![0].Pattern, Is.EqualTo("secret"));
        Assert.That(rules.RegexReplacements[0].Replacement, Is.EqualTo("[redacted]"));
        Assert.That(rules.RegexReplacements[0].IgnoreCase, Is.True);
    }

    [Test]
    public void ValidatorEditor_EditsCleanupRules()
    {
        var block = CreateBlock(BlockType.Validator);

        block.ValidatorEditor!.Cleanup.StripAnsi = true;

        Assert.That(((ValidatorBlockConfig)block.Config).Cleanup, Is.Not.Null);
        Assert.That(((ValidatorBlockConfig)block.Config).Cleanup!.StripAnsi, Is.True);
    }

    [Test]
    public void LlmEditor_ValidatesResponseSchema()
    {
        var applied = new List<LlmBlockConfig>();
        var editor = new LlmConfigEditorViewModel(
            new LlmBlockConfig(LlmProviderIds.OpenAiChat, "model", 0.7, 128, null),
            applied.Add);

        editor.ResponseSchema = "{ not a schema";
        editor.ValidateSchemaCommand.Execute(null);
        Assert.That(editor.SchemaError, Is.Not.Null);

        editor.ResponseSchema = "{}";
        editor.ValidateSchemaCommand.Execute(null);
        Assert.That(editor.SchemaError, Is.Null);
    }

    [Test]
    public void SftExportEditor_ValidatesFieldNames()
    {
        var applied = new List<SftExportBlockConfig>();
        var editor = new SftExportConfigEditorViewModel(new SftExportBlockConfig(string.Empty), applied.Add);

        editor.FieldsText = "messages, tools";
        Assert.That(editor.FieldsError, Is.Null);

        editor.FieldsText = "messages, messages";
        Assert.That(editor.FieldsError, Is.Not.Null);

        editor.FieldsText = "bad field";
        Assert.That(editor.FieldsError, Is.Not.Null);
    }

    private static BlockNodeViewModel CreateBlock(BlockType type) => new()
    {
        Type = type,
        Name = "block",
        DisplayName = "Block",
        Config = BlockConfig.CreateDefault(type)
    };
}
