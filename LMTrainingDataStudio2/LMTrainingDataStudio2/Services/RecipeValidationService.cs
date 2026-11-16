using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Services;

/// <summary>
/// Validates a Recipe DAG before execution.
/// Checks for cycles, disconnected nodes, missing configurations, etc.
/// </summary>
public sealed class RecipeValidationService
{
    private static readonly HashSet<string> RealSandboxTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "python", "powershell", "bash", "web_fetch"
    };

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
        ValidatePortContracts(recipe, result);
        ValidateEdgeConnections(recipe, result);
        ValidateRequiredInputConnections(recipe, result);
        ValidateIsolatedBlocks(recipe, result);
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

    private void ValidatePortContracts(Recipe recipe, ValidationResult result)
    {
        foreach (var block in recipe.Blocks)
        {
            ValidatePortCollection(
                block, block.InputPorts, PortDirection.Input, BlockPortSchema.GetInputSpec(block.Type), result);
            ValidatePortCollection(
                block, block.OutputPorts, PortDirection.Output, BlockPortSchema.GetOutputSpec(block.Type), result);
        }
    }

    private static void ValidatePortCollection(
        RecipeBlock block,
        IReadOnlyCollection<BlockPort> ports,
        PortDirection direction,
        BlockPortSpec? expected,
        ValidationResult result)
    {
        var directionName = direction == PortDirection.Input ? "input" : "output";

        if (expected is null)
        {
            if (ports.Count > 0)
            {
                AddError(result,
                    $"Block '{block.Name}' ({block.Type}) must not have {directionName} ports.",
                    block.Id);
            }

            return;
        }

        if (ports.Count == 0)
        {
            AddError(result,
                $"Block '{block.Name}' ({block.Type}) requires an {directionName} port.",
                block.Id);
            return;
        }

        if (ports.Count > 1)
        {
            AddError(result,
                $"Block '{block.Name}' ({block.Type}) supports at most one {directionName} port.",
                block.Id);
        }

        foreach (var port in ports)
        {
            if (port.Direction != direction)
            {
                AddError(result,
                    $"Block '{block.Name}' port '{port.Name}' must be an {directionName} port.",
                    block.Id);
            }

            if (!port.DataType.Equals(expected.DataType, StringComparison.OrdinalIgnoreCase))
            {
                AddError(result,
                    $"Block '{block.Name}' {directionName} port '{port.Name}' must have data type " +
                    $"'{expected.DataType}'; got '{port.DataType}'.",
                    block.Id);
            }
        }
    }

    private static void ValidateRequiredInputConnections(Recipe recipe, ValidationResult result)
    {
        foreach (var block in recipe.Blocks)
        {
            if (!BlockPortSchema.ExpectsInput(block.Type) || block.InputPorts.Count == 0)
            {
                continue;
            }

            if (recipe.Edges.Any(edge => edge.TargetBlockId == block.Id))
            {
                continue;
            }

            AddError(result,
                $"Block '{block.Name}' ({block.Type}) requires an incoming connection.",
                block.Id);
        }
    }

    private void ValidateEdgeConnections(Recipe recipe, ValidationResult result)
    {
        var blocks = recipe.Blocks.ToDictionary(b => b.Id);
        var ports = new Dictionary<string, (RecipeBlock Block, BlockPort Port)>();
        foreach (var block in recipe.Blocks)
        {
            foreach (var port in block.InputPorts.Concat(block.OutputPorts))
            {
                ports.TryAdd(port.Id, (block, port));
            }
        }

        var seenEdges = new HashSet<(string SourceBlock, string SourcePort, string TargetBlock, string TargetPort)>();

        foreach (var edge in recipe.Edges)
        {
            var sourceBlockExists = blocks.TryGetValue(edge.SourceBlockId, out var sourceBlock);
            if (!sourceBlockExists)
            {
                AddError(result, $"Edge references non-existent source block: {edge.SourceBlockId}");
            }

            var targetBlockExists = blocks.TryGetValue(edge.TargetBlockId, out var targetBlock);
            if (!targetBlockExists)
            {
                AddError(result, $"Edge references non-existent target block: {edge.TargetBlockId}");
            }

            var sourcePortExists = ports.TryGetValue(edge.SourcePortId, out var sourcePort);
            if (!sourcePortExists)
            {
                AddError(result, $"Edge references non-existent source port: {edge.SourcePortId}");
            }

            var targetPortExists = ports.TryGetValue(edge.TargetPortId, out var targetPort);
            if (!targetPortExists)
            {
                AddError(result, $"Edge references non-existent target port: {edge.TargetPortId}");
            }

            if (sourceBlockExists && sourcePortExists)
            {
                if (sourcePort.Block.Id != sourceBlock!.Id)
                {
                    AddError(result, $"Source port '{edge.SourcePortId}' does not belong to block '{sourceBlock.Name}'.",
                        sourceBlock.Id);
                }
                else if (sourcePort.Port.Direction != PortDirection.Output)
                {
                    AddError(result, $"Source port '{sourcePort.Port.Name}' on block '{sourceBlock.Name}' must be an output port.",
                        sourceBlock.Id);
                }
            }

            if (targetBlockExists && targetPortExists)
            {
                if (targetPort.Block.Id != targetBlock!.Id)
                {
                    AddError(result, $"Target port '{edge.TargetPortId}' does not belong to block '{targetBlock.Name}'.",
                        targetBlock.Id);
                }
                else if (targetPort.Port.Direction != PortDirection.Input)
                {
                    AddError(result, $"Target port '{targetPort.Port.Name}' on block '{targetBlock.Name}' must be an input port.",
                        targetBlock.Id);
                }
            }

            if (!seenEdges.Add((edge.SourceBlockId, edge.SourcePortId, edge.TargetBlockId, edge.TargetPortId)))
            {
                AddError(result, $"Duplicate edge from '{edge.SourceBlockId}' to '{edge.TargetBlockId}'.",
                    targetBlock?.Id);
            }

            if (sourcePortExists && targetPortExists &&
                !ConnectionRules.ArePortTypesCompatible(sourcePort.Port.DataType, targetPort.Port.DataType))
            {
                AddError(result,
                    $"Port type mismatch: '{sourcePort.Port.DataType}' cannot connect to '{targetPort.Port.DataType}'.",
                    targetBlock?.Id);
            }
        }

        foreach (var fanIn in recipe.Edges
                     .GroupBy(edge => (edge.TargetBlockId, edge.TargetPortId))
                     .Where(group => group.Count() > 1))
        {
            var block = blocks.GetValueOrDefault(fanIn.Key.TargetBlockId);
            var port = ports.TryGetValue(fanIn.Key.TargetPortId, out var entry) ? entry.Port : null;
            AddError(result,
                $"Input port '{port?.Name ?? fanIn.Key.TargetPortId}' on block '{block?.Name ?? fanIn.Key.TargetBlockId}' " +
                $"accepts only one incoming connection (found {fanIn.Count()}).",
                block?.Id);
        }
    }

    private void ValidateIsolatedBlocks(Recipe recipe, ValidationResult result)
    {
        if (recipe.Blocks.Count <= 1) return;

        var connectedIds = new HashSet<string>();
        foreach (var edge in recipe.Edges)
        {
            connectedIds.Add(edge.SourceBlockId);
            connectedIds.Add(edge.TargetBlockId);
        }

        foreach (var block in recipe.Blocks)
        {
            if (connectedIds.Contains(block.Id)) continue;

            result.Issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Warning,
                Message = $"Block '{block.Name}' is not connected to any edge.",
                BlockId = block.Id
            });
        }
    }

    private static void AddError(ValidationResult result, string message, string? blockId = null) =>
        result.Issues.Add(new ValidationIssue
        {
            Severity = ValidationSeverity.Error,
            Message = message,
            BlockId = blockId
        });

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

                case SeedBlockConfig seed when seed.FileType != DataFileType.Jsonl:
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = $"Seed block '{block.Name}' uses {seed.FileType}; only JSONL is executable at the moment.",
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

                case LlmBlockConfig llm when LlmProviderIds.Normalize(llm.Provider) is null:
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = $"LLM block '{block.Name}' uses unknown provider '{llm.Provider}'. " +
                                  $"Supported: {LlmProviderIds.OpenAiChat}, {LlmProviderIds.OpenAiResponses}, {LlmProviderIds.Anthropic}.",
                        BlockId = block.Id
                    });
                    break;

                case LlmBlockConfig llm when llm.TimeoutSeconds <= 0:
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = $"LLM block '{block.Name}' timeout must be greater than 0 seconds.",
                        BlockId = block.Id
                    });
                    break;

                case LlmBlockConfig llm when llm.MaxRetries < 0:
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = $"LLM block '{block.Name}' max retries must not be negative.",
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

                case QuerySourceBlockConfig query when query.Queries is not { Length: > 0 } ||
                                                       query.Queries.All(string.IsNullOrWhiteSpace):
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = $"Query source block '{block.Name}' has no queries configured.",
                        BlockId = block.Id
                    });
                    break;

                case QuerySourceBlockConfig query when query.Repeat < 1:
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = $"Query source block '{block.Name}' repeat must be at least 1; got {query.Repeat}.",
                        BlockId = block.Id
                    });
                    break;

                case SimulationBlockConfig simulation when string.IsNullOrWhiteSpace(simulation.ModelAlias):
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = $"Simulation block '{block.Name}' has no model alias configured.",
                        BlockId = block.Id
                    });
                    break;

                case SimulationBlockConfig simulation when LlmProviderIds.Normalize(simulation.Provider) is null:
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = $"Simulation block '{block.Name}' uses unknown provider '{simulation.Provider}'.",
                        BlockId = block.Id
                    });
                    break;

                case SimulationBlockConfig simulation when simulation.MaxRounds <= 0:
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = $"Simulation block '{block.Name}' max rounds must be greater than 0.",
                        BlockId = block.Id
                    });
                    break;

                case SimulationBlockConfig simulation when simulation.TimeoutSeconds <= 0:
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = $"Simulation block '{block.Name}' timeout must be greater than 0 seconds.",
                        BlockId = block.Id
                    });
                    break;

                case SimulationBlockConfig simulation when simulation.ModelMaxRetries < 0:
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = $"Simulation block '{block.Name}' model max retries must not be negative.",
                        BlockId = block.Id
                    });
                    break;

                case SimulationBlockConfig simulation when simulation.Tools is { Length: > 0 } tools &&
                                                            tools.Any(tool => string.IsNullOrWhiteSpace(tool.Name)):
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = $"Simulation block '{block.Name}' has a tool with an empty name.",
                        BlockId = block.Id
                    });
                    break;

                case SimulationBlockConfig simulation when simulation.Tools is { Length: > 0 } tools &&
                                                            tools.GroupBy(tool => tool.Name, StringComparer.OrdinalIgnoreCase)
                                                                .Any(group => group.Count() > 1):
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = $"Simulation block '{block.Name}' declares duplicate tool names.",
                        BlockId = block.Id
                    });
                    break;

                case SimulationBlockConfig simulation when simulation.Rules is { MaxRetries: < 0 }:
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = $"Simulation block '{block.Name}' tool max retries must not be negative.",
                        BlockId = block.Id
                    });
                    break;

                case SimulationBlockConfig simulation when simulation.Rules is { DefaultTimeoutMs: < 0 }:
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = $"Simulation block '{block.Name}' tool timeout must not be negative.",
                        BlockId = block.Id
                    });
                    break;

                case SimulationBlockConfig simulation when simulation.MaxConcurrency < 1:
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = $"Simulation block '{block.Name}' max concurrency must be at least 1.",
                        BlockId = block.Id
                    });
                    break;

                case SimulationBlockConfig simulation when simulation.MaxFailureSamples < 0:
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = $"Simulation block '{block.Name}' max failure samples must not be negative.",
                        BlockId = block.Id
                    });
                    break;

                case SimulationBlockConfig simulation when
                    simulation.ModelMode == SimulationModelMode.Provider &&
                    !IsValidEndpoint(simulation.Endpoint):
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = $"Simulation block '{block.Name}' endpoint must be an absolute http(s) URL.",
                        BlockId = block.Id
                    });
                    break;

                case SimulationBlockConfig simulation when
                    simulation.Sandbox is { } sandbox && HasNegativeSandboxLimit(sandbox):
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = $"Simulation block '{block.Name}' has negative sandbox limits.",
                        BlockId = block.Id
                    });
                    break;

                case SimulationBlockConfig simulation when
                    simulation.ToolMode == ToolExecutionMode.Real &&
                    simulation.Tools is { Length: > 0 } realTools &&
                    realTools.Any(tool => !RealSandboxTools.Contains(tool.Name)):
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = $"Simulation block '{block.Name}' has tools without a real sandbox executor: " +
                                  string.Join(", ", realTools
                                      .Where(tool => !RealSandboxTools.Contains(tool.Name))
                                      .Select(tool => tool.Name)) + ".",
                        BlockId = block.Id
                    });
                    break;

                case CleanerBlockConfig cleaner when !cleaner.Rules.HasAnyRule:
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = $"Cleaner block '{block.Name}' has no cleanup rules enabled.",
                        BlockId = block.Id
                    });
                    break;

                case SftExportBlockConfig export when string.IsNullOrWhiteSpace(export.OutputPath):
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = $"SFT export block '{block.Name}' has no output path configured.",
                        BlockId = block.Id
                    });
                    break;
            }
        }
    }

    private static bool IsValidEndpoint(string? endpoint) =>
        string.IsNullOrWhiteSpace(endpoint) ||
        (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) &&
         (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps));

    private static bool HasNegativeSandboxLimit(SandboxLimits sandbox) =>
        sandbox.TimeoutMs < 0 ||
        sandbox.MaxOutputBytes < 0 ||
        sandbox.MaxMemoryMb < 0 ||
        sandbox.MaxProcesses < 0 ||
        sandbox.MaxFetchBytes < 0;
}
