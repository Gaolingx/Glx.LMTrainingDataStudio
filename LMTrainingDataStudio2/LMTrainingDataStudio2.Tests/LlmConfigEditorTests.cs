using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.ViewModels;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class LlmConfigEditorTests
{
    [Test]
    public void EditingFields_UpdatesBlockConfig()
    {
        var block = CreateLlmBlock(
            new LlmBlockConfig(LlmProviderIds.OpenAiChat, "gpt-4o", Temperature: 0.7, MaxTokens: 2048, Prompt: "prompt"));
        var editor = block.LlmEditor!;

        editor.SelectedProvider = editor.Providers.Single(option => option.Id == LlmProviderIds.Anthropic);
        editor.ModelAlias = "claude-sonnet-4-5";
        editor.Temperature = 0.3;
        editor.MaxTokens = 4096;
        editor.Prompt = string.Empty;
        editor.Endpoint = "https://proxy.example/v1";
        editor.TimeoutSeconds = 30;
        editor.MaxRetries = 5;
        editor.MaxToolRounds = 4;
        editor.SelectedToolMode = editor.ToolModes.Single(option => option.Mode == ToolExecutionMode.Real);

        var config = (LlmBlockConfig)block.Config;
        Assert.That(config.Provider, Is.EqualTo(LlmProviderIds.Anthropic));
        Assert.That(config.ModelAlias, Is.EqualTo("claude-sonnet-4-5"));
        Assert.That(config.Temperature, Is.EqualTo(0.3));
        Assert.That(config.MaxTokens, Is.EqualTo(4096));
        Assert.That(config.Prompt, Is.Null);
        Assert.That(config.Endpoint, Is.EqualTo("https://proxy.example/v1"));
        Assert.That(config.TimeoutSeconds, Is.EqualTo(30));
        Assert.That(config.MaxRetries, Is.EqualTo(5));
        Assert.That(config.MaxToolRounds, Is.EqualTo(4));
        Assert.That(config.ToolMode, Is.EqualTo(ToolExecutionMode.Real));
        Assert.That(block.LlmEditor, Is.SameAs(editor));
    }

    [Test]
    public void EditingFields_PreservesUneditedSandboxSettings()
    {
        var sandbox = new SandboxLimits { TimeoutMs = 1234, AllowNetwork = true };
        var block = CreateLlmBlock(new LlmBlockConfig(
            LlmProviderIds.OpenAiChat, "gpt-4o", Temperature: 0.7, MaxTokens: 2048, Prompt: null,
            Sandbox: sandbox));

        block.LlmEditor!.MaxToolRounds = 2;

        Assert.That(((LlmBlockConfig)block.Config).Sandbox, Is.EqualTo(sandbox));
    }

    [Test]
    public void LegacyProviderAlias_SelectsCanonicalProvider()
    {
        var block = CreateLlmBlock(
            new LlmBlockConfig("openai", "gpt-4o", Temperature: 0.7, MaxTokens: 2048, Prompt: null));

        Assert.That(block.LlmEditor!.SelectedProvider.Id, Is.EqualTo(LlmProviderIds.OpenAiChat));
    }

    [Test]
    public void ReplacingConfig_RebuildsEditorFromNewValues()
    {
        var block = CreateLlmBlock(
            new LlmBlockConfig(LlmProviderIds.OpenAiChat, "gpt-4o", Temperature: 0.7, MaxTokens: 2048, Prompt: null));

        block.Config = new LlmBlockConfig(
            LlmProviderIds.OpenAiResponses, "gpt-4o-mini", Temperature: 0.1, MaxTokens: 1024, Prompt: "hello");

        Assert.That(block.LlmEditor, Is.Not.Null);
        Assert.That(block.LlmEditor!.SelectedProvider.Id, Is.EqualTo(LlmProviderIds.OpenAiResponses));
        Assert.That(block.LlmEditor.ModelAlias, Is.EqualTo("gpt-4o-mini"));
        Assert.That(block.LlmEditor.MaxTokens, Is.EqualTo(1024));
        Assert.That(block.LlmEditor.Prompt, Is.EqualTo("hello"));
    }

    [Test]
    public void NonLlmBlock_HasNoLlmEditor()
    {
        var block = new BlockNodeViewModel
        {
            Type = BlockType.Seed,
            Name = "seed",
            DisplayName = "Seed",
            Config = new SeedBlockConfig("data.jsonl", DataFileType.Jsonl, Streaming: false, UseIndexCache: true)
        };

        Assert.That(block.LlmEditor, Is.Null);
    }

    private static BlockNodeViewModel CreateLlmBlock(LlmBlockConfig config) => new()
    {
        Type = BlockType.Llm,
        Name = "generator",
        DisplayName = "LLM Generator",
        Config = config
    };
}
