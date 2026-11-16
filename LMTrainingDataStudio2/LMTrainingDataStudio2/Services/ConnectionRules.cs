using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Services;

/// <summary>
/// Validates a candidate edge before it is added to the recipe.
/// Returns a user-facing error message, or null when the connection is allowed.
/// </summary>
public static class ConnectionRules
{
    public static string? Validate(
        IReadOnlyCollection<RecipeBlock> blocks,
        IReadOnlyCollection<RecipeEdge> edges,
        string sourceBlockId,
        string sourcePortId,
        string targetBlockId,
        string targetPortId)
    {
        if (sourceBlockId == targetBlockId)
        {
            return "A block cannot connect to itself.";
        }

        var sourceBlock = blocks.FirstOrDefault(block => block.Id == sourceBlockId);
        if (sourceBlock is null)
        {
            return $"Source block '{sourceBlockId}' does not exist.";
        }

        var targetBlock = blocks.FirstOrDefault(block => block.Id == targetBlockId);
        if (targetBlock is null)
        {
            return $"Target block '{targetBlockId}' does not exist.";
        }

        var sourcePort = sourceBlock.OutputPorts.FirstOrDefault(port => port.Id == sourcePortId);
        if (sourcePort is null)
        {
            return $"Block '{sourceBlock.Name}' has no output port '{sourcePortId}'.";
        }

        if (sourcePort.Direction != PortDirection.Output)
        {
            return $"Port '{sourcePort.Name}' on block '{sourceBlock.Name}' is not an output port.";
        }

        var targetPort = targetBlock.InputPorts.FirstOrDefault(port => port.Id == targetPortId);
        if (targetPort is null)
        {
            return $"Block '{targetBlock.Name}' has no input port '{targetPortId}'.";
        }

        if (targetPort.Direction != PortDirection.Input)
        {
            return $"Port '{targetPort.Name}' on block '{targetBlock.Name}' is not an input port.";
        }

        if (!ArePortTypesCompatible(sourcePort.DataType, targetPort.DataType))
        {
            return $"Port type mismatch: '{sourcePort.DataType}' cannot connect to '{targetPort.DataType}'.";
        }

        if (edges.Any(edge =>
                edge.SourceBlockId == sourceBlockId &&
                edge.SourcePortId == sourcePortId &&
                edge.TargetBlockId == targetBlockId &&
                edge.TargetPortId == targetPortId))
        {
            return "This connection already exists.";
        }

        if (edges.Any(edge =>
                edge.TargetBlockId == targetBlockId &&
                edge.TargetPortId == targetPortId))
        {
            return $"Input port '{targetPort.Name}' on block '{targetBlock.Name}' already has an incoming connection.";
        }

        if (CreatesCycle(edges, sourceBlockId, targetBlockId))
        {
            return $"Connecting '{sourceBlock.Name}' to '{targetBlock.Name}' would create a cycle.";
        }

        return null;
    }

    public static bool ArePortTypesCompatible(string sourceType, string targetType) =>
        sourceType.Equals("any", StringComparison.OrdinalIgnoreCase) ||
        targetType.Equals("any", StringComparison.OrdinalIgnoreCase) ||
        sourceType.Equals(targetType, StringComparison.OrdinalIgnoreCase);

    private static bool CreatesCycle(
        IReadOnlyCollection<RecipeEdge> edges,
        string sourceBlockId,
        string targetBlockId)
    {
        var adjacency = new Dictionary<string, List<string>>();
        foreach (var edge in edges)
        {
            if (!adjacency.TryGetValue(edge.SourceBlockId, out var targets))
            {
                targets = new List<string>();
                adjacency[edge.SourceBlockId] = targets;
            }

            targets.Add(edge.TargetBlockId);
        }

        if (!adjacency.TryGetValue(targetBlockId, out var start))
        {
            return false;
        }

        var visited = new HashSet<string>();
        var pending = new Queue<string>(start);
        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            if (current == sourceBlockId)
            {
                return true;
            }

            if (!visited.Add(current) || !adjacency.TryGetValue(current, out var next))
            {
                continue;
            }

            foreach (var blockId in next)
            {
                pending.Enqueue(blockId);
            }
        }

        return false;
    }
}
