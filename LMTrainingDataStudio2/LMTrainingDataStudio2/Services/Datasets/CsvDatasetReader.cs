using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CsvHelper;
using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Services.Datasets;

/// <summary>
/// Random-access CSV reader. Builds a record-offset index once, then reads only the requested page.
/// Every row is exposed as a JSON object with header-named string fields; JSON-looking values stay structured.
/// </summary>
public sealed class CsvDatasetReader : IDatasetReader, IDisposable
{
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private List<RecordRange> _records = new();
    private string[] _header = Array.Empty<string>();
    private FileStream? _stream;
    private bool _disposed;

    public string SourcePath { get; private set; } = string.Empty;

    public DataFileType FileType => DataFileType.Csv;

    /// <summary>Data rows, excluding the header row.</summary>
    public long TotalRows => Math.Max(0, _records.Count - 1);

    public async Task OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (_stream is not null)
        {
            throw new InvalidOperationException("The reader is already open.");
        }

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"CSV file not found: '{fullPath}'.", fullPath);
        }

        _records = await ScanRecordsAsync(fullPath, cancellationToken);
        _stream = new FileStream(
            fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536,
            FileOptions.Asynchronous | FileOptions.RandomAccess);
        SourcePath = fullPath;

        if (_records.Count > 0)
        {
            var headerText = await ReadRangeAsync(_stream, _records[0], cancellationToken);
            _header = NormalizeHeader(ParseRecord(headerText, fullPath));
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
            for (var row = startRow; row < endRow; row++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var range = _records[(int)(row + 1)];
                var raw = await ReadRangeAsync(_stream!, range, cancellationToken);
                var fields = ParseRecord(raw, SourcePath);

                var obj = new JsonObject();
                for (var i = 0; i < _header.Length; i++)
                {
                    obj[_header[i]] = ConvertValue(i < fields.Length ? fields[i] : string.Empty);
                }

                for (var i = _header.Length; i < fields.Length; i++)
                {
                    obj[$"column{i + 1}"] = ConvertValue(fields[i]);
                }

                rows.Add(new DatasetRow(row, obj.ToJsonString()));
            }
        }
        finally
        {
            _ioLock.Release();
        }

        return rows;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _stream?.Dispose();
        _ioLock.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private void EnsureOpen()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_stream is null)
        {
            throw new InvalidOperationException("The reader is not open. Call OpenAsync before reading.");
        }
    }

    private static async Task<string> ReadRangeAsync(
        FileStream stream, RecordRange range, CancellationToken cancellationToken)
    {
        var length = checked((int)range.Length);
        var buffer = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            stream.Seek(range.Start, SeekOrigin.Begin);

            var totalRead = 0;
            while (totalRead < length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(totalRead, length - totalRead), cancellationToken);
                if (read == 0) break;
                totalRead += read;
            }

            return Encoding.UTF8.GetString(buffer, 0, totalRead).TrimEnd('\r', '\n');
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static string[] ParseRecord(string text, string sourcePath)
    {
        try
        {
            using var parser = new CsvParser(new StringReader(text), CultureInfo.InvariantCulture);
            return parser.Read() ? parser.Record ?? Array.Empty<string>() : Array.Empty<string>();
        }
        catch (CsvHelperException ex)
        {
            throw new DatasetFormatException($"Failed to parse CSV record from '{sourcePath}': {ex.Message}");
        }
    }

    private static JsonNode? ConvertValue(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length > 1 && (trimmed[0] == '{' || trimmed[0] == '['))
        {
            try
            {
                var node = JsonNode.Parse(trimmed);
                if (node is JsonObject or JsonArray)
                {
                    return node;
                }
            }
            catch (JsonException)
            {
            }
        }

        return JsonValue.Create(value);
    }

    private static string[] NormalizeHeader(string[] fields)
    {
        var result = new string[fields.Length];
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var i = 0; i < fields.Length; i++)
        {
            var name = fields[i].Trim();
            if (name.Length == 0)
            {
                name = $"column{i + 1}";
            }

            if (seen.TryGetValue(name, out var count))
            {
                seen[name] = count + 1;
                name = $"{name}_{count + 1}";
            }
            else
            {
                seen[name] = 1;
            }

            result[i] = name;
        }

        return result;
    }

    /// <summary>
    /// Scans RFC 4180 record boundaries without loading the file into memory.
    /// Quoted newlines are ignored, doubled quotes are escaped, blank records are skipped.
    /// </summary>
    private static async Task<List<RecordRange>> ScanRecordsAsync(string path, CancellationToken cancellationToken)
    {
        var records = new List<RecordRange>();
        var buffer = ArrayPool<byte>.Shared.Rent(65536);
        var recordStart = 0L;
        var recordHasContent = false;
        var inQuotes = false;
        var pendingQuote = false;
        var bomChecked = false;

        try
        {
            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536,
                FileOptions.SequentialScan | FileOptions.Asynchronous);

            long absolute = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                var index = 0;
                if (!bomChecked)
                {
                    bomChecked = true;
                    if (read >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
                    {
                        index = 3;
                        recordStart = 3;
                    }
                }

                for (; index < read; index++)
                {
                    var value = buffer[index];
                    var position = absolute + index;

                    if (pendingQuote)
                    {
                        pendingQuote = false;
                        if (value == (byte)'"')
                        {
                            // Doubled quote inside a quoted field; still inside quotes.
                            continue;
                        }

                        inQuotes = false;
                    }

                    if (value == (byte)'"')
                    {
                        if (inQuotes)
                        {
                            pendingQuote = true;
                        }
                        else
                        {
                            inQuotes = true;
                        }
                    }
                    else if (value == (byte)'\n' && !inQuotes)
                    {
                        if (recordHasContent)
                        {
                            records.Add(new RecordRange(recordStart, position + 1 - recordStart));
                        }

                        recordStart = position + 1;
                        recordHasContent = false;
                    }
                    else if (value is not (byte)'\r' and not (byte)'\n' and not (byte)' ' and not (byte)'\t')
                    {
                        recordHasContent = true;
                    }
                }

                absolute += read;
            }

            if (recordHasContent)
            {
                records.Add(new RecordRange(recordStart, absolute - recordStart));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return records;
    }

    private readonly record struct RecordRange(long Start, long Length);
}
