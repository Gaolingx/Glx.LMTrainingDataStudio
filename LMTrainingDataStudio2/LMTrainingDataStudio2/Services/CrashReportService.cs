using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Services;

/// <summary>
/// Builds a zip package with logs, redacted settings and execution summaries
/// for bug reports. Credentials are removed from every text entry.
/// </summary>
public static class CrashReportService
{
    private const int MaxExecutionFiles = 10;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Writes a diagnostics package to <paramref name="outputPath"/>.
    /// </summary>
    public static void CreateReport(
        string outputPath,
        string? reason = null,
        Exception? exception = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var workingDirectory = Path.Combine(
            Path.GetTempPath(), "lmtds-diagnostics", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);

        try
        {
            var settings = TryLoadSettings(out var settingsError);
            var secrets = CollectKnownSecrets(settings);

            WriteSummary(workingDirectory, reason, exception, secrets);
            CopyLogs(workingDirectory, secrets);
            WriteRedactedSettings(workingDirectory, settings, settingsError);
            CopyExecutionSummaries(workingDirectory, secrets);

            var temporaryPath = fullPath + ".tmp";
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            ZipFile.CreateFromDirectory(
                workingDirectory, temporaryPath, CompressionLevel.Optimal, includeBaseDirectory: false);
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            try { Directory.Delete(workingDirectory, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Writes an automatic crash package next to the application logs.
    /// Returns null when the report cannot be created.
    /// </summary>
    public static string? WriteAutomaticCrashReport(string reason, Exception? exception)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LMTrainingDataStudio",
                "crashes");
            Directory.CreateDirectory(directory);
            AppDirectorySecurity.TryRestrictToCurrentUser(directory);

            var path = Path.Combine(
                directory, $"crash-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.zip");
            CreateReport(path, reason, exception);
            return path;
        }
        catch
        {
            return null;
        }
    }

    private static AppSettings? TryLoadSettings(out string? error)
    {
        try
        {
            error = null;
            return new SettingsStore().Load().Settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                       JsonException or NotSupportedException)
        {
            error = ex.Message;
            return null;
        }
    }

    private static string[] CollectKnownSecrets(AppSettings? settings)
    {
        var secrets = new List<string?>
        {
            settings?.OpenAiApiKey,
            settings?.AnthropicApiKey,
            Environment.GetEnvironmentVariable("OPENAI_API_KEY"),
            Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
        };

        return secrets
            .Where(secret => !string.IsNullOrWhiteSpace(secret))
            .Select(secret => secret!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static void WriteSummary(
        string directory, string? reason, Exception? exception, IReadOnlyCollection<string> secrets)
    {
        var version = typeof(CrashReportService).Assembly.GetName().Version?.ToString() ?? "unknown";
        var builder = new StringBuilder()
            .AppendLine("LLM Training Data Studio diagnostics")
            .AppendLine($"Timestamp (UTC): {DateTime.UtcNow:O}")
            .AppendLine($"App version: {version}")
            .AppendLine($"OS: {Environment.OSVersion}")
            .AppendLine($"Runtime: {Environment.Version}")
            .AppendLine($"64-bit process: {Environment.Is64BitProcess}")
            .AppendLine($"Processor count: {Environment.ProcessorCount}")
            .AppendLine($"Working set: {Environment.WorkingSet / (1024.0 * 1024.0):F1} MB")
            .AppendLine($"Log directory: {AppLog.LogDirectory}");

        if (!string.IsNullOrWhiteSpace(reason))
        {
            builder.AppendLine().AppendLine($"Reason: {reason}");
        }

        if (exception is not null)
        {
            builder.AppendLine().AppendLine("Exception:").AppendLine(exception.ToString());
        }

        var text = DiagnosticRedactor.Redact(builder.ToString(), secrets);
        File.WriteAllText(Path.Combine(directory, "summary.txt"), text, Encoding.UTF8);
    }

    /// <summary>
    /// Removes secrets from settings before they are written to a diagnostics package.
    /// </summary>
    public static void RedactSettingsForDiagnostics(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.OpenAiApiKey = null;
        settings.AnthropicApiKey = null;
    }

    private static void CopyLogs(string directory, IReadOnlyCollection<string> secrets)
    {
        if (!Directory.Exists(AppLog.LogDirectory)) return;

        var logsDirectory = Path.Combine(directory, "logs");
        Directory.CreateDirectory(logsDirectory);

        foreach (var file in Directory.EnumerateFiles(AppLog.LogDirectory, "*.log"))
        {
            try
            {
                var text = ReadShared(file);
                File.WriteAllText(
                    Path.Combine(logsDirectory, Path.GetFileName(file)),
                    DiagnosticRedactor.Redact(text, secrets),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The active log may be locked for a moment; skip it.
            }
        }
    }

    private static string ReadShared(string path)
    {
        using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static void WriteRedactedSettings(
        string directory, AppSettings? settings, string? error)
    {
        var path = Path.Combine(directory, "settings.redacted.json");
        if (settings is null)
        {
            File.WriteAllText(
                path,
                $"{{\"error\": {JsonSerializer.Serialize(error ?? "Settings could not be loaded.")}}}",
                Encoding.UTF8);
            return;
        }

        RedactSettingsForDiagnostics(settings);
        var json = JsonSerializer.Serialize(settings, SerializerOptions);
        File.WriteAllText(path, DiagnosticRedactor.Redact(json), Encoding.UTF8);
    }

    private static void CopyExecutionSummaries(string directory, IReadOnlyCollection<string> secrets)
    {
        var executionsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LMTrainingDataStudio",
            "executions");
        if (!Directory.Exists(executionsDirectory)) return;

        var target = Path.Combine(directory, "executions");
        Directory.CreateDirectory(target);

        var files = Directory.EnumerateFiles(executionsDirectory, "*.json")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Take(MaxExecutionFiles);

        foreach (var file in files)
        {
            try
            {
                var text = ReadShared(file.FullName);
                File.WriteAllText(
                    Path.Combine(target, file.Name),
                    DiagnosticRedactor.Redact(text, secrets),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best effort only.
            }
        }
    }
}
