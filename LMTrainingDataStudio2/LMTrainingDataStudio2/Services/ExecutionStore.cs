using System.Text.Json;
using System.Text.Json.Serialization;
using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Services;

/// <summary>
/// Persists execution runs under the user's application data folder.
/// Run metadata is written to {runId}.json and logs to {runId}.log.
/// </summary>
public sealed class ExecutionStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public ExecutionStore(string? directory = null)
    {
        DirectoryPath = Path.GetFullPath(directory ?? GetDefaultDirectory());
    }

    public string DirectoryPath { get; }

    public static string GetDefaultDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LMTrainingDataStudio",
        "executions");

    public async Task SaveAsync(ExecutionRun run, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        ValidateRunId(run.RunId);
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(DirectoryPath);

        var jsonPath = GetRunPath(run.RunId);
        var jsonTempPath = jsonPath + ".tmp";
        var logPath = GetLogPath(run.RunId);
        var logTempPath = logPath + ".tmp";

        try
        {
            await File.WriteAllLinesAsync(logTempPath, run.Logs, cancellationToken);

            await using (var stream = new FileStream(
                jsonTempPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, run, SerializerOptions, cancellationToken);
            }

            File.Move(logTempPath, logPath, overwrite: true);
            File.Move(jsonTempPath, jsonPath, overwrite: true);
        }
        catch
        {
            TryDelete(jsonTempPath);
            TryDelete(logTempPath);
            throw;
        }
    }

    public async Task<ExecutionRun?> LoadAsync(string runId, CancellationToken cancellationToken = default)
    {
        ValidateRunId(runId);
        cancellationToken.ThrowIfCancellationRequested();

        var jsonPath = GetRunPath(runId);
        if (!File.Exists(jsonPath))
        {
            return null;
        }

        var run = await ReadRunAsync(jsonPath, runId, cancellationToken);
        var logPath = GetLogPath(runId);
        if (File.Exists(logPath))
        {
            run.Logs.AddRange(await File.ReadAllLinesAsync(logPath, cancellationToken));
        }

        return run;
    }

    public async Task<IReadOnlyList<ExecutionRunSummary>> GetRecentAsync(
        int count, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        cancellationToken.ThrowIfCancellationRequested();

        if (count == 0 || !Directory.Exists(DirectoryPath))
        {
            return Array.Empty<ExecutionRunSummary>();
        }

        var summaries = new List<ExecutionRunSummary>();
        foreach (var path in Directory.GetFiles(DirectoryPath, "*.json"))
        {
            var runId = Path.GetFileNameWithoutExtension(path);
            if (!IsValidRunId(runId))
            {
                continue;
            }

            var run = await ReadRunAsync(path, runId, cancellationToken);
            summaries.Add(new ExecutionRunSummary(
                run.RunId, run.RecipeId, run.Status, run.StartedAt, run.FinishedAt));
        }

        return summaries
            .OrderByDescending(summary => summary.StartedAt)
            .Take(count)
            .ToList();
    }

    private string GetRunPath(string runId) => Path.Combine(DirectoryPath, $"{runId}.json");

    private string GetLogPath(string runId) => Path.Combine(DirectoryPath, $"{runId}.log");

    private static async Task<ExecutionRun> ReadRunAsync(
        string jsonPath, string runId, CancellationToken cancellationToken)
    {
        ExecutionRun? run;
        try
        {
            await using var stream = new FileStream(
                jsonPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);
            run = await JsonSerializer.DeserializeAsync<ExecutionRun>(stream, SerializerOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"Failed to load execution '{runId}' from '{jsonPath}': {ex.Message}", ex);
        }

        return run ?? throw new InvalidDataException(
            $"Execution file '{jsonPath}' does not contain an execution object.");
    }

    private static void ValidateRunId(string runId)
    {
        if (!IsValidRunId(runId))
        {
            throw new ArgumentException($"Run id '{runId}' is not a valid file name.", nameof(runId));
        }
    }

    private static bool IsValidRunId(string? runId) =>
        !string.IsNullOrWhiteSpace(runId) &&
        runId.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
        !runId.Contains(Path.DirectorySeparatorChar) &&
        !runId.Contains(Path.AltDirectorySeparatorChar);

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
