using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Services;

/// <summary>
/// Topological layering and deterministic left-to-right auto layout for recipes.
/// </summary>
public static class RecipeLayoutService
{
    public static List<List<RecipeBlock>> BuildLayers(Recipe recipe)
    {
        var blocks = recipe.Blocks.ToDictionary(block => block.Id);
        var inDegree = blocks.Keys.ToDictionary(id => id, _ => 0);
        var adjacency = blocks.Keys.ToDictionary(id => id, _ => new List<string>());

        foreach (var edge in recipe.Edges)
        {
            if (!adjacency.TryGetValue(edge.SourceBlockId, out var targets) ||
                !inDegree.ContainsKey(edge.TargetBlockId))
            {
                continue;
            }

            targets.Add(edge.TargetBlockId);
            inDegree[edge.TargetBlockId]++;
        }

        var layers = new List<List<RecipeBlock>>();
        var ready = inDegree.Where(pair => pair.Value == 0)
            .Select(pair => pair.Key)
            .OrderBy(id => blocks[id].Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        while (ready.Count > 0)
        {
            layers.Add(ready.Select(id => blocks[id]).ToList());

            var next = new List<string>();
            foreach (var id in ready)
            {
                foreach (var target in adjacency[id])
                {
                    if (--inDegree[target] == 0)
                    {
                        next.Add(target);
                    }
                }
            }

            ready = next
                .OrderBy(id => blocks[id].Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return layers;
    }

    /// <summary>
    /// Arranges blocks by topological column and row order.
    /// </summary>
    public static IReadOnlyDictionary<string, (double X, double Y)> Arrange(
        Recipe recipe,
        double originX = 80,
        double originY = 80,
        double columnGap = 280,
        double rowGap = 140)
    {
        var positions = new Dictionary<string, (double X, double Y)>();
        var layers = BuildLayers(recipe);

        var placed = layers.SelectMany(layer => layer).Select(block => block.Id).ToHashSet();
        var unplaced = recipe.Blocks.Where(block => !placed.Contains(block.Id)).ToList();
        if (unplaced.Count > 0)
        {
            layers.Add(unplaced);
        }

        for (var layerIndex = 0; layerIndex < layers.Count; layerIndex++)
        {
            var layer = layers[layerIndex];
            for (var rowIndex = 0; rowIndex < layer.Count; rowIndex++)
            {
                positions[layer[rowIndex].Id] = (
                    originX + layerIndex * columnGap,
                    originY + rowIndex * rowGap);
            }
        }

        return positions;
    }
}
