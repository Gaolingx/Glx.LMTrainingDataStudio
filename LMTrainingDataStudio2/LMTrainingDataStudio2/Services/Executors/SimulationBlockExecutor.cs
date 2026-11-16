using System.Runtime.CompilerServices;
using System.Text.Json;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services.Simulation;

namespace LMTrainingDataStudio2.Services.Executors;

/// <summary>
/// Runs the mini harness per query row and emits OpenAI chat-message samples.
/// </summary>
public sealed class SimulationBlockExecutor : IBlockExecutor
{
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public SimulationBlockExecutor(Func<TimeSpan, CancellationToken, Task>? delay = null) =>
        _delay = delay ?? ((duration, token) => Task.Delay(duration, token));

    public BlockType Type => BlockType.Simulation;

    public Task<BlockExecutionResult> ExecuteAsync(
        BlockExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (context.Block.Config is not SimulationBlockConfig config)
        {
            throw new InvalidOperationException(
                $"Simulation block '{context.Block.Name}' has an invalid configuration.");
        }

        if (context.Input is null)
        {
            throw new InvalidOperationException(
                $"Simulation block '{context.Block.Name}' requires an input connection.");
        }

        if (string.IsNullOrWhiteSpace(config.ModelAlias))
        {
            throw new InvalidOperationException(
                $"Simulation block '{context.Block.Name}' has no model alias configured.");
        }

        return Task.FromResult(new BlockExecutionResult(
            GenerateAsync(context, config, cancellationToken)));
    }

    private async IAsyncEnumerable<DatasetRow> GenerateAsync(
        BlockExecutionContext context,
        SimulationBlockConfig config,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var outputIndex = 0L;
        var inputIndex = 0L;

        await foreach (var row in context.Input!.WithCancellation(ct))
        {
            var (query, systemPrompt) = ReadQuery(row, context.Block.Name, config.SystemPrompt);

            var harness = new SimulationHarness(
                new ScriptedChatClient(config.Script, config.FinalContent),
                new SimulatedToolRuntime(_delay),
                _delay);

            context.Log($"Row {row.LineNumber}: simulating query '{Truncate(query)}'.");
            var trajectory = await harness.RunAsync(
                query,
                config,
                systemPrompt,
                message => context.Log($"Row {row.LineNumber}: {message}"),
                ct);

            inputIndex++;
            context.RowsProcessed?.Report(inputIndex);

            if (trajectory.Status != TrajectoryStatus.Completed)
            {
                if (!config.EmitIncompleteTrajectories)
                {
                    context.Log(
                        $"Row {row.LineNumber}: skipped {trajectory.Status} trajectory: {trajectory.Error}");
                    continue;
                }

                context.Log(
                    $"Row {row.LineNumber}: emitting {trajectory.Status} trajectory: {trajectory.Error}");
            }

            yield return new DatasetRow(outputIndex++, TrajectorySerializer.ToOaiJson(trajectory));
        }

        context.Log($"Simulated {inputIndex} query row(s), emitted {outputIndex} trajectory row(s).");
    }

    private static (string Query, string? SystemPrompt) ReadQuery(
        DatasetRow row,
        string blockName,
        string? configuredSystemPrompt)
    {
        using var document = JsonRowReader.Parse(row, blockName);
        var root = document.RootElement;

        string? query = null;
        string? systemPrompt = null;

        if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("query", out var queryValue) &&
                queryValue.ValueKind == JsonValueKind.String)
            {
                query = queryValue.GetString();
            }
            else if (root.TryGetProperty("prompt", out var promptValue) &&
                     promptValue.ValueKind == JsonValueKind.String)
            {
                query = promptValue.GetString();
            }

            if (root.TryGetProperty("system", out var systemValue) &&
                systemValue.ValueKind == JsonValueKind.String)
            {
                systemPrompt = systemValue.GetString();
            }
        }
        else if (root.ValueKind == JsonValueKind.String)
        {
            query = root.GetString();
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            throw new InvalidDataException(
                $"Simulation block '{blockName}' row {row.LineNumber} has no 'query' string field.");
        }

        return (query, string.IsNullOrWhiteSpace(systemPrompt) ? configuredSystemPrompt : systemPrompt);
    }

    private static string Truncate(string value) =>
        value.Length <= 60 ? value : value[..57] + "...";
}
