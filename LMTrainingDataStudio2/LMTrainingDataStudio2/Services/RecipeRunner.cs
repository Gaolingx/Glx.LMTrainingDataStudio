using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services.Executors;

namespace LMTrainingDataStudio2.Services;

/// <summary>
/// Executes recipes layer by layer. Blocks inside a layer run concurrently up to
/// <see cref="RecipeRunOptions.EffectiveMaxParallelism"/>; the first failed block
/// cancels its siblings and skips downstream blocks.
/// </summary>
public sealed class RecipeRunner
{
    private readonly RecipeValidationService _validation;
    private readonly Dictionary<BlockType, IBlockExecutor> _executors;
    private readonly TimeProvider _timeProvider;
    private readonly RecipeRunOptions _options;

    public RecipeRunner(
        RecipeValidationService validation,
        IEnumerable<IBlockExecutor> executors,
        TimeProvider? timeProvider = null,
        RecipeRunOptions? options = null)
    {
        _validation = validation;
        _executors = executors.ToDictionary(executor => executor.Type);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _options = options ?? new RecipeRunOptions();
    }

    public async Task<ExecutionRun> RunAsync(
        Recipe recipe,
        IProgress<ExecutionProgress>? progress = null,
        CancellationToken cancellationToken = default,
        ExecutionPauseController? pause = null)
    {
        var run = new ExecutionRun
        {
            RecipeId = recipe.Id,
            Status = RunStatus.Running,
            StartedAt = _timeProvider.GetUtcNow()
        };
        PrepareOutputDirectory(run);
        Log(run, $"Run {run.RunId} started for recipe '{recipe.Name}'.");

        if (cancellationToken.IsCancellationRequested)
        {
            return FinishCancelled(run, Array.Empty<NodeExecutionResult>(), "Run cancelled before execution.");
        }

        var validation = _validation.Validate(recipe);
        foreach (var issue in validation.Issues)
        {
            Log(run, $"[{issue.Severity}] {issue.Message}");
        }

        if (!validation.IsValid)
        {
            run.Status = RunStatus.Failed;
            run.FinishedAt = _timeProvider.GetUtcNow();
            Log(run, "Run rejected: recipe has validation errors.");
            WriteSummary(run);
            return run;
        }

        var layers = RecipeLayoutService.BuildLayers(recipe);
        if (layers.Sum(layer => layer.Count) != recipe.Blocks.Count)
        {
            run.Status = RunStatus.Failed;
            run.FinishedAt = _timeProvider.GetUtcNow();
            Log(run, "Run rejected: recipe contains a cycle.");
            WriteSummary(run);
            return run;
        }

        var results = recipe.Blocks.ToDictionary(block => block.Id, block => new NodeExecutionResult
        {
            BlockId = block.Id,
            BlockName = block.Name
        });
        run.Nodes.AddRange(recipe.Blocks.Select(block => results[block.Id]));

        var outputs = new ConcurrentDictionary<string, List<DatasetRow>>();
        var state = new RunState();
        var totalBlocks = recipe.Blocks.Count;
        using var parallelism = new SemaphoreSlim(_options.EffectiveMaxParallelism);

        foreach (var layer in layers)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return FinishCancelled(run, results.Values, "Run cancelled before a layer started.");
            }

            if (pause is { IsPaused: true })
            {
                Log(run, "Run paused; waiting to resume.");
                await pause.WaitIfPausedAsync(cancellationToken);
                Log(run, "Run resumed.");
            }

            using var layerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var tasks = layer.Select(block => ExecuteBlockAsync(
                block,
                recipe,
                outputs,
                run,
                results,
                state,
                parallelism,
                pause,
                progress,
                totalBlocks,
                layerCancellation,
                layerCancellation.Token));

            var outcomes = await Task.WhenAll(tasks);

            if (cancellationToken.IsCancellationRequested)
            {
                return FinishCancelled(run, results.Values, "Run cancelled.");
            }

            var failure = outcomes.FirstOrDefault(outcome => outcome.Kind == BlockOutcomeKind.Failed);
            if (failure is not null)
            {
                return FinishFailed(
                    run, results.Values, $"Run failed at block '{failure.Block.Name}'.");
            }

