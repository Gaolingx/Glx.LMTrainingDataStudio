using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services.Executors;
using Microsoft.Data.Sqlite;

namespace LMTrainingDataStudio2.Services.Datasets;

/// <summary>
/// Deduplicate by exact row text hash, shuffle with a deterministic seeded order and
/// split into <c>name:ratio</c> / <c>name:count</c> files. All stages stream pages and
/// keep memory bounded through a temporary SQLite database.
/// </summary>
public sealed record DatasetTransformOptions(
    bool Deduplicate = false,
    bool Shuffle = false,
    long Seed = 0,
    IReadOnlyList<string>? Splits = null,
    string? OutputPath = null,
    string? OutputDirectory = null);

public sealed record DatasetSplitOutput(string Name, string Path, long Rows);

public sealed class DatasetTransformResult
{
    public long TotalRows { get; internal set; }
    public long OutputRows { get; internal set; }
    public long DuplicateRows { get; internal set; }
    public string? OutputPath { get; internal set; }
    public List<DatasetSplitOutput> Splits { get; } = new();
    public TimeSpan Elapsed { get; internal set; }
}

public sealed class DatasetTransformService
{
    private const int PageSize = 500;
    private const int ShuffleBatchSize = 2000;
    private const int ReadGroupWindow = 16;
    private const int MaxPageRead = 4096;

    private readonly AppSettings _settings;

    public DatasetTransformService(AppSettings? settings = null)
    {
        _settings = settings ?? new AppSettings();
    }

