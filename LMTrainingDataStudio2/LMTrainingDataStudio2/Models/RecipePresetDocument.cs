namespace LMTrainingDataStudio2.Models;

/// <summary>
/// Serialized user preset: metadata plus a full recipe snapshot.
/// </summary>
public sealed class RecipePresetDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Recipe Recipe { get; set; } = new();
}
