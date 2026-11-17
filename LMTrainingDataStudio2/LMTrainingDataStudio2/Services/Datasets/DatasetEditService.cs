using System.Diagnostics;
using System.Text.Json;
using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Services.Datasets;

/// <summary>One pending change, used by the pre-save diff summary.</summary>
public sealed record DatasetEditChangeSample(
    string Kind,
    long? SourceLine,
    string Before,
    string After);

/// <summary>Counts and a bounded sample of pending edits for the save confirmation.</summary>
public sealed record DatasetEditSummary(
    long SourceRows,
    long EffectiveRows,
    int ReplacedRows,
    int DeletedRows,
    int InsertedRows,
    IReadOnlyList<DatasetEditChangeSample> Samples);

public sealed record DatasetEditSaveResult(
    string TargetPath,
    long SourceRows,
    long WrittenRows,
    int ReplacedRows,
    int DeletedRows,
    int InsertedRows,
    TimeSpan Elapsed)
{
    public long UnchangedRows => SourceRows - ReplacedRows - DeletedRows;
}

/// <summary>
/// Applies a <see cref="DatasetEditSnapshot"/> to a dataset by streaming the source in
/// chunks and rewriting into a temp file. Untouched JSONL rows keep their exact text.
/// </summary>
public sealed class DatasetEditService
{
    private const int DefaultChunkRows = 2048;

    private readonly AppSettings _settings;

    public DatasetEditService(AppSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <summary>
    /// Validates edited rows and returns counts plus up to <paramref name="maxSamples"/>
    /// before/after samples. The reader is only used for the "before" text.
    /// </summary>
    public async Task<DatasetEditSummary> BuildSummaryAsync(
        DatasetEditSnapshot snapshot,
        IDatasetReader reader,
        int maxSamples = 5,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentOutOfRangeException.ThrowIfNegative(maxSamples);

        ValidateSnapshot(snapshot, reader.TotalRows);

        var samples = new List<DatasetEditChangeSample>();
        var remaining = maxSamples;

        foreach (var pair in snapshot.Replacements.OrderBy(pair => pair.Key))
        {
            if (remaining == 0) break;
            if (snapshot.Deletions.Contains(pair.Key)) continue;

            samples.Add(new DatasetEditChangeSample(
                "replace",
                pair.Key + 1,
                await ReadRowJsonAsync(reader, pair.Key, cancellationToken),
                pair.Value));
            remaining--;
        }

        foreach (var row in snapshot.Deletions.OrderBy(row => row))
        {
            if (remaining == 0) break;

            samples.Add(new DatasetEditChangeSample(
                "delete",
                row + 1,
                await ReadRowJsonAsync(reader, row, cancellationToken),
                string.Empty));
            remaining--;
        }

        foreach (var inserted in EnumerateInserted(snapshot))
        {
            if (remaining == 0) break;

            samples.Add(new DatasetEditChangeSample(
                "insert",
                inserted.AnchorRow + 1,
                string.Empty,
                inserted.Json));
            remaining--;
        }

        return new DatasetEditSummary(
            reader.TotalRows,
            reader.TotalRows - snapshot.Deletions.Count + snapshot.InsertedRows,
            snapshot.Replacements.Keys.Count(row => !snapshot.Deletions.Contains(row)),
            snapshot.Deletions.Count,
            snapshot.InsertedRows,
            samples);
    }

    public async Task<DatasetEditSaveResult> SaveAsync(
        DatasetEditSnapshot snapshot,
        string sourcePath,
        DataFileType sourceType,
        string targetPath,
        int chunkRows = DefaultChunkRows,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkRows, 1);

        var targetType = DatasetIO.DetectFileType(targetPath);
        if (targetType != sourceType)
        {
            throw new DatasetFormatException(
                $"Edited saves keep the source format ({sourceType}); convert the dataset first to write {targetType}.");
        }

        ValidateSnapshot(snapshot, long.MaxValue);

        var stopwatch = Stopwatch.StartNew();
        IDatasetReader? reader = null;
        IDatasetWriter? writer = null;
        var completed = false;

        try
        {
            reader = DatasetIO.CreateReader(sourceType, _settings);
            await reader.OpenAsync(sourcePath, cancellationToken);
            ValidateSnapshot(snapshot, reader.TotalRows);

            var sourceRows = reader.TotalRows;
            writer = DatasetIO.CreateWriter(targetType, targetPath);

            long written = 0;
            for (long start = 0; start < sourceRows; start += chunkRows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = (int)Math.Min(chunkRows, sourceRows - start);
                var page = await reader.ReadPageAsync(start, count, cancellationToken);

                foreach (var row in page)
                {
                    written += await WriteInsertedAsync(writer, snapshot, row.LineNumber, cancellationToken);

                    if (!snapshot.Deletions.Contains(row.LineNumber))
                    {
                        var json = snapshot.Replacements.TryGetValue(row.LineNumber, out var replacement)
                            ? replacement
                            : row.Json;
                        await writer.WriteRowAsync(new DatasetRow(row.LineNumber, json), cancellationToken);
                        written++;
                    }
                }

                progress?.Report(sourceRows == 0 ? 1 : Math.Min(1, (start + count) / (double)sourceRows));
            }

            written += await WriteInsertedAsync(writer, snapshot, sourceRows, cancellationToken);

            await reader.DisposeAsync();
            reader = null;

            await writer.CompleteAsync(cancellationToken);
            completed = true;
            stopwatch.Stop();

            return new DatasetEditSaveResult(
                Path.GetFullPath(targetPath),
                sourceRows,
                written,
                snapshot.Replacements.Keys.Count(row => !snapshot.Deletions.Contains(row)),
                snapshot.Deletions.Count,
                snapshot.InsertedRows,
                stopwatch.Elapsed);
        }
        finally
        {
            if (reader is not null)
            {
                await reader.DisposeAsync();
            }

            if (writer is not null && !completed)
            {
                await writer.DisposeAsync();
            }
        }
    }

