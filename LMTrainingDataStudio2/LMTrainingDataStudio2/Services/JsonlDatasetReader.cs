using LMTrainingDataStudio2.Models;
using Microsoft.Data.Sqlite;

namespace LMTrainingDataStudio2.Services;

/// <summary>
/// Reads JSONL rows through a persistent line index and a single open file stream.
/// </summary>
public sealed class JsonlDatasetReader : IDisposable, IAsyncDisposable
{
    private readonly IndexCacheService _indexCache;
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private SqliteConnection? _connection;
    private FileStream? _stream;
    private bool _disposed;

    public JsonlDatasetReader(AppSettings settings)
    {
        _indexCache = new IndexCacheService(settings);
    }

    public string SourcePath { get; private set; } = string.Empty;

    public long TotalLines { get; private set; }

    /// <summary>
    /// Opens the file, building or reusing its index.
    /// </summary>
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
            throw new FileNotFoundException($"JSONL file not found: '{fullPath}'.", fullPath);
        }

        var connection = await _indexCache.OpenCacheAsync(fullPath, cancellationToken);
        try
        {
            TotalLines = await _indexCache.EnsureIndexAsync(fullPath, connection, progress: null, cancellationToken);
            _stream = new FileStream(
                fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536,
                FileOptions.Asynchronous | FileOptions.RandomAccess);
            _connection = connection;
            SourcePath = fullPath;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Reads up to <paramref name="count"/> rows starting at a zero-based line number.
    /// </summary>
    public async Task<IReadOnlyList<DatasetRow>> ReadPageAsync(
        long startLine, int count, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        ArgumentOutOfRangeException.ThrowIfNegative(startLine);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        if (count == 0 || startLine >= TotalLines)
        {
            return Array.Empty<DatasetRow>();
        }

        var endLine = Math.Min(TotalLines, startLine + count);
        var rows = new List<DatasetRow>((int)(endLine - startLine));

        await _ioLock.WaitAsync(cancellationToken);
        try
        {
            for (var line = startLine; line < endLine; line++)
            {
                var row = await ReadLineCoreAsync(line, cancellationToken);
                if (row is not null)
                {
                    rows.Add(row);
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
    /// Reads a single row by zero-based line number. Returns null when out of range.
    /// </summary>
    public async Task<DatasetRow?> ReadLineAsync(long lineNumber, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        if (lineNumber < 0 || lineNumber >= TotalLines)
        {
            return null;
        }

        await _ioLock.WaitAsync(cancellationToken);
        try
        {
            return await ReadLineCoreAsync(lineNumber, cancellationToken);
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _stream?.Dispose();
        _connection?.Dispose();
        _indexCache.Dispose();
        _ioLock.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_stream is not null)
        {
            await _stream.DisposeAsync();
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        _indexCache.Dispose();
        _ioLock.Dispose();
    }

    private async Task<DatasetRow?> ReadLineCoreAsync(long lineNumber, CancellationToken ct)
    {
        try
        {
            var json = await _indexCache.ReadLineAsync(_connection!, _stream!, lineNumber, ct);
            return json is null ? null : new DatasetRow(lineNumber, json);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException($"Failed to read line {lineNumber} from '{SourcePath}': {ex.Message}", ex);
        }
    }

    private void EnsureOpen()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_stream is null || _connection is null)
        {
            throw new InvalidOperationException("The reader is not open. Call OpenAsync before reading.");
        }
    }
}