using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Services.Executors;

/// <summary>
/// Deterministic sampler with reservoir sampling for count, Bernoulli sampling for ratio,
/// and multi-file deterministic splits. Count, ratio and splits can be stratified by a
/// top-level field.
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

        if (splitPlan is not null)
        {
            return Task.FromResult(CreateSplitOutputs(context, config, splitPlan, cancellationToken));
        }

        return Task.FromResult(new BlockExecutionResult(SampleAsync(context, config, cancellationToken)));
    }

    private static BlockExecutionResult CreateSplitOutputs(
        BlockExecutionContext context,
        SamplerBlockConfig config,
        SamplerSplitPlan plan,
        CancellationToken ct)
    {
        var connected = context.ConnectedOutputPorts;
        var hasSplitNamedOutput = plan.Specs.Any(spec =>
            spec.Name.Equals("output", StringComparison.OrdinalIgnoreCase));
        var emitPrimary = !hasSplitNamedOutput &&
                          (connected is null || connected.Contains("output", StringComparer.OrdinalIgnoreCase));

        var primary = emitPrimary
            ? Channel.CreateUnbounded<DatasetRow>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true
            })
            : null;

        var splitChannels = new Dictionary<int, Channel<DatasetRow>>();
        var additional = new Dictionary<string, IAsyncEnumerable<DatasetRow>>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < plan.Specs.Count; i++)
        {
            var spec = plan.Specs[i];
            if (connected is not null &&
                !connected.Contains(spec.Name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var channel = Channel.CreateUnbounded<DatasetRow>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true
            });
            splitChannels[i] = channel;
            additional[spec.Name] = channel.Reader.ReadAllAsync(ct);
        }

        var completion = ProduceSplitsAsync(context, config, plan, primary?.Writer, splitChannels, ct);

        var output = primary is null ? EmptyAsync(ct) : primary.Reader.ReadAllAsync(ct);
        return new BlockExecutionResult(output, additional, completion);
    }

    private static async IAsyncEnumerable<DatasetRow> EmptyAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask;
        ct.ThrowIfCancellationRequested();
        yield break;
    }

    private static async Task ProduceSplitsAsync(
        BlockExecutionContext context,
        SamplerBlockConfig config,
        SamplerSplitPlan plan,
        ChannelWriter<DatasetRow>? primary,
        IReadOnlyDictionary<int, Channel<DatasetRow>> splitChannels,
        CancellationToken ct)
    {
        await using var writer = SamplerSplitWriter.Create(config.OutputDirectory!, plan.Specs);

        var random = new Random(config.Seed);
        var groupRandoms = new Dictionary<string, Random>(StringComparer.Ordinal);
        var counts = new long[plan.Specs.Count];
        var outputIndex = 0L;
        var seen = 0L;

        try
        {
            await foreach (var row in context.Input!.WithCancellation(ct))
            {
                var split = plan.Assign(seen, ResolveRandom(config, random, groupRandoms, row));
                counts[split]++;
                await writer.WriteAsync(split, row, ct);

                var emitted = new DatasetRow(outputIndex++, row.Json);
                if (primary is not null)
                {
                    await primary.WriteAsync(emitted, ct);
                }

                if (splitChannels.TryGetValue(split, out var channel))
                {
                    await channel.Writer.WriteAsync(emitted, ct);
                }

                seen++;
                context.RowsProcessed?.Report(seen);
            }

            await writer.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            primary?.TryComplete(ex);
            foreach (var channel in splitChannels.Values)
            {
                channel.Writer.TryComplete(ex);
            }

            context.Log($"Split failed after {seen} row(s): {ex.Message}");
            throw;
        }

        primary?.TryComplete();
        foreach (var channel in splitChannels.Values)
        {
            channel.Writer.TryComplete();
        }

        var summary = string.Join(
            ", ",
            plan.Specs.Select((spec, index) => $"{spec.Name}={counts[index]}"));
        var stratified = string.IsNullOrWhiteSpace(config.StratifyField)
            ? string.Empty
            : $" (stratified by '{config.StratifyField}')";
        context.Log($"Split {seen} row(s) into {plan.Specs.Count} file(s){stratified}: {summary}.");
    }

    private static Random ResolveRandom(
        SamplerBlockConfig config,
        Random shared,
        Dictionary<string, Random> groupRandoms,
        DatasetRow row)
    {
        if (string.IsNullOrWhiteSpace(config.StratifyField))
        {
            return shared;
        }

        var group = ReadGroupValue(row.Json, config.StratifyField!);
        if (!groupRandoms.TryGetValue(group, out var random))
        {
            random = new Random(StableGroupSeed(config.Seed, group));
            groupRandoms[group] = random;
        }

        return random;
    }

    private static string ReadGroupValue(string json, string field)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty(field, out var value))
            {
                return value.ValueKind switch
                {
                    JsonValueKind.String => value.GetString() ?? string.Empty,
                    JsonValueKind.Number => value.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Null => "(null)",
                    _ => value.GetRawText()
                };
            }
        }
        catch (JsonException)
        {
        }

        return "(missing)";
    }

    private static async IAsyncEnumerable<DatasetRow> SampleAsync(
        BlockExecutionContext context,
        SamplerBlockConfig config,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var random = new Random(config.Seed);

        if (config.Count is { } count)
        {
            if (!string.IsNullOrWhiteSpace(config.StratifyField))
            {
                await foreach (var row in SampleStratifiedAsync(context, config, count, ct))
                {
                    yield return row;
                }

                yield break;
            }

            var reservoir = new List<DatasetRow>((int)Math.Min(count, int.MaxValue));
            var seen = 0L;

            await foreach (var row in context.Input!.WithCancellation(ct))
            {
                seen++;
                if (reservoir.Count < count)
                {
                    reservoir.Add(row);
                }
                else if (count > 0)
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
        var groupRandoms = new Dictionary<string, Random>(StringComparer.Ordinal);
        var index = 0L;

        await foreach (var row in context.Input!.WithCancellation(ct))
        {
            index++;
            var groupRandom = ResolveRandom(config, random, groupRandoms, row);
            if (groupRandom.NextDouble() < ratio)
            {
                yield return row;
            }

            context.RowsProcessed?.Report(index);
        }
    }

    private static async IAsyncEnumerable<DatasetRow> SampleStratifiedAsync(
        BlockExecutionContext context,
        SamplerBlockConfig config,
        long count,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var groups = new Dictionary<string, StratifiedGroup>(StringComparer.Ordinal);
        var seen = 0L;

        await foreach (var row in context.Input!.WithCancellation(ct))
        {
            seen++;
            var group = ReadGroupValue(row.Json, config.StratifyField!);
            if (!groups.TryGetValue(group, out var state))
            {
                state = new StratifiedGroup(new Random(StableGroupSeed(config.Seed, group)));
                groups.Add(group, state);
            }

            state.Seen++;
            if (state.Reservoir.Count < count)
            {
                state.Reservoir.Add(row);
            }
            else if (count > 0)
            {
                var slot = state.Random.NextInt64(state.Seen);
                if (slot < count)
                {
                    state.Reservoir[(int)slot] = row;
                }
            }

            context.RowsProcessed?.Report(seen);
        }

        if (count <= 0 || seen == 0)
        {
            yield break;
        }

        var quotas = AllocateCountQuotas(groups, count, seen);
        var selected = new List<DatasetRow>();
        foreach (var (group, state) in groups)
        {
            var take = (int)Math.Min(quotas[group], state.Reservoir.Count);
            for (var index = 0; index < take; index++)
            {
                selected.Add(state.Reservoir[index]);
            }
        }

        selected.Sort((left, right) => left.LineNumber.CompareTo(right.LineNumber));
        foreach (var row in selected)
        {
            yield return row;
        }

        context.Log(
            $"Sampled {selected.Count:N0} row(s) from {groups.Count:N0} group(s) " +
            $"(stratified by '{config.StratifyField}', seed {config.Seed}).");
    }

    private static Dictionary<string, long> AllocateCountQuotas(
        Dictionary<string, StratifiedGroup> groups,
        long count,
        long seen)
    {
        var quotas = new Dictionary<string, long>(groups.Count, StringComparer.Ordinal);
        var allocated = 0L;

        foreach (var (group, state) in groups)
        {
            var exact = count >= seen ? state.Seen : (double)count * state.Seen / seen;
            var quota = (long)Math.Floor(exact);
            if (quota > state.Seen)
            {
                quota = state.Seen;
            }

            quotas[group] = quota;
            allocated += quota;
        }

        var remaining = Math.Min(count, seen) - allocated;
        if (remaining <= 0)
        {
            return quotas;
        }

        var ordered = groups
            .Select(pair => new
            {
                pair.Key,
                Fraction = (count >= seen ? 0 : (double)count * pair.Value.Seen / seen)
                           - Math.Floor((count >= seen ? 0 : (double)count * pair.Value.Seen / seen))
            })
            .OrderByDescending(entry => entry.Fraction)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => entry.Key)
            .ToArray();

        while (remaining > 0)
        {
            var assigned = false;
            foreach (var group in ordered)
            {
                if (remaining <= 0)
                {
                    break;
                }

                if (quotas[group] >= groups[group].Seen)
                {
                    continue;
                }

                quotas[group]++;
                remaining--;
                assigned = true;
            }

            if (!assigned)
            {
                break;
            }
        }

        return quotas;
    }

    private static int StableGroupSeed(int seed, string group)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var value in Encoding.UTF8.GetBytes(group))
            {
                hash ^= value;
                hash *= 16777619u;
            }

            hash ^= (uint)seed;
            return (int)(hash & 0x7FFFFFFF);
        }
    }

    private sealed class StratifiedGroup(Random random)
    {
        public Random Random { get; } = random;
        public long Seen { get; set; }
        public List<DatasetRow> Reservoir { get; } = new();
    }
}
