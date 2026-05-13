using Microsoft.Data.Sqlite;
using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Services;

/// <summary>
/// Manages SQLite-based index caches for large JSONL files.
/// Stores line number → byte offset mappings for fast random access.
/// </summary>
public sealed class IndexCacheService : IDisposable
{
    private readonly string _cacheDirectory;
    private readonly long _maxCacheSizeBytes;
    private SqliteConnection? _connection;

    public IndexCacheService(AppSettings settings)
    {
        _cacheDirectory = settings.CacheDirectory;
        _maxCacheSizeBytes = settings.MaxCacheSizeMb * 1024 * 1024;
        Directory.CreateDirectory(_cacheDirectory);
    }

    /// <summary>
    /// Gets the cache database path for a given source file.
    /// </summary>
    private string GetCachePath(string sourceFilePath)
    {
        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(sourceFilePath)))[..16];
        return Path.Combine(_cacheDirectory, $"{hash}.db");
    }

    /// <summary>
    /// Opens or creates an index cache for the specified file.
    /// </summary>
    public async Task<SqliteConnection> OpenCacheAsync(string sourceFilePath, CancellationToken ct = default)
    {
        var cachePath = GetCachePath(sourceFilePath);
        var connection = new SqliteConnection($"Data Source={cachePath}");
        await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS line_index (
                line_number INTEGER PRIMARY KEY,
                byte_offset INTEGER NOT NULL,
                line_length INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS metadata (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            """;
        await cmd.ExecuteNonQueryAsync(ct);

        _connection = connection;
        return connection;
    }

    /// <summary>
    /// Builds the line index for a JSONL file using streaming reads.
    /// Reports progress via IProgress.
    /// </summary>
    public async Task BuildIndexAsync(
        string filePath,
        SqliteConnection connection,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var fileInfo = new FileInfo(filePath);
        var totalBytes = fileInfo.Length;
        long currentOffset = 0;
        long lineNumber = 0;

        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var insertCmd = connection.CreateCommand();
        insertCmd.CommandText = "INSERT OR REPLACE INTO line_index (line_number, byte_offset, line_length) VALUES ($ln, $offset, $length)";
        var lnParam = insertCmd.Parameters.Add("$ln", SqliteType.Integer);
        var offsetParam = insertCmd.Parameters.Add("$offset", SqliteType.Integer);
        var lengthParam = insertCmd.Parameters.Add("$length", SqliteType.Integer);

        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan | FileOptions.Asynchronous);
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 65536);

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var lineStart = currentOffset;
            var line = await reader.ReadLineAsync(ct);
            if (line == null) break;

            var lineBytes = System.Text.Encoding.UTF8.GetByteCount(line) + 1; // +1 for newline

            lnParam.Value = lineNumber;
            offsetParam.Value = lineStart;
            lengthParam.Value = lineBytes;
            await insertCmd.ExecuteNonQueryAsync(ct);

            currentOffset += lineBytes;
            lineNumber++;

            if (lineNumber % 10000 == 0)
            {
                progress?.Report((double)currentOffset / totalBytes);
            }
        }

        // Store metadata
        await using var metaCmd = connection.CreateCommand();
        metaCmd.CommandText = "INSERT OR REPLACE INTO metadata (key, value) VALUES ('total_lines', $val)";
        metaCmd.Parameters.AddWithValue("$val", lineNumber.ToString());
        await metaCmd.ExecuteNonQueryAsync(ct);

        metaCmd.CommandText = "INSERT OR REPLACE INTO metadata (key, value) VALUES ('source_file', $val)";
        metaCmd.Parameters["$val"].Value = filePath;
        await metaCmd.ExecuteNonQueryAsync(ct);

        await transaction.CommitAsync(ct);
        progress?.Report(1.0);
    }

    /// <summary>
    /// Gets the byte offset for a specific line number.
    /// </summary>
    public async Task<(long Offset, long Length)?> GetLineOffsetAsync(
        SqliteConnection connection, long lineNumber, CancellationToken ct = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT byte_offset, line_length FROM line_index WHERE line_number = $ln";
        cmd.Parameters.AddWithValue("$ln", lineNumber);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return (reader.GetInt64(0), reader.GetInt64(1));
        }
        return null;
    }

    /// <summary>
    /// Gets the total number of indexed lines.
    /// </summary>
    public async Task<long> GetTotalLinesAsync(SqliteConnection connection, CancellationToken ct = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM metadata WHERE key = 'total_lines'";
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is string s ? long.Parse(s) : 0;
    }

    /// <summary>
    /// Reads a specific line from a JSONL file using the index cache.
    /// </summary>
    public async Task<string?> ReadLineAsync(
        string filePath, SqliteConnection connection, long lineNumber, CancellationToken ct = default)
    {
        var offset = await GetLineOffsetAsync(connection, lineNumber, ct);
        if (offset == null) return null;

        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        stream.Seek(offset.Value.Offset, SeekOrigin.Begin);

        var buffer = new byte[offset.Value.Length];
        var bytesRead = await stream.ReadAsync(buffer, ct);
        var line = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead).TrimEnd('\r', '\n');
        return line;
    }

    /// <summary>
    /// Gets the total size of all cache files in bytes.
    /// </summary>
    public long GetTotalCacheSize()
    {
        if (!Directory.Exists(_cacheDirectory)) return 0;
        return Directory.GetFiles(_cacheDirectory, "*.db")
            .Sum(f => new FileInfo(f).Length);
    }

    /// <summary>
    /// Cleans all cache files.
    /// </summary>
    public void CleanCache()
    {
        if (!Directory.Exists(_cacheDirectory)) return;
        foreach (var file in Directory.GetFiles(_cacheDirectory, "*.db"))
        {
            try { File.Delete(file); } catch { /* ignore locked files */ }
        }
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }
}
