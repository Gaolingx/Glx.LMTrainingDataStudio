using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Services;

/// <summary>
/// Builds a zip package with logs, redacted settings and execution summaries
/// for bug reports. API keys are always removed before writing.
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
            WriteSummary(workingDirectory, reason, exception);
            CopyLogs(workingDirectory);
            WriteRedactedSettings(workingDirectory);
            CopyExecutionSummaries(workingDirectory);

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

    private static void WriteSummary(string directory, string? reason, Exception? exception)
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

        File.WriteAllText(Path.Combine(directory, "summary.txt"), builder.ToString(), Encoding.UTF8);
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

    private static void CopyLogs(string directory)
    {
        if (!Directory.Exists(AppLog.LogDirectory)) return;

        var logsDirectory = Path.Combine(directory, "logs");
        Directory.CreateDirectory(logsDirectory);

        foreach (var file in Directory.EnumerateFiles(AppLog.LogDirectory, "*.log"))
        {
            try
            {
                File.Copy(file, Path.Combine(logsDirectory, Path.GetFileName(file)), overwrite: true);
            }
            catch (IOException)
            {
                // The active log may be locked for a moment; skip it.
            }
        }
    }

    private static void WriteRedactedSettings(string directory)
    {
        try
        {
            var settings = new SettingsStore().Load().Settings;
            RedactSettingsForDiagnostics(settings);
            File.WriteAllText(
                Path.Combine(directory, "settings.redacted.json"),
                JsonSerializer.Serialize(settings, SerializerOptions),
                Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            File.WriteAllText(
                Path.Combine(directory, "settings.redacted.json"),
                $"{{\"error\": {JsonSerializer.Serialize(ex.Message)}}}",
                Encoding.UTF8);
        }
    }

    private static void CopyExecutionSummaries(string directory)
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
                File.Copy(file.FullName, Path.Combine(target, file.Name), overwrite: true);
            }
            catch (IOException)
            {
                // Best effort only.
            }
        }
    }
}
