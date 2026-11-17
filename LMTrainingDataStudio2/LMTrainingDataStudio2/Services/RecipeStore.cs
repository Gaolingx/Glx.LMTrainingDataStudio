using System.Text.Json;
using System.Text.Json.Serialization;
using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Services;

/// <summary>
/// Loads and saves recipe files using the current JSON schema.
/// Writes go through a temporary file so a failed save never corrupts the target.
/// </summary>
public sealed class RecipeStore
{
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task SaveAsync(Recipe recipe, string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        var tempPath = fullPath + ".tmp";
        try
        {
            await using (var stream = new FileStream(
                tempPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, recipe, SerializerOptions, cancellationToken);
            }

            File.Move(tempPath, fullPath, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    public async Task<Recipe> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);
            var recipe = await JsonSerializer.DeserializeAsync<Recipe>(stream, SerializerOptions, cancellationToken);

            if (recipe is null)
            {
                throw new RecipeFormatException(path, "File does not contain a recipe object.");
            }

            if (recipe.SchemaVersion != Recipe.CurrentSchemaVersion)
            {
                throw new RecipeFormatException(
                    path,
                    $"Unsupported recipe schema version {recipe.SchemaVersion}; expected {Recipe.CurrentSchemaVersion}.");
            }

            return recipe;
        }
        catch (JsonException ex)
        {
            throw new RecipeFormatException(path, ex.Message, ex.LineNumber, ex.BytePositionInLine, ex);
        }
    }

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
