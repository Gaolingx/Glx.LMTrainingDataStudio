using System.Runtime.CompilerServices;
using System.Text;
using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Services.Executors;

/// <summary>
/// Holds inter-block rows in memory up to a configurable row limit, then spills the
/// remainder to a temporary file so large pipelines stay memory bounded.
/// </summary>
public sealed class RowSpillBuffer : IAsyncDisposable
{
    private readonly long _memoryLimitRows;
    private readonly string? _directory;
    private readonly List<DatasetRow> _memory = new();
    private StreamWriter? _writer;
    private string? _path;
    private long _count;

    public RowSpillBuffer(long memoryLimitRows, string? directory = null)
    {
        _memoryLimitRows = Math.Max(0, memoryLimitRows);
        _directory = directory;
    }

    private RowSpillBuffer(string path, long count)
    {
        _path = path;
        _count = count;
        _memoryLimitRows = 0;
        PersistsFile = true;
    }

    /// <summary>
    /// Opens an existing spill/persisted file without loading it into memory.
    /// </summary>
    public static RowSpillBuffer FromFile(string path, long count) =>
        new(Path.GetFullPath(path), Math.Max(0, count));

    public long Count => _count;

    public bool IsSpilled => _path is not null;

    public string? FilePath => _path;

    /// <summary>Whether the backing file belongs to the caller and must not be deleted on dispose.</summary>
    private bool PersistsFile { get; init; }

    public async ValueTask AppendAsync(DatasetRow row, CancellationToken cancellationToken = default)
    {
        if (PersistsFile)
        {
            throw new InvalidOperationException("Cannot append to a read-only row buffer.");
        }

        if (_writer is null && _memory.Count >= _memoryLimitRows)
        {
            await StartSpillAsync(cancellationToken);
        }

        if (_writer is null)
        {
            _memory.Add(row);
        }
        else
        {
            await WriteRowAsync(_writer, row, cancellationToken);
        }

        _count++;
    }

    /// <summary>
    /// Flushes pending writes so the buffer can be read back.
    /// </summary>
    public async ValueTask SealAsync(CancellationToken cancellationToken = default)
    {
        if (_writer is null) return;

        var writer = _writer;
        _writer = null;
        await writer.FlushAsync(cancellationToken);
        await writer.DisposeAsync();
    }

    public async IAsyncEnumerable<DatasetRow> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_writer is not null)
        {
            await SealAsync(cancellationToken);
        }

        if (_path is null)
        {
            foreach (var row in _memory)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return row;
            }

            yield break;
        }

        await using var stream = new FileStream(
            _path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, 64 * 1024);

        var index = 0L;
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return Parse(line, _path, index++);
        }

        if (index != _count)
        {
            throw new InvalidDataException(
                $"Row buffer '{_path}' contains {index} row(s) but {_count} were expected.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_writer is not null)
        {
            var writer = _writer;
            _writer = null;
            await writer.DisposeAsync();
        }

        if (_path is not null && !PersistsFile)
        {
            TryDelete(_path);
            _path = null;
        }
    }

    private async ValueTask StartSpillAsync(CancellationToken cancellationToken)
    {
        var directory = _directory ?? Path.Combine(Path.GetTempPath(), "LMTrainingDataStudio", "run-spill");
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, $"rows-{Guid.NewGuid():N}.rows");

        var stream = new FileStream(
            _path, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        foreach (var row in _memory)
        {
            await WriteRowAsync(_writer, row, cancellationToken);
        }

        _memory.Clear();
        _memory.TrimExcess();
    }

    private static async ValueTask WriteRowAsync(
        StreamWriter writer, DatasetRow row, CancellationToken cancellationToken)
    {
        await writer.WriteLineAsync($"{row.LineNumber}\t{row.Json}".AsMemory(), cancellationToken);
    }

    private static DatasetRow Parse(string line, string path, long index)
    {
        var separator = line.IndexOf('\t');
        if (separator <= 0 || !long.TryParse(line.AsSpan(0, separator), out var lineNumber))
        {
            throw new InvalidDataException(
                $"Row buffer '{path}' has a malformed entry at position {index}.");
        }

        return new DatasetRow(lineNumber, line[(separator + 1)..]);
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