    public async Task<DatasetTransformResult> TransformAsync(
        string sourcePath,
        DataFileType sourceFileType,
        DatasetTransformOptions options,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var source = Path.GetFullPath(sourcePath);
        if (!File.Exists(source))
        {
            throw new FileNotFoundException($"Dataset file not found: '{source}'.", source);
        }

        var stopwatch = Stopwatch.StartNew();
        var result = new DatasetTransformResult();

        await using var reader = DatasetIO.CreateReader(sourceFileType, _settings);
        await reader.OpenAsync(source, cancellationToken);

        var store = options.Deduplicate || options.Shuffle
            ? await TransformStore.CreateAsync(options.Deduplicate, options.Shuffle, cancellationToken)
            : null;

        try
        {
            await using var output = CreateOutput(source, sourceFileType, options);
            var total = reader.TotalRows;
            var shuffleOrdinal = 0L;

            for (var start = 0L; start < total; start += PageSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = await reader.ReadPageAsync(start, PageSize, cancellationToken);
                foreach (var row in page)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    result.TotalRows++;

                    if (options.Deduplicate)
                    {
                        var hash = HashRow(row.Json);
                        if (!await store!.TryAddHashAsync(hash, cancellationToken))
                        {
                            result.DuplicateRows++;
                            continue;
                        }
                    }

                    if (options.Shuffle)
                    {
                        var key = CreateShuffleKey(options.Seed, shuffleOrdinal);
                        await store!.AddShuffleAsync(shuffleOrdinal, key, row.LineNumber, cancellationToken);
                        shuffleOrdinal++;
                    }
                    else
                    {
                        await output.WriteAsync(row, cancellationToken);
                        result.OutputRows++;
                    }
                }

                var readProgress = total == 0 ? 1.0 : (double)(start + page.Count) / total;
                progress?.Report(options.Shuffle ? readProgress * 0.6 : readProgress);
            }

            if (store is not null)
            {
                await store.FlushAsync(cancellationToken);
            }

            if (options.Shuffle)
            {
                var batch = new List<(long Seq, long Src)>(ShuffleBatchSize);
                await foreach (var entry in store!.ReadShuffledAsync(cancellationToken))
                {
                    batch.Add(entry);
                    if (batch.Count >= ShuffleBatchSize)
                    {
                        await WriteShuffledBatchAsync(reader, output, batch, cancellationToken);
                        result.OutputRows += batch.Count;
                        batch.Clear();
                        progress?.Report(total == 0 ? 1.0 : 0.6 + 0.4 * result.OutputRows / total);
                    }
                }

                if (batch.Count > 0)
                {
                    await WriteShuffledBatchAsync(reader, output, batch, cancellationToken);
                    result.OutputRows += batch.Count;
                }
            }

            await output.CompleteAsync(cancellationToken);
            result.OutputPath = output.SinglePath;
            result.Splits.AddRange(output.GetSplitOutputs());
            progress?.Report(1.0);
            return result;
        }
        finally
        {
            result.Elapsed = stopwatch.Elapsed;
            if (store is not null)
            {
                await store.DisposeAsync();
            }
        }
    }

    private static async Task WriteShuffledBatchAsync(
        IDatasetReader reader,
        TransformOutput output,
        List<(long Seq, long Src)> batch,
        CancellationToken cancellationToken)
    {
        var sortedSources = batch.Select(entry => entry.Src).OrderBy(index => index).ToArray();
        var rows = await ReadRowsAsync(reader, sortedSources, cancellationToken);
        foreach (var (_, sourceIndex) in batch)
        {
            if (rows.TryGetValue(sourceIndex, out var row))
            {
                await output.WriteAsync(row, cancellationToken);
            }
        }
    }

    private static async Task<Dictionary<long, DatasetRow>> ReadRowsAsync(
        IDatasetReader reader,
        long[] sortedIndices,
        CancellationToken cancellationToken)
    {
        var rows = new Dictionary<long, DatasetRow>(sortedIndices.Length);
        var index = 0;
        while (index < sortedIndices.Length)
        {
            var groupStart = sortedIndices[index];
            var groupEnd = groupStart;
            var next = index + 1;

            while (next < sortedIndices.Length &&
                   sortedIndices[next] <= groupEnd + ReadGroupWindow &&
                   sortedIndices[next] - groupStart < MaxPageRead)
            {
                groupEnd = sortedIndices[next];
                next++;
            }

            var page = await reader.ReadPageAsync(
                groupStart, (int)(groupEnd - groupStart + 1), cancellationToken);
            foreach (var row in page)
            {
                rows[row.LineNumber] = row;
            }

            index = next;
        }

        return rows;
    }

    private static TransformOutput CreateOutput(
        string source,
        DataFileType sourceFileType,
        DatasetTransformOptions options)
    {
        var splitSpecs = options.Splits?
            .Where(spec => !string.IsNullOrWhiteSpace(spec))
            .ToArray();

        if (splitSpecs is { Length: > 0 })
        {
            if (string.IsNullOrWhiteSpace(options.OutputDirectory))
            {
                throw new ArgumentException(
                    "An output directory is required when splitting.", nameof(options));
            }

            if (!SamplerSplitPlan.TryParse(splitSpecs, out var plan, out var error))
            {
                throw new ArgumentException($"Invalid splits: {error}", nameof(options));
            }

            var directory = Path.GetFullPath(options.OutputDirectory);
            var extension = ExtensionFor(sourceFileType);
            var sinks = new List<SplitSink>();
            foreach (var spec in plan!.Specs)
            {
                var path = Path.Combine(directory, spec.Name + extension);
                sinks.Add(new SplitSink(
                    spec.Name,
                    path,
                    DatasetIO.CreateWriter(sourceFileType, path)));
            }

            return new TransformOutput(
                single: null, singlePath: null, sinks, plan, options.Seed);
        }

        if (string.IsNullOrWhiteSpace(options.OutputPath))
        {
            throw new ArgumentException(
                "An output path is required when not splitting.", nameof(options));
        }

        var targetPath = Path.GetFullPath(options.OutputPath);
        if (string.Equals(source, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Source and target paths must differ.", nameof(options));
        }

        var targetType = DatasetIO.DetectFileType(targetPath);
        return new TransformOutput(
            DatasetIO.CreateWriter(targetType, targetPath),
            targetPath,
            new List<SplitSink>(),
            plan: null,
            options.Seed);
    }

    private static string ExtensionFor(DataFileType fileType) => fileType switch
    {
        DataFileType.Jsonl => ".jsonl",
        DataFileType.Csv => ".csv",
        DataFileType.Parquet => ".parquet",
        _ => ".jsonl"
    };

    private static byte[] HashRow(string json) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(json.Trim()));

    private static byte[] CreateShuffleKey(long seed, long ordinal)
    {
        var key = new byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(key, Mix(Mix((ulong)seed) ^ (ulong)ordinal));
        return key;
    }

    private static ulong Mix(ulong value)
    {
        value += 0x9E3779B97F4A7C15UL;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }

    private sealed class TransformOutput : IAsyncDisposable
    {
        private readonly IDatasetWriter? _single;
        private readonly List<SplitSink> _sinks;
        private readonly SamplerSplitPlan? _plan;
        private readonly Random? _random;
        private readonly long[] _splitCounts;
        private long _ordinal;
        private bool _completed;

        public TransformOutput(
            IDatasetWriter? single,
            string? singlePath,
            List<SplitSink> sinks,
            SamplerSplitPlan? plan,
            long seed)
        {
            _single = single;
            SinglePath = singlePath;
            _sinks = sinks;
            _plan = plan;
            _random = plan is null ? null : new Random(unchecked((int)(seed ^ (seed >> 32))));
            _splitCounts = new long[sinks.Count];
        }

        public string? SinglePath { get; }

        public async Task WriteAsync(DatasetRow row, CancellationToken cancellationToken)
        {
            if (_plan is null)
            {
                await _single!.WriteRowAsync(row, cancellationToken);
                return;
            }

            var split = _plan.Assign(_ordinal, _random!);
            await _sinks[split].Writer.WriteRowAsync(row, cancellationToken);
            _splitCounts[split]++;
            _ordinal++;
        }

        public async Task CompleteAsync(CancellationToken cancellationToken)
        {
            if (_completed) return;

            if (_single is not null)
            {
                await _single.CompleteAsync(cancellationToken);
            }

            foreach (var sink in _sinks)
            {
                await sink.Writer.CompleteAsync(cancellationToken);
            }

            _completed = true;
        }

        public IReadOnlyList<DatasetSplitOutput> GetSplitOutputs()
        {
            var outputs = new List<DatasetSplitOutput>(_sinks.Count);
            for (var i = 0; i < _sinks.Count; i++)
            {
                outputs.Add(new DatasetSplitOutput(_sinks[i].Name, _sinks[i].Path, _splitCounts[i]));
            }

            return outputs;
        }

        public async ValueTask DisposeAsync()
        {
            if (_single is not null)
            {
                await _single.DisposeAsync();
            }

            foreach (var sink in _sinks)
            {
                await sink.Writer.DisposeAsync();
            }
        }
    }

    private sealed record SplitSink(string Name, string Path, IDatasetWriter Writer);

    private sealed class TransformStore : IAsyncDisposable
    {
        private const int FlushEvery = 50_000;

        private readonly SqliteConnection _connection;
        private readonly string _path;
        private readonly SqliteCommand? _insertHash;
        private readonly SqliteCommand? _insertShuffle;
        private SqliteTransaction? _transaction;
        private int _pending;
        private bool _shuffleIndexed;

        private TransformStore(
            SqliteConnection connection,
            string path,
            SqliteCommand? insertHash,
            SqliteCommand? insertShuffle)
        {
            _connection = connection;
            _path = path;
            _insertHash = insertHash;
            _insertShuffle = insertShuffle;
        }

        public static async Task<TransformStore> CreateAsync(
            bool deduplicate, bool shuffle, CancellationToken cancellationToken)
        {
            var directory = Path.Combine(Path.GetTempPath(), "lmtds-transforms");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".db");

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Private
            }.ToString();

            var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            await ExecuteAsync(connection, "PRAGMA journal_mode=OFF;", cancellationToken);
            await ExecuteAsync(connection, "PRAGMA synchronous=OFF;", cancellationToken);

            SqliteCommand? insertHash = null;
            SqliteCommand? insertShuffle = null;

            if (deduplicate)
            {
                await ExecuteAsync(connection,
                    "CREATE TABLE hashes(hash BLOB PRIMARY KEY) WITHOUT ROWID;",
                    cancellationToken);
                insertHash = connection.CreateCommand();
                insertHash.CommandText = "INSERT OR IGNORE INTO hashes(hash) VALUES ($hash);";
                insertHash.Parameters.Add("$hash", SqliteType.Blob);
            }

            if (shuffle)
            {
                await ExecuteAsync(connection,
                    "CREATE TABLE shuffle(seq INTEGER PRIMARY KEY, key BLOB NOT NULL, src INTEGER NOT NULL);",
                    cancellationToken);
                insertShuffle = connection.CreateCommand();
                insertShuffle.CommandText =
                    "INSERT INTO shuffle(seq, key, src) VALUES ($seq, $key, $src);";
                insertShuffle.Parameters.Add("$seq", SqliteType.Integer);
                insertShuffle.Parameters.Add("$key", SqliteType.Blob);
                insertShuffle.Parameters.Add("$src", SqliteType.Integer);
            }

            return new TransformStore(connection, path, insertHash, insertShuffle);
        }

        public async Task<bool> TryAddHashAsync(byte[] hash, CancellationToken cancellationToken)
        {
            EnsureTransaction();
            _insertHash!.Transaction = _transaction;
            _insertHash.Parameters["$hash"].Value = hash;
            var changes = await _insertHash.ExecuteNonQueryAsync(cancellationToken);
            await CountWriteAsync(cancellationToken);
            return changes > 0;
        }

        public async Task AddShuffleAsync(
            long seq, byte[] key, long sourceIndex, CancellationToken cancellationToken)
        {
            EnsureTransaction();
            _insertShuffle!.Transaction = _transaction;
            _insertShuffle.Parameters["$seq"].Value = seq;
            _insertShuffle.Parameters["$key"].Value = key;
            _insertShuffle.Parameters["$src"].Value = sourceIndex;
            await _insertShuffle.ExecuteNonQueryAsync(cancellationToken);
            await CountWriteAsync(cancellationToken);
        }

        public async IAsyncEnumerable<(long Seq, long Src)> ReadShuffledAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await FlushAsync(cancellationToken);

            if (!_shuffleIndexed)
            {
                await ExecuteAsync(_connection,
                    "CREATE INDEX idx_shuffle_key ON shuffle(key);", cancellationToken);
                _shuffleIndexed = true;
            }

            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT seq, src FROM shuffle ORDER BY key, seq;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                yield return (reader.GetInt64(0), reader.GetInt64(1));
            }
        }

        public async Task FlushAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_transaction is null)
            {
                return;
            }

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

            _insertHash?.Dispose();
            _insertShuffle?.Dispose();
            await _connection.DisposeAsync();
            SqliteConnection.ClearAllPools();
            TryDelete(_path);
        }

        private void EnsureTransaction()
        {
            _transaction ??= _connection.BeginTransaction();
        }

        private async Task CountWriteAsync(CancellationToken cancellationToken)
        {
            _pending++;
            if (_pending >= FlushEvery)
            {
                await FlushAsync(cancellationToken);
            }
        }

        private static async Task ExecuteAsync(
            SqliteConnection connection, string sql, CancellationToken cancellationToken)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(cancellationToken);
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