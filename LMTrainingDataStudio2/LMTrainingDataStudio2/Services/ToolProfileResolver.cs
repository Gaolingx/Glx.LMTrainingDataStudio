using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Services;

/// <summary>
/// Resolves a Tool Profile block referenced by name from Simulation or LLM configuration.
/// The reference may match the Tool Profile block's Jinja name or its profile name.
/// </summary>
public static class ToolProfileResolver
{
    public static string? GetReference(BlockConfig config) => config switch
    {
        SimulationBlockConfig simulation => simulation.ToolProfileName,
        LlmBlockConfig llm => llm.ToolProfileName,
        _ => null
    };

    public static ToolProfile? Resolve(Recipe recipe, RecipeBlock block)
    {
        var reference = GetReference(block.Config);
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        foreach (var candidate in recipe.Blocks)
        {
            if (candidate.Type != BlockType.ToolProfile ||
                candidate.Config is not ToolProfileBlockConfig profileConfig ||
                profileConfig.Profile is not { } profile)
            {
                continue;
            }

            if (string.Equals(candidate.Name, reference, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(profile.Name, reference, StringComparison.OrdinalIgnoreCase))
            {
                return profile;
            }
        }

        return null;
    }
}
