using System.Text.Json;
using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Services;

public sealed record PresetLoadResult(
    IReadOnlyList<RecipePresetDocument> Presets,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Persists user presets as one JSON file per preset under the application data folder.
/// Writes are atomic; invalid files are skipped and reported through <see cref="PresetLoadResult.Warnings"/>.
/// </summary>
public sealed class RecipePresetStore
{
    public const int MaxNameLength = 120;
    public const int MaxDescriptionLength = 2000;

    public RecipePresetStore(string? directoryPath = null)
    {
        DirectoryPath = Path.GetFullPath(directoryPath ?? GetDefaultDirectory());
    }

    public string DirectoryPath { get; }

    public static string GetDefaultDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LMTrainingDataStudio",
        "presets");

    public PresetLoadResult LoadAll()
    {
        if (!Directory.Exists(DirectoryPath))
        {
            return new PresetLoadResult(Array.Empty<RecipePresetDocument>(), Array.Empty<string>());
        }

        var warnings = new List<string>();
        var presets = new List<RecipePresetDocument>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string[] files;
        try
        {
            files = Directory.GetFiles(DirectoryPath, "*.json");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"Could not enumerate preset directory '{DirectoryPath}': {ex.Message}");
            return new PresetLoadResult(presets, warnings);
        }

        foreach (var file in files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var document = ReadFile(file);
                if (!seenIds.Add(document.Id))
                {
                    warnings.Add($"Preset '{file}' uses duplicate id '{document.Id}' and was skipped.");
                    continue;
                }

                presets.Add(document);
            }
            catch (Exception ex) when (ex is
                RecipePresetFormatException or IOException or UnauthorizedAccessException or NotSupportedException)
            {
                warnings.Add(ex.Message);
            }
        }

        var ordered = presets
            .OrderByDescending(preset => preset.CreatedAt)
            .ThenBy(preset => preset.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return new PresetLoadResult(ordered, warnings);
    }

    public async Task<RecipePresetDocument> SaveAsync(
        string name,
        string? description,
        Recipe recipe,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        var normalizedName = NormalizeName(name);
        var normalizedDescription = NormalizeDescription(description);

        if (recipe.Blocks.Count == 0)
        {
            throw new ArgumentException("Preset recipe must contain at least one block.", nameof(recipe));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = CloneRecipe(recipe);
        snapshot.Name = normalizedName;

        var document = new RecipePresetDocument
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = normalizedName,
            Description = normalizedDescription,
            CreatedAt = DateTime.UtcNow,
            Recipe = snapshot
        };

        Directory.CreateDirectory(DirectoryPath);
        await WriteFileAsync(document, cancellationToken);
        return document;
    }

    /// <summary>
    /// Reads a preset file and stores an independent copy in the local preset library.
    /// A new local id is assigned so importing never overwrites an existing preset.
    /// </summary>
    public async Task<RecipePresetDocument> ImportAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        cancellationToken.ThrowIfCancellationRequested();

        var document = ReadFile(Path.GetFullPath(sourcePath));
        document.Id = Guid.NewGuid().ToString("N");
        document.CreatedAt = DateTime.UtcNow;

        Directory.CreateDirectory(DirectoryPath);
        await WriteFileAsync(document, cancellationToken);
        return document;
    }

    public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParseExact(id, "N", out _))
        {
            throw new ArgumentException($"Invalid preset id '{id}'.", nameof(id));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var path = GetPresetPath(id);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Builds a fresh recipe from a stored preset so editor changes do not mutate the library copy.
    /// </summary>
    public static Recipe BuildRecipe(RecipePresetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var recipe = CloneRecipe(document.Recipe);
        recipe.Name = document.Name;
        return recipe;
    }

    private static Recipe CloneRecipe(Recipe recipe)
    {
        var json = JsonSerializer.Serialize(recipe, RecipeStore.SerializerOptions);
        return JsonSerializer.Deserialize<Recipe>(json, RecipeStore.SerializerOptions)
               ?? throw new InvalidOperationException("Preset recipe could not be cloned.");
    }

    private static string NormalizeName(string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("Preset name must not be empty.", nameof(name));
        }

        return trimmed.Length <= MaxNameLength
            ? trimmed
            : throw new ArgumentException(
                $"Preset name must be at most {MaxNameLength} characters.", nameof(name));
    }

    private static string NormalizeDescription(string? description)
    {
        var trimmed = description?.Trim() ?? string.Empty;
        return trimmed.Length <= MaxDescriptionLength
            ? trimmed
            : throw new ArgumentException(
                $"Preset description must be at most {MaxDescriptionLength} characters.",
                nameof(description));
    }

    private RecipePresetDocument ReadFile(string path)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new RecipePresetFormatException(path, ex.Message, innerException: ex);
        }

        RecipePresetDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<RecipePresetDocument>(json, RecipeStore.SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new RecipePresetFormatException(
                path, ex.Message, ex.LineNumber, ex.BytePositionInLine, ex);
        }
        catch (NotSupportedException ex)
        {
            throw new RecipePresetFormatException(path, ex.Message, innerException: ex);
        }

        if (document is null)
        {
            throw new RecipePresetFormatException(path, "File does not contain a preset object.");
        }

        if (document.SchemaVersion != RecipePresetDocument.CurrentSchemaVersion)
        {
            throw new RecipePresetFormatException(
                path,
                $"Unsupported preset schema version {document.SchemaVersion}; " +
                $"expected {RecipePresetDocument.CurrentSchemaVersion}.");
        }

        if (!Guid.TryParseExact(document.Id, "N", out _))
        {
            throw new RecipePresetFormatException(path, "Preset id must be a 32-character GUID.");
        }

        try
        {
            document.Name = NormalizeName(document.Name);
            document.Description = NormalizeDescription(document.Description);
        }
        catch (ArgumentException ex)
        {
            throw new RecipePresetFormatException(path, ex.Message, innerException: ex);
        }

        if (document.Recipe is null)
        {
            throw new RecipePresetFormatException(path, "Preset does not contain a recipe.");
        }

        if (document.Recipe.SchemaVersion != Recipe.CurrentSchemaVersion)
        {
            throw new RecipePresetFormatException(
                path,
                $"Unsupported recipe schema version {document.Recipe.SchemaVersion}; " +
                $"expected {Recipe.CurrentSchemaVersion}.");
        }

        if (document.Recipe.Blocks is not { Count: > 0 })
        {
            throw new RecipePresetFormatException(path, "Preset recipe has no blocks.");
        }

        if (document.Recipe.Edges is null)
        {
            document.Recipe.Edges = new List<RecipeEdge>();
        }

        return document;
    }

    private async Task WriteFileAsync(RecipePresetDocument document, CancellationToken cancellationToken)
    {
        var path = GetPresetPath(document.Id);
        var tempPath = path + ".tmp";
        try
        {
            await using (var stream = new FileStream(
                tempPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, document, RecipeStore.SerializerOptions, cancellationToken);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private string GetPresetPath(string id) => Path.Combine(DirectoryPath, id + ".json");

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
