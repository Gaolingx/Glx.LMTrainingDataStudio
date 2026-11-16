using System.Runtime.CompilerServices;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services.Executors;

namespace LMTrainingDataStudio2.Services;

/// <summary>
/// Executes recipes layer by layer using registered block executors.
/// Fails fast: the first failing block stops the run and marks remaining blocks as skipped.
/// </summary>
public sealed class RecipeRunner
{
    private readonly RecipeValidationService _validation;
    private readonly Dictionary<BlockType, IBlockExecutor> _executors;
    private readonly TimeProvider _timeProvider;

    public RecipeRunner(
        RecipeValidationService validation,
        IEnumerable<IBlockExecutor> executors,
        TimeProvider? timeProvider = null)
    {
        _validation = validation;
        _executors = executors.ToDictionary(executor => executor.Type);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ExecutionRun> RunAsync(
        Recipe recipe,
        IProgress<ExecutionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var run = new ExecutionRun
        {
            RecipeId = recipe.Id,
            Status = RunStatus.Running,
            StartedAt = _timeProvider.GetUtcNow()
        };
        run.Logs.Add($"Run {run.RunId} started for recipe '{recipe.Name}'.");

        if (cancellationToken.IsCancellationRequested)
        {
            return FinishCancelled(run, Array.Empty<NodeExecutionResult>(), "Run cancelled before execution.");
        }

        var validation = _validation.Validate(recipe);
        foreach (var issue in validation.Issues)
        {
            run.Logs.Add($"[{issue.Severity}] {issue.Message}");
        }

        if (!validation.IsValid)
        {
            run.Status = RunStatus.Failed;
            run.FinishedAt = _timeProvider.GetUtcNow();
            run.Logs.Add("Run rejected: recipe has validation errors.");
            return run;
        }

        var layers = BuildLayers(recipe);
        if (layers.Sum(layer => layer.Count) != recipe.Blocks.Count)
        {
            run.Status = RunStatus.Failed;
            run.FinishedAt = _timeProvider.GetUtcNow();
            run.Logs.Add("Run rejected: recipe contains a cycle.");
            return run;
        }

        var results = recipe.Blocks.ToDictionary(block => block.Id, block => new NodeExecutionResult
        {
            BlockId = block.Id,
            BlockName = block.Name
        });
        run.Nodes.AddRange(recipe.Blocks.Select(block => results[block.Id]));

        var outputs = new Dictionary<string, List<DatasetRow>>();
        var completedBlocks = 0;
        long processedRows = 0;
        var totalBlocks = recipe.Blocks.Count;

        foreach (var layer in layers)
        {
            foreach (var block in layer)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return FinishCancelled(run, results.Values, $"Run cancelled at block '{block.Name}'.");
                }

                var result = results[block.Id];
                var inputEdges = recipe.Edges.Where(edge => edge.TargetBlockId == block.Id).ToList();
                var inputRows = inputEdges.Sum(edge =>
                    outputs.TryGetValue(edge.SourceBlockId, out var rows) ? rows.Count : 0);

                result.Status = ExecutionStatus.Running;
                result.StartedAt = _timeProvider.GetUtcNow();
                result.InputRows = inputRows;
                progress?.Report(new ExecutionProgress(
                    block.Id, block.Name, completedBlocks, totalBlocks, processedRows));

                var rowsProcessedBefore = processedRows;

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
                        Log = message => run.Logs.Add($"[{block.Name}] {message}"),
                        RowsProcessed = new InlineProgress<long>(rows =>
                            progress?.Report(new ExecutionProgress(
                                block.Id, block.Name, completedBlocks, totalBlocks, rowsProcessedBefore + rows))),
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
                    completedBlocks++;
                    processedRows = rowsProcessedBefore + outputRows.Count;
                    run.Logs.Add($"[{block.Name}] Completed: {inputRows} in, {outputRows.Count} out.");

                    progress?.Report(new ExecutionProgress(
                        block.Id, block.Name, completedBlocks, totalBlocks, processedRows));
                }
                catch (OperationCanceledException)
                {
                    result.Status = ExecutionStatus.Cancelled;
                    result.FinishedAt = _timeProvider.GetUtcNow();
                    return FinishCancelled(run, results.Values, $"Run cancelled at block '{block.Name}'.");
                }
                catch (Exception ex)
                {
                    result.Status = ExecutionStatus.Failed;
                    result.Error = ex.Message;
                    result.FinishedAt = _timeProvider.GetUtcNow();
                    run.Logs.Add($"[{block.Name}] Failed: {ex.Message}");
                    return FinishFailed(run, results.Values, $"Run failed at block '{block.Name}'.");
                }
            }
        }

        run.Status = RunStatus.Succeeded;
        run.FinishedAt = _timeProvider.GetUtcNow();
        run.Logs.Add($"Run completed: {completedBlocks} block(s), {processedRows} row(s).");
        return run;
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

    private static List<List<RecipeBlock>> BuildLayers(Recipe recipe)
    {
        var blocks = recipe.Blocks.ToDictionary(block => block.Id);
        var inDegree = blocks.Keys.ToDictionary(id => id, _ => 0);
        var adjacency = blocks.Keys.ToDictionary(id => id, _ => new List<string>());

        foreach (var edge in recipe.Edges)
        {
            if (!adjacency.TryGetValue(edge.SourceBlockId, out var targets) ||
                !inDegree.ContainsKey(edge.TargetBlockId))
            {
                continue;
            }

            targets.Add(edge.TargetBlockId);
            inDegree[edge.TargetBlockId]++;
        }

        var layers = new List<List<RecipeBlock>>();
        var ready = inDegree.Where(pair => pair.Value == 0).Select(pair => pair.Key).ToList();

        while (ready.Count > 0)
        {
            layers.Add(ready.Select(id => blocks[id]).ToList());

            var next = new List<string>();
            foreach (var id in ready)
            {
                foreach (var target in adjacency[id])
                {
                    if (--inDegree[target] == 0)
                    {
                        next.Add(target);
                    }
                }
            }

            ready = next;
        }

        return layers;
    }

    private ExecutionRun FinishCancelled(
        ExecutionRun run, IEnumerable<NodeExecutionResult> results, string reason)
    {
        MarkPendingSkipped(results, reason);
        run.Status = RunStatus.Cancelled;
        run.FinishedAt = _timeProvider.GetUtcNow();
        run.Logs.Add(reason);
        return run;
    }

    private ExecutionRun FinishFailed(
        ExecutionRun run, IEnumerable<NodeExecutionResult> results, string reason)
    {
        MarkPendingSkipped(results, reason);
        run.Status = RunStatus.Failed;
        run.FinishedAt = _timeProvider.GetUtcNow();
        run.Logs.Add(reason);
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

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}