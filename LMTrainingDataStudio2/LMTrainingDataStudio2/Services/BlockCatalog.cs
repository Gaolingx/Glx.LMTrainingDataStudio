using LMTrainingDataStudio2.Localization;
using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Services;

public sealed record BlockDefinition(BlockType Type, string NameKey, string SummaryKey, string HelpKey);

/// <summary>
/// Single source of truth for localized block names, descriptions and port summaries.
/// </summary>
public static class BlockCatalog
{
    public static IReadOnlyList<BlockType> Types { get; } =
    [
        BlockType.Seed,
        BlockType.Llm,
        BlockType.Expression,
        BlockType.Validator,
        BlockType.Sampler,
        BlockType.ToolProfile,
        BlockType.QuerySource,
        BlockType.Simulation,
        BlockType.Cleaner,
        BlockType.SftExport
    ];

    private static readonly Dictionary<BlockType, BlockDefinition> Definitions = new()
    {
        [BlockType.Seed] = new(BlockType.Seed, "node.seed.name", "node.seed.summary", "node.seed.help"),
        [BlockType.Llm] = new(BlockType.Llm, "node.llm.name", "node.llm.summary", "node.llm.help"),
        [BlockType.Expression] = new(
            BlockType.Expression, "node.expression.name", "node.expression.summary", "node.expression.help"),
        [BlockType.Validator] = new(
            BlockType.Validator, "node.validator.name", "node.validator.summary", "node.validator.help"),
        [BlockType.Sampler] = new(BlockType.Sampler, "node.sampler.name", "node.sampler.summary", "node.sampler.help"),
        [BlockType.ToolProfile] = new(
            BlockType.ToolProfile, "node.toolprofile.name", "node.toolprofile.summary", "node.toolprofile.help"),
        [BlockType.QuerySource] = new(
            BlockType.QuerySource, "node.querysource.name", "node.querysource.summary", "node.querysource.help"),
        [BlockType.Simulation] = new(
            BlockType.Simulation, "node.simulation.name", "node.simulation.summary", "node.simulation.help"),
        [BlockType.Cleaner] = new(BlockType.Cleaner, "node.cleaner.name", "node.cleaner.summary", "node.cleaner.help"),
        [BlockType.SftExport] = new(
            BlockType.SftExport, "node.sftexport.name", "node.sftexport.summary", "node.sftexport.help")
    };

    public static BlockDefinition Get(BlockType type) =>
        Definitions.TryGetValue(type, out var definition)
            ? definition
            : throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported block type.");

    public static string GetName(BlockType type) => LocalizationService.L(Get(type).NameKey);

    public static string GetSummary(BlockType type) => LocalizationService.L(Get(type).SummaryKey);

    public static string GetHelp(BlockType type) => LocalizationService.L(Get(type).HelpKey);

    public static string DescribePorts(BlockType type)
    {
        var input = BlockPortSchema.GetInputSpec(type);
        var output = BlockPortSchema.GetOutputSpec(type);
        var inputText = input is null
            ? LocalizationService.L("node.port.none")
            : $"{input.Name} ({input.DataType})";
        var outputText = output is null
            ? LocalizationService.L("node.port.none")
            : $"{output.Name} ({output.DataType})";
        return string.Format(LocalizationService.L("property.ports_format"), inputText, outputText);
    }
}
