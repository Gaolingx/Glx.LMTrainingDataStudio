using System.Globalization;
using System.Text.Json.Nodes;
using LMTrainingDataStudio2.Models;
using Parquet;
using Parquet.Data;

namespace LMTrainingDataStudio2.Services.Datasets;

/// <summary>
/// Random-access Parquet reader. Row groups are located from metadata; only the groups covering
/// the requested page are decoded. Nested values written by <see cref="ParquetDatasetWriter"/>
/// are restored from the <c>lmtds.json_fields</c> schema metadata.
/// </summary>
public sealed class ParquetDatasetReader : IDatasetReader, IDisposable
{
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private readonly HashSet<string> _jsonFields = new(StringComparer.Ordinal);
    private FileStream? _stream;
    private ParquetReader? _reader;
    private long[] _rowGroupStarts = Array.Empty<long>();
    private long[] _rowGroupRows = Array.Empty<long>();
    private bool _disposed;

    public string SourcePath { get; private set; } = string.Empty;

    public DataFileType FileType => DataFileType.Parquet;

    public long TotalRows { get; private set; }

    public async Task OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (_reader is not null)
        {
            throw new InvalidOperationException("The reader is already open.");
        }

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Parquet file not found: '{fullPath}'.", fullPath);
        }

        var stream = new FileStream(
            fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536,
            FileOptions.Asynchronous | FileOptions.RandomAccess);
        try
        {
            var reader = await ParquetReader.CreateAsync(stream, cancellationToken: cancellationToken);
            _reader = reader;
            _stream = stream;
            SourcePath = fullPath;

            var rowGroups = reader.Metadata?.RowGroups
                            ?? throw new DatasetFormatException($"Parquet metadata is missing in '{fullPath}'.");
            _rowGroupRows = rowGroups.Select(group => group.NumRows).ToArray();
            _rowGroupStarts = new long[_rowGroupRows.Length];
            long total = 0;
            for (var i = 0; i < _rowGroupRows.Length; i++)
            {
                _rowGroupStarts[i] = total;
                total += _rowGroupRows[i];
            }

            TotalRows = total;

            if (reader.CustomMetadata.TryGetValue(ParquetDatasetWriter.JsonFieldsMetadataKey, out var jsonFields))
            {
                foreach (var field in jsonFields.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    _jsonFields.Add(field);
                }
            }
        }
        catch
        {
            await stream.DisposeAsync();
            throw;
        }
    }

    public async Task<IReadOnlyList<DatasetRow>> ReadPageAsync(
        long startRow, int count, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        ArgumentOutOfRangeException.ThrowIfNegative(startRow);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        if (count == 0 || startRow >= TotalRows)
        {
            return Array.Empty<DatasetRow>();
        }

        var endRow = Math.Min(TotalRows, startRow + count);
        var rows = new List<DatasetRow>((int)(endRow - startRow));

        await _ioLock.WaitAsync(cancellationToken);
        try
        {
            var current = startRow;
            while (current < endRow)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var groupIndex = FindRowGroup(current);
                var offset = (int)(current - _rowGroupStarts[groupIndex]);
                var take = (int)Math.Min(endRow - current, _rowGroupRows[groupIndex] - offset);
                var columns = await _reader!.ReadEntireRowGroupAsync(groupIndex, cancellationToken);

                for (var i = 0; i < take; i++)
                {
                    var rowIndex = offset + i;
                    var obj = new JsonObject();
                    foreach (var column in columns)
                    {
                        var value = column.Data.GetValue(rowIndex);
                        obj[column.Field.Name] = ToJsonNode(value, _jsonFields.Contains(column.Field.Name));
                    }

                    rows.Add(new DatasetRow(current, obj.ToJsonString()));
                    current++;
                }
            }
        }
        finally
        {
            _ioLock.Release();
        }

        return rows;
    }

    /// <summary>
    /// Reads arbitrary rows while decoding each touched row group only once.
    /// </summary>
    public async Task<IReadOnlyList<DatasetRow>> ReadRowsAsync(
        IReadOnlyList<long> rowNumbers, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        ArgumentNullException.ThrowIfNull(rowNumbers);
        if (rowNumbers.Count == 0)
        {
            return Array.Empty<DatasetRow>();
        }

        var ordered = new DatasetRow?[rowNumbers.Count];
        var positions = Enumerable.Range(0, rowNumbers.Count)
            .OrderBy(index => rowNumbers[index])
            .ToArray();

        await _ioLock.WaitAsync(cancellationToken);
        try
        {
            var position = 0;
            while (position < positions.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var groupIndex = FindRowGroup(rowNumbers[positions[position]]);
                var groupStart = _rowGroupStarts[groupIndex];
                var groupEnd = groupStart + _rowGroupRows[groupIndex];
                var end = position;
                while (end < positions.Length && rowNumbers[positions[end]] < groupEnd)
                {
                    end++;
                }

                var columns = await _reader!.ReadEntireRowGroupAsync(groupIndex, cancellationToken);
                for (var i = position; i < end; i++)
                {
                    var target = positions[i];
                    var rowNumber = rowNumbers[target];
                    var offset = (int)(rowNumber - groupStart);
                    var obj = new JsonObject();
                    foreach (var column in columns)
                    {
                        var value = column.Data.GetValue(offset);
                        obj[column.Field.Name] = ToJsonNode(value, _jsonFields.Contains(column.Field.Name));
                    }

                    ordered[target] = new DatasetRow(rowNumber, obj.ToJsonString());
                }

                position = end;
            }
        }
        finally
        {
            _ioLock.Release();
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _reader?.Dispose();
        _stream?.Dispose();
        _ioLock.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private int FindRowGroup(long rowNumber)
    {
        var low = 0;
        var high = _rowGroupStarts.Length - 1;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            var start = _rowGroupStarts[middle];
            var end = start + _rowGroupRows[middle];
            if (rowNumber < start)
            {
                high = middle - 1;
            }
            else if (rowNumber >= end)
            {
                low = middle + 1;
            }
            else
            {
                return middle;
            }
        }

        throw new DatasetFormatException($"Row {rowNumber} is outside the parquet row groups.");
    }

    private static JsonNode? ToJsonNode(object? value, bool jsonEncoded)
    {
        if (value is null) return null;

        if (jsonEncoded && value is string json)
        {
            try
            {
                return JsonNode.Parse(json);
            }
            catch (System.Text.Json.JsonException)
            {
                return JsonValue.Create(json);
            }
        }

        return value switch
        {
            string text => JsonValue.Create(text),
            bool boolean => JsonValue.Create(boolean),
            byte number => JsonValue.Create((long)number),
            sbyte number => JsonValue.Create((long)number),
            short number => JsonValue.Create((long)number),
            ushort number => JsonValue.Create((long)number),
            int number => JsonValue.Create((long)number),
            uint number => JsonValue.Create((long)number),
            long number => JsonValue.Create(number),
            ulong number => JsonValue.Create(number),
            float number => JsonValue.Create((double)number),
            double number => JsonValue.Create(number),
            decimal number => JsonValue.Create(number),
            DateTime dateTime => JsonValue.Create(dateTime.ToString("O", CultureInfo.InvariantCulture)),
            DateTimeOffset dateTimeOffset => JsonValue.Create(dateTimeOffset.ToString("O", CultureInfo.InvariantCulture)),
            byte[] bytes => JsonValue.Create(Convert.ToBase64String(bytes)),
            _ => JsonValue.Create(value.ToString())
        };
    }

    private void EnsureOpen()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_reader is null)
        {
            throw new InvalidOperationException("The reader is not open. Call OpenAsync before reading.");
        }
    }
}
