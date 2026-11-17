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
    private const char PortKeySeparator = '\u001f';

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
            var connectedPorts = GetConnectedOutputPortNames(recipe, block);
            var inputRows = inputEdges.Sum(edge => GetSourceBuffer(recipe, edge, outputs)?.Count ?? 0);

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
                        Input = BuildInput(recipe, inputEdges, outputs),
                        Log = message => Log(run, $"[{block.Name}] {message}"),
                        RowsProcessed = new InlineProgress<long>(rows =>
                            progress?.Report(new ExecutionProgress(
                                block.Id,
                                block.Name,
                                state.CompletedBlocks,
                                totalBlocks,
                                state.ProcessedRows + rows))),
                        ToolProfile = ToolProfileResolver.Resolve(recipe, block),
                        ConnectedOutputPorts = connectedPorts
                    };

                    var execution = await executor.ExecuteAsync(context, cancellationToken);
                    await DisposeBlockBuffersAsync(outputs, block.Id);

                    var streams = MapOutputs(block, execution);
                    var buffers = new Dictionary<string, RowSpillBuffer>(StringComparer.OrdinalIgnoreCase);
                    foreach (var port in streams.Keys)
                    {
                        var buffer = new RowSpillBuffer(
                            _options.EffectiveSpillThresholdRows,
                            Path.Combine(spillRoot, "buffers"));
                        buffers[port] = buffer;
                        outputs[GetOutputKey(block.Id, port)] = buffer;
                    }

                    await Task.WhenAll(streams.Select(stream =>
                        DrainAsync(stream.Value, buffers[stream.Key], cancellationToken)));

                    if (execution.Completion is not null)
                    {
                        await execution.Completion.WaitAsync(cancellationToken);
                    }

                    foreach (var buffer in buffers.Values)
                    {
                        await buffer.SealAsync(cancellationToken);
                    }

                    await PersistNodeOutputsAsync(run, block, buffers, cancellationToken);

                    var outputRows = buffers
                        .Where(pair => IsCountedOutputPort(block, pair.Key))
                        .Sum(pair => pair.Value.Count);

                    result.Status = ExecutionStatus.Succeeded;
                    result.OutputRows = outputRows;
                    result.FinishedAt = _timeProvider.GetUtcNow();
                    Interlocked.Increment(ref state.CompletedBlocks);
                    Interlocked.Add(ref state.ProcessedRows, outputRows);
                    Log(run, $"[{block.Name}] Completed: {inputRows} in, {outputRows} out.");
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
            var ports = GetCheckpointPortNames(recipe, block);
            var paths = run.OutputDirectory is null
                ? null
                : ports.ToDictionary(
                    port => port,
                    port => GetNodeOutputPath(run.OutputDirectory, block, port));

            if (result.Status == ExecutionStatus.Succeeded &&
                paths is not null &&
                (paths.Count == 0 || paths.Values.All(File.Exists)))
            {
                foreach (var (port, path) in paths!)
                {
                    outputs[GetOutputKey(block.Id, port)] =
                        RowSpillBuffer.FromFile(path, CountLines(path));
                }

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

    private async Task PersistNodeOutputsAsync(
        ExecutionRun run,
        RecipeBlock block,
        IReadOnlyDictionary<string, RowSpillBuffer> buffers,
        CancellationToken cancellationToken)
    {
        if (!_options.PersistNodeOutputs || run.OutputDirectory is null) return;

        var declared = block.OutputPorts.Select(port => port.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (port, buffer) in buffers)
        {
            if (declared.Count > 0 && !declared.Contains(port))
            {
                continue;
            }

            var path = GetNodeOutputPath(run.OutputDirectory, block, port);
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
                    $"[Warning] Could not persist output for block '{block.Name}' port '{port}': {ex.Message}. " +
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
    }

    /// <summary>
    /// Ports whose persisted files must exist before a completed block can be reused.
    /// Sinks still get a single primary marker so they are not executed twice.
    /// </summary>
    private static IReadOnlyList<string> GetCheckpointPortNames(Recipe recipe, RecipeBlock block)
    {
        var connected = GetConnectedOutputPortNames(recipe, block);
        if (connected.Count > 0)
        {
            return connected.ToArray();
        }

        return [ResolvePrimaryPortName(block) ?? "output"];
    }

    private static async Task DrainAsync(
        IAsyncEnumerable<DatasetRow> rows,
        RowSpillBuffer buffer,
        CancellationToken cancellationToken)
    {
        await foreach (var row in rows.WithCancellation(cancellationToken))
        {
            await buffer.AppendAsync(row, cancellationToken);
        }
    }

    private static Dictionary<string, IAsyncEnumerable<DatasetRow>> MapOutputs(
        RecipeBlock block,
        BlockExecutionResult execution)
    {
        var streams = new Dictionary<string, IAsyncEnumerable<DatasetRow>>(StringComparer.OrdinalIgnoreCase);
        var primary = ResolvePrimaryPortName(block);
        if (primary is not null)
        {
            streams[primary] = execution.Output;
        }

        if (execution.AdditionalOutputs is not null)
        {
            foreach (var (port, rows) in execution.AdditionalOutputs)
            {
                if (!string.IsNullOrWhiteSpace(port))
                {
                    streams[port] = rows;
                }
            }
        }

        return streams;
    }

    private static string? ResolvePrimaryPortName(RecipeBlock block)
    {
        var declared = block.OutputPorts.Select(port => port.Name).ToList();
        var preferred = BlockPortSchema.GetOutputSpec(block.Type)?.Name;

        if (preferred is not null &&
            declared.FirstOrDefault(name => name.Equals(preferred, StringComparison.OrdinalIgnoreCase)) is { } match)
        {
            return match;
        }

        return declared.Count switch
        {
            0 => "output",
            1 => declared[0],
            _ => null
        };
    }

    private static bool IsCountedOutputPort(RecipeBlock block, string port) =>
        block.OutputPorts.Count == 0 ||
        block.OutputPorts.Any(candidate => candidate.Name.Equals(port, StringComparison.OrdinalIgnoreCase));

    private static HashSet<string> GetConnectedOutputPortNames(Recipe recipe, RecipeBlock block)
    {
        var portNames = block.OutputPorts.ToDictionary(
            port => port.Id, port => port.Name, StringComparer.Ordinal);
        return recipe.Edges
            .Where(edge => edge.SourceBlockId == block.Id)
            .Select(edge => portNames.GetValueOrDefault(edge.SourcePortId))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string GetOutputKey(string blockId, string portName) =>
        $"{blockId}{PortKeySeparator}{portName}";

    private static RowSpillBuffer? GetSourceBuffer(
        Recipe recipe,
        RecipeEdge edge,
        IReadOnlyDictionary<string, RowSpillBuffer> outputs)
    {
        var source = recipe.Blocks.FirstOrDefault(block => block.Id == edge.SourceBlockId);
        if (source is null)
        {
            return null;
        }

        var portName = source.OutputPorts
            .FirstOrDefault(port => port.Id == edge.SourcePortId)?.Name;
        if (portName is not null &&
            outputs.TryGetValue(GetOutputKey(source.Id, portName), out var buffer))
        {
            return buffer;
        }

        var fallback = ResolvePrimaryPortName(source);
        return fallback is not null &&
               outputs.TryGetValue(GetOutputKey(source.Id, fallback), out var primary)
            ? primary
            : null;
    }

    private static async Task DisposeBlockBuffersAsync(
        ConcurrentDictionary<string, RowSpillBuffer> outputs,
        string blockId)
    {
        var prefix = blockId + PortKeySeparator;
        foreach (var key in outputs.Keys
                     .Where(key => key.StartsWith(prefix, StringComparison.Ordinal))
                     .ToList())
        {
            if (outputs.TryRemove(key, out var buffer))
            {
                await buffer.DisposeAsync();
            }
        }
    }

    private static long CountLines(string path)
    {
        using var reader = new StreamReader(path);
        var count = 0L;
        while (reader.ReadLine() is not null)
        {
            count++;
        }

        return count;
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

    private static string GetNodeOutputPath(
        string outputDirectory,
        RecipeBlock block,
        string portName)
    {
        var fileName = GetNodeFileName(block.Id);
        if (block.OutputPorts.Count > 1)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var safePort = new string(portName.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
            fileName = $"{fileName}-{safePort}";
        }

        return Path.Combine(outputDirectory, NodeOutputDirectoryName, fileName + NodeOutputExtension);
    }

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
        return $"{safe}-{hash}";
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
        Recipe recipe,
        IReadOnlyList<RecipeEdge> inputEdges,
        IReadOnlyDictionary<string, RowSpillBuffer> outputs)
    {
        var sources = inputEdges
            .Select(edge => GetSourceBuffer(recipe, edge, outputs))
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
