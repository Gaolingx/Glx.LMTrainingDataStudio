using System.Text.Json;
using System.Text.Json.Serialization;
using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Services;

/// <summary>
/// Stores settings under the user's application data folder.
/// Invalid values fall back to defaults and are reported through <see cref="SettingsLoadResult.Warnings"/>.
/// </summary>
public sealed class SettingsStore : ISettingsStore
{
    public const long MinCacheSizeMb = 16;
    public const int MinGridSnapSize = 1;
    public const int MaxGridSnapSize = 256;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public SettingsStore(string? filePath = null)
    {
        FilePath = Path.GetFullPath(filePath ?? GetDefaultFilePath());
    }

    public string FilePath { get; }

    public static string GetDefaultFilePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LMTrainingDataStudio",
        "settings.json");

    public SettingsLoadResult Load()
    {
        var warnings = new List<string>();
        var defaults = new AppSettings();

        if (!File.Exists(FilePath))
        {
            return new SettingsLoadResult(defaults, warnings);
        }

        AppSettings? settings;
        try
        {
            var json = File.ReadAllText(FilePath);
            settings = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            warnings.Add($"Failed to read settings from '{FilePath}': {ex.Message}");
            return new SettingsLoadResult(defaults, warnings);
        }

        if (settings is null)
        {
            warnings.Add($"Settings file '{FilePath}' does not contain a settings object; using defaults.");
            return new SettingsLoadResult(defaults, warnings);
        }

        if (settings.MaxCacheSizeMb < MinCacheSizeMb)
        {
            warnings.Add(
                $"maxCacheSizeMb must be at least {MinCacheSizeMb}; found {settings.MaxCacheSizeMb}. " +
                $"Using default {defaults.MaxCacheSizeMb}.");
            settings.MaxCacheSizeMb = defaults.MaxCacheSizeMb;
        }

        if (settings.GridSnapSize < MinGridSnapSize || settings.GridSnapSize > MaxGridSnapSize)
        {
            warnings.Add(
                $"gridSnapSize must be between {MinGridSnapSize} and {MaxGridSnapSize}; found {settings.GridSnapSize}. " +
                $"Using default {defaults.GridSnapSize}.");
            settings.GridSnapSize = defaults.GridSnapSize;
        }

        if (string.IsNullOrWhiteSpace(settings.CacheDirectory))
        {
            warnings.Add($"cacheDirectory must not be empty. Using default '{defaults.CacheDirectory}'.");
            settings.CacheDirectory = defaults.CacheDirectory;
        }
        else
        {
            settings.CacheDirectory = settings.CacheDirectory.Trim();
        }

        return new SettingsLoadResult(settings, warnings);
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

        var tempPath = FilePath + ".tmp";
        try
        {
            await using (var stream = new FileStream(
                tempPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, settings, SerializerOptions, cancellationToken);
            }

            File.Move(tempPath, FilePath, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
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