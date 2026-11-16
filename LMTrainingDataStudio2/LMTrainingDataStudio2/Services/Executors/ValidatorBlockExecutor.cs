using System.Runtime.CompilerServices;
using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Services.Executors;

/// <summary>
/// Filters rows with a minimal expression rule. Rejected rows are dropped until
/// the editor models a dedicated reject output port.
/// </summary>
public sealed class ValidatorBlockExecutor : IBlockExecutor
{
    public BlockType Type => BlockType.Validator;

    public Task<BlockExecutionResult> ExecuteAsync(BlockExecutionContext context, CancellationToken cancellationToken)
    {
        if (context.Block.Config is not ValidatorBlockConfig config)
        {
            throw new InvalidOperationException(
                $"Validator block '{context.Block.Name}' has an invalid configuration.");
        }

        if (context.Input is null)
        {
            throw new InvalidOperationException(
                $"Validator block '{context.Block.Name}' requires an input connection.");
        }

        if (string.IsNullOrWhiteSpace(config.Rule))
        {
            throw new InvalidOperationException($"Validator block '{context.Block.Name}' has no rule configured.");
        }

        if (!string.Equals(config.RuleKind, "expression", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                $"Validator rule kind '{config.RuleKind}' is not supported; use 'expression'.");
        }

        return Task.FromResult(new BlockExecutionResult(FilterAsync(context, config, cancellationToken)));
    }

    private static async IAsyncEnumerable<DatasetRow> FilterAsync(
        BlockExecutionContext context,
        ValidatorBlockConfig config,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var outputIndex = 0L;
        var rejected = 0L;

        await foreach (var row in context.Input!.WithCancellation(ct))
        {
            bool passed;
            try
            {
                using var document = JsonRowReader.Parse(row, context.Block.Name);
                passed = PredicateRuleEvaluator.Evaluate(config.Rule, document.RootElement);
            }
            catch (InvalidDataException)
            {
                passed = false;
            }

            if (passed)
            {
                yield return new DatasetRow(outputIndex++, row.Json);
            }
            else
            {
                rejected++;
            }

            context.RowsProcessed?.Report(outputIndex + rejected);
        }

        context.Log($"Passed {outputIndex}, rejected {rejected} row(s).");
    }
}