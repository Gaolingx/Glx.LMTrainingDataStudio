using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LMTrainingDataStudio2.Models;
using Microsoft.Data.Sqlite;

namespace LMTrainingDataStudio2.Services;

/// <summary>
/// Manages SQLite-based index caches for large JSONL files.
/// Stores line number → byte offset mappings for fast random access.
/// </summary>
public sealed class IndexCacheService : IDisposable
{
    private const int FingerprintChunkSize = 4096;
    private const string FingerprintKey = "source_fingerprint";
    private const string TotalLinesKey = "total_lines";
    private const string SourceFileKey = "source_file";

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
            SHA256.HashData(Encoding.UTF8.GetBytes(sourceFilePath)))[..16];
        return Path.Combine(_cacheDirectory, $"{hash}.db");
    }

    /// <summary>
    /// Opens or creates an index cache for the specified file, releasing any previous connection.
    /// </summary>
    public async Task<SqliteConnection> OpenCacheAsync(string sourceFilePath, CancellationToken ct = default)
    {
        _connection?.Dispose();
        _connection = null;

        var cachePath = GetCachePath(sourceFilePath);
        var connection = new SqliteConnection($"Data Source={cachePath}");
        await connection.OpenAsync(ct);

        try
        {
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
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }

        _connection = connection;
        return connection;
    }

    /// <summary>
    /// Ensures the cache for a source file matches its current fingerprint, rebuilding it when stale.
    /// Returns the total number of indexed lines.
    /// </summary>
    public async Task<long> EnsureIndexAsync(
        string filePath,
        SqliteConnection connection,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var fingerprint = await ComputeFingerprintAsync(filePath, ct);
        var storedFingerprint = await GetMetadataAsync(connection, FingerprintKey, ct);

        if (!string.Equals(storedFingerprint, fingerprint, StringComparison.Ordinal))
        {
            EnforceCacheCapacity(GetCachePath(filePath));
            await BuildIndexAsync(filePath, connection, progress, ct);
            await SetMetadataAsync(connection, FingerprintKey, fingerprint, ct);
        }

        TouchCacheFile(GetCachePath(filePath));
        return await GetTotalLinesAsync(connection, ct);
    }

    /// <summary>
    /// Builds the line index for a JSONL file using byte-exact streaming reads.
    /// Existing index entries are replaced.
    /// </summary>
    public async Task BuildIndexAsync(
        string filePath,
        SqliteConnection connection,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var totalBytes = new FileInfo(filePath).Length;
        long lineNumber = 0;
        long lineStart = 0;
        long absoluteOffset = 0;
        var lastReportedPercent = -1;

        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using (var clearCmd = connection.CreateCommand())
        {
            clearCmd.CommandText = "DELETE FROM line_index; DELETE FROM metadata;";
            await clearCmd.ExecuteNonQueryAsync(ct);
        }

        await using var insertCmd = connection.CreateCommand();
        insertCmd.CommandText = "INSERT INTO line_index (line_number, byte_offset, line_length) VALUES ($ln, $offset, $length)";
        insertCmd.Parameters.Add("$ln", SqliteType.Integer);
        insertCmd.Parameters.Add("$offset", SqliteType.Integer);
        insertCmd.Parameters.Add("$length", SqliteType.Integer);

        await using var stream = new FileStream(
            filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536,
            FileOptions.SequentialScan | FileOptions.Asynchronous);

        var prefix = new byte[3];
        var prefixLength = await stream.ReadAtLeastAsync(prefix, prefix.Length, throwOnEndOfStream: false, ct);
        var hasBom = prefixLength == 3 && prefix[0] == 0xEF && prefix[1] == 0xBB && prefix[2] == 0xBF;
        if (hasBom)
        {
            absoluteOffset = 3;
            lineStart = 3;
        }
        else if (prefixLength > 0)
        {
            stream.Seek(0, SeekOrigin.Begin);
        }

        var buffer = ArrayPool<byte>.Shared.Rent(65536);
        try
        {
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
            {
                for (var i = 0; i < read; i++)
                {
                    if (buffer[i] != (byte)'\n') continue;

                    var lineEnd = absoluteOffset + i + 1;
                    await InsertLineAsync(insertCmd, lineNumber++, lineStart, lineEnd - lineStart, ct);
                    lineStart = lineEnd;
                }

                absoluteOffset += read;

                if (totalBytes > 0)
                {
                    var percent = (int)(absoluteOffset * 100 / totalBytes);
                    if (percent != lastReportedPercent)
                    {
                        lastReportedPercent = percent;
                        progress?.Report(Math.Min(1.0, percent / 100.0));
                    }
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        if (lineStart < totalBytes)
        {
            await InsertLineAsync(insertCmd, lineNumber++, lineStart, totalBytes - lineStart, ct);
        }

        await SetMetadataAsync(connection, TotalLinesKey, lineNumber.ToString(CultureInfo.InvariantCulture), ct);
        await SetMetadataAsync(connection, SourceFileKey, filePath, ct);

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
        var value = await GetMetadataAsync(connection, TotalLinesKey, ct);
        return value is null ? 0 : long.Parse(value, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Reads a specific line from a JSONL file, opening it for a single read.
    /// </summary>
    public async Task<string?> ReadLineAsync(
        string filePath, SqliteConnection connection, long lineNumber, CancellationToken ct = default)
    {
        await using var stream = new FileStream(
            filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        return await ReadLineAsync(connection, stream, lineNumber, ct);
    }

    /// <summary>
    /// Reads a specific line using an already open stream. Reads until the indexed length is filled.
    /// </summary>
    public async Task<string?> ReadLineAsync(
        SqliteConnection connection, Stream stream, long lineNumber, CancellationToken ct = default)
    {
        var entry = await GetLineOffsetAsync(connection, lineNumber, ct);
        if (entry is null) return null;

        var length = checked((int)entry.Value.Length);
        var buffer = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            stream.Seek(entry.Value.Offset, SeekOrigin.Begin);

            var totalRead = 0;
            while (totalRead < length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(totalRead, length - totalRead), ct);
                if (read == 0) break;
                totalRead += read;
            }

            if (totalRead < length)
            {
                throw new InvalidDataException(
                    $"Indexed line {lineNumber} is shorter than expected; the source file may have changed since it was indexed.");
            }

            return Encoding.UTF8.GetString(buffer, 0, totalRead).TrimEnd('\r', '\n');
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
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
        _connection = null;
    }

    private static async Task<string> ComputeFingerprintAsync(string filePath, CancellationToken ct)
    {
        var info = new FileInfo(filePath);
        if (!info.Exists)
        {
            throw new FileNotFoundException($"JSONL file not found: '{filePath}'.", filePath);
        }

        var size = info.Length;
        await using var stream = new FileStream(
            filePath, FileMode.Open, FileAccess.Read, FileShare.Read, FingerprintChunkSize, FileOptions.Asynchronous);

        var head = new byte[(int)Math.Min(FingerprintChunkSize, size)];
        await stream.ReadExactlyAsync(head, ct);
        var headHash = Hash(head);

        string tailHash;
        if (size <= FingerprintChunkSize)
        {
            tailHash = headHash;
        }
        else
        {
            stream.Seek(-FingerprintChunkSize, SeekOrigin.End);
            var tail = new byte[FingerprintChunkSize];
            await stream.ReadExactlyAsync(tail, ct);
            tailHash = Hash(tail);
        }

        return $"{size.ToString(CultureInfo.InvariantCulture)}:" +
               $"{info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture)}:" +
               $"{headHash}:{tailHash}";
    }

    private void EnforceCacheCapacity(string currentCachePath)
    {
        if (_maxCacheSizeBytes <= 0 || !Directory.Exists(_cacheDirectory)) return;

        var currentPath = Path.GetFullPath(currentCachePath);
        var otherCaches = Directory.GetFiles(_cacheDirectory, "*.db")
            .Where(f => !string.Equals(Path.GetFullPath(f), currentPath, StringComparison.OrdinalIgnoreCase))
            .Select(f => new FileInfo(f))
            .Select(f => (Info: f, Length: f.Length))
            .OrderBy(x => x.Info.LastWriteTimeUtc)
            .ToList();

        var currentSize = File.Exists(currentPath) ? new FileInfo(currentPath).Length : 0;
        var total = otherCaches.Sum(x => x.Length);

        foreach (var (cache, length) in otherCaches)
        {
            if (total + currentSize <= _maxCacheSizeBytes) break;

            try
            {
                cache.Delete();
                total -= length;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static void TouchCacheFile(string cachePath)
    {
        try
        {
            File.SetLastWriteTimeUtc(cachePath, DateTime.UtcNow);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static async Task InsertLineAsync(
        SqliteCommand command, long lineNumber, long offset, long length, CancellationToken ct)
    {
        command.Parameters["$ln"].Value = lineNumber;
        command.Parameters["$offset"].Value = offset;
        command.Parameters["$length"].Value = length;
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<string?> GetMetadataAsync(
        SqliteConnection connection, string key, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM metadata WHERE key = $key";
        cmd.Parameters.AddWithValue("$key", key);
        return await cmd.ExecuteScalarAsync(ct) as string;
    }

    private static async Task SetMetadataAsync(
        SqliteConnection connection, string key, string value, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO metadata (key, value) VALUES ($key, $value)";
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$value", value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
