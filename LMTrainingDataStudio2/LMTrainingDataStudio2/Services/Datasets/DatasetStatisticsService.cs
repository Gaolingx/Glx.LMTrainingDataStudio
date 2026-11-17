using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using LMTrainingDataStudio2.Models;
using Microsoft.Data.Sqlite;

namespace LMTrainingDataStudio2.Services.Datasets;

public sealed record DatasetLengthBucket(string Label, long Min, long Max, long Count);

public sealed class DatasetStatisticsResult
{
    public long TotalRows { get; internal set; }
    public long MalformedRows { get; internal set; }
    public long EmptyRows { get; internal set; }
    public long DuplicateRows { get; internal set; }
    public long TotalCharacters { get; internal set; }
    public long EmptyContentMessages { get; internal set; }
    public long ToolCallCount { get; internal set; }
    public long ToolMessageCount { get; internal set; }
    public Dictionary<string, long> RoleCounts { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, long> FormatCounts { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<DatasetLengthBucket> LengthBuckets { get; } = new();
    public TimeSpan Elapsed { get; internal set; }

    public double DuplicateRate => TotalRows == 0 ? 0 : (double)DuplicateRows / TotalRows;

    public double AverageCharacters => TotalRows == 0 ? 0 : (double)TotalCharacters / TotalRows;
}

/// <summary>
/// Streaming dataset profile: role distribution, length buckets, empty/duplicate samples,
/// tool-call counts and detected formats. Duplicate detection uses a temporary SQLite set
/// so memory stays bounded for multi-million-row files.
/// </summary>
public sealed class DatasetStatisticsService
{
    private const int PageSize = 500;

    private static readonly Dictionary<string, string> RoleAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["human"] = "user",
        ["user"] = "user",
        ["prompter"] = "user",
        ["instruction"] = "user",
        ["gpt"] = "assistant",
        ["assistant"] = "assistant",
        ["bot"] = "assistant",
        ["model"] = "assistant",
        ["function_call"] = "assistant",
        ["system"] = "system",
        ["developer"] = "developer",
        ["function"] = "tool",
        ["observation"] = "tool",
        ["tool"] = "tool"
    };

    private static readonly (string Label, long Min, long Max)[] Buckets =
    {
        ("0", 0, 0),
        ("1–100", 1, 100),
        ("101–500", 101, 500),
        ("501–2,000", 501, 2_000),
        ("2,001–8,000", 2_001, 8_000),
        ("8,001–32,000", 8_001, 32_000),
        ("32,001+", 32_001, long.MaxValue)
    };

    private readonly AppSettings _settings;
    private readonly DatasetFormatConverter _formatDetector = new();

    public DatasetStatisticsService(AppSettings? settings = null)
    {
        _settings = settings ?? new AppSettings();
    }

    public async Task<DatasetStatisticsResult> ComputeAsync(
        string sourcePath,
        DataFileType fileType,
        bool computeDuplicates = true,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var source = Path.GetFullPath(sourcePath);
        if (!File.Exists(source))
        {
            throw new FileNotFoundException($"Dataset file not found: '{source}'.", source);
        }

        var stopwatch = Stopwatch.StartNew();
        var result = new DatasetStatisticsResult();
        var bucketCounts = new long[Buckets.Length];
        DuplicateStore? duplicates = computeDuplicates ? await DuplicateStore.CreateAsync() : null;

        try
        {
            await using var reader = DatasetIO.CreateReader(fileType, _settings);
            await reader.OpenAsync(source, cancellationToken);

            for (var start = 0L; start < reader.TotalRows; start += PageSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = await reader.ReadPageAsync(start, PageSize, cancellationToken);
                foreach (var row in page)
                {
                    result.TotalRows++;

                    if (duplicates is not null)
                    {
                        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(row.Json.Trim()));
                        if (!await duplicates.TryAddAsync(hash, cancellationToken))
                        {
                            result.DuplicateRows++;
                        }
                    }

                    await AccumulateRow(row.Json, result, bucketCounts, cancellationToken);
                }

                progress?.Report(reader.TotalRows == 0
                    ? 1.0
                    : (double)(start + page.Count) / reader.TotalRows);
            }

            if (duplicates is not null)
            {
                await duplicates.FlushAsync(cancellationToken);
            }

            for (var i = 0; i < Buckets.Length; i++)
            {
                result.LengthBuckets.Add(new DatasetLengthBucket(
                    Buckets[i].Label, Buckets[i].Min, Buckets[i].Max, bucketCounts[i]));
            }

            progress?.Report(1.0);
            return result;
        }
        finally
        {
            result.Elapsed = stopwatch.Elapsed;
            if (duplicates is not null)
            {
                await duplicates.DisposeAsync();
            }
        }
    }

