using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.ViewModels;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class SeedConfigEditorTests
{
    [Test]
    public void EditingFields_UpdatesBlockConfig()
    {
        var block = CreateSeedBlock(
            new SeedBlockConfig("old.jsonl", DataFileType.Jsonl, Streaming: false, UseIndexCache: true));

        block.SeedEditor!.SourcePath = "new.jsonl";
        block.SeedEditor.FileType = DataFileType.Csv;
        block.SeedEditor.UseIndexCache = false;

        var config = (SeedBlockConfig)block.Config;
        Assert.That(config.SourcePath, Is.EqualTo("new.jsonl"));
        Assert.That(config.FileType, Is.EqualTo(DataFileType.Csv));
        Assert.That(config.UseIndexCache, Is.False);
        Assert.That(config.Streaming, Is.False);
    }

    [Test]
    public void EditingFields_KeepsEditorInstance()
    {
        var block = CreateSeedBlock(
            new SeedBlockConfig("old.jsonl", DataFileType.Jsonl, Streaming: false, UseIndexCache: true));
        var editor = block.SeedEditor;

        editor!.SourcePath = "changed.jsonl";

        Assert.That(block.SeedEditor, Is.SameAs(editor));
    }

    [Test]
    public void ReplacingConfig_RebuildsEditorFromNewValues()
    {
        var block = CreateSeedBlock(
            new SeedBlockConfig("old.jsonl", DataFileType.Jsonl, Streaming: false, UseIndexCache: true));

        block.Config = new SeedBlockConfig("replacement.csv", DataFileType.Csv, Streaming: false, UseIndexCache: false);

        Assert.That(block.SeedEditor, Is.Not.Null);
        Assert.That(block.SeedEditor!.SourcePath, Is.EqualTo("replacement.csv"));
        Assert.That(block.SeedEditor.FileType, Is.EqualTo(DataFileType.Csv));
        Assert.That(block.SeedEditor.UseIndexCache, Is.False);
    }

    [Test]
    public void NonSeedBlock_HasNoSeedEditor()
    {
        var block = new BlockNodeViewModel
        {
            Type = BlockType.Validator,
            Name = "check",
            DisplayName = "Check",
            Config = new ValidatorBlockConfig("expression", "messages exists", RejectOnFailure: true)
        };

        Assert.That(block.SeedEditor, Is.Null);
    }

    [Test]
    public void EditingSeedConfig_ClearsConfigError()
    {
        var block = CreateSeedBlock(
            new SeedBlockConfig(string.Empty, DataFileType.Jsonl, Streaming: false, UseIndexCache: true));
        block.ConfigError = "Seed has no data source.";

        block.SeedEditor!.SourcePath = "data.jsonl";

        Assert.That(block.ConfigError, Is.Null);
    }

    private static BlockNodeViewModel CreateSeedBlock(SeedBlockConfig config) => new()
    {
        Type = BlockType.Seed,
        Name = "seed",
        DisplayName = "Seed",
        Config = config
    };
}