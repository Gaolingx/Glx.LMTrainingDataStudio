using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Services.Datasets;

public sealed record BatchConversionRequest(
    string SourcePath,
    DataFileType SourceFileType,
    DatasetFormat SourceFormat,
    string TargetPath,
    DataFileType TargetFileType,
    DatasetFormat TargetFormat);

public sealed record BatchConversionResult(
    long TotalRows,
    long ConvertedRows,
    long FailedRows,
    string? ErrorFilePath,
    TimeSpan Elapsed,
    IReadOnlyList<string> SampleErrors);

/// <summary>
/// Streams a dataset through <see cref="DatasetConversionService"/>, writing the target atomically.
/// Bad rows are reported to a sibling <c>.errors.jsonl</c> file instead of aborting the run.
/// </summary>
public sealed class DatasetBatchConverter
{
    private const int PageSize = 500;
    private const int MaxSampleErrors = 50;

    private readonly AppSettings _settings;
    private readonly DatasetConversionService _conversion = new();

    public DatasetBatchConverter(AppSettings settings) => _settings = settings;

    public async Task<BatchConversionResult> ConvertAsync(
        BatchConversionRequest request,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var sourcePath = Path.GetFullPath(request.SourcePath);
        var targetPath = Path.GetFullPath(request.TargetPath);

        if (string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Source and target paths must differ.", nameof(request));
        }

        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException($"Dataset file not found: '{sourcePath}'.", sourcePath);
        }

        if (request.TargetFormat == DatasetFormat.Auto)
        {
            throw new DatasetFormatException("A concrete target format is required.");
        }

        string? errorPath = GetErrorPath(targetPath);
        var errorTempPath = errorPath + ".tmp-" + Guid.NewGuid().ToString("N");
        var sampleErrors = new List<string>();
        var stopwatch = Stopwatch.StartNew();

        await using var reader = DatasetIO.CreateReader(request.SourceFileType, _settings);
        await reader.OpenAsync(sourcePath, cancellationToken);
        await using var writer = DatasetIO.CreateWriter(request.TargetFileType, targetPath);

        StreamWriter? errorWriter = null;
        long processed = 0;
        long converted = 0;
        long failed = 0;

        try
        {
            for (var start = 0L; start < reader.TotalRows; start += PageSize)
            {
                var page = await reader.ReadPageAsync(start, PageSize, cancellationToken);
                foreach (var row in page)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var convertedRow = ConvertRow(row, request);
                        await writer.WriteRowAsync(convertedRow, cancellationToken);
                        converted++;
                    }
                    catch (Exception ex) when (ex is DatasetFormatException or JsonException)
                    {
                        failed++;
                        if (sampleErrors.Count < MaxSampleErrors)
                        {
                            sampleErrors.Add($"Row {row.LineNumber + 1}: {ex.Message}");
                        }

                        errorWriter ??= CreateErrorWriter(errorTempPath);
                        await errorWriter.WriteLineAsync(
                            BuildErrorLine(row, ex.Message).AsMemory(), cancellationToken);
                    }

                    processed++;
                }

                progress?.Report(reader.TotalRows == 0 ? 1.0 : (double)processed / reader.TotalRows);
            }

            await writer.CompleteAsync(cancellationToken);

            if (errorWriter is not null)
            {
                await errorWriter.FlushAsync(cancellationToken);
                await errorWriter.DisposeAsync();
                errorWriter = null;
                File.Move(errorTempPath, errorPath, overwrite: true);
            }
            else
            {
                TryDelete(errorPath);
                errorPath = null;
            }

            progress?.Report(1.0);

            return new BatchConversionResult(
                reader.TotalRows,
                converted,
                failed,
                errorPath,
                stopwatch.Elapsed,
                sampleErrors);
        }
        catch
        {
            if (errorWriter is not null)
            {
                await errorWriter.DisposeAsync();
            }

            TryDelete(errorTempPath);
            throw;
        }
    }

    private DatasetRow ConvertRow(DatasetRow row, BatchConversionRequest request)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(row.Json);
        }
        catch (JsonException ex)
        {
            throw new DatasetFormatException($"Invalid JSON: {ex.Message}", row.LineNumber + 1);
        }

        var obj = node as JsonObject
                  ?? throw new DatasetFormatException("Row must be a JSON object.", row.LineNumber + 1);

        var converted = _conversion.Convert(
            obj, request.SourceFormat, request.TargetFormat, row.LineNumber + 1);

        return new DatasetRow(row.LineNumber, converted.ToJsonString());
    }

    private static string BuildErrorLine(DatasetRow row, string reason)
    {
        var error = new JsonObject
        {
            ["lineNumber"] = row.LineNumber + 1,
            ["reason"] = reason
        };

        try
        {
            error["data"] = JsonNode.Parse(row.Json);
        }
        catch (JsonException)
        {
            error["data"] = row.Json;
        }

        return error.ToJsonString();
    }

    private static StreamWriter CreateErrorWriter(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return new StreamWriter(
            new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string GetErrorPath(string targetPath)
    {
        var directory = Path.GetDirectoryName(targetPath);
        var name = Path.GetFileNameWithoutExtension(targetPath) + ".errors.jsonl";
        return string.IsNullOrEmpty(directory) ? name : Path.Combine(directory, name);
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
