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
        var config = new LlmBlockConfig(
            "openai", "gpt-4o",
            Temperature: 0.2, MaxTokens: 4096,
            Prompt: "Rewrite {{ content }}",
            ToolProfileName: "agent_tools");

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
        var config = new ValidatorBlockConfig(
            "schema",
            """{"type":"object"}""",
            RejectOnFailure: true,
            RejectOutputPath: "out/rejected.jsonl");

        var loaded = RoundTrip(config);

        Assert.That(loaded, Is.TypeOf<ValidatorBlockConfig>());
        Assert.That(loaded, Is.EqualTo(config));
    }

    [Test]
    public void RoundTrip_SamplerConfig_PreservesFields()
    {
        var config = new SamplerBlockConfig(
            Count: 1000, Ratio: null, Seed: 7,
            Splits: new[] { "train", "eval" },
            OutputDirectory: "out/splits");

        var loaded = RoundTrip(config);

        Assert.That(loaded, Is.TypeOf<SamplerBlockConfig>());
        var sampler = (SamplerBlockConfig)loaded;
        Assert.That(sampler.Count, Is.EqualTo(config.Count));
        Assert.That(sampler.Ratio, Is.Null);
        Assert.That(sampler.Seed, Is.EqualTo(config.Seed));
        Assert.That(sampler.Splits, Is.EqualTo(config.Splits));
        Assert.That(sampler.OutputDirectory, Is.EqualTo("out/splits"));
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
            (new ToolProfileBlockConfig(new ToolProfile()), "tool_profile"),
            (new QuerySourceBlockConfig(new[] { "q" }, QuerySetMode.SameQuery, 2, "sys"), "query_source"),
            (new SimulationBlockConfig("sim", MaxRounds: 3), "simulation"),
            (new CleanerBlockConfig(new DatasetCleanupRules { NormalizeWhitespace = true }), "cleaner"),
            (new SftExportBlockConfig("out.jsonl"), "sft_export")
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

    [Test]
    public void RoundTrip_QuerySourceConfig_PreservesFields()
    {
        var config = new QuerySourceBlockConfig(
            new[] { "q1", "q2" },
            QuerySetMode.SameQuery,
            Repeat: 3,
            SystemPrompt: "be brief");

        var loaded = (QuerySourceBlockConfig)RoundTrip(config);

        Assert.That(loaded.Queries, Is.EqualTo(config.Queries));
        Assert.That(loaded.Mode, Is.EqualTo(QuerySetMode.SameQuery));
        Assert.That(loaded.Repeat, Is.EqualTo(3));
        Assert.That(loaded.SystemPrompt, Is.EqualTo("be brief"));
    }

    [Test]
    public void RoundTrip_SimulationConfig_PreservesNestedRules()
    {
        var config = new SimulationBlockConfig(
            "sim-model",
            MaxRounds: 5,
            SystemPrompt: "system",
            Tools: new[]
            {
                new SimulatedToolRule
                {
                    Name = "python",
                    Description = "Run Python",
                    ParametersJson = """{"type":"object","properties":{"code":{"type":"string"}}}""",
                    ResponseTemplate = "{{arguments}}",
                    DelayMs = 10,
                    FailuresBeforeSuccess = 1,
                    TimeoutMs = 1000
                }
            },
            Rules: new ToolHarnessRules
            {
                AllowedTools = new[] { "python" },
                DefaultTimeoutMs = 2000,
                MaxRetries = 2,
                RetryDelayMs = 1,
                AbortOnToolFailure = true
            },
            Script: new[]
            {
                new SimulatedTurn(null, new[] { new SimulatedToolCallSpec("python", """{"code":"1"}""") }),
                new SimulatedTurn("done", null)
            },
            FinalContent: "final",
            Provider: "anthropic",
            Temperature: 0.2,
            MaxTokens: 512,
            TimeoutSeconds: 30,
            ModelMaxRetries: 1,
            EmitIncompleteTrajectories: true,
            ModelMode: SimulationModelMode.Provider,
            ToolMode: ToolExecutionMode.Real,
            Sandbox: new SandboxLimits
            {
                TimeoutMs = 5000,
                MaxOutputBytes = 4096,
                MaxMemoryMb = 256,
                MaxProcesses = 4,
                AllowNetwork = true,
                AllowPrivateNetwork = true,
                MaxFetchBytes = 8192,
                PythonExecutable = "python3",
                PowerShellExecutable = "pwsh",
                BashExecutable = "bash"
            },
            Endpoint: "https://proxy.example/v1",
            MaxConcurrency: 6,
            FailureOutputPath: "failures/out.jsonl",
            MaxFailureSamples: 25,
            InputCostPerMillionTokens: 1.5,
            OutputCostPerMillionTokens: 3.5,
            ToolProfileName: "agent_tools");

        var loaded = (SimulationBlockConfig)RoundTrip(config);

        Assert.That(loaded.ModelAlias, Is.EqualTo("sim-model"));
        Assert.That(loaded.MaxRounds, Is.EqualTo(5));
        Assert.That(loaded.Provider, Is.EqualTo("anthropic"));
        Assert.That(loaded.TimeoutSeconds, Is.EqualTo(30));
        Assert.That(loaded.ModelMaxRetries, Is.EqualTo(1));
        Assert.That(loaded.EmitIncompleteTrajectories, Is.True);
        Assert.That(loaded.ModelMode, Is.EqualTo(SimulationModelMode.Provider));
        Assert.That(loaded.ToolMode, Is.EqualTo(ToolExecutionMode.Real));
        Assert.That(loaded.Endpoint, Is.EqualTo("https://proxy.example/v1"));
        Assert.That(loaded.ToolProfileName, Is.EqualTo("agent_tools"));
        Assert.That(loaded.MaxConcurrency, Is.EqualTo(6));
        Assert.That(loaded.FailureOutputPath, Is.EqualTo("failures/out.jsonl"));
        Assert.That(loaded.MaxFailureSamples, Is.EqualTo(25));
        Assert.That(loaded.InputCostPerMillionTokens, Is.EqualTo(1.5));
        Assert.That(loaded.OutputCostPerMillionTokens, Is.EqualTo(3.5));

        Assert.That(loaded.Sandbox, Is.Not.Null);
        Assert.That(loaded.Sandbox!.TimeoutMs, Is.EqualTo(5000));
        Assert.That(loaded.Sandbox.MaxOutputBytes, Is.EqualTo(4096));
        Assert.That(loaded.Sandbox.MaxMemoryMb, Is.EqualTo(256));
        Assert.That(loaded.Sandbox.MaxProcesses, Is.EqualTo(4));
        Assert.That(loaded.Sandbox.AllowNetwork, Is.True);
        Assert.That(loaded.Sandbox.AllowPrivateNetwork, Is.True);
        Assert.That(loaded.Sandbox.MaxFetchBytes, Is.EqualTo(8192));
        Assert.That(loaded.Sandbox.PythonExecutable, Is.EqualTo("python3"));
        Assert.That(loaded.Sandbox.PowerShellExecutable, Is.EqualTo("pwsh"));
        Assert.That(loaded.Sandbox.BashExecutable, Is.EqualTo("bash"));

        Assert.That(loaded.Tools, Has.Length.EqualTo(1));
        Assert.That(loaded.Tools![0].Name, Is.EqualTo("python"));
        Assert.That(loaded.Tools[0].FailuresBeforeSuccess, Is.EqualTo(1));
        Assert.That(loaded.Tools[0].TimeoutMs, Is.EqualTo(1000));

        Assert.That(loaded.Rules, Is.Not.Null);
        Assert.That(loaded.Rules!.AllowedTools, Is.EqualTo(new[] { "python" }));
        Assert.That(loaded.Rules.MaxRetries, Is.EqualTo(2));
        Assert.That(loaded.Rules.AbortOnToolFailure, Is.True);

        Assert.That(loaded.Script, Has.Length.EqualTo(2));
        Assert.That(loaded.Script![0].ToolCalls![0].Name, Is.EqualTo("python"));
        Assert.That(loaded.Script[0].ToolCalls![0].ArgumentsJson, Is.EqualTo("""{"code":"1"}"""));
        Assert.That(loaded.Script[1].Content, Is.EqualTo("done"));
    }

    [Test]
    public void RoundTrip_CleanerConfig_PreservesRules()
    {
        var config = new CleanerBlockConfig(new DatasetCleanupRules
        {
            RegexReplacements = new[] { new RegexReplacementRule(@"\s+$", "", IgnoreCase: true) },
            NormalizeWhitespace = true,
            NormalizeNewlines = true,
            StripAnsi = true,
            RemoveReasoningContent = true,
            DropEmptyMessages = true
        });

        var loaded = (CleanerBlockConfig)RoundTrip(config);

        Assert.That(loaded.Rules.RegexReplacements, Has.Length.EqualTo(1));
        Assert.That(loaded.Rules.RegexReplacements![0].Pattern, Is.EqualTo(@"\s+$"));
        Assert.That(loaded.Rules.RegexReplacements[0].IgnoreCase, Is.True);
        Assert.That(loaded.Rules.NormalizeWhitespace, Is.True);
        Assert.That(loaded.Rules.StripAnsi, Is.True);
        Assert.That(loaded.Rules.RemoveReasoningContent, Is.True);
        Assert.That(loaded.Rules.DropEmptyMessages, Is.True);
    }

    [Test]
    public void RoundTrip_SftExportConfig_PreservesPath()
    {
        var loaded = (SftExportBlockConfig)RoundTrip(new SftExportBlockConfig("data/sft.jsonl"));

        Assert.That(loaded.OutputPath, Is.EqualTo("data/sft.jsonl"));
    }

    [Test]
    public void DeepClone_SimulationConfig_DoesNotShareArrays()
    {
        var config = new SimulationBlockConfig(
            "sim",
            Tools: new[] { new SimulatedToolRule { Name = "python" } },
            Rules: new ToolHarnessRules { AllowedTools = new[] { "python" } },
            Sandbox: new SandboxLimits { TimeoutMs = 1000 },
            Script: new[] { new SimulatedTurn(null, new[] { new SimulatedToolCallSpec("python") }) });

        var clone = (SimulationBlockConfig)config.DeepClone();

        Assert.That(clone.Tools, Is.Not.SameAs(config.Tools));
        Assert.That(clone.Rules, Is.Not.SameAs(config.Rules));
        Assert.That(clone.Sandbox, Is.Not.SameAs(config.Sandbox));
        Assert.That(clone.Rules!.AllowedTools, Is.Not.SameAs(config.Rules!.AllowedTools));
        Assert.That(clone.Script, Is.Not.SameAs(config.Script));
        Assert.That(clone.Script![0].ToolCalls, Is.Not.SameAs(config.Script![0].ToolCalls));
    }

    private static BlockConfig RoundTrip(BlockConfig config)
    {
        var json = JsonSerializer.Serialize(config, Options);
        var loaded = JsonSerializer.Deserialize<BlockConfig>(json, Options);

        Assert.That(loaded, Is.Not.Null);
        return loaded!;
    }
}