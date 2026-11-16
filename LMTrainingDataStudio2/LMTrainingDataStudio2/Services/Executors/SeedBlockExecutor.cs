using System.Runtime.CompilerServices;
using System.Text;
using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Services.Executors;

/// <summary>
/// Streams rows from a JSONL source, using the index cache when enabled.
/// </summary>
public sealed class SeedBlockExecutor : IBlockExecutor
{
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

        if (config.FileType != DataFileType.Jsonl)
        {
            throw new NotSupportedException(
                $"Seed block '{context.Block.Name}' supports JSONL only; '{config.FileType}' is not implemented.");
        }

        return Task.FromResult(new BlockExecutionResult(ReadAsync(config, context, cancellationToken)));
    }

    private async IAsyncEnumerable<DatasetRow> ReadAsync(
        SeedBlockConfig config,
        BlockExecutionContext context,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (config.UseIndexCache)
        {
            await using var reader = new JsonlDatasetReader(_settings);
            await reader.OpenAsync(config.SourcePath, ct);
            context.Log($"Indexed {reader.TotalLines} line(s) from '{reader.SourcePath}'.");

            for (var line = 0L; line < reader.TotalLines; line++)
            {
                var row = await reader.ReadLineAsync(line, ct);
                if (row is null) continue;

                context.RowsProcessed?.Report(line + 1);
                yield return row;
            }

            yield break;
        }

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