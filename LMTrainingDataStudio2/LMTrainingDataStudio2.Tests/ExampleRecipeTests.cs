using LMTrainingDataStudio2.Localization;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services;

namespace LMTrainingDataStudio2.Tests;

/// <summary>
/// Example recipes shipped in /examples. Run WriteExamples once after changing presets.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class ExampleRecipeTests
{
    [Test]
    public async Task BundledExamples_LoadAndValidate()
    {
        var directory = GetExamplesDirectory();
        Assert.That(Directory.Exists(directory), Is.True, $"Examples directory not found: {directory}");

        var files = Directory.GetFiles(directory, "*.recipe.json");
        Assert.That(files, Is.Not.Empty);

        var store = new RecipeStore();
        var validator = new RecipeValidationService();

        foreach (var file in files)
        {
            var recipe = await store.LoadAsync(file);
            var result = validator.Validate(recipe);
            Assert.That(result.Issues.Where(issue => issue.Severity == ValidationSeverity.Error),
                Is.Empty, $"{Path.GetFileName(file)} has validation errors.");
        }
    }

    [Test]
    [Explicit("Writes example recipe files; run manually after preset changes.")]
    public async Task WriteExamples()
    {
        var directory = GetExamplesDirectory();
        Directory.CreateDirectory(directory);

        var store = new RecipeStore();
        LocalizationService.Instance.SetLanguage(LocalizationService.English);
        foreach (var preset in RecipePresetCatalog.All)
        {
            var recipe = preset.Create();
            recipe.Id = preset.Id;
            recipe.CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            recipe.ModifiedAt = recipe.CreatedAt;
            NormalizePortIds(recipe);
            var path = Path.Combine(directory, $"{preset.Id}.recipe.json");
            await store.SaveAsync(recipe, path);
        }
    }

    private static void NormalizePortIds(Recipe recipe)
    {
        foreach (var block in recipe.Blocks)
        {
            var inputIds = new Dictionary<string, string>(StringComparer.Ordinal);
            var outputIds = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < block.InputPorts.Count; i++)
            {
                inputIds[block.InputPorts[i].Id] = $"{block.Id}_in_{i}";
                block.InputPorts[i].Id = $"{block.Id}_in_{i}";
            }

            for (var i = 0; i < block.OutputPorts.Count; i++)
            {
                outputIds[block.OutputPorts[i].Id] = $"{block.Id}_out_{i}";
                block.OutputPorts[i].Id = $"{block.Id}_out_{i}";
            }

            foreach (var edge in recipe.Edges.Where(edge => edge.SourceBlockId == block.Id))
            {
                if (outputIds.TryGetValue(edge.SourcePortId, out var id))
                {
                    edge.SourcePortId = id;
                }
            }

            foreach (var edge in recipe.Edges.Where(edge => edge.TargetBlockId == block.Id))
            {
                if (inputIds.TryGetValue(edge.TargetPortId, out var id))
                {
                    edge.TargetPortId = id;
                }
            }
        }
    }

    public static string GetExamplesDirectory()
    {
        var root = GetRepositoryRoot();
        return Path.Combine(root, "examples");
    }

    private static string GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "LMTrainingDataStudio2.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.Parent?.FullName ?? Directory.GetCurrentDirectory();
    }
}
