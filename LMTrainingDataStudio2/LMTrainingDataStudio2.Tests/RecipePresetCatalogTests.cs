using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class RecipePresetCatalogTests
{
    private readonly RecipeValidationService _validation = new();

    [Test]
    public void AllPresets_CreateValidConnectedRecipes()
    {
        Assert.That(RecipePresetCatalog.All, Has.Count.GreaterThanOrEqualTo(5));

        foreach (var preset in RecipePresetCatalog.All)
        {
            var recipe = preset.Create();

            Assert.That(recipe.Blocks, Has.Count.GreaterThanOrEqualTo(3), preset.Id);
            Assert.That(recipe.Edges, Has.Count.EqualTo(recipe.Blocks.Count - 1), preset.Id);
            Assert.That(recipe.Blocks.Select(block => block.Name).Distinct().Count(),
                Is.EqualTo(recipe.Blocks.Count), $"{preset.Id}: duplicate block names");

            foreach (var block in recipe.Blocks)
            {
                Assert.That(block.Config.GetBlockType(), Is.EqualTo(block.Type), $"{preset.Id}/{block.Name}");
                Assert.That(block.InputPorts.Select(port => port.Name),
                    Is.EqualTo(BlockPortSchema.CreateInputPorts(block.Type).Select(port => port.Name)));
                Assert.That(block.OutputPorts.Select(port => port.Name),
                    Is.EqualTo(BlockPortSchema.CreateOutputPorts(block.Type, block.Config).Select(port => port.Name)));
            }

            var result = _validation.Validate(recipe);
            Assert.That(result.IsValid, Is.True,
                $"{preset.Id}: {string.Join("; ", result.Issues.Where(issue => issue.Severity == ValidationSeverity.Error).Select(issue => issue.Message))}");
        }
    }

    [Test]
    public void Create_ReturnsFreshRecipeEachTime()
    {
        var first = RecipePresetCatalog.Create("llm_batch");
        var second = RecipePresetCatalog.Create("llm_batch");

        Assert.That(first, Is.Not.SameAs(second));
        Assert.That(first.Blocks, Is.Not.SameAs(second.Blocks));
        Assert.That(first.Blocks[0].Config, Is.Not.SameAs(second.Blocks[0].Config));
    }

    [Test]
    public void Find_UnknownPreset_ReturnsNull()
    {
        Assert.That(RecipePresetCatalog.Find("does_not_exist"), Is.Null);
    }

    [Test]
    public void Create_UnknownPreset_Throws()
    {
        Assert.Throws<ArgumentException>(() => RecipePresetCatalog.Create("does_not_exist"));
    }
}
