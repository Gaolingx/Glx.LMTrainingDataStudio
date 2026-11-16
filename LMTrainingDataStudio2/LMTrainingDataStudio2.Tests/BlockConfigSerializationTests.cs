using System.Text.Json;
using System.Text.Json.Serialization;
using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class BlockConfigSerializationTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Test]
    public void RoundTrip_SeedConfig_PreservesFields()
    {
        var config = new SeedBlockConfig("data/train.jsonl", DataFileType.Csv, Streaming: true, UseIndexCache: false);

        var loaded = RoundTrip(config);

        Assert.That(loaded, Is.TypeOf<SeedBlockConfig>());
        Assert.That(loaded, Is.EqualTo(config));
    }

    [Test]
    public void RoundTrip_LlmConfig_PreservesFields()
    {
        var config = new LlmBlockConfig("openai", "gpt-4o", Temperature: 0.2, MaxTokens: 4096, Prompt: "Rewrite {{ content }}");

        var loaded = RoundTrip(config);

        Assert.That(loaded, Is.TypeOf<LlmBlockConfig>());
        Assert.That(loaded, Is.EqualTo(config));
    }

    [Test]
    public void RoundTrip_ExpressionConfig_PreservesFields()
    {
        var config = new ExpressionBlockConfig("{{ prompt }}", new Dictionary<string, string> { ["prompt"] = "content" });

        var loaded = RoundTrip(config);

        Assert.That(loaded, Is.TypeOf<ExpressionBlockConfig>());
        Assert.That(((ExpressionBlockConfig)loaded).Template, Is.EqualTo(config.Template));
        Assert.That(((ExpressionBlockConfig)loaded).Fields, Is.EquivalentTo(config.Fields));
    }

    [Test]
    public void RoundTrip_ValidatorConfig_PreservesFields()
    {
        var config = new ValidatorBlockConfig("expression", "len(messages) > 0", RejectOnFailure: true);

        var loaded = RoundTrip(config);

        Assert.That(loaded, Is.TypeOf<ValidatorBlockConfig>());
        Assert.That(loaded, Is.EqualTo(config));
    }

    [Test]
    public void RoundTrip_SamplerConfig_PreservesFields()
    {
        var config = new SamplerBlockConfig(Count: 1000, Ratio: null, Seed: 7, Splits: new[] { "train", "eval" });

        var loaded = RoundTrip(config);

        Assert.That(loaded, Is.TypeOf<SamplerBlockConfig>());
        var sampler = (SamplerBlockConfig)loaded;
        Assert.That(sampler.Count, Is.EqualTo(config.Count));
        Assert.That(sampler.Ratio, Is.Null);
        Assert.That(sampler.Seed, Is.EqualTo(config.Seed));
        Assert.That(sampler.Splits, Is.EqualTo(config.Splits));
    }

    [Test]
    public void RoundTrip_ToolProfileConfig_PreservesProfile()
    {
        var profile = new ToolProfile
        {
            Id = "tools1",
            Name = "Default tools",
            Tools = { new ToolDefinition { Function = new ToolFunction { Name = "search" } } }
        };

        var loaded = RoundTrip(new ToolProfileBlockConfig(profile));

        Assert.That(loaded, Is.TypeOf<ToolProfileBlockConfig>());
        var toolProfile = ((ToolProfileBlockConfig)loaded).Profile;
        Assert.That(toolProfile.Id, Is.EqualTo(profile.Id));
        Assert.That(toolProfile.Name, Is.EqualTo(profile.Name));
        Assert.That(toolProfile.Tools, Has.Count.EqualTo(1));
        Assert.That(toolProfile.Tools[0].Function.Name, Is.EqualTo("search"));
    }

    [Test]
    public void Serialize_EveryConfigType_WritesKindDiscriminator()
    {
        (BlockConfig Config, string Kind)[] cases =
        {
            (new SeedBlockConfig(string.Empty, DataFileType.Jsonl, false, false), "seed"),
            (new LlmBlockConfig("openai", "gpt-4o", 0.7, 2048, null), "llm"),
            (new ExpressionBlockConfig("x", new Dictionary<string, string>()), "expression"),
            (new ValidatorBlockConfig("expression", "x", true), "validator"),
            (new SamplerBlockConfig(1, null, 42, Array.Empty<string>()), "sampler"),
            (new ToolProfileBlockConfig(new ToolProfile()), "tool_profile")
        };

        foreach (var (config, kind) in cases)
        {
            var json = JsonSerializer.Serialize(config, Options);
            Assert.That(json, Does.Contain($"\"kind\":\"{kind}\""), $"{config.GetType().Name} should use kind '{kind}'.");
        }
    }

    [Test]
    public void RoundTrip_RecipeBlock_RestoresPolymorphicConfig()
    {
        var block = new RecipeBlock
        {
            Id = "seed1",
            Type = BlockType.Seed,
            Name = "seed_data",
            DisplayName = "Training Data",
            Config = new SeedBlockConfig("data/train.jsonl", DataFileType.Jsonl, Streaming: false, UseIndexCache: true)
        };

        var json = JsonSerializer.Serialize(block, Options);
        var loaded = JsonSerializer.Deserialize<RecipeBlock>(json, Options);

        Assert.That(json, Does.Contain("\"kind\":\"seed\""));
        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.Config, Is.TypeOf<SeedBlockConfig>());
    }

    [Test]
    public void Deserialize_RecipeBlockWithoutConfig_Throws()
    {
        const string json = """{"id":"b1","type":"Seed","name":"seed_data","displayName":"Training Data"}""";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<RecipeBlock>(json, Options));
    }

    [Test]
    public void Deserialize_UnknownKind_Throws()
    {
        const string json = """{"kind":"mystery"}""";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<BlockConfig>(json, Options));
    }

    private static BlockConfig RoundTrip(BlockConfig config)
    {
        var json = JsonSerializer.Serialize(config, Options);
        var loaded = JsonSerializer.Deserialize<BlockConfig>(json, Options);

        Assert.That(loaded, Is.Not.Null);
        return loaded!;
    }
}