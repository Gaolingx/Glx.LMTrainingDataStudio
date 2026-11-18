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

    /// <summary>
    /// Reads arbitrary rows and returns them in the requested order. The default implementation
    /// groups consecutive row numbers into page reads; row-group formats override it.
    /// </summary>
    async Task<IReadOnlyList<DatasetRow>> ReadRowsAsync(
        IReadOnlyList<long> rowNumbers, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rowNumbers);
        if (rowNumbers.Count == 0)
        {
            return Array.Empty<DatasetRow>();
        }

        var ordered = new DatasetRow?[rowNumbers.Count];
        var positions = Enumerable.Range(0, rowNumbers.Count)
            .OrderBy(index => rowNumbers[index])
            .ToArray();

        var position = 0;
        while (position < positions.Length)
        {
            var startRow = rowNumbers[positions[position]];
            var end = position + 1;
            while (end < positions.Length &&
                   rowNumbers[positions[end]] == rowNumbers[positions[end - 1]] + 1)
            {
                end++;
            }

            var page = await ReadPageAsync(startRow, end - position, cancellationToken);
            var byLine = new Dictionary<long, DatasetRow>(page.Count);
            foreach (var row in page)
            {
                byLine[row.LineNumber] = row;
            }

            for (var i = position; i < end; i++)
            {
                var target = positions[i];
                if (byLine.TryGetValue(rowNumbers[target], out var row))
                {
                    ordered[target] = row;
                }
            }

            position = end;
        }

        var rows = new List<DatasetRow>(rowNumbers.Count);
        foreach (var row in ordered)
        {
            if (row is not null)
            {
                rows.Add(row);
            }
        }

        return rows;
    }
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
