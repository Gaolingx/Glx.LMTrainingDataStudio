using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Services.Executors;

/// <summary>
/// Emits user queries from configuration. Supports one repeated query or multiple query groups.
/// </summary>
public sealed class QuerySourceBlockExecutor : IBlockExecutor
{
    public BlockType Type => BlockType.QuerySource;

    public Task<BlockExecutionResult> ExecuteAsync(
        BlockExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (context.Block.Config is not QuerySourceBlockConfig config)
        {
            throw new InvalidOperationException(
                $"Query source block '{context.Block.Name}' has an invalid configuration.");
        }

        var queries = config.Queries.Where(query => !string.IsNullOrWhiteSpace(query)).ToArray();
        if (queries.Length == 0)
        {
            throw new InvalidOperationException(
                $"Query source block '{context.Block.Name}' has no queries configured.");
        }

        if (config.Repeat < 1)
        {
            throw new InvalidOperationException(
                $"Query source block '{context.Block.Name}' repeat must be at least 1; got {config.Repeat}.");
        }

        return Task.FromResult(new BlockExecutionResult(
            GenerateAsync(context, config, queries, cancellationToken)));
    }

    private static async IAsyncEnumerable<DatasetRow> GenerateAsync(
        BlockExecutionContext context,
        QuerySourceBlockConfig config,
        string[] queries,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var sequence = config.Mode == QuerySetMode.SameQuery
            ? Enumerable.Repeat(queries[0], config.Repeat)
            : queries.SelectMany(query => Enumerable.Repeat(query, config.Repeat));

        var index = 0L;
        foreach (var query in sequence)
        {
            ct.ThrowIfCancellationRequested();

            var row = new JsonObject { ["query"] = query };
            if (!string.IsNullOrWhiteSpace(config.SystemPrompt))
            {
                row["system"] = config.SystemPrompt;
            }

            yield return new DatasetRow(index++, row.ToJsonString());
            context.RowsProcessed?.Report(index);
        }

        context.Log($"Emitted {index} query row(s) using mode '{config.Mode}'.");
        await Task.CompletedTask;
    }
}
