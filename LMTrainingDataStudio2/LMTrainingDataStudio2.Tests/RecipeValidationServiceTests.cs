using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class RecipeValidationServiceTests
{
    private readonly RecipeValidationService _service = new();

    [Test]
    public void Validate_EmptyRecipe_ReturnsWarning()
    {
        var result = _service.Validate(new Recipe());

        Assert.That(result.IsValid, Is.True);
        Assert.That(result.Issues, Has.Count.EqualTo(1));
        Assert.That(result.Issues[0].Severity, Is.EqualTo(ValidationSeverity.Warning));
    }

    [Test]
    public void Validate_ValidGraph_IsValidWithoutIssues()
    {
        var recipe = new Recipe();
        var seed = CreateBlock("seed1", BlockType.Seed, "seed_data",
            new SeedBlockConfig("data.jsonl", DataFileType.Jsonl, Streaming: false, UseIndexCache: true));
        seed.OutputPorts.Add(CreatePort("seed_out", PortDirection.Output));
        var validator = CreateBlock("val1", BlockType.Validator, "code_check",
            new ValidatorBlockConfig("expression", "len(messages) > 0", RejectOnFailure: true));
        validator.InputPorts.Add(CreatePort("val_in", PortDirection.Input));

        recipe.Blocks.AddRange(new[] { seed, validator });
        recipe.Edges.Add(CreateEdge("edge1", "seed1", "seed_out", "val1", "val_in"));

        var result = _service.Validate(recipe);

        Assert.That(result.IsValid, Is.True);
        Assert.That(result.Issues, Is.Empty);
    }

    [Test]
    public void Validate_DuplicateBlockNames_ReportsError()
    {
        var recipe = new Recipe();
        var first = CreateBlock("b1", BlockType.Validator, "dup");
        var second = CreateBlock("b2", BlockType.Validator, "dup");
        recipe.Blocks.AddRange(new[] { first, second });

        var result = _service.Validate(recipe);

        Assert.That(result.Issues.Any(i =>
            i.Severity == ValidationSeverity.Error && i.Message.Contains("Duplicate block name")), Is.True);
    }

    [Test]
    public void Validate_Cycle_ReportsError()
    {
        var recipe = new Recipe();
        var a = CreateBlock("a", BlockType.Validator, "a");
        a.InputPorts.Add(CreatePort("a_in", PortDirection.Input));
        a.OutputPorts.Add(CreatePort("a_out", PortDirection.Output));
        var b = CreateBlock("b", BlockType.Validator, "b");
        b.InputPorts.Add(CreatePort("b_in", PortDirection.Input));
        b.OutputPorts.Add(CreatePort("b_out", PortDirection.Output));

        recipe.Blocks.AddRange(new[] { a, b });
        recipe.Edges.Add(CreateEdge("e1", "a", "a_out", "b", "b_in"));
        recipe.Edges.Add(CreateEdge("e2", "b", "b_out", "a", "a_in"));

        var result = _service.Validate(recipe);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Issues.Any(i => i.Message.Contains("cycle")), Is.True);
    }

    [Test]
    public void Validate_DanglingEdge_ReportsMissingBlocksAndPorts()
    {
        var recipe = new Recipe();
        var block = CreateBlock("b1", BlockType.Validator, "only_block");
        recipe.Blocks.Add(block);
        recipe.Edges.Add(CreateEdge("e1", "missing_source", "missing_out", "missing_target", "missing_in"));

        var result = _service.Validate(recipe);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Issues.Count(i => i.Severity == ValidationSeverity.Error), Is.EqualTo(4));
    }

    [Test]
    public void Validate_SeedWithoutSource_ReportsWarning()
    {
        var recipe = new Recipe();
        recipe.Blocks.Add(CreateBlock("seed1", BlockType.Seed, "seed_data"));

        var result = _service.Validate(recipe);

        Assert.That(result.IsValid, Is.True);
        Assert.That(result.Issues.Any(i =>
            i.Severity == ValidationSeverity.Warning && i.Message.Contains("no data source")), Is.True);
    }

    [Test]
    public void Validate_LlmWithInvalidTimeout_ReportsWarning()
    {
        var recipe = new Recipe();
        recipe.Blocks.Add(CreateBlock("llm1", BlockType.Llm, "generator",
            new LlmBlockConfig("openai_chat", "gpt-4o", Temperature: 0.7, MaxTokens: 2048, Prompt: null,
                TimeoutSeconds: 0)));

        var result = _service.Validate(recipe);

        Assert.That(result.Issues.Any(i =>
            i.Severity == ValidationSeverity.Warning && i.Message.Contains("timeout")), Is.True);
    }

    [Test]
    public void Validate_LlmWithNegativeRetries_ReportsWarning()
    {
        var recipe = new Recipe();
        recipe.Blocks.Add(CreateBlock("llm1", BlockType.Llm, "generator",
            new LlmBlockConfig("openai_chat", "gpt-4o", Temperature: 0.7, MaxTokens: 2048, Prompt: null,
                MaxRetries: -1)));

        var result = _service.Validate(recipe);

        Assert.That(result.Issues.Any(i =>
            i.Severity == ValidationSeverity.Warning && i.Message.Contains("max retries")), Is.True);
    }

    [Test]
    public void Validate_LlmWithUnknownProvider_ReportsWarning()
    {
        var recipe = new Recipe();
        recipe.Blocks.Add(CreateBlock("llm1", BlockType.Llm, "generator",
            new LlmBlockConfig("mystery", "gpt-4o", Temperature: 0.7, MaxTokens: 2048, Prompt: null)));

        var result = _service.Validate(recipe);

        Assert.That(result.Issues.Any(i =>
            i.Severity == ValidationSeverity.Warning && i.Message.Contains("unknown provider")), Is.True);
    }

    [Test]
    public void Validate_SeedWithNonJsonlFile_ReportsWarning()
    {
        var recipe = new Recipe();
        recipe.Blocks.Add(CreateBlock("seed1", BlockType.Seed, "seed_data",
            new SeedBlockConfig("data.csv", DataFileType.Csv, Streaming: false, UseIndexCache: true)));

        var result = _service.Validate(recipe);

        Assert.That(result.Issues.Any(i =>
            i.Severity == ValidationSeverity.Warning && i.Message.Contains("only JSONL is executable")), Is.True);
    }

    [Test]
    public void Validate_LlmWithoutModelAlias_ReportsWarning()
    {
        var recipe = new Recipe();
        recipe.Blocks.Add(CreateBlock("llm1", BlockType.Llm, "generator"));

        var result = _service.Validate(recipe);

        Assert.That(result.IsValid, Is.True);
        Assert.That(result.Issues.Any(i =>
            i.Severity == ValidationSeverity.Warning && i.Message.Contains("no model alias")), Is.True);
    }

    [Test]
    public void Validate_ConfigKindMismatch_ReportsError()
    {
        var recipe = new Recipe();
        recipe.Blocks.Add(CreateBlock("b1", BlockType.Seed, "seed_data",
            new ValidatorBlockConfig("expression", "len(rows) > 0", RejectOnFailure: true)));

        var result = _service.Validate(recipe);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Issues.Any(i =>
            i.Severity == ValidationSeverity.Error &&
            i.Message.Contains("uses ValidatorBlockConfig")), Is.True);
    }

    [Test]
    public void Validate_ExpressionWithoutTemplate_ReportsWarning()
    {
        var recipe = new Recipe();
        recipe.Blocks.Add(CreateBlock("e1", BlockType.Expression, "render"));

        var result = _service.Validate(recipe);

        Assert.That(result.Issues.Any(i =>
            i.Severity == ValidationSeverity.Warning && i.Message.Contains("no template")), Is.True);
    }

    [Test]
    public void Validate_ValidatorWithoutRule_ReportsWarning()
    {
        var recipe = new Recipe();
        recipe.Blocks.Add(CreateBlock("v1", BlockType.Validator, "check"));

        var result = _service.Validate(recipe);

        Assert.That(result.Issues.Any(i =>
            i.Severity == ValidationSeverity.Warning && i.Message.Contains("no rule")), Is.True);
    }

    [Test]
    public void Validate_SamplerWithoutCountOrRatio_ReportsWarning()
    {
        var recipe = new Recipe();
        recipe.Blocks.Add(CreateBlock("s1", BlockType.Sampler, "sample"));

        var result = _service.Validate(recipe);

        Assert.That(result.Issues.Any(i =>
            i.Severity == ValidationSeverity.Warning && i.Message.Contains("neither count nor ratio")), Is.True);
    }

    [Test]
    public void Validate_SamplerWithCount_NoSamplerWarning()
    {
        var recipe = new Recipe();
        recipe.Blocks.Add(CreateBlock("s1", BlockType.Sampler, "sample",
            new SamplerBlockConfig(Count: 100, Ratio: null, Seed: 42, Splits: Array.Empty<string>())));

        var result = _service.Validate(recipe);

        Assert.That(result.Issues.Any(i => i.Message.Contains("Sampler")), Is.False);
    }

    [Test]
    public void Validate_ToolProfileWithoutTools_ReportsWarning()
    {
        var recipe = new Recipe();
        recipe.Blocks.Add(CreateBlock("t1", BlockType.ToolProfile, "tools"));

        var result = _service.Validate(recipe);

        Assert.That(result.Issues.Any(i =>
            i.Severity == ValidationSeverity.Warning && i.Message.Contains("no tools")), Is.True);
    }

    [Test]
    public void Validate_IsolatedBlock_ReportsWarning()
    {
        var recipe = new Recipe();
        var source = CreateBlock("a", BlockType.Validator, "a");
        source.OutputPorts.Add(CreatePort("a_out", PortDirection.Output));
        var sink = CreateBlock("b", BlockType.Validator, "b");
        sink.InputPorts.Add(CreatePort("b_in", PortDirection.Input));
        var isolated = CreateBlock("c", BlockType.Validator, "c");

        recipe.Blocks.AddRange(new[] { source, sink, isolated });
        recipe.Edges.Add(CreateEdge("e1", "a", "a_out", "b", "b_in"));

        var result = _service.Validate(recipe);

        Assert.That(result.Issues.Any(i =>
            i.Severity == ValidationSeverity.Warning &&
            i.BlockId == "c" &&
            i.Message.Contains("not connected")), Is.True);
    }

    [Test]
    public void Validate_DuplicateEdge_ReportsError()
    {
        var recipe = new Recipe();
        var source = CreateBlock("a", BlockType.Validator, "a");
        source.OutputPorts.Add(CreatePort("a_out", PortDirection.Output));
        var sink = CreateBlock("b", BlockType.Validator, "b");
        sink.InputPorts.Add(CreatePort("b_in", PortDirection.Input));

        recipe.Blocks.AddRange(new[] { source, sink });
        var edge = CreateEdge("e1", "a", "a_out", "b", "b_in");
        recipe.Edges.Add(edge);
        recipe.Edges.Add(new RecipeEdge
        {
            Id = "e2",
            SourceBlockId = edge.SourceBlockId,
            SourcePortId = edge.SourcePortId,
            TargetBlockId = edge.TargetBlockId,
            TargetPortId = edge.TargetPortId
        });

        var result = _service.Validate(recipe);

        Assert.That(result.Issues.Any(i =>
            i.Severity == ValidationSeverity.Error && i.Message.Contains("Duplicate edge")), Is.True);
    }

    [Test]
    public void Validate_WrongPortDirections_ReportErrors()
    {
        var recipe = new Recipe();
        var source = CreateBlock("a", BlockType.Validator, "a");
        source.InputPorts.Add(CreatePort("a_in", PortDirection.Input));
        var sink = CreateBlock("b", BlockType.Validator, "b");
        sink.OutputPorts.Add(CreatePort("b_out", PortDirection.Output));

        recipe.Blocks.AddRange(new[] { source, sink });
        recipe.Edges.Add(CreateEdge("e1", "a", "a_in", "b", "b_out"));

        var result = _service.Validate(recipe);

        Assert.That(result.Issues.Any(i => i.Message.Contains("must be an output")), Is.True);
        Assert.That(result.Issues.Any(i => i.Message.Contains("must be an input")), Is.True);
    }

    [Test]
    public void Validate_PortFromOtherBlock_ReportsError()
    {
        var recipe = new Recipe();
        var source = CreateBlock("a", BlockType.Validator, "a");
        source.InputPorts.Add(CreatePort("a_in", PortDirection.Input));
        var sink = CreateBlock("b", BlockType.Validator, "b");
        sink.InputPorts.Add(CreatePort("b_in", PortDirection.Input));
        sink.OutputPorts.Add(CreatePort("b_out", PortDirection.Output));

        recipe.Blocks.AddRange(new[] { source, sink });
        recipe.Edges.Add(CreateEdge("e1", "a", "b_out", "b", "b_in"));

        var result = _service.Validate(recipe);

        Assert.That(result.Issues.Any(i =>
            i.Severity == ValidationSeverity.Error && i.Message.Contains("does not belong")), Is.True);
    }

    [Test]
    public void Validate_IncompatiblePortTypes_ReportsError()
    {
        var recipe = new Recipe();
        var source = CreateBlock("a", BlockType.Validator, "a");
        source.OutputPorts.Add(CreatePort("a_out", PortDirection.Output, "jsonl"));
        var sink = CreateBlock("b", BlockType.Validator, "b");
        sink.InputPorts.Add(CreatePort("b_in", PortDirection.Input, "messages[]"));

        recipe.Blocks.AddRange(new[] { source, sink });
        recipe.Edges.Add(CreateEdge("e1", "a", "a_out", "b", "b_in"));

        var result = _service.Validate(recipe);

        Assert.That(result.Issues.Any(i =>
            i.Severity == ValidationSeverity.Error && i.Message.Contains("Port type mismatch")), Is.True);
    }

    [Test]
    public void Validate_AnyPortType_IsCompatible()
    {
        var recipe = new Recipe();
        var source = CreateBlock("a", BlockType.Validator, "a");
        source.OutputPorts.Add(CreatePort("a_out", PortDirection.Output, "jsonl"));
        var sink = CreateBlock("b", BlockType.Validator, "b");
        sink.InputPorts.Add(CreatePort("b_in", PortDirection.Input));

        recipe.Blocks.AddRange(new[] { source, sink });
        recipe.Edges.Add(CreateEdge("e1", "a", "a_out", "b", "b_in"));

        var result = _service.Validate(recipe);

        Assert.That(result.Issues.Any(i => i.Message.Contains("Port type mismatch")), Is.False);
    }

    [Test]
    public void Validate_QuerySourceWithoutQueries_ReportsWarning()
    {
        var recipe = new Recipe();
        recipe.Blocks.Add(CreateBlock("q", BlockType.QuerySource, "queries",
            new QuerySourceBlockConfig(Array.Empty<string>())));

        var result = _service.Validate(recipe);

        Assert.That(result.Issues.Any(i =>
            i.Severity == ValidationSeverity.Warning && i.Message.Contains("no queries")), Is.True);
    }

    [Test]
    public void Validate_SimulationWithInvalidSettings_ReportsWarnings()
    {
        var recipe = new Recipe();
        recipe.Blocks.Add(CreateBlock("sim1", BlockType.Simulation, "sim_missing_model",
            new SimulationBlockConfig(string.Empty)));
        recipe.Blocks.Add(CreateBlock("sim2", BlockType.Simulation, "sim_rounds",
            new SimulationBlockConfig("model", MaxRounds: 0)));
        recipe.Blocks.Add(CreateBlock("sim3", BlockType.Simulation, "sim_timeout",
            new SimulationBlockConfig("model", TimeoutSeconds: 0)));

        var result = _service.Validate(recipe);

        Assert.That(result.Issues.Any(i => i.Message.Contains("no model alias")), Is.True);
        Assert.That(result.Issues.Any(i => i.Message.Contains("max rounds")), Is.True);
        Assert.That(result.Issues.Any(i => i.Message.Contains("timeout")), Is.True);
    }

    [Test]
    public void Validate_SimulationWithDuplicateTools_ReportsWarning()
    {
        var recipe = new Recipe();
        recipe.Blocks.Add(CreateBlock("sim", BlockType.Simulation, "sim",
            new SimulationBlockConfig(
                "model",
                Tools: new[]
                {
                    new SimulatedToolRule { Name = "python" },
                    new SimulatedToolRule { Name = "PYTHON" }
                })));

        var result = _service.Validate(recipe);

        Assert.That(result.Issues.Any(i => i.Message.Contains("duplicate tool names")), Is.True);
    }

    [Test]
    public void Validate_SimulationProviderAndSandboxSettings_ReportWarnings()
    {
        var recipe = new Recipe();
        recipe.Blocks.Add(CreateBlock("sim1", BlockType.Simulation, "sim_endpoint",
            new SimulationBlockConfig(
                "model",
                ModelMode: SimulationModelMode.Provider,
                Endpoint: "not-a-url")));
        recipe.Blocks.Add(CreateBlock("sim2", BlockType.Simulation, "sim_concurrency",
            new SimulationBlockConfig("model", MaxConcurrency: 0)));
        recipe.Blocks.Add(CreateBlock("sim3", BlockType.Simulation, "sim_failure_samples",
            new SimulationBlockConfig("model", MaxFailureSamples: -1)));
        recipe.Blocks.Add(CreateBlock("sim4", BlockType.Simulation, "sim_sandbox",
            new SimulationBlockConfig(
                "model",
                ToolMode: ToolExecutionMode.Real,
                Sandbox: new SandboxLimits { TimeoutMs = -1 })));
        recipe.Blocks.Add(CreateBlock("sim5", BlockType.Simulation, "sim_real_tools",
            new SimulationBlockConfig(
                "model",
                ToolMode: ToolExecutionMode.Real,
                Tools: new[] { new SimulatedToolRule { Name = "python" }, new SimulatedToolRule { Name = "custom" } })));

        var result = _service.Validate(recipe);

        Assert.That(result.Issues.Any(i => i.Message.Contains("absolute http(s) URL")), Is.True);
        Assert.That(result.Issues.Any(i => i.Message.Contains("max concurrency")), Is.True);
        Assert.That(result.Issues.Any(i => i.Message.Contains("max failure samples")), Is.True);
        Assert.That(result.Issues.Any(i => i.Message.Contains("negative sandbox limits")), Is.True);
        Assert.That(result.Issues.Any(i => i.Message.Contains("without a real sandbox executor")), Is.True);
    }

    [Test]
    public void Validate_CleanerWithoutRules_ReportsWarning()
    {
        var recipe = new Recipe();
        recipe.Blocks.Add(CreateBlock("clean", BlockType.Cleaner, "clean",
            new CleanerBlockConfig(new DatasetCleanupRules())));

        var result = _service.Validate(recipe);

        Assert.That(result.Issues.Any(i =>
            i.Severity == ValidationSeverity.Warning && i.Message.Contains("no cleanup rules")), Is.True);
    }

    [Test]
    public void Validate_SftExportWithoutPath_ReportsWarning()
    {
        var recipe = new Recipe();
        recipe.Blocks.Add(CreateBlock("export", BlockType.SftExport, "export",
            new SftExportBlockConfig(string.Empty)));

        var result = _service.Validate(recipe);

        Assert.That(result.Issues.Any(i => i.Message.Contains("no output path")), Is.True);
    }

    private static RecipeBlock CreateBlock(string id, BlockType type, string name, BlockConfig? config = null) => new()
    {
        Id = id,
        Type = type,
        Name = name,
        DisplayName = name,
        Config = config ?? BlockConfig.CreateDefault(type)
    };

    private static BlockPort CreatePort(string id, PortDirection direction, string dataType = "any") => new()
    {
        Id = id,
        Name = id,
        Direction = direction,
        DataType = dataType
    };

    private static RecipeEdge CreateEdge(string id, string sourceBlock, string sourcePort, string targetBlock, string targetPort) => new()
    {
        Id = id,
        SourceBlockId = sourceBlock,
        SourcePortId = sourcePort,
        TargetBlockId = targetBlock,
        TargetPortId = targetPort
    };
}
