using LMTrainingDataStudio2.Services.Executors;

namespace LMTrainingDataStudio2.Models;

/// <summary>
/// Declares the fixed port contract for a block type.
/// </summary>
public sealed record BlockPortSpec(string Name, string DataType);

/// <summary>
/// Single source of truth for block input/output port names, directions and data types.
/// Seed and Query Source are sources (output only), SFT Export is a sink (input only),
/// Tool Profile carries no data ports, and every other block is a transform.
/// Validator exposes pass/reject ports and Sampler exposes one port per split.
/// </summary>
public static class BlockPortSchema
{
    public const string RowsDataType = "rows";
    public const string ValidatorPassPort = "pass";
    public const string ValidatorRejectPort = "reject";

    public static readonly BlockPortSpec Input = new("input", RowsDataType);
    public static readonly BlockPortSpec Output = new("output", RowsDataType);
    public static readonly BlockPortSpec SeedOutput = new("rows", RowsDataType);
    public static readonly BlockPortSpec QueryOutput = new("queries", RowsDataType);
    public static readonly BlockPortSpec PassOutput = new(ValidatorPassPort, RowsDataType);
    public static readonly BlockPortSpec RejectOutput = new(ValidatorRejectPort, RowsDataType);

    public static bool ExpectsInput(BlockType type) =>
        type is not (BlockType.Seed or BlockType.QuerySource or BlockType.ToolProfile);

    public static bool ExpectsOutput(BlockType type) =>
        type is not (BlockType.SftExport or BlockType.ToolProfile);

    public static BlockPortSpec? GetInputSpec(BlockType type) =>
        ExpectsInput(type) ? Input : null;

    /// <summary>
    /// Primary output port used for descriptions and single-stream fallbacks.
    /// </summary>
    public static BlockPortSpec? GetOutputSpec(BlockType type) => type switch
    {
        BlockType.Seed => SeedOutput,
        BlockType.QuerySource => QueryOutput,
        BlockType.SftExport or BlockType.ToolProfile => null,
        BlockType.Validator => PassOutput,
        _ => Output
    };

    /// <summary>
    /// All output ports for a block. Sampler ports follow its split names when configured.
    /// </summary>
    public static IReadOnlyList<BlockPortSpec> GetOutputSpecs(BlockType type, BlockConfig? config = null)
    {
        if (type == BlockType.Validator)
        {
            return [PassOutput, RejectOutput];
        }

        if (type == BlockType.Sampler &&
            config is SamplerBlockConfig { Splits.Length: > 0 } sampler &&
            SamplerSplitPlan.TryParse(sampler.Splits, out var plan, out _))
        {
            return plan!.Specs.Select(spec => new BlockPortSpec(spec.Name, RowsDataType)).ToArray();
        }

        return GetOutputSpec(type) is { } spec ? [spec] : [];
    }

    public static List<BlockPort> CreateInputPorts(BlockType type) =>
        GetInputSpec(type) is { } spec
            ? [new BlockPort { Name = spec.Name, DataType = spec.DataType, Direction = PortDirection.Input }]
            : [];

    public static List<BlockPort> CreateOutputPorts(BlockType type) =>
        CreateOutputPorts(type, config: null);

    public static List<BlockPort> CreateOutputPorts(BlockType type, BlockConfig? config) =>
        GetOutputSpecs(type, config)
            .Select(spec => new BlockPort
            {
                Name = spec.Name,
                DataType = spec.DataType,
                Direction = PortDirection.Output
            })
            .ToList();
}
