using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using LMTrainingDataStudio2.Localization;
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
    public const int MinParallelism = 1;
    public const int MaxParallelism = 64;
    public const int MaxNodeRetries = 10;
    public const int MaxRecentFiles = 10;
    public const long MaxSpillThresholdRows = 10_000_000;
    public const long MaxSpillThresholdMb = 4_096;
    public const long MaxCheckpointQuotaMb = 1_000_000;
    public const int MaxExecutionHistoryLimit = 10_000;
    public const int MaxExecutionRetentionDays = 3_650;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IApiKeyProtector _apiKeyProtector;

    public SettingsStore(string? filePath = null, IApiKeyProtector? apiKeyProtector = null)
    {
        FilePath = Path.GetFullPath(filePath ?? GetDefaultFilePath());
        _apiKeyProtector = apiKeyProtector ?? new DpapiApiKeyProtector();
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

        if (!LanguageCatalog.IsSupported(settings.Language))
        {
            warnings.Add(
                $"language must be one of 'system', '{LocalizationService.Chinese}', '{LocalizationService.English}'; " +
                $"found '{settings.Language}'. Using default '{defaults.Language}'.");
            settings.Language = defaults.Language;
        }
        else
        {
            settings.Language = string.IsNullOrWhiteSpace(settings.Language)
                ? defaults.Language
                : settings.Language.Trim();
        }

        if (settings.MaxParallelism < MinParallelism || settings.MaxParallelism > MaxParallelism)
        {
            warnings.Add(
                $"maxParallelism must be between {MinParallelism} and {MaxParallelism}; found {settings.MaxParallelism}. " +
                $"Using default {defaults.MaxParallelism}.");
            settings.MaxParallelism = defaults.MaxParallelism;
        }

        if (settings.NodeRetryCount < 0 || settings.NodeRetryCount > MaxNodeRetries)
        {
            warnings.Add(
                $"nodeRetryCount must be between 0 and {MaxNodeRetries}; found {settings.NodeRetryCount}. " +
                $"Using default {defaults.NodeRetryCount}.");
            settings.NodeRetryCount = defaults.NodeRetryCount;
        }

        if (string.IsNullOrWhiteSpace(settings.RunOutputDirectory))
        {
            warnings.Add(
                $"runOutputDirectory must not be empty. Using default '{defaults.RunOutputDirectory}'.");
            settings.RunOutputDirectory = defaults.RunOutputDirectory;
        }
        else
        {
            settings.RunOutputDirectory = settings.RunOutputDirectory.Trim();
        }

        if (settings.SpillThresholdRows < 0 || settings.SpillThresholdRows > MaxSpillThresholdRows)
        {
            warnings.Add(
                $"spillThresholdRows must be between 0 and {MaxSpillThresholdRows}; " +
                $"found {settings.SpillThresholdRows}. Using default {defaults.SpillThresholdRows}.");
            settings.SpillThresholdRows = defaults.SpillThresholdRows;
        }

        if (settings.SpillThresholdBytes < 0 ||
            settings.SpillThresholdBytes > MaxSpillThresholdMb * 1024L * 1024L)
        {
            warnings.Add(
                $"spillThresholdBytes must be between 0 and {MaxSpillThresholdMb * 1024L * 1024L}; " +
                $"found {settings.SpillThresholdBytes}. Using default {defaults.SpillThresholdBytes}.");
            settings.SpillThresholdBytes = defaults.SpillThresholdBytes;
        }

        if (string.IsNullOrWhiteSpace(settings.RunTempDirectory))
        {
            warnings.Add(
                $"runTempDirectory must not be empty. Using default '{defaults.RunTempDirectory}'.");
            settings.RunTempDirectory = defaults.RunTempDirectory;
        }
        else
        {
            settings.RunTempDirectory = settings.RunTempDirectory.Trim();
        }

        if (settings.CheckpointQuotaMb < 0 || settings.CheckpointQuotaMb > MaxCheckpointQuotaMb)
        {
            warnings.Add(
                $"checkpointQuotaMb must be between 0 and {MaxCheckpointQuotaMb}; " +
                $"found {settings.CheckpointQuotaMb}. Using default {defaults.CheckpointQuotaMb}.");
            settings.CheckpointQuotaMb = defaults.CheckpointQuotaMb;
        }

        if (settings.ExecutionHistoryLimit < 0 ||
            settings.ExecutionHistoryLimit > MaxExecutionHistoryLimit)
        {
            warnings.Add(
                $"executionHistoryLimit must be between 0 and {MaxExecutionHistoryLimit}; " +
                $"found {settings.ExecutionHistoryLimit}. Using default {defaults.ExecutionHistoryLimit}.");
            settings.ExecutionHistoryLimit = defaults.ExecutionHistoryLimit;
        }

        if (settings.ExecutionHistoryRetentionDays < 0 ||
            settings.ExecutionHistoryRetentionDays > MaxExecutionRetentionDays)
        {
            warnings.Add(
                $"executionHistoryRetentionDays must be between 0 and {MaxExecutionRetentionDays}; " +
                $"found {settings.ExecutionHistoryRetentionDays}. " +
                $"Using default {defaults.ExecutionHistoryRetentionDays}.");
            settings.ExecutionHistoryRetentionDays = defaults.ExecutionHistoryRetentionDays;
        }

        settings.RecentFiles = settings.RecentFiles?
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxRecentFiles)
            .ToList() ?? new List<string>();

        settings.LastRecipePath = string.IsNullOrWhiteSpace(settings.LastRecipePath)
            ? null
            : settings.LastRecipePath.Trim();

        var keysMigrated = false;
        settings.OpenAiApiKey = ReadApiKey(
            settings.OpenAiApiKey, "openAiApiKey", warnings, ref keysMigrated);
        settings.AnthropicApiKey = ReadApiKey(
            settings.AnthropicApiKey, "anthropicApiKey", warnings, ref keysMigrated);

        if (keysMigrated)
        {
            TryMigrateApiKeys(settings, warnings);
        }

        return new SettingsLoadResult(settings, warnings);
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();

        var settingsDirectory = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(settingsDirectory);
        AppDirectorySecurity.TryRestrictToCurrentUser(settingsDirectory);

        var persisted = PrepareForStorage(settings);
        var tempPath = FilePath + ".tmp";
        try
        {
            await using (var stream = new FileStream(
                tempPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, persisted, SerializerOptions, cancellationToken);
            }

            File.Move(tempPath, FilePath, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private AppSettings PrepareForStorage(AppSettings settings)
    {
        var persisted = settings.Clone();
        persisted.OpenAiApiKey = ProtectApiKey(settings.OpenAiApiKey, "openAiApiKey");
        persisted.AnthropicApiKey = ProtectApiKey(settings.AnthropicApiKey, "anthropicApiKey");
        return persisted;
    }

    private string? ProtectApiKey(string? plaintext, string name)
    {
        if (string.IsNullOrWhiteSpace(plaintext))
        {
            return null;
        }

        var value = plaintext.Trim();
        if (_apiKeyProtector.IsProtected(value))
        {
            return value;
        }

        if (!_apiKeyProtector.IsAvailable)
        {
            AppLog.Warning(
                $"{name} was not saved because API keys are only protected on Windows; " +
                "set the matching environment variable instead.");
            return null;
        }

        try
        {
            return _apiKeyProtector.Protect(value);
        }
        catch (Exception ex) when (ex is CryptographicException or PlatformNotSupportedException)
        {
            throw new IOException($"Failed to encrypt {name} for storage: {ex.Message}", ex);
        }
    }

    private string? ReadApiKey(
        string? storedValue,
        string name,
        List<string> warnings,
        ref bool migrated)
    {
        if (string.IsNullOrWhiteSpace(storedValue))
        {
            return null;
        }

        var value = storedValue.Trim();
        if (!_apiKeyProtector.IsProtected(value))
        {
            if (_apiKeyProtector.IsAvailable)
            {
                migrated = true;
            }
            else
            {
                warnings.Add(
                    $"{name} is stored in plaintext and cannot be encrypted on this platform; " +
                    "prefer the matching environment variable.");
            }

            return value;
        }

        if (!_apiKeyProtector.IsAvailable)
        {
            warnings.Add(
                $"{name} is encrypted with Windows DPAPI and cannot be read on this platform; " +
                "set the matching environment variable instead.");
            return null;
        }

        if (!_apiKeyProtector.TryUnprotect(value, out var plaintext) ||
            string.IsNullOrWhiteSpace(plaintext))
        {
            warnings.Add(
                $"{name} could not be decrypted and was ignored; re-enter the key in Settings.");
            return null;
        }

        return plaintext;
    }

    private void TryMigrateApiKeys(AppSettings settings, List<string> warnings)
    {
        var tempPath = FilePath + ".tmp";
        try
        {
            var settingsDirectory = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(settingsDirectory);
            AppDirectorySecurity.TryRestrictToCurrentUser(settingsDirectory);
            var persisted = PrepareForStorage(settings);
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, persisted, SerializerOptions);
            }

            File.Move(tempPath, FilePath, overwrite: true);
            warnings.Add(
                $"API keys in '{FilePath}' were migrated to Windows DPAPI encryption.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                       CryptographicException or PlatformNotSupportedException)
        {
            TryDelete(tempPath);
            warnings.Add(
                $"Failed to write encrypted API keys to '{FilePath}': {ex.Message}. " +
                "The plaintext keys remain until settings are saved again.");
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
