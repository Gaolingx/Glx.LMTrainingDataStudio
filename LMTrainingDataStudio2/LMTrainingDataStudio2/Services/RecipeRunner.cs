using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services.Executors;

namespace LMTrainingDataStudio2.Services;

/// <summary>
/// Executes recipes layer by layer. Blocks inside a layer run concurrently up to
/// <see cref="RecipeRunOptions.EffectiveMaxParallelism"/>; the first failed block
/// cancels its siblings and skips downstream blocks.
/// Inter-block rows are buffered in memory up to a configurable limit and spilled
/// to disk beyond it. Completed block outputs are checkpointed so a failed or
/// cancelled run can resume from the first unfinished block.
/// </summary>
public sealed class RecipeRunner
{
    private const string NodeOutputDirectoryName = "nodes";
    private const string NodeOutputExtension = ".rows";

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
        ExecutionPauseController? pause = null,
        ExecutionRun? resumeFrom = null)
    {
        var resuming = resumeFrom is not null;
        var run = resumeFrom ?? new ExecutionRun
        {
            RecipeId = recipe.Id,
            Status = RunStatus.Running,
            StartedAt = _timeProvider.GetUtcNow()
        };

        if (resuming)
        {
            if (!string.Equals(run.RecipeId, recipe.Id, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Run '{run.RunId}' belongs to recipe '{run.RecipeId}', not '{recipe.Id}'.",
                    nameof(resumeFrom));
            }

            run.Status = RunStatus.Running;
            run.FinishedAt = null;
            EnsureOutputDirectory(run);
            Log(run, $"Resuming run {run.RunId} for recipe '{recipe.Name}'.");
        }
        else
        {
            PrepareOutputDirectory(run);
            Log(run, $"Run {run.RunId} started for recipe '{recipe.Name}'.");
        }

        var spillRoot = GetSpillRoot(run);
        var outputs = new ConcurrentDictionary<string, RowSpillBuffer>();
        var state = new RunState();

        try
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return FinishCancelled(run, run.Nodes, "Run cancelled before execution.");
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

            var results = BuildResults(run, recipe);
            var reused = new HashSet<string>(StringComparer.Ordinal);
            if (resuming)
            {
                RehydrateCompletedNodes(run, recipe, results, outputs, reused, state);
            }

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
                var tasks = layer
                    .Where(block => !reused.Contains(block.Id))
                    .Select(block => ExecuteBlockAsync(
                        block,
                        recipe,
                        outputs,
                        spillRoot,
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
            CleanupPersistedNodeOutputs(run);
            WriteSummary(run);
            return run;
        }
        finally
        {
            foreach (var buffer in outputs.Values)
            {
                await buffer.DisposeAsync();
            }

            DeleteSpillRoot(spillRoot);
        }
    }

    private async Task<BlockOutcome> ExecuteBlockAsync(
        RecipeBlock block,
        Recipe recipe,
        ConcurrentDictionary<string, RowSpillBuffer> outputs,
        string spillRoot,
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
                    if (outputs.TryRemove(block.Id, out var previousBuffer))
                    {
                        await previousBuffer.DisposeAsync();
                    }

                    var buffer = new RowSpillBuffer(
                        _options.EffectiveSpillThresholdRows,
                        Path.Combine(spillRoot, "buffers"));
                    outputs[block.Id] = buffer;

                    await foreach (var row in execution.Output.WithCancellation(cancellationToken))
                    {
                        await buffer.AppendAsync(row, cancellationToken);
                    }

                    await buffer.SealAsync(cancellationToken);
                    await PersistNodeOutputAsync(run, block, buffer, cancellationToken);

                    result.Status = ExecutionStatus.Succeeded;
                    result.OutputRows = buffer.Count;
                    result.FinishedAt = _timeProvider.GetUtcNow();
                    Interlocked.Increment(ref state.CompletedBlocks);
                    Interlocked.Add(ref state.ProcessedRows, buffer.Count);
                    Log(run, $"[{block.Name}] Completed: {inputRows} in, {buffer.Count} out.");
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

    private static Dictionary<string, NodeExecutionResult> BuildResults(ExecutionRun run, Recipe recipe)
    {
        var blockIds = recipe.Blocks.Select(block => block.Id).ToHashSet(StringComparer.Ordinal);
        run.Nodes.RemoveAll(node => !blockIds.Contains(node.BlockId));

        var results = new Dictionary<string, NodeExecutionResult>(StringComparer.Ordinal);
        foreach (var block in recipe.Blocks)
        {
            var result = run.Nodes.FirstOrDefault(node => node.BlockId == block.Id);
            if (result is null)
            {
                result = new NodeExecutionResult { BlockId = block.Id, BlockName = block.Name };
                run.Nodes.Add(result);
            }

            results[block.Id] = result;
        }

        return results;
    }

    private void RehydrateCompletedNodes(
        ExecutionRun run,
        Recipe recipe,
        IReadOnlyDictionary<string, NodeExecutionResult> results,
        ConcurrentDictionary<string, RowSpillBuffer> outputs,
        HashSet<string> reused,
        RunState state)
    {
        var checkpointCount = 0;
        foreach (var block in recipe.Blocks)
        {
            var result = results[block.Id];
            var outputPath = run.OutputDirectory is null
                ? null
                : GetNodeOutputPath(run.OutputDirectory, block.Id);

            if (result.Status == ExecutionStatus.Succeeded &&
                outputPath is not null &&
                File.Exists(outputPath))
            {
                outputs[block.Id] = RowSpillBuffer.FromFile(outputPath, result.OutputRows);
                reused.Add(block.Id);
                state.CompletedBlocks++;
                state.ProcessedRows += result.OutputRows;
                checkpointCount++;
                continue;
            }

            result.Status = ExecutionStatus.Pending;
            result.Error = null;
            result.StartedAt = null;
            result.FinishedAt = null;
            result.InputRows = 0;
            result.OutputRows = 0;
        }

        Log(run, checkpointCount == 0
            ? "Resume checkpoint: no completed block outputs to reuse."
            : $"Resume checkpoint: reusing {checkpointCount} completed block output(s).");
    }

    private async Task PersistNodeOutputAsync(
        ExecutionRun run,
        RecipeBlock block,
        RowSpillBuffer buffer,
        CancellationToken cancellationToken)
    {
        if (!_options.PersistNodeOutputs || run.OutputDirectory is null) return;

        var path = GetNodeOutputPath(run.OutputDirectory, block.Id);
        var tempPath = path + ".tmp";
        var completed = false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using (var stream = new FileStream(
                tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var writer = new StreamWriter(
                stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                await foreach (var row in buffer.ReadAsync(cancellationToken))
                {
                    await writer.WriteLineAsync(
                        $"{row.LineNumber}\t{row.Json}".AsMemory(), cancellationToken);
                }
            }

            File.Move(tempPath, path, overwrite: true);
            completed = true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log(run,
                $"[Warning] Could not persist output for block '{block.Name}': {ex.Message}. " +
                "Resume will re-run this block.");
        }
        finally
        {
            if (!completed)
            {
                TryDeleteFile(tempPath);
            }
        }
    }

    private void CleanupPersistedNodeOutputs(ExecutionRun run)
    {
        if (!_options.PersistNodeOutputs || run.OutputDirectory is null) return;

        var directory = Path.Combine(run.OutputDirectory, NodeOutputDirectoryName);
        try
        {
            if (!Directory.Exists(directory)) return;

            Directory.Delete(directory, recursive: true);
            Log(run, "Removed intermediate node outputs after a successful run.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log(run, $"[Warning] Could not remove intermediate node outputs: {ex.Message}");
        }
    }

    private static string GetNodeOutputPath(string outputDirectory, string blockId) =>
        Path.Combine(outputDirectory, NodeOutputDirectoryName, GetNodeFileName(blockId));

    private static string GetNodeFileName(string blockId)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(blockId.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        if (safe.Length == 0)
        {
            safe = "block";
        }
        else if (safe.Length > 64)
        {
            safe = safe[..64];
        }

        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(blockId)))[..8];
        return $"{safe}-{hash}{NodeOutputExtension}";
    }

    private string GetSpillRoot(ExecutionRun run) =>
        Path.Combine(_options.EffectiveSpillDirectory, $"{run.RunId}-{Guid.NewGuid():N}");

    private static void DeleteSpillRoot(string spillRoot)
    {
        try
        {
            if (Directory.Exists(spillRoot))
            {
                Directory.Delete(spillRoot, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static IAsyncEnumerable<DatasetRow>? BuildInput(
        IReadOnlyList<RecipeEdge> inputEdges,
        IReadOnlyDictionary<string, RowSpillBuffer> outputs)
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
        IEnumerable<RowSpillBuffer> sources,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Yield();

        foreach (var source in sources)
        {
            await foreach (var row in source.ReadAsync(ct))
            {
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

    private void EnsureOutputDirectory(ExecutionRun run)
    {
        if (string.IsNullOrWhiteSpace(run.OutputDirectory)) return;

        try
        {
            Directory.CreateDirectory(run.OutputDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            run.OutputDirectory = null;
            Log(run, $"[Warning] Could not reopen output directory: {ex.Message}");
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

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
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
