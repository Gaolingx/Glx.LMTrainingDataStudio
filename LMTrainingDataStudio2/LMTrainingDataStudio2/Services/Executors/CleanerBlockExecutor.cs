using System.Runtime.CompilerServices;
using System.Text.Json;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services.Simulation;

namespace LMTrainingDataStudio2.Services.Executors;

/// <summary>
/// Applies regex and formatting cleanup to OAI message rows.
/// Rows that are not JSON objects are passed through unchanged.
/// </summary>
public sealed class CleanerBlockExecutor : IBlockExecutor
{
    public BlockType Type => BlockType.Cleaner;

    public Task<BlockExecutionResult> ExecuteAsync(
        BlockExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (context.Block.Config is not CleanerBlockConfig config)
        {
            throw new InvalidOperationException(
                $"Cleaner block '{context.Block.Name}' has an invalid configuration.");
        }

        if (context.Input is null)
        {
            throw new InvalidOperationException(
                $"Cleaner block '{context.Block.Name}' requires an input connection.");
        }

        return Task.FromResult(new BlockExecutionResult(
            CleanAsync(context, config, cancellationToken)));
    }

    private static async IAsyncEnumerable<DatasetRow> CleanAsync(
        BlockExecutionContext context,
        CleanerBlockConfig config,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var outputIndex = 0L;
        var changedRows = 0L;

        await foreach (var row in context.Input!.WithCancellation(ct))
        {
            string json;
            try
            {
                json = TrajectoryCleaner.CleanJson(row.Json, config.Rules);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException(
                    $"Cleaner block '{context.Block.Name}' could not parse line {row.LineNumber}: {ex.Message}",
                    ex);
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidOperationException(
                    $"Cleaner block '{context.Block.Name}' failed on line {row.LineNumber}: {ex.Message}",
                    ex);
            }

            if (json != row.Json)
            {
                changedRows++;
            }

            yield return new DatasetRow(outputIndex++, json);
            context.RowsProcessed?.Report(outputIndex);
        }

        context.Log($"Cleaned {outputIndex} row(s), {changedRows} changed.");
    }
}
