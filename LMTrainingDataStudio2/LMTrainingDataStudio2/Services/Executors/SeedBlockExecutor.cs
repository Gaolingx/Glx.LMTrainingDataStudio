using System.Runtime.CompilerServices;
using System.Text;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services.Datasets;

namespace LMTrainingDataStudio2.Services.Executors;

/// <summary>
/// Streams rows from a JSONL/CSV/Parquet source, using the index cache for JSONL when enabled.
/// </summary>
public sealed class SeedBlockExecutor : IBlockExecutor
{
    private const int PageSize = 500;

    private readonly AppSettings _settings;

    public SeedBlockExecutor(AppSettings settings) => _settings = settings;

    public BlockType Type => BlockType.Seed;

    public Task<BlockExecutionResult> ExecuteAsync(BlockExecutionContext context, CancellationToken cancellationToken)
    {
        if (context.Block.Config is not SeedBlockConfig config)
        {
            throw new InvalidOperationException($"Seed block '{context.Block.Name}' has an invalid configuration.");
        }

        if (string.IsNullOrWhiteSpace(config.SourcePath))
        {
            throw new InvalidOperationException($"Seed block '{context.Block.Name}' has no source path configured.");
        }

        return Task.FromResult(new BlockExecutionResult(ReadAsync(config, context, cancellationToken)));
    }

    private async IAsyncEnumerable<DatasetRow> ReadAsync(
        SeedBlockConfig config,
        BlockExecutionContext context,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (config.FileType == DataFileType.Jsonl && !config.UseIndexCache)
        {
            await foreach (var row in ReadJsonlStreamingAsync(config, context, ct))
            {
                yield return row;
            }

            yield break;
        }

        await using var reader = DatasetIO.CreateReader(config.FileType, _settings);
        await reader.OpenAsync(config.SourcePath, ct);
        context.Log($"Indexed {reader.TotalRows} {config.FileType} row(s) from '{reader.SourcePath}'.");

        for (var start = 0L; start < reader.TotalRows; start += PageSize)
        {
            var page = await reader.ReadPageAsync(start, PageSize, ct);
            foreach (var row in page)
            {
                ct.ThrowIfCancellationRequested();
                context.RowsProcessed?.Report(row.LineNumber + 1);
                yield return row;
            }
        }
    }

    private static async IAsyncEnumerable<DatasetRow> ReadJsonlStreamingAsync(
        SeedBlockConfig config,
        BlockExecutionContext context,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await using var stream = new FileStream(
            config.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var textReader = new StreamReader(
            stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 65536);

        var index = 0L;
        string? lineText;
        while ((lineText = await textReader.ReadLineAsync(ct)) is not null)
        {
            yield return new DatasetRow(index++, lineText);
            context.RowsProcessed?.Report(index);
        }
    }
}