            if (outcomes.Any(outcome => outcome.Kind == BlockOutcomeKind.Cancelled))
            {
                return FinishCancelled(run, results.Values, "Run cancelled by a block.");
            }
        }

        run.Status = RunStatus.Succeeded;
        run.FinishedAt = _timeProvider.GetUtcNow();
        Log(run, $"Run completed: {state.CompletedBlocks} block(s), {state.ProcessedRows} row(s).");
        WriteSummary(run);
        return run;
    }

    private async Task<BlockOutcome> ExecuteBlockAsync(
        RecipeBlock block,
        Recipe recipe,
        ConcurrentDictionary<string, List<DatasetRow>> outputs,
        ExecutionRun run,
        IReadOnlyDictionary<string, NodeExecutionResult> results,
        RunState state,
        SemaphoreSlim parallelism,
        ExecutionPauseController? pause,
        IProgress<ExecutionProgress>? progress,
        int totalBlocks,
        CancellationTokenSource layerCancellation,
        CancellationToken cancellationToken)
    {
        await parallelism.WaitAsync(cancellationToken);
        try
        {
            if (pause is not null)
            {
                await pause.WaitIfPausedAsync(cancellationToken);
            }

            var result = results[block.Id];
            var inputEdges = recipe.Edges.Where(edge => edge.TargetBlockId == block.Id).ToList();
            var inputRows = inputEdges.Sum(edge =>
                outputs.TryGetValue(edge.SourceBlockId, out var rows) ? rows.Count : 0);

            result.Status = ExecutionStatus.Running;
            result.StartedAt = _timeProvider.GetUtcNow();
            result.InputRows = inputRows;
            progress?.Report(new ExecutionProgress(
                block.Id, block.Name, state.CompletedBlocks, totalBlocks, state.ProcessedRows));

            for (var attempt = 0; ; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (!_executors.TryGetValue(block.Type, out var executor))
                    {
                        throw new NotSupportedException(
                            $"No executor is registered for block type '{block.Type}'.");
                    }

                    var context = new BlockExecutionContext
                    {
                        Block = block,
                        Input = BuildInput(inputEdges, outputs),
                        Log = message => Log(run, $"[{block.Name}] {message}"),
                        RowsProcessed = new InlineProgress<long>(rows =>
                            progress?.Report(new ExecutionProgress(
                                block.Id,
                                block.Name,
                                state.CompletedBlocks,
                                totalBlocks,
                                state.ProcessedRows + rows))),
                        ToolProfile = ToolProfileResolver.Resolve(recipe, block)
                    };

                    var execution = await executor.ExecuteAsync(context, cancellationToken);
                    var outputRows = new List<DatasetRow>();

                    await foreach (var row in execution.Output.WithCancellation(cancellationToken))
                    {
                        outputRows.Add(row);
                    }

                    outputs[block.Id] = outputRows;

                    result.Status = ExecutionStatus.Succeeded;
                    result.OutputRows = outputRows.Count;
                    result.FinishedAt = _timeProvider.GetUtcNow();
                    Interlocked.Increment(ref state.CompletedBlocks);
                    Interlocked.Add(ref state.ProcessedRows, outputRows.Count);
                    Log(run, $"[{block.Name}] Completed: {inputRows} in, {outputRows.Count} out.");
                    progress?.Report(new ExecutionProgress(
                        block.Id, block.Name, state.CompletedBlocks, totalBlocks, state.ProcessedRows));
                    return new BlockOutcome(block, BlockOutcomeKind.Succeeded);
                }
                catch (OperationCanceledException)
                {
                    result.Status = ExecutionStatus.Cancelled;
                    result.FinishedAt = _timeProvider.GetUtcNow();
                    return new BlockOutcome(block, BlockOutcomeKind.Cancelled);
                }
                catch (Exception ex)
                {
                    if (attempt < _options.EffectiveRetryCount && !cancellationToken.IsCancellationRequested)
                    {
                        Log(run,
                            $"[{block.Name}] Attempt {attempt + 1} failed: {ex.Message}. Retrying...");
                        continue;
                    }

                    result.Status = ExecutionStatus.Failed;
                    result.Error = ex.Message;
                    result.FinishedAt = _timeProvider.GetUtcNow();
                    Log(run, $"[{block.Name}] Failed: {ex.Message}");
                    layerCancellation.Cancel();
                    return new BlockOutcome(block, BlockOutcomeKind.Failed);
                }
            }
        }
        finally
        {
            parallelism.Release();
        }
    }

    private static IAsyncEnumerable<DatasetRow>? BuildInput(
        IReadOnlyList<RecipeEdge> inputEdges,
        IReadOnlyDictionary<string, List<DatasetRow>> outputs)
    {
        var sources = inputEdges
            .Select(edge => outputs.TryGetValue(edge.SourceBlockId, out var rows) ? rows : null)
            .Where(rows => rows is not null)
            .Select(rows => rows!)
            .ToList();

        if (sources.Count == 0)
        {
            return null;
        }

        return ConcatAsync(sources);
    }

    private static async IAsyncEnumerable<DatasetRow> ConcatAsync(
        IEnumerable<List<DatasetRow>> sources,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Yield();

        foreach (var source in sources)
        {
            foreach (var row in source)
            {
                ct.ThrowIfCancellationRequested();
                yield return row;
            }
        }
    }

    private ExecutionRun FinishCancelled(
        ExecutionRun run, IEnumerable<NodeExecutionResult> results, string reason)
    {
        MarkPendingSkipped(results, reason);
        run.Status = RunStatus.Cancelled;
        run.FinishedAt = _timeProvider.GetUtcNow();
        Log(run, reason);
        WriteSummary(run);
        return run;
    }

    private ExecutionRun FinishFailed(
        ExecutionRun run, IEnumerable<NodeExecutionResult> results, string reason)
    {
        MarkPendingSkipped(results, reason);
        run.Status = RunStatus.Failed;
        run.FinishedAt = _timeProvider.GetUtcNow();
        Log(run, reason);
        WriteSummary(run);
        return run;
    }

    private static void MarkPendingSkipped(IEnumerable<NodeExecutionResult> results, string reason)
    {
        foreach (var result in results)
        {
            if (result.Status != ExecutionStatus.Pending) continue;

            result.Status = ExecutionStatus.Skipped;
            result.Error = reason;
        }
    }

    private void PrepareOutputDirectory(ExecutionRun run)
    {
        if (string.IsNullOrWhiteSpace(_options.OutputDirectory)) return;

        try
        {
            var directory = Path.Combine(Path.GetFullPath(_options.OutputDirectory), run.RunId);
            Directory.CreateDirectory(directory);
            run.OutputDirectory = directory;
            Log(run, $"Output directory: {directory}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Log(run, $"[Warning] Could not create output directory: {ex.Message}");
        }
    }

    private static void WriteSummary(ExecutionRun run)
    {
        if (run.OutputDirectory is null) return;

        try
        {
            var summary = new
            {
                run.RunId,
                run.RecipeId,
                Status = run.Status.ToString(),
                run.StartedAt,
                run.FinishedAt,
                Nodes = run.Nodes.Select(node => new
                {
                    node.BlockId,
                    node.BlockName,
                    Status = node.Status.ToString(),
                    node.InputRows,
                    node.OutputRows,
                    node.Error
                })
            };

            File.WriteAllText(
                Path.Combine(run.OutputDirectory, "run-summary.json"),
                JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log(run, $"[Warning] Could not write run summary: {ex.Message}");
        }
    }

    private static void Log(ExecutionRun run, string message)
    {
        lock (run.Logs)
        {
            run.Logs.Add(message);
        }
    }

    private sealed class RunState
    {
        public int CompletedBlocks;
        public long ProcessedRows;
    }

    private enum BlockOutcomeKind
    {
        Succeeded,
        Failed,
        Cancelled
    }

    private sealed record BlockOutcome(RecipeBlock Block, BlockOutcomeKind Kind);

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}