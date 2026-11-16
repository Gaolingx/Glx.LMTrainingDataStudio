using System.Runtime.CompilerServices;
using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Services.Executors;

/// <summary>
/// Deterministic sampler with reservoir sampling for count, Bernoulli sampling for ratio,
/// and multi-file deterministic splits.
/// </summary>
public sealed class SamplerBlockExecutor : IBlockExecutor
{
    public BlockType Type => BlockType.Sampler;

    public Task<BlockExecutionResult> ExecuteAsync(BlockExecutionContext context, CancellationToken cancellationToken)
    {
        if (context.Block.Config is not SamplerBlockConfig config)
        {
            throw new InvalidOperationException(
                $"Sampler block '{context.Block.Name}' has an invalid configuration.");
        }

        if (context.Input is null)
        {
            throw new InvalidOperationException(
                $"Sampler block '{context.Block.Name}' requires an input connection.");
        }

        SamplerSplitPlan? splitPlan = null;
        if (config.Splits is { Length: > 0 })
        {
            if (string.IsNullOrWhiteSpace(config.OutputDirectory))
            {
                throw new InvalidOperationException(
                    $"Sampler block '{context.Block.Name}' requires an output directory when splits are configured.");
            }

            if (!SamplerSplitPlan.TryParse(config.Splits, out splitPlan, out var splitError))
            {
                throw new InvalidOperationException(
                    $"Sampler block '{context.Block.Name}' has invalid splits: {splitError}");
            }
        }
        else
        {
            if (config.Count is null && config.Ratio is null)
            {
                throw new InvalidOperationException(
                    $"Sampler block '{context.Block.Name}' has neither count nor ratio configured.");
            }

            if (config.Count is < 0)
            {
                throw new InvalidOperationException($"Sampler block '{context.Block.Name}' has a negative count.");
            }

            if (config.Ratio is <= 0 or > 1)
            {
                throw new InvalidOperationException(
                    $"Sampler block '{context.Block.Name}' ratio must be greater than 0 and at most 1.");
            }
        }

        return Task.FromResult(new BlockExecutionResult(
            SampleAsync(context, config, splitPlan, cancellationToken)));
    }

    private static async IAsyncEnumerable<DatasetRow> SampleAsync(
        BlockExecutionContext context,
        SamplerBlockConfig config,
        SamplerSplitPlan? splitPlan,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (splitPlan is not null)
        {
            await foreach (var row in SplitAsync(context, config, splitPlan, ct))
            {
                yield return row;
            }

            yield break;
        }

        var random = new Random(config.Seed);

        if (config.Count is { } count)
        {
            var reservoir = new List<DatasetRow>((int)Math.Min(count, int.MaxValue));
            var seen = 0L;

            await foreach (var row in context.Input!.WithCancellation(ct))
            {
                seen++;
                if (reservoir.Count < count)
                {
                    reservoir.Add(row);
                }
                else
                {
                    var slot = random.NextInt64(seen);
                    if (slot < count)
                    {
                        reservoir[(int)slot] = row;
                    }
                }

                context.RowsProcessed?.Report(seen);
            }

            foreach (var row in reservoir)
            {
                yield return row;
            }

            yield break;
        }

        var ratio = config.Ratio!.Value;
        var index = 0L;

        await foreach (var row in context.Input!.WithCancellation(ct))
        {
            index++;
            if (random.NextDouble() < ratio)
            {
                yield return row;
            }

            context.RowsProcessed?.Report(index);
        }
    }

    private static async IAsyncEnumerable<DatasetRow> SplitAsync(
        BlockExecutionContext context,
        SamplerBlockConfig config,
        SamplerSplitPlan plan,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await using var writer = SamplerSplitWriter.Create(config.OutputDirectory!, plan.Specs);

        var random = new Random(config.Seed);
        var counts = new long[plan.Specs.Count];
        var seen = 0L;
        var outputIndex = 0L;

        await foreach (var row in context.Input!.WithCancellation(ct))
        {
            var split = plan.Assign(seen, random);
            counts[split]++;
            await writer.WriteAsync(split, row, ct);

            seen++;
            context.RowsProcessed?.Report(seen);
            yield return new DatasetRow(outputIndex++, row.Json);
        }

        await writer.CommitAsync(ct);

        var summary = string.Join(
            ", ",
            plan.Specs.Select((spec, index) => $"{spec.Name}={counts[index]}"));
        context.Log($"Split {seen} row(s) into {plan.Specs.Count} file(s): {summary}.");
    }
}