    private Task AccumulateRow(
        string json,
        DatasetStatisticsResult result,
        long[] bucketCounts,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch
        {
            result.MalformedRows++;
            bucketCounts[FindBucket(0)]++;
            return Task.CompletedTask;
        }

        if (node is not JsonObject row)
        {
            result.MalformedRows++;
            bucketCounts[FindBucket(0)]++;
            return Task.CompletedTask;
        }

        var format = _formatDetector.DetectFormat(row);
        var formatKey = format == DatasetFormat.Auto ? "Unknown" : format.ToString();
        Increment(result.FormatCounts, formatKey);

        long characters = 0;
        long toolCalls = 0;
        long toolMessages = 0;
        var emptyMessages = 0L;

        switch (format)
        {
            case DatasetFormat.OpenAI:
                AccumulateMessages(row["messages"] as JsonArray, result,
                    out characters, out toolCalls, out toolMessages, out emptyMessages);
                break;
            case DatasetFormat.ShareGPT:
                AccumulateShareGpt(row["conversations"] as JsonArray, result,
                    out characters, out toolCalls, out toolMessages, out emptyMessages);
                break;
            case DatasetFormat.Alpaca:
                characters = AccumulateAlpaca(row, result);
                break;
            case DatasetFormat.Dpo:
                characters = AccumulateDpo(row, result);
                break;
            case DatasetFormat.Pretrain:
                characters = NodeTextLength(row["text"]);
                break;
            default:
                characters = row.Count == 0 ? 0 : json.Length;
                break;
        }

        result.TotalCharacters += characters;
        result.ToolCallCount += toolCalls;
        result.ToolMessageCount += toolMessages;
        result.EmptyContentMessages += emptyMessages;

        if (characters == 0 && toolCalls == 0)
        {
            result.EmptyRows++;
        }

        var bucket = FindBucket(characters);
        bucketCounts[bucket]++;
        return Task.CompletedTask;
    }

    private static void AccumulateMessages(
        JsonArray? messages,
        DatasetStatisticsResult result,
        out long characters,
        out long toolCalls,
        out long toolMessages,
        out long emptyMessages)
    {
        characters = 0;
        toolCalls = 0;
        toolMessages = 0;
        emptyMessages = 0;

        if (messages is null)
        {
            return;
        }

        foreach (var item in messages)
        {
            if (item is not JsonObject message)
            {
                continue;
            }

            var role = NormalizeRole(ReadString(message, "role"));
            if (role.Length > 0)
            {
                Increment(result.RoleCounts, role);
            }

            var content = message["content"];
            var length = NodeTextLength(content);
            characters += length;
            if (length == 0)
            {
                emptyMessages++;
            }

            if (message["tool_calls"] is JsonArray calls)
            {
                toolCalls += calls.Count;
                if (role == "assistant")
                {
                    characters += calls.ToJsonString().Length;
                }
            }

            if (role == "tool")
            {
                toolMessages++;
            }
        }
    }

    private static void AccumulateShareGpt(
        JsonArray? conversations,
        DatasetStatisticsResult result,
        out long characters,
        out long toolCalls,
        out long toolMessages,
        out long emptyMessages)
    {
        characters = 0;
        toolCalls = 0;
        toolMessages = 0;
        emptyMessages = 0;

        if (conversations is null)
        {
            return;
        }

        foreach (var item in conversations)
        {
            if (item is not JsonObject turn)
            {
                continue;
            }

            var role = NormalizeRole(ReadString(turn, "from") ?? ReadString(turn, "role"));
            if (role.Length > 0)
            {
                Increment(result.RoleCounts, role);
            }

            var content = turn["value"] ?? turn["content"];
            var length = NodeTextLength(content);
            characters += length;
            if (length == 0)
            {
                emptyMessages++;
            }

            if (turn["tool_calls"] is JsonArray calls)
            {
                toolCalls += calls.Count;
            }

            if (role == "tool")
            {
                toolMessages++;
            }
        }
    }

