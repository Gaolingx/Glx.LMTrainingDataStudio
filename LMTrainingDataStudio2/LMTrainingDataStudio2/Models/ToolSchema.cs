namespace LMTrainingDataStudio2.Models;

/// <summary>
/// OpenAI-compatible Tool Schema for visual editing.
/// Supports nested object parameters with expand/collapse.
/// </summary>
public sealed class ToolSchemaNode
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "string";
    public string? Description { get; set; }
    public bool IsRequired { get; set; }
    public bool IsExpanded { get; set; } = true;
    public List<ToolSchemaNode> Children { get; set; } = new();
    public List<string>? EnumValues { get; set; }
    public object? DefaultValue { get; set; }
}

/// <summary>
/// A complete tool profile containing multiple tool definitions.
/// </summary>
public sealed class ToolProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public List<ToolDefinition> Tools { get; set; } = new();
}
