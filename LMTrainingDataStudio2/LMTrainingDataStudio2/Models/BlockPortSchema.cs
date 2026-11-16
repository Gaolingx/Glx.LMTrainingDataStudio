namespace LMTrainingDataStudio2.Models;

/// <summary>
/// Declares the fixed port contract for a block type.
/// </summary>
public sealed record BlockPortSpec(string Name, string DataType);

/// <summary>
/// Single source of truth for block input/output port names, directions and data types.
/// Seed and Query Source are sources (output only), SFT Export is a sink (input only),
/// Tool Profile carries no data ports, and every other block is a transform.
/// </summary>
public static class BlockPortSchema
{
    public const string RowsDataType = "rows";

    public static readonly BlockPortSpec Input = new("input", RowsDataType);
    public static readonly BlockPortSpec Output = new("output", RowsDataType);
    public static readonly BlockPortSpec SeedOutput = new("rows", RowsDataType);
    public static readonly BlockPortSpec QueryOutput = new("queries", RowsDataType);

    public static bool ExpectsInput(BlockType type) =>
        type is not (BlockType.Seed or BlockType.QuerySource or BlockType.ToolProfile);

    public static bool ExpectsOutput(BlockType type) =>
        type is not (BlockType.SftExport or BlockType.ToolProfile);

    public static BlockPortSpec? GetInputSpec(BlockType type) =>
        ExpectsInput(type) ? Input : null;

    public static BlockPortSpec? GetOutputSpec(BlockType type) => type switch
    {
        BlockType.Seed => SeedOutput,
        BlockType.QuerySource => QueryOutput,
        BlockType.SftExport or BlockType.ToolProfile => null,
        _ => Output
    };

    public static List<BlockPort> CreateInputPorts(BlockType type) =>
        GetInputSpec(type) is { } spec
            ? [new BlockPort { Name = spec.Name, DataType = spec.DataType, Direction = PortDirection.Input }]
            : [];

    public static List<BlockPort> CreateOutputPorts(BlockType type) =>
        GetOutputSpec(type) is { } spec
            ? [new BlockPort { Name = spec.Name, DataType = spec.DataType, Direction = PortDirection.Output }]
            : [];
}