    private static long AccumulateAlpaca(JsonObject row, DatasetStatisticsResult result)
    {
        var instruction = NodeTextLength(row["instruction"]);
        var input = NodeTextLength(row["input"]);
        var output = NodeTextLength(row["output"]);

        if (instruction + input > 0)
        {
            Increment(result.RoleCounts, "user");
        }

        if (output > 0)
        {
            Increment(result.RoleCounts, "assistant");
        }

        return instruction + input + output;
    }

    private static long AccumulateDpo(JsonObject row, DatasetStatisticsResult result)
    {
        var prompt = NodeTextLength(row["prompt"]);
        var chosen = NodeTextLength(row["chosen"]);
        var rejected = NodeTextLength(row["rejected"]);

        if (prompt > 0)
        {
            Increment(result.RoleCounts, "user");
        }

        if (chosen > 0)
        {
            Increment(result.RoleCounts, "assistant");
        }

        if (rejected > 0)
        {
            Increment(result.RoleCounts, "assistant");
        }

        return prompt + chosen + rejected;
    }

    private static string NormalizeRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return string.Empty;
        }

        var trimmed = role.Trim().ToLowerInvariant();
        return RoleAliases.TryGetValue(trimmed, out var normalized) ? normalized : trimmed;
    }

    private static string? ReadString(JsonObject obj, string key)
    {
        return obj.TryGetPropertyValue(key, out var node) &&
               node is JsonValue value &&
               value.TryGetValue<string>(out var text)
            ? text
            : null;
    }

    private static long NodeTextLength(JsonNode? node)
    {
        return node switch
        {
            null => 0,
            JsonValue value => value.TryGetValue<string>(out var text)
                ? text.Length
                : value.ToJsonString().Length,
            _ => node.ToJsonString().Length
        };
    }

    private static int FindBucket(long characters)
    {
        for (var i = Buckets.Length - 1; i >= 0; i--)
        {
            if (characters >= Buckets[i].Min)
            {
                return i;
            }
        }

        return 0;
    }

    private static void Increment(Dictionary<string, long> counts, string key)
    {
        counts.TryGetValue(key, out var current);
        counts[key] = current + 1;
    }

    private sealed class DuplicateStore : IAsyncDisposable
    {
        private const int FlushEvery = 50_000;

        private readonly SqliteConnection _connection;
        private readonly string _path;
        private readonly SqliteCommand _insert;
        private SqliteTransaction? _transaction;
        private int _pending;

        private DuplicateStore(SqliteConnection connection, string path, SqliteCommand insert)
        {
            _connection = connection;
            _path = path;
            _insert = insert;
        }

        public static async Task<DuplicateStore> CreateAsync()
        {
            var directory = Path.Combine(Path.GetTempPath(), "lmtds-stats");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".db");

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Private
            }.ToString();

            var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            using (var pragma = connection.CreateCommand())
            {
                pragma.CommandText = "PRAGMA journal_mode=OFF; PRAGMA synchronous=OFF;";
                await pragma.ExecuteNonQueryAsync();
            }

            using (var create = connection.CreateCommand())
            {
                create.CommandText = "CREATE TABLE hashes(hash BLOB PRIMARY KEY) WITHOUT ROWID;";
                await create.ExecuteNonQueryAsync();
            }

            var insert = connection.CreateCommand();
            insert.CommandText = "INSERT OR IGNORE INTO hashes(hash) VALUES ($hash);";
            insert.Parameters.Add("$hash", SqliteType.Blob);

            return new DuplicateStore(connection, path, insert);
        }

        public async Task<bool> TryAddAsync(byte[] hash, CancellationToken cancellationToken)
        {
            _transaction ??= _connection.BeginTransaction();
            _insert.Transaction = _transaction;
            _insert.Parameters["$hash"].Value = hash;
            var changes = await _insert.ExecuteNonQueryAsync(cancellationToken);

            if (++_pending >= FlushEvery)
            {
                await FlushAsync(cancellationToken);
            }

            return changes > 0;
        }

        public async Task FlushAsync(CancellationToken cancellationToken)
        {
            if (_transaction is null)
            {
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            await _transaction.CommitAsync(cancellationToken);
            await _transaction.DisposeAsync();
            _transaction = null;
            _pending = 0;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_transaction is not null)
                {
                    await _transaction.RollbackAsync();
                    await _transaction.DisposeAsync();
                    _transaction = null;
                }
            }
            catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
            {
            }

            _insert.Dispose();
            await _connection.DisposeAsync();
            SqliteConnection.ClearAllPools();
            TryDelete(_path);
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
}
