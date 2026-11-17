using System.Text.Json;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services.Simulation;

namespace LMTrainingDataStudio2.Services.Datasets;

/// <summary>One preview row. In diff mode <see cref="Before"/> and <see cref="After"/> differ when cleaned.</summary>
public sealed record SamplePreviewRow(
    long? LineNumber,
    string Before,
    string After,
    bool Changed,
    string? Note = null);

public sealed record SamplePreviewDocument(
    string Path,
    DataFileType FileType,
    long TotalRows,
    bool IsDiff,
    IReadOnlyList<SamplePreviewRow> Rows);

/// <summary>
/// Loads the first N rows of a dataset for the unified sample preview, or builds a
/// cleaner before/after diff without writing any file.
/// </summary>
public sealed class SamplePreviewService
{
    public const int DefaultMaxRows = 50;

    private readonly AppSettings _settings;

    public SamplePreviewService(AppSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public async Task<SamplePreviewDocument> LoadFileAsync(
        string path,
        int maxRows = DefaultMaxRows,
        long startRow = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRows, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(startRow);

        var fileType = DatasetIO.DetectFileType(path);
        await using var reader = DatasetIO.CreateReader(fileType, _settings);
        await reader.OpenAsync(path, cancellationToken);
        var page = await reader.ReadPageAsync(startRow, maxRows, cancellationToken);

        var rows = page
            .Select(row => new SamplePreviewRow(row.LineNumber + 1, string.Empty, row.Json, false))
            .ToArray();

        return new SamplePreviewDocument(reader.SourcePath, fileType, reader.TotalRows, false, rows);
    }

    public async Task<SamplePreviewDocument> BuildCleanerDiffAsync(
        string path,
        DatasetCleanupRules rules,
        int maxRows = DefaultMaxRows,
        long startRow = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentOutOfRangeException.ThrowIfNegative(startRow);

        var fileType = DatasetIO.DetectFileType(path);
        if (fileType != DataFileType.Jsonl)
        {
            throw new DatasetFormatException("Cleaner diff preview requires a JSONL file.");
        }

        await using var reader = DatasetIO.CreateReader(fileType, _settings);
        await reader.OpenAsync(path, cancellationToken);
        var page = await reader.ReadPageAsync(startRow, maxRows, cancellationToken);

        var rows = new List<SamplePreviewRow>(page.Count);
        foreach (var row in page)
        {
            try
            {
                var cleaned = TrajectoryCleaner.CleanJson(row.Json, rules);
                rows.Add(new SamplePreviewRow(
                    row.LineNumber + 1,
                    row.Json,
                    cleaned,
                    !string.Equals(cleaned, row.Json, StringComparison.Ordinal)));
            }
            catch (JsonException ex)
            {
                rows.Add(new SamplePreviewRow(
                    row.LineNumber + 1,
                    row.Json,
                    row.Json,
                    false,
                    ex.Message));
            }
        }

        return new SamplePreviewDocument(reader.SourcePath, fileType, reader.TotalRows, true, rows);
    }
}
