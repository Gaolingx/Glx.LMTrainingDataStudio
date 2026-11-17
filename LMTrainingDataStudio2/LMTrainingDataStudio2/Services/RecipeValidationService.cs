using System.Text.Json;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services.Executors;

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
        ValidateLlmToolSettings(recipe, result);
        ValidateToolProfileReferences(recipe, result);

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
            ValidateInputPorts(block, result);
            ValidateOutputPorts(block, result);
        }
    }

    private static void ValidateInputPorts(RecipeBlock block, ValidationResult result)
    {
        var expected = BlockPortSchema.GetInputSpec(block.Type);
        if (expected is null)
        {
            if (block.InputPorts.Count > 0)
            {
                AddError(result,
                    $"Block '{block.Name}' ({block.Type}) must not have input ports.",
                    block.Id);
            }

            return;
        }

        if (block.InputPorts.Count == 0)
        {
            AddError(result,
                $"Block '{block.Name}' ({block.Type}) requires an input port.",
                block.Id);
            return;
        }

        if (block.InputPorts.Count > 1)
        {
            AddError(result,
                $"Block '{block.Name}' ({block.Type}) supports at most one input port.",
                block.Id);
        }

        ValidatePortDataType(block, block.InputPorts, PortDirection.Input, expected.DataType, result);
    }

    private static void ValidateOutputPorts(RecipeBlock block, ValidationResult result)
    {
        var expected = BlockPortSchema.GetOutputSpecs(block.Type, block.Config);
        if (expected.Count == 0)
        {
            if (block.OutputPorts.Count > 0)
            {
                AddError(result,
                    $"Block '{block.Name}' ({block.Type}) must not have output ports.",
                    block.Id);
            }

            return;
        }

        if (block.OutputPorts.Count == 0)
        {
            AddError(result,
                $"Block '{block.Name}' ({block.Type}) requires an output port.",
                block.Id);
            return;
        }

        if (block.OutputPorts.Count > expected.Count)
        {
            AddError(result,
                $"Block '{block.Name}' ({block.Type}) supports at most {expected.Count} output port(s); " +
                $"found {block.OutputPorts.Count}.",
                block.Id);
        }

        ValidatePortDataType(block, block.OutputPorts, PortDirection.Output, expected[0].DataType, result);
    }

    private static void ValidatePortDataType(
        RecipeBlock block,
        IReadOnlyCollection<BlockPort> ports,
        PortDirection direction,
        string dataType,
        ValidationResult result)
    {
        var directionName = direction == PortDirection.Input ? "input" : "output";
        foreach (var port in ports)
        {
            if (port.Direction != direction)
            {
                AddError(result,
                    $"Block '{block.Name}' port '{port.Name}' must be an {directionName} port.",
                    block.Id);
            }

            if (!port.DataType.Equals(dataType, StringComparison.OrdinalIgnoreCase))
            {
                AddError(result,
                    $"Block '{block.Name}' {directionName} port '{port.Name}' must have data type " +
                    $"'{dataType}'; got '{port.DataType}'.",
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
            if (adjacency.TryGetValue(edge.SourceBlockId, out var targets))
                targets.Add(edge.TargetBlockId);
        }

        // Topological sort with cycle detection (Kahn's algorithm)
        var inDegree = recipe.Blocks.ToDictionary(b => b.Id, _ => 0);
        foreach (var edge in recipe.Edges)
        {
            if (inDegree.TryGetValue(edge.TargetBlockId, out var degree))
                inDegree[edge.TargetBlockId] = degree + 1;
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

                case LlmBlockConfig llm when llm.MaxConcurrency < 1:
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = $"LLM block '{block.Name}' max concurrency must be at least 1.",
                        BlockId = block.Id
                    });
                    break;

                case LlmBlockConfig llm when llm.MaxFailureSamples < 0:
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = $"LLM block '{block.Name}' max failure samples must not be negative.",
                        BlockId = block.Id
                    });
                    break;

                case LlmBlockConfig llm when
                    !string.IsNullOrWhiteSpace(llm.ResponseSchema) &&
                    !TryValidateJsonSchema(llm.ResponseSchema!, out var responseSchemaError):
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = $"LLM block '{block.Name}' has an invalid response schema: {responseSchemaError}",
                        BlockId = block.Id
                    });
                    break;

                case ExpressionBlockConfig expression when
                    string.IsNullOrWhiteSpace(expression.Template) && expression.Fields.Count == 0:
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = $"Expression block '{block.Name}' has no template or field mapping configured.",
                        BlockId = block.Id
                    });
                    break;

                case ExpressionBlockConfig expression when
                    !string.IsNullOrWhiteSpace(expression.Template) &&
                    !TemplateRenderer.Validate(expression.Template).IsValid:
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = $"Expression block '{block.Name}' has an invalid template: " +
                                  TemplateRenderer.Validate(expression.Template).Error,
                        BlockId = block.Id
                    });
                    break;

                case ValidatorBlockConfig validator when GetValidatorRules(validator).Count == 0:
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = $"Validator block '{block.Name}' has no rule configured.",
                        BlockId = block.Id
                    });
                    break;

                case ValidatorBlockConfig validator when validator.TimeoutMs < 0:
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = $"Validator block '{block.Name}' timeout must not be negative.",
                        BlockId = block.Id
                    });
                    break;

                case ValidatorBlockConfig validator when
                    GetValidatorRules(validator).Any(rule => !IsKnownValidatorRuleKind(rule.Kind)):
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = $"Validator block '{block.Name}' uses an unknown rule kind. " +
                                  "Supported: expression, schema, regex, script.",
                        BlockId = block.Id
                    });
                    break;

                case ValidatorBlockConfig validator when
                    GetValidatorRules(validator).Any(rule =>
                        IsRegexRuleKind(rule.Kind) && !IsValidRegex(rule.Rule)):
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = $"Validator block '{block.Name}' has an invalid regular expression.",
                        BlockId = block.Id
                    });
                    break;

                case ValidatorBlockConfig validator when
                    GetValidatorRules(validator).Any(rule =>
                        IsSchemaRuleKind(rule.Kind) &&
                        !string.IsNullOrWhiteSpace(rule.Rule) &&
                        TryValidateJsonSchema(rule.Rule, out _) is false):
                    var invalidSchema = GetValidatorRules(validator).First(rule =>
                        IsSchemaRuleKind(rule.Kind) &&
                        !string.IsNullOrWhiteSpace(rule.Rule) &&
                        TryValidateJsonSchema(rule.Rule, out _) is false);
                    TryValidateJsonSchema(invalidSchema.Rule, out var schemaError);
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = $"Validator block '{block.Name}' has an invalid JSON Schema: {schemaError}",
                        BlockId = block.Id
                    });
                    break;

                case SamplerBlockConfig sampler when sampler.Splits is { Length: > 0 }:
                    ValidateSamplerSplits(sampler, block, result);
                    break;

                case SamplerBlockConfig sampler when sampler.Count is null && sampler.Ratio is null:
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = $"Sampler block '{block.Name}' has neither count nor ratio configured.",
                        BlockId = block.Id
                    });
                    break;

                case SamplerBlockConfig sampler when
                    !string.IsNullOrWhiteSpace(sampler.StratifyField) &&
                    sampler.Splits is not { Length: > 0 }:
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = $"Sampler block '{block.Name}' can only stratify when splits are configured.",
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

                case QuerySourceBlockConfig query when
                    string.IsNullOrWhiteSpace(query.SourcePath) &&
                    (query.Queries is not { Length: > 0 } || query.Queries.All(string.IsNullOrWhiteSpace)):
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = $"Query source block '{block.Name}' has no queries configured.",
                        BlockId = block.Id
                    });
                    break;

                case QuerySourceBlockConfig query when
                    !string.IsNullOrWhiteSpace(query.SourcePath) &&
                    !IsSupportedDatasetExtension(query.SourcePath!):
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = $"Query source block '{block.Name}' source must be .jsonl, .csv or .parquet.",
                        BlockId = block.Id
                    });
                    break;

                case QuerySourceBlockConfig query when
                    !string.IsNullOrWhiteSpace(query.TemplateVariablesJson) &&
                    !IsValidJsonObject(query.TemplateVariablesJson!):
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = $"Query source block '{block.Name}' template variables must be a JSON object.",
                        BlockId = block.Id
                    });
                    break;

                case QuerySourceBlockConfig query when
                    string.IsNullOrWhiteSpace(query.QueryField):
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = $"Query source block '{block.Name}' query field must not be empty.",
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

                case SimulationBlockConfig simulation when simulation.RequestsPerMinute < 0:
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = $"Simulation block '{block.Name}' requests per minute must not be negative.",
                        BlockId = block.Id
                    });
                    break;

                case SimulationBlockConfig simulation when
                    simulation.StreamDeltas &&
                    LlmProviderIds.Normalize(simulation.Provider) != LlmProviderIds.OpenAiChat:
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = $"Simulation block '{block.Name}' streams deltas only with the OpenAI Chat provider.",
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

                case SftExportBlockConfig export when export.TargetFormat is DatasetFormat.Dpo or DatasetFormat.Pretrain:
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = $"SFT export block '{block.Name}' cannot produce {export.TargetFormat} rows; " +
                                  "choose Auto, OpenAI, ShareGPT or Alpaca.",
                        BlockId = block.Id
                    });
                    break;

                case SftExportBlockConfig export when export.SplitMaxRows < 0:
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = $"SFT export block '{block.Name}' split size cannot be negative.",
                        BlockId = block.Id
                    });
                    break;

                case SftExportBlockConfig export when export.Append && export.SplitMaxRows > 0:
                    result.Issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = $"SFT export block '{block.Name}' cannot combine append mode with file splitting.",
                        BlockId = block.Id
                    });
                    break;
            }
        }
    }

    private static void ValidateLlmToolSettings(Recipe recipe, ValidationResult result)
    {
        foreach (var block in recipe.Blocks)
        {
            if (block.Config is not LlmBlockConfig llm)
            {
                continue;
            }

            if (llm.MaxToolRounds < 0)
            {
                result.Issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Warning,
                    Message = $"LLM block '{block.Name}' max tool rounds must not be negative.",
                    BlockId = block.Id
                });
                continue;
            }

            if (llm.MaxToolRounds == 0)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(llm.ToolProfileName))
            {
                result.Issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Warning,
                    Message = $"LLM block '{block.Name}' enables tool rounds but has no tool profile configured.",
                    BlockId = block.Id
                });
            }
            else if (!string.IsNullOrWhiteSpace(llm.ResponseSchema) &&
                     LlmProviderIds.Normalize(llm.Provider) == LlmProviderIds.Anthropic)
            {
                result.Issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Warning,
                    Message = $"LLM block '{block.Name}' uses Anthropic structured output, which forces the " +
                              "'structured_output' tool and prevents tool rounds from running.",
                    BlockId = block.Id
                });
            }
        }
    }

    private void ValidateToolProfileReferences(Recipe recipe, ValidationResult result)
    {
        foreach (var block in recipe.Blocks)
        {
            var reference = ToolProfileResolver.GetReference(block.Config);
            if (string.IsNullOrWhiteSpace(reference))
            {
                continue;
            }

            var profile = ToolProfileResolver.Resolve(recipe, block);
            if (profile is null)
            {
                result.Issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Warning,
                    Message = $"Block '{block.Name}' references unknown tool profile '{reference}'.",
                    BlockId = block.Id
                });
            }
            else if (profile.Tools is not { Count: > 0 })
            {
                result.Issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Warning,
                    Message = $"Block '{block.Name}' references tool profile '{reference}', which has no tools.",
                    BlockId = block.Id
                });
            }
            else if (block.Config is SimulationBlockConfig { ToolMode: ToolExecutionMode.Real } &&
                     profile.Tools
                         .Select(tool => tool.Function.Name)
                         .FirstOrDefault(name => !RealSandboxTools.Contains(name)) is { } unsupported)
            {
                result.Issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Warning,
                    Message = $"Simulation block '{block.Name}' tool profile '{reference}' exposes " +
                              $"'{unsupported}', which has no real sandbox executor.",
                    BlockId = block.Id
                });
            }
        }
    }

    private static void ValidateSamplerSplits(
        SamplerBlockConfig sampler,
        RecipeBlock block,
        ValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(sampler.OutputDirectory))
        {
            result.Issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Warning,
                Message = $"Sampler block '{block.Name}' has splits but no output directory.",
                BlockId = block.Id
            });
        }
        else if (!SamplerSplitPlan.TryParse(sampler.Splits, out _, out var error))
        {
            result.Issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Warning,
                Message = $"Sampler block '{block.Name}' has invalid splits: {error}",
                BlockId = block.Id
            });
        }

        if (sampler.Count is not null || sampler.Ratio is not null)
        {
            result.Issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Warning,
                Message = $"Sampler block '{block.Name}' ignores count/ratio while splits are configured.",
                BlockId = block.Id
            });
        }
    }

    private static IReadOnlyList<ValidatorRuleSpec> GetValidatorRules(ValidatorBlockConfig config)
    {
        if (config.Rules is { Length: > 0 })
        {
            return config.Rules
                .Where(rule => !string.IsNullOrWhiteSpace(rule.Rule))
                .ToArray();
        }

        return string.IsNullOrWhiteSpace(config.Rule)
            ? Array.Empty<ValidatorRuleSpec>()
            : [new ValidatorRuleSpec(config.RuleKind, config.Rule)];
    }

    private static bool IsKnownValidatorRuleKind(string ruleKind) =>
        string.Equals(ruleKind, "expression", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(ruleKind, "regex", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(ruleKind, "script", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(ruleKind, "code", StringComparison.OrdinalIgnoreCase) ||
        IsSchemaRuleKind(ruleKind);

    private static bool IsSchemaRuleKind(string ruleKind) =>
        string.Equals(ruleKind, "schema", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(ruleKind, "json_schema", StringComparison.OrdinalIgnoreCase);

    private static bool IsRegexRuleKind(string ruleKind) =>
        string.Equals(ruleKind, "regex", StringComparison.OrdinalIgnoreCase);

    private static bool IsValidRegex(string pattern)
    {
        try
        {
            _ = System.Text.RegularExpressions.Regex.IsMatch(string.Empty, pattern);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool TryValidateJsonSchema(string json, out string? error)
    {
        try
        {
            JsonSchemaRuleEvaluator.Parse(json);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool IsSupportedDatasetExtension(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".jsonl" or ".ndjson" or ".json" or ".csv" or ".parquet" or ".pq";

    private static bool IsValidJsonObject(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
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
