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
        var seed = CreateBlock("seed1", BlockType.Seed, "seed_data");
        seed.Properties["source_path"] = "data.jsonl";
        seed.OutputPorts.Add(CreatePort("seed_out", PortDirection.Output));
        var validator = CreateBlock("val1", BlockType.Validator, "code_check");
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
    public void Validate_LlmWithoutModelAlias_ReportsWarning()
    {
        var recipe = new Recipe();
        recipe.Blocks.Add(CreateBlock("llm1", BlockType.Llm, "generator"));

        var result = _service.Validate(recipe);

        Assert.That(result.IsValid, Is.True);
        Assert.That(result.Issues.Any(i =>
            i.Severity == ValidationSeverity.Warning && i.Message.Contains("no model alias")), Is.True);
    }

    private static RecipeBlock CreateBlock(string id, BlockType type, string name) => new()
    {
        Id = id,
        Type = type,
        Name = name,
        DisplayName = name
    };

    private static BlockPort CreatePort(string id, PortDirection direction) => new()
    {
        Id = id,
        Name = id,
        Direction = direction
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
