using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Services.Datasets;

/// <summary>
/// Random-access reader over a dataset file. Rows are zero-based and always exposed as JSON text.
/// </summary>
public interface IDatasetReader : IAsyncDisposable
{
    string SourcePath { get; }

    DataFileType FileType { get; }

    long TotalRows { get; }

    Task OpenAsync(string path, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DatasetRow>> ReadPageAsync(
        long startRow, int count, CancellationToken cancellationToken = default);
}

/// <summary>
/// Streaming writer that replaces the target atomically on <see cref="CompleteAsync"/>.
/// </summary>
public interface IDatasetWriter : IAsyncDisposable
{
    Task WriteRowAsync(DatasetRow row, CancellationToken cancellationToken = default);

    Task CompleteAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Row-level dataset format problem; carries the 1-based source row when available.
/// </summary>
public sealed class DatasetFormatException : Exception
{
    public DatasetFormatException(string message, long? rowNumber = null)
        : base(rowNumber is null ? message : $"Row {rowNumber}: {message}")
    {
        RowNumber = rowNumber;
    }

    public long? RowNumber { get; }
}
