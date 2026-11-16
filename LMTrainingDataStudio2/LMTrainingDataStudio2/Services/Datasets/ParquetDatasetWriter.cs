using System.Text.Json.Nodes;
using LMTrainingDataStudio2.Models;
using Parquet;
using Parquet.Data;
using Parquet.Schema;

namespace LMTrainingDataStudio2.Services.Datasets;

/// <summary>
/// Streaming Parquet writer. The schema is inferred from the first row; nested JSON values are stored as
/// strings and tagged in <c>lmtds.json_fields</c> so <see cref="ParquetDatasetReader"/> can restore them.
/// </summary>
public sealed class ParquetDatasetWriter : IDatasetWriter
{
    public const string JsonFieldsMetadataKey = "lmtds.json_fields";

    private const int RowGroupSize = 8192;

    private readonly string _targetPath;
    private readonly string _tempPath;
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private FileStream? _stream;
    private ParquetWriter? _writer;
    private List<ColumnBuffer>? _columns;
    private int _bufferedRows;
    private bool _completed;

    public ParquetDatasetWriter(string targetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        _targetPath = Path.GetFullPath(targetPath);
        var directory = Path.GetDirectoryName(_targetPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _tempPath = _targetPath + ".tmp-" + Guid.NewGuid().ToString("N");
        _stream = new FileStream(
            _tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536, FileOptions.Asynchronous);
    }

    public async Task WriteRowAsync(DatasetRow row, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_completed, this);
        await _ioLock.WaitAsync(cancellationToken);
        try
        {
            var obj = ParseObject(row);
            if (_columns is null)
            {
                await InitializeAsync(obj, cancellationToken);
            }

            foreach (var property in obj)
            {
                if (!_columns!.Any(column => column.Field.Name == property.Key))
                {
                    throw new DatasetFormatException(
                        $"Parquet column '{property.Key}' is not present in the first row.", row.LineNumber + 1);
                }
            }

            foreach (var column in _columns!)
            {
                obj.TryGetPropertyValue(column.Field.Name, out var node);
                column.Values.Add(Coerce(node, column, row.LineNumber + 1));
            }

            _bufferedRows++;
            if (_bufferedRows >= RowGroupSize)
            {
                await FlushRowGroupAsync(cancellationToken);
            }
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
            if (_writer is null)
            {
                throw new DatasetFormatException("Cannot write an empty Parquet file without a schema.");
            }

            await FlushRowGroupAsync(cancellationToken);
            await _writer.DisposeAsync();
            _writer = null;
            await _stream!.DisposeAsync();
            _stream = null;
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

        if (_writer is not null)
        {
            await _writer.DisposeAsync();
        }

        if (_stream is not null)
        {
            await _stream.DisposeAsync();
        }

        TryDelete(_tempPath);
    }

    private async Task InitializeAsync(JsonObject firstRow, CancellationToken cancellationToken)
    {
        if (firstRow.Count == 0)
        {
            throw new DatasetFormatException("The first row must contain at least one field to infer a parquet schema.");
        }

        _columns = new List<ColumnBuffer>(firstRow.Count);
        foreach (var property in firstRow)
        {
            var jsonEncoded = property.Value is JsonObject or JsonArray;
            var type = jsonEncoded ? typeof(string) : InferType(property.Value);
            _columns.Add(new ColumnBuffer(new DataField(property.Key, type, isNullable: true), jsonEncoded));
        }

        var schema = new ParquetSchema(_columns.Select(column => (Field)column.Field));
        _writer = await ParquetWriter.CreateAsync(schema, _stream!, cancellationToken: cancellationToken);

        var jsonFields = _columns.Where(column => column.JsonEncoded).Select(column => column.Field.Name).ToArray();
        if (jsonFields.Length > 0)
        {
            _writer.CustomMetadata = new Dictionary<string, string>
            {
                [JsonFieldsMetadataKey] = string.Join(",", jsonFields)
            };
        }
    }

    private async Task FlushRowGroupAsync(CancellationToken cancellationToken)
    {
        if (_bufferedRows == 0) return;

        var rowGroup = _writer!.CreateRowGroup();
        try
        {
            foreach (var column in _columns!)
            {
                await rowGroup.WriteColumnAsync(new DataColumn(column.Field, column.ToArray()), cancellationToken);
                column.Values.Clear();
            }
        }
        catch
        {
            try
            {
                rowGroup.Dispose();
            }
            catch (InvalidOperationException)
            {
                // Dispose reports incomplete columns; the original write error is more useful.
            }

            throw;
        }

        rowGroup.Dispose();
        _bufferedRows = 0;
    }

    private static Type InferType(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return typeof(string);
        }

        if (value.TryGetValue<bool>(out _)) return typeof(bool);
        if (value.TryGetValue<long>(out _)) return typeof(long);
        if (value.TryGetValue<double>(out _)) return typeof(double);
        return typeof(string);
    }

    private static object? Coerce(JsonNode? node, ColumnBuffer column, long rowNumber)
    {
        if (node is null) return null;
        if (column.JsonEncoded) return node.ToJsonString();

        var type = column.Field.ClrType;
        if (type == typeof(string))
        {
            return node is JsonValue text && text.TryGetValue<string>(out var value)
                ? value
                : node.ToJsonString();
        }

        if (node is not JsonValue scalar)
        {
            throw new DatasetFormatException(
                $"Field '{column.Field.Name}' expects {type.Name} but received a nested JSON value.", rowNumber);
        }

        if (type == typeof(long) && scalar.TryGetValue<long>(out var number)) return number;
        if (type == typeof(double) && scalar.TryGetValue<double>(out var real)) return real;
        if (type == typeof(bool) && scalar.TryGetValue<bool>(out var boolean)) return boolean;

        throw new DatasetFormatException(
            $"Field '{column.Field.Name}' expects {type.Name} but received '{scalar.ToJsonString()}'.", rowNumber);
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

    private sealed class ColumnBuffer
    {
        public ColumnBuffer(DataField field, bool jsonEncoded)
        {
            Field = field;
            JsonEncoded = jsonEncoded;
        }

        public DataField Field { get; }

        public bool JsonEncoded { get; }

        public List<object?> Values { get; } = new();

        public Array ToArray()
        {
            var array = Array.CreateInstance(Field.ClrNullableIfHasNullsType, Values.Count);
            for (var i = 0; i < Values.Count; i++)
            {
                array.SetValue(Values[i], i);
            }

            return array;
        }
    }
}
