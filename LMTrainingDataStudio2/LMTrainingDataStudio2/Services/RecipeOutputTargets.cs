using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services.Executors;

namespace LMTrainingDataStudio2.Services;

/// <summary>
/// A path written by a recipe block. Sampler output directories are kept as directory
/// targets and also expanded into their concrete split files.
/// </summary>
public readonly record struct RecipeOutputTarget(
    string BlockId,
    string BlockName,
    string Path,
    bool IsDirectory);

/// <summary>
/// Collects the output targets of a recipe so static validation and run-time locking
/// use the same definition of "what this recipe writes".
/// </summary>
public static class RecipeOutputTargets
{
    public static IReadOnlyList<RecipeOutputTarget> Collect(Recipe recipe)
    {
        var targets = new List<RecipeOutputTarget>();
        foreach (var block in recipe.Blocks)
        {
            switch (block.Config)
            {
                case LlmBlockConfig llm when !string.IsNullOrWhiteSpace(llm.FailureOutputPath):
                    targets.Add(new RecipeOutputTarget(block.Id, block.Name, llm.FailureOutputPath, false));
                    break;
                case SimulationBlockConfig simulation when !string.IsNullOrWhiteSpace(simulation.FailureOutputPath):
                    targets.Add(new RecipeOutputTarget(
                        block.Id, block.Name, simulation.FailureOutputPath, false));
                    break;
                case ValidatorBlockConfig validator when !string.IsNullOrWhiteSpace(validator.RejectOutputPath):
                    targets.Add(new RecipeOutputTarget(
                        block.Id, block.Name, validator.RejectOutputPath, false));
                    break;
                case SftExportBlockConfig export when
                    export.Splits is { Length: > 0 } &&
                    !string.IsNullOrWhiteSpace(export.OutputPath):
                    var splitDirectory = Path.GetDirectoryName(Path.GetFullPath(export.OutputPath));
                    if (string.IsNullOrWhiteSpace(splitDirectory))
                    {
                        break;
                    }

                    targets.Add(new RecipeOutputTarget(block.Id, block.Name, splitDirectory, true));
                    if (SamplerSplitPlan.TryParse(export.Splits, out var splitPlan, out _))
                    {
                        foreach (var split in splitPlan!.Specs)
                        {
                            targets.Add(new RecipeOutputTarget(
                                block.Id,
                                block.Name,
                                Path.Combine(splitDirectory, $"{split.Name}.jsonl"),
                                false));
                        }
                    }

                    break;
                case SftExportBlockConfig export when !string.IsNullOrWhiteSpace(export.OutputPath):
                    targets.Add(new RecipeOutputTarget(block.Id, block.Name, export.OutputPath, false));
                    break;
                case SamplerBlockConfig sampler when !string.IsNullOrWhiteSpace(sampler.OutputDirectory):
                    targets.Add(new RecipeOutputTarget(
                        block.Id, block.Name, sampler.OutputDirectory, true));
                    foreach (var split in sampler.Splits)
                    {
                        if (string.IsNullOrWhiteSpace(split)) continue;
                        targets.Add(new RecipeOutputTarget(
                            block.Id,
                            block.Name,
                            Path.Combine(sampler.OutputDirectory, $"{split}.jsonl"),
                            false));
                    }

                    break;
            }
        }

        return targets;
    }
}
