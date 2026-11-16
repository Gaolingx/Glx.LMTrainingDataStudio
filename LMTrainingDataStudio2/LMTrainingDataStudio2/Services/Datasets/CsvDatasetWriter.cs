using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using CsvHelper;
using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Services.Datasets;

/// <summary>
/// Writes rows as CSV through a temp file and atomically replaces the target on completion.
/// Columns come from the first row; nested JSON values are stored as JSON text.
/// </summary>
public sealed class CsvDatasetWriter : IDatasetWriter
{
    private readonly string _targetPath;
    private readonly string _tempPath;
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private StreamWriter? _textWriter;
    private CsvWriter? _csvWriter;
    private string[]? _columns;
    private bool _completed;

    public CsvDatasetWriter(string targetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        _targetPath = Path.GetFullPath(targetPath);
        var directory = Path.GetDirectoryName(_targetPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _tempPath = _targetPath + ".tmp-" + Guid.NewGuid().ToString("N");
        _textWriter = new StreamWriter(
            new FileStream(_tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        _csvWriter = new CsvWriter(_textWriter, CultureInfo.InvariantCulture);
    }

    public async Task WriteRowAsync(DatasetRow row, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_completed, this);
        await _ioLock.WaitAsync(cancellationToken);
        try
        {
            var obj = ParseObject(row);
            EnsureHeader(obj, row.LineNumber);

            foreach (var column in _columns!)
            {
                if (!obj.TryGetPropertyValue(column, out var node))
                {
                    _csvWriter!.WriteField((string?)null);
                    continue;
                }

                _csvWriter!.WriteField(ToCsvValue(node));
            }

            await _csvWriter!.NextRecordAsync();
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public async Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        if (_completed) return;

        await _ioLock.WaitAsync(cancellationToken);
        try
        {
            await _csvWriter!.FlushAsync();
            await _textWriter!.FlushAsync(cancellationToken);
            await _csvWriter.DisposeAsync();
            await _textWriter.DisposeAsync();
            _csvWriter = null;
            _textWriter = null;
        }
        finally
        {
            _ioLock.Release();
        }

        File.Move(_tempPath, _targetPath, overwrite: true);
        _completed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_completed) return;

        if (_csvWriter is not null)
        {
            await _csvWriter.DisposeAsync();
        }

        if (_textWriter is not null)
        {
            await _textWriter.DisposeAsync();
        }

        TryDelete(_tempPath);
    }

    private void EnsureHeader(JsonObject obj, long rowNumber)
    {
        if (_columns is not null)
        {
            foreach (var property in obj)
            {
                if (Array.IndexOf(_columns, property.Key) < 0)
                {
                    throw new DatasetFormatException(
                        $"CSV column '{property.Key}' is not present in the first row.", rowNumber + 1);
                }
            }

            return;
        }

        _columns = obj.Select(property => property.Key).ToArray();
        foreach (var column in _columns)
        {
            _csvWriter!.WriteField(column);
        }

        _csvWriter!.NextRecord();
    }

    private static JsonObject ParseObject(DatasetRow row)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(row.Json);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or ArgumentException)
        {
            throw new DatasetFormatException($"Invalid JSON: {ex.Message}", row.LineNumber + 1);
        }

        return node as JsonObject
               ?? throw new DatasetFormatException("Row must be a JSON object.", row.LineNumber + 1);
    }

    private static string? ToCsvValue(JsonNode? node)
    {
        if (node is null) return null;
        if (node is JsonValue value && value.TryGetValue<string>(out var text)) return text;
        return node.ToJsonString();
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
