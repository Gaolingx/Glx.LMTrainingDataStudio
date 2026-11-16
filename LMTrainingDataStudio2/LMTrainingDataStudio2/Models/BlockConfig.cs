using System.Text.Json.Serialization;

namespace LMTrainingDataStudio2.Models;

/// <summary>
/// Strongly typed configuration for a recipe block.
/// Serialized polymorphically using the <c>kind</c> discriminator.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(SeedBlockConfig), "seed")]
[JsonDerivedType(typeof(LlmBlockConfig), "llm")]
[JsonDerivedType(typeof(ExpressionBlockConfig), "expression")]
[JsonDerivedType(typeof(ValidatorBlockConfig), "validator")]
[JsonDerivedType(typeof(SamplerBlockConfig), "sampler")]
[JsonDerivedType(typeof(ToolProfileBlockConfig), "tool_profile")]
[JsonDerivedType(typeof(QuerySourceBlockConfig), "query_source")]
[JsonDerivedType(typeof(SimulationBlockConfig), "simulation")]
[JsonDerivedType(typeof(CleanerBlockConfig), "cleaner")]
[JsonDerivedType(typeof(SftExportBlockConfig), "sft_export")]
public abstract record BlockConfig
{
    /// <summary>
    /// Creates a configuration with defaults for the given block type.
    /// </summary>
    public static BlockConfig CreateDefault(BlockType type) => type switch
    {
        BlockType.Seed => new SeedBlockConfig(string.Empty, DataFileType.Jsonl, Streaming: false, UseIndexCache: true),
        BlockType.Llm => new LlmBlockConfig(LlmProviderIds.OpenAiChat, string.Empty, Temperature: 0.7, MaxTokens: 2048, Prompt: null),
        BlockType.Expression => new ExpressionBlockConfig(string.Empty, new Dictionary<string, string>()),
        BlockType.Validator => new ValidatorBlockConfig("expression", string.Empty, RejectOnFailure: true),
        BlockType.Sampler => new SamplerBlockConfig(Count: null, Ratio: null, Seed: 42, Splits: Array.Empty<string>()),
        BlockType.ToolProfile => new ToolProfileBlockConfig(new ToolProfile()),
        BlockType.QuerySource => new QuerySourceBlockConfig(Array.Empty<string>()),
        BlockType.Simulation => new SimulationBlockConfig(
            string.Empty,
            Tools: SimulatedToolCatalog.Common(),
            Rules: new ToolHarnessRules()),
        BlockType.Cleaner => new CleanerBlockConfig(new DatasetCleanupRules
        {
            NormalizeNewlines = true,
            NormalizeWhitespace = true
        }),
        BlockType.SftExport => new SftExportBlockConfig(string.Empty),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported block type.")
    };

    /// <summary>
    /// Gets the block type this configuration belongs to.
    /// </summary>
    public BlockType GetBlockType() => this switch
    {
        SeedBlockConfig => BlockType.Seed,
        LlmBlockConfig => BlockType.Llm,
        ExpressionBlockConfig => BlockType.Expression,
        ValidatorBlockConfig => BlockType.Validator,
        SamplerBlockConfig => BlockType.Sampler,
        ToolProfileBlockConfig => BlockType.ToolProfile,
        QuerySourceBlockConfig => BlockType.QuerySource,
        SimulationBlockConfig => BlockType.Simulation,
        CleanerBlockConfig => BlockType.Cleaner,
        SftExportBlockConfig => BlockType.SftExport,
        _ => throw new NotSupportedException($"Unsupported block configuration type: {GetType().Name}.")
    };

    /// <summary>
    /// Creates an independent copy so cloned blocks do not share mutable state.
    /// </summary>
    public BlockConfig DeepClone() => this switch
    {
        SeedBlockConfig seed => seed with { },
        LlmBlockConfig llm => llm with { },
        ExpressionBlockConfig expression => expression with
        {
            Fields = new Dictionary<string, string>(expression.Fields)
        },
        ValidatorBlockConfig validator => validator with { Cleanup = CloneCleanup(validator.Cleanup) },
        SamplerBlockConfig sampler => sampler with { Splits = [.. sampler.Splits] },
        ToolProfileBlockConfig { Profile: var profile } => new ToolProfileBlockConfig(new ToolProfile
        {
            Id = profile.Id,
            Name = profile.Name,
            Tools = profile.Tools.Select(CloneTool).ToList()
        }),
        QuerySourceBlockConfig query => query with { Queries = [.. query.Queries] },
        SimulationBlockConfig simulation => simulation with
        {
            Tools = simulation.Tools?.Select(tool => tool with { }).ToArray(),
            Sandbox = simulation.Sandbox is null ? null : simulation.Sandbox with { },
            Rules = simulation.Rules is null
                ? null
                : simulation.Rules with
                {
                    AllowedTools = simulation.Rules.AllowedTools is null
                        ? null
                        : [.. simulation.Rules.AllowedTools]
                },
            Script = simulation.Script?
                .Select(turn => turn with
                {
                    ToolCalls = turn.ToolCalls?.Select(call => call with { }).ToArray()
                })
                .ToArray()
        },
        CleanerBlockConfig cleaner => cleaner with { Rules = CloneCleanup(cleaner.Rules)! },
        SftExportBlockConfig export => export with { },
        _ => throw new NotSupportedException($"Unsupported block configuration type: {GetType().Name}.")
    };

    private static DatasetCleanupRules? CloneCleanup(DatasetCleanupRules? rules) => rules is null
        ? null
        : rules with
        {
            RegexReplacements = rules.RegexReplacements?.Select(rule => rule with { }).ToArray()
        };

    private static ToolDefinition CloneTool(ToolDefinition tool) => new()
    {
        Type = tool.Type,
        Function = new ToolFunction
        {
            Name = tool.Function.Name,
            Description = tool.Function.Description,
            Parameters = tool.Function.Parameters is null
                ? null
                : new Dictionary<string, object>(tool.Function.Parameters)
        }
    };
}

public sealed record SeedBlockConfig(
    string SourcePath,
    DataFileType FileType,
    bool Streaming,
    bool UseIndexCache) : BlockConfig;

public sealed record LlmBlockConfig(
    string Provider,
    string ModelAlias,
    double Temperature,
    int MaxTokens,
    string? Prompt,
    string? Endpoint = null,
    int TimeoutSeconds = 100,
    int MaxRetries = 2) : BlockConfig;

public sealed record ExpressionBlockConfig(
    string Template,
    Dictionary<string, string> Fields) : BlockConfig;

public sealed record ValidatorBlockConfig(
    string RuleKind,
    string Rule,
    bool RejectOnFailure,
    DatasetCleanupRules? Cleanup = null) : BlockConfig;

public sealed record SamplerBlockConfig(
    long? Count,
    double? Ratio,
    int Seed,
    string[] Splits) : BlockConfig;

public sealed record ToolProfileBlockConfig(ToolProfile Profile) : BlockConfig;