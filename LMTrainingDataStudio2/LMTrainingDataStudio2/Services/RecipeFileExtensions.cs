using System.IO;

namespace LMTrainingDataStudio2.Services;

/// <summary>
/// Shared recipe file extension rules used by startup, file pickers and save flows.
/// </summary>
public static class RecipeFileExtensions
{
    public const string Json = ".json";
    public const string Recipe = ".recipe";

    public static readonly string[] Patterns = ["*.json", "*.recipe"];

    /// <summary>
    /// Returns true for <c>.json</c>, <c>.recipe</c> and compound names such as <c>.recipe.json</c>.
    /// </summary>
    public static bool IsRecipePath(string? path) =>
        !string.IsNullOrWhiteSpace(path) && HasRecipeExtension(path.Trim());

    /// <summary>
    /// Selects recipe paths from command-line arguments, preserving their order.
    /// </summary>
    public static string[] FilterRecipePaths(IEnumerable<string> arguments) =>
        arguments.Where(IsRecipePath).ToArray();

    /// <summary>
    /// Preserves a recognized recipe extension; otherwise appends <c>.json</c>.
    /// </summary>
    public static string EnsureRecipeExtension(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var trimmed = path.Trim();
        return HasRecipeExtension(trimmed) ? trimmed : trimmed + Json;
    }

    /// <summary>
    /// Builds a safe default file name for Save As, keeping an explicitly typed recipe extension.
    /// </summary>
    public static string GetSuggestedFileName(string? recipeName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = new string((recipeName ?? string.Empty).Trim()
            .Select(character => invalid.Contains(character) ? '_' : character)
            .ToArray());

        if (string.IsNullOrWhiteSpace(name))
        {
            name = "recipe";
        }

        return EnsureRecipeExtension(name);
    }

    private static bool HasRecipeExtension(string path) =>
        IsRecipeExtension(Path.GetExtension(path));

    private static bool IsRecipeExtension(string extension) =>
        string.Equals(extension, Json, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(extension, Recipe, StringComparison.OrdinalIgnoreCase);
}
