namespace LMTrainingDataStudio2.Models;

/// <summary>
/// Types of recipe blocks available in the DAG editor.
/// </summary>
public enum BlockType
{
    Seed,
    Llm,
    Expression,
    Validator,
    Sampler,
    ToolProfile
}

/// <summary>
/// Represents a single block (node) in the Recipe DAG.
/// </summary>
public sealed class RecipeBlock
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public BlockType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 200;
    public double Height { get; set; } = 80;
    public List<BlockPort> InputPorts { get; set; } = new();
    public List<BlockPort> OutputPorts { get; set; } = new();
    public Dictionary<string, object?> Properties { get; set; } = new();
}

/// <summary>
/// A connection port on a block node.
/// </summary>
public sealed class BlockPort
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string DataType { get; set; } = "any";
    public PortDirection Direction { get; set; }
}

public enum PortDirection
{
    Input,
    Output
}

/// <summary>
/// An edge connecting two ports in the Recipe DAG.
/// </summary>
public sealed class RecipeEdge
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SourceBlockId { get; set; } = string.Empty;
    public string SourcePortId { get; set; } = string.Empty;
    public string TargetBlockId { get; set; } = string.Empty;
    public string TargetPortId { get; set; } = string.Empty;
    public string? Label { get; set; }
}

/// <summary>
/// A complete recipe containing blocks and edges.
/// </summary>
public sealed class Recipe
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Untitled Recipe";
    public List<RecipeBlock> Blocks { get; set; } = new();
    public List<RecipeEdge> Edges { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
}
