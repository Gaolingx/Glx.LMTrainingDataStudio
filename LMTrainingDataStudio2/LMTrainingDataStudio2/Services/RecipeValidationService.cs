using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Services;

/// <summary>
/// Validates a Recipe DAG before execution.
/// Checks for cycles, disconnected nodes, missing configurations, etc.
/// </summary>
public sealed class RecipeValidationService
{
    /// <summary>
    /// Validates the entire recipe graph.
    /// </summary>
    public ValidationResult Validate(Recipe recipe)
    {
        var result = new ValidationResult();

        if (recipe.Blocks.Count == 0)
        {
            result.Issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Warning,
                Message = "Recipe has no blocks."
            });
            return result;
        }

        ValidateBlockNames(recipe, result);
        ValidateEdgeConnections(recipe, result);
        ValidateCycles(recipe, result);
        ValidateBlockConfigurations(recipe, result);

        return result;
    }

    private void ValidateBlockNames(Recipe recipe, ValidationResult result)
    {
        var names = new HashSet<string>();
        foreach (var block in recipe.Blocks)
        {
            if (string.IsNullOrWhiteSpace(block.Name))
            {
                result.Issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Error,
                    Message = $"Block '{block.DisplayName}' has no Jinja reference name.",
                    BlockId = block.Id
                });
            }
            else if (!names.Add(block.Name))
            {
                result.Issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Error,
                    Message = $"Duplicate block name: '{block.Name}'.",
                    BlockId = block.Id
                });
            }
        }
    }

    private void ValidateEdgeConnections(Recipe recipe, ValidationResult result)
    {
        var blockIds = recipe.Blocks.Select(b => b.Id).ToHashSet();
        var portIds = recipe.Blocks
            .SelectMany(b => b.InputPorts.Concat(b.OutputPorts))
            .Select(p => p.Id)
            .ToHashSet();

        foreach (var edge in recipe.Edges)
        {
            if (!blockIds.Contains(edge.SourceBlockId))
            {
                result.Issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Error,
                    Message = $"Edge references non-existent source block: {edge.SourceBlockId}"
                });
            }
            if (!blockIds.Contains(edge.TargetBlockId))
            {
                result.Issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Error,
                    Message = $"Edge references non-existent target block: {edge.TargetBlockId}"
                });
            }
            if (!portIds.Contains(edge.SourcePortId))
            {
                result.Issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Error,
                    Message = $"Edge references non-existent source port: {edge.SourcePortId}"
                });
            }
            if (!portIds.Contains(edge.TargetPortId))
            {
                result.Issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Error,
                    Message = $"Edge references non-existent target port: {edge.TargetPortId}"
                });
            }
        }
    }

    private void ValidateCycles(Recipe recipe, ValidationResult result)
    {
        // Build adjacency list
        var adjacency = new Dictionary<string, List<string>>();
        foreach (var block in recipe.Blocks)
            adjacency[block.Id] = new List<string>();

        foreach (var edge in recipe.Edges)
        {
            if (adjacency.ContainsKey(edge.SourceBlockId))
                adjacency[edge.SourceBlockId].Add(edge.TargetBlockId);
        }

        // Topological sort with cycle detection (Kahn's algorithm)
        var inDegree = recipe.Blocks.ToDictionary(b => b.Id, _ => 0);
        foreach (var edge in recipe.Edges)
        {
            if (inDegree.ContainsKey(edge.TargetBlockId))
                inDegree[edge.TargetBlockId]++;
        }

        var queue = new Queue<string>(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        var visited = 0;

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            visited++;
            foreach (var neighbor in adjacency[node])
            {
                if (!inDegree.ContainsKey(neighbor)) continue;
                inDegree[neighbor]--;
                if (inDegree[neighbor] == 0)
                    queue.Enqueue(neighbor);
            }
        }

        if (visited < recipe.Blocks.Count)
        {
            result.Issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Error,
                Message = "Recipe contains a cycle. DAG must be acyclic."
            });
        }
    }

    private void ValidateBlockConfigurations(Recipe recipe, ValidationResult result)
    {
        foreach (var block in recipe.Blocks)
        {
            if (block.Config.GetBlockType() != block.Type)
            {
                result.Issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Error,
                    Message = $"Block '{block.Name}' is a {block.Type} node but uses {block.Config.GetType().Name}.",
                    BlockId = block.Id
                });
                continue;
            }

            switch (block.Config)
            {
                case SeedBlockConfig seed when string.IsNullOrWhiteSpace(seed.SourcePath):
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = $"Seed block '{block.Name}' has no data source configured.",
                        BlockId = block.Id
                    });
                    break;

                case LlmBlockConfig llm when string.IsNullOrWhiteSpace(llm.ModelAlias):
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = $"LLM block '{block.Name}' has no model alias configured.",
                        BlockId = block.Id
                    });
                    break;

                case ExpressionBlockConfig expression when string.IsNullOrWhiteSpace(expression.Template):
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = $"Expression block '{block.Name}' has no template configured.",
                        BlockId = block.Id
                    });
                    break;

                case ValidatorBlockConfig validator when string.IsNullOrWhiteSpace(validator.Rule):
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = $"Validator block '{block.Name}' has no rule configured.",
                        BlockId = block.Id
                    });
                    break;

                case SamplerBlockConfig sampler when sampler.Count is null && sampler.Ratio is null:
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = $"Sampler block '{block.Name}' has neither count nor ratio configured.",
                        BlockId = block.Id
                    });
                    break;

                case ToolProfileBlockConfig toolProfile when toolProfile.Profile?.Tools is not { Count: > 0 }:
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = $"Tool profile block '{block.Name}' has no tools configured.",
                        BlockId = block.Id
                    });
                    break;
            }
        }
    }
}