    private static async Task<int> WriteInsertedAsync(
        IDatasetWriter writer,
        DatasetEditSnapshot snapshot,
        long anchorRow,
        CancellationToken cancellationToken)
    {
        if (!snapshot.Insertions.TryGetValue(anchorRow, out var rows))
        {
            return 0;
        }

        foreach (var inserted in rows)
        {
            await writer.WriteRowAsync(new DatasetRow(anchorRow, inserted.Json), cancellationToken);
        }

        return rows.Count;
    }

    private static IEnumerable<DatasetInsertedRow> EnumerateInserted(DatasetEditSnapshot snapshot)
    {
        foreach (var pair in snapshot.Insertions.OrderBy(pair => pair.Key))
        {
            foreach (var row in pair.Value)
            {
                yield return row;
            }
        }
    }

    private static async Task<string> ReadRowJsonAsync(
        IDatasetReader reader,
        long row,
        CancellationToken cancellationToken)
    {
        var page = await reader.ReadPageAsync(row, 1, cancellationToken);
        return page.Count == 0 ? string.Empty : page[0].Json;
    }

    /// <summary>
    /// Fails fast with row context before any target file is created.
    /// </summary>
    public static void ValidateSnapshot(DatasetEditSnapshot snapshot, long totalSourceRows)
    {
        foreach (var pair in snapshot.Replacements)
        {
            if (totalSourceRows != long.MaxValue && pair.Key >= totalSourceRows)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(snapshot), pair.Key, "Replacement row is outside the dataset.");
            }

            ValidateJson(pair.Value, pair.Key, "replacement");
        }

        foreach (var row in snapshot.Deletions)
        {
            if (totalSourceRows != long.MaxValue && row >= totalSourceRows)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(snapshot), row, "Deleted row is outside the dataset.");
            }
        }

        foreach (var pair in snapshot.Insertions)
        {
            if (totalSourceRows != long.MaxValue && pair.Key > totalSourceRows)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(snapshot), pair.Key, "Insert anchor is outside the dataset.");
            }

            foreach (var inserted in pair.Value)
            {
                ValidateJson(inserted.Json, pair.Key, "inserted row");
            }
        }
    }

    private static void ValidateJson(string json, long sourceRow, string kind)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new DatasetFormatException($"Row {sourceRow + 1}: {kind} is empty.", sourceRow + 1);
        }

        try
        {
            using var document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new DatasetFormatException(
                $"Row {sourceRow + 1}: {kind} is not valid JSON ({ex.Message}).", sourceRow + 1);
        }
    }
}
