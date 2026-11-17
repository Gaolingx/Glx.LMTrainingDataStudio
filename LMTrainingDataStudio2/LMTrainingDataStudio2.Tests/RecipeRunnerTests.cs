using System.Diagnostics;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services;
using LMTrainingDataStudio2.Services.Executors;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class RecipeRunnerTests
{
    private string _directory = null!;
    private string _dataPath = null!;
    private AppSettings _settings = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "lmtds-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _dataPath = Path.Combine(_directory, "data.jsonl");
        _settings = new AppSettings
        {
            CacheDirectory = Path.Combine(_directory, "cache"),
            MaxCacheSizeMb = 16
        };
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_directory, true); } catch { /* best effort */ }
    }

    [Test]
    public async Task RunAsync_Pipeline_SucceedsAndReportsProgress()
    {
        await WriteRowsAsync(5);
        var (recipe, seed, validator, sampler) = CreatePipeline(sampleCount: 2);
        var progress = new List<ExecutionProgress>();

        var run = await CreateRunner().RunAsync(recipe, new CollectingProgress<ExecutionProgress>(progress));

        Assert.That(run.Status, Is.EqualTo(RunStatus.Succeeded));
        Assert.That(run.FinishedAt, Is.Not.Null);
        Assert.That(run.Nodes.Select(node => node.Status), Is.All.EqualTo(ExecutionStatus.Succeeded));
        Assert.That(Node(run, seed).OutputRows, Is.EqualTo(5));
        Assert.That(Node(run, validator).InputRows, Is.EqualTo(5));
        Assert.That(Node(run, validator).OutputRows, Is.EqualTo(5));
        Assert.That(Node(run, sampler).InputRows, Is.EqualTo(5));
        Assert.That(Node(run, sampler).OutputRows, Is.EqualTo(2));
        Assert.That(run.Logs.Any(log => log.Contains("Completed")), Is.True);

        Assert.That(progress, Is.Not.Empty);
        Assert.That(progress[^1].CompletedBlocks, Is.EqualTo(3));
        Assert.That(progress[^1].TotalBlocks, Is.EqualTo(3));
        Assert.That(progress[^1].ProcessedRows, Is.EqualTo(12));
    }

    [Test]
    public async Task RunAsync_NodeFailure_FailsAndSkipsDownstream()
    {
        var (recipe, seed, validator, sampler) = CreatePipeline(sampleCount: 2);

        var run = await CreateRunner().RunAsync(recipe);

        Assert.That(run.Status, Is.EqualTo(RunStatus.Failed));
        Assert.That(Node(run, seed).Status, Is.EqualTo(ExecutionStatus.Failed));
        Assert.That(Node(run, seed).Error, Is.Not.Null);
        Assert.That(Node(run, validator).Status, Is.EqualTo(ExecutionStatus.Skipped));
        Assert.That(Node(run, sampler).Status, Is.EqualTo(ExecutionStatus.Skipped));
        Assert.That(run.Logs.Any(log => log.Contains("Failed")), Is.True);
    }

    [Test]
    public async Task RunAsync_InvalidRecipe_IsRejectedBeforeExecution()
    {
        await WriteRowsAsync(1);
        var (recipe, _, validator, _) = CreatePipeline(sampleCount: 1);
        validator.Name = "seed_data";

        var run = await CreateRunner().RunAsync(recipe);

        Assert.That(run.Status, Is.EqualTo(RunStatus.Failed));
        Assert.That(run.Nodes, Is.Empty);
        Assert.That(run.Logs.Any(log => log.Contains("Duplicate block name")), Is.True);
        Assert.That(run.Logs.Any(log => log.Contains("validation errors")), Is.True);
    }

    [Test]
    public async Task RunAsync_PreCancelled_ReturnsCancelled()
    {
        await WriteRowsAsync(3);
        var (recipe, _, _, _) = CreatePipeline(sampleCount: 1);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var run = await CreateRunner().RunAsync(recipe, cancellationToken: cts.Token);

        Assert.That(run.Status, Is.EqualTo(RunStatus.Cancelled));
        Assert.That(run.FinishedAt, Is.Not.Null);
        Assert.That(run.Logs.Any(log => log.Contains("cancelled", StringComparison.OrdinalIgnoreCase)), Is.True);
    }

    [Test]
    public async Task RunAsync_ExecutorCancellation_MarksNodeCancelled()
    {
        var (recipe, seed, _, _) = CreatePipeline(sampleCount: 1);
        var runner = new RecipeRunner(
            new RecipeValidationService(),
            new IBlockExecutor[] { new CancellingExecutor(BlockType.Seed) });

        var run = await runner.RunAsync(recipe);

        Assert.That(run.Status, Is.EqualTo(RunStatus.Cancelled));
        Assert.That(Node(run, seed).Status, Is.EqualTo(ExecutionStatus.Cancelled));
    }

    [Test]
public async Task RunAsync_IndependentLayerBlocks_RunInParallel()
{
    await WriteRowsAsync(1);
    var recipe = CreateProbeRecipe();
    var executor = new ProbeExecutor(BlockType.Seed);
    var runner = new RecipeRunner(
        new RecipeValidationService(),
        new IBlockExecutor[] { executor },
        options: new RecipeRunOptions(MaxParallelism: 2));

    var run = await runner.RunAsync(recipe).WaitAsync(TimeSpan.FromSeconds(10));

    Assert.That(run.Status, Is.EqualTo(RunStatus.Succeeded));
    Assert.That(run.Nodes, Has.Count.EqualTo(2));
    Assert.That(run.Nodes.Select(node => node.Status),
        Is.All.EqualTo(ExecutionStatus.Succeeded));
    Assert.That(executor.MaxConcurrency, Is.GreaterThanOrEqualTo(2));
}

[Test]
public async Task RunAsync_PausedBeforeStart_WaitsForResume()
{
    await WriteRowsAsync(1);
    var recipe = CreateSingleSeedRecipe();
    var runner = new RecipeRunner(
        new RecipeValidationService(),
        new IBlockExecutor[] { new EmptyExecutor(BlockType.Seed) });
    var pause = new ExecutionPauseController();
    pause.Pause();

    var runTask = runner.RunAsync(recipe, pause: pause);
    await Task.Delay(150);
    Assert.That(runTask.IsCompleted, Is.False);

    pause.Resume();
    var run = await runTask.WaitAsync(TimeSpan.FromSeconds(5));

    Assert.That(run.Status, Is.EqualTo(RunStatus.Succeeded));
    Assert.That(run.Logs.Any(log => log.Contains("paused", StringComparison.OrdinalIgnoreCase)),
        Is.True);
    Assert.That(run.Logs.Any(log => log.Contains("resumed", StringComparison.OrdinalIgnoreCase)),
        Is.True);
}

[Test]
public async Task RunAsync_NodeRetry_RetriesFailedBlock()
{
    await WriteRowsAsync(1);
    var recipe = CreateSingleSeedRecipe();
    var executor = new FlakyExecutor(BlockType.Seed, failuresBeforeSuccess: 1);
    var runner = new RecipeRunner(
        new RecipeValidationService(),
        new IBlockExecutor[] { executor },
        options: new RecipeRunOptions(NodeRetryCount: 1));

    var run = await runner.RunAsync(recipe);

    Assert.That(run.Status, Is.EqualTo(RunStatus.Succeeded));
    Assert.That(executor.Attempts, Is.EqualTo(2));
    Assert.That(run.Logs.Any(log => log.Contains("Retrying", StringComparison.Ordinal)), Is.True);
}

[Test]
public async Task RunAsync_WritesOutputDirectorySummary()
{
    await WriteRowsAsync(1);
    var recipe = CreateSingleSeedRecipe();
    var output = Path.Combine(_directory, "runs");
    var runner = new RecipeRunner(
        new RecipeValidationService(),
        new IBlockExecutor[] { new EmptyExecutor(BlockType.Seed) },
        options: new RecipeRunOptions(OutputDirectory: output));

    var run = await runner.RunAsync(recipe);

    Assert.That(run.OutputDirectory, Is.Not.Null);
    Assert.That(Directory.Exists(run.OutputDirectory), Is.True);
    var summaryPath = Path.Combine(run.OutputDirectory!, "run-summary.json");
    Assert.That(File.Exists(summaryPath), Is.True);
    Assert.That(await File.ReadAllTextAsync(summaryPath), Does.Contain("\"Status\": \"Succeeded\""));
}

    [Test]
    public async Task RunAsync_LowSpillThreshold_SpillsRowsAndCleansTempDirectory()
    {
        await WriteRowsAsync(25);
        var spillDirectory = Path.Combine(_directory, "spill");
        var (recipe, seed, _, _) = CreatePipeline(sampleCount: 5);
        var runner = CreateRunner(new RecipeRunOptions(
            SpillThresholdRows: 4,
            SpillDirectory: spillDirectory));

        var run = await runner.RunAsync(recipe);

        Assert.That(run.Status, Is.EqualTo(RunStatus.Succeeded));
        Assert.That(Node(run, seed).OutputRows, Is.EqualTo(25));
        Assert.That(GetFiles(spillDirectory), Is.Empty);
    }

    [Test]
    public async Task RunAsync_FailedRun_KeepsCheckpointsAndResumeReusesCompletedBlock()
    {
        var (recipe, seed, expression) = CreateTwoBlockRecipe();
        var seedExecutor = new CountingRowSourceExecutor(BlockType.Seed, rows: 4);
        var expressionExecutor = new FailThenSucceedExecutor(BlockType.Expression) { Fail = true };
        var options = new RecipeRunOptions(
            SpillThresholdRows: 1,
            SpillDirectory: Path.Combine(_directory, "spill"),
            OutputDirectory: Path.Combine(_directory, "runs"));

        var firstRunner = new RecipeRunner(
            new RecipeValidationService(),
            new IBlockExecutor[] { seedExecutor, expressionExecutor },
            options: options);
        var firstRun = await firstRunner.RunAsync(recipe);

        Assert.That(firstRun.Status, Is.EqualTo(RunStatus.Failed));
        Assert.That(Node(firstRun, seed).Status, Is.EqualTo(ExecutionStatus.Succeeded));
        Assert.That(Node(firstRun, expression).Status, Is.EqualTo(ExecutionStatus.Failed));
        var checkpointDirectory = Path.Combine(firstRun.OutputDirectory!, "nodes");
        Assert.That(Directory.Exists(checkpointDirectory), Is.True);
        Assert.That(GetFiles(checkpointDirectory), Has.Length.EqualTo(2));

        expressionExecutor.Fail = false;
        var resumeRunner = new RecipeRunner(
            new RecipeValidationService(),
            new IBlockExecutor[] { seedExecutor, expressionExecutor },
            options: options);
        var resumed = await resumeRunner.RunAsync(recipe, resumeFrom: firstRun);

        Assert.That(resumed.Status, Is.EqualTo(RunStatus.Succeeded), string.Join('\n', resumed.Logs));
        Assert.That(Node(resumed, seed).Status, Is.EqualTo(ExecutionStatus.Succeeded));
        Assert.That(Node(resumed, expression).Status, Is.EqualTo(ExecutionStatus.Succeeded));
        Assert.That(seedExecutor.Attempts, Is.EqualTo(1));
        Assert.That(expressionExecutor.Attempts, Is.EqualTo(2));
        Assert.That(Node(resumed, expression).OutputRows, Is.EqualTo(4));
        Assert.That(resumed.Logs.Any(log => log.Contains("reusing 1 completed block output")), Is.True);
        Assert.That(Directory.Exists(checkpointDirectory), Is.False);
    }

    [Test]
    public void RunAsync_ResumeWithDifferentRecipe_Throws()
    {
        var run = new ExecutionRun { RecipeId = "recipe-a" };
        var recipe = new Recipe { Id = "recipe-b", Name = "other" };

        Assert.ThrowsAsync<ArgumentException>(
            () => CreateRunner().RunAsync(recipe, resumeFrom: run));
    }

    [Test]
    public async Task RunAsync_ResumeAfterRecipeChange_Throws()
    {
        var (recipe, seed, expression) = CreateTwoBlockRecipe();
        var options = new RecipeRunOptions(
            SpillThresholdRows: 1,
            SpillDirectory: Path.Combine(_directory, "spill"),
            OutputDirectory: Path.Combine(_directory, "runs"));
        var firstRunner = new RecipeRunner(
            new RecipeValidationService(),
            new IBlockExecutor[]
            {
                new CountingRowSourceExecutor(BlockType.Seed, rows: 4),
                new FailThenSucceedExecutor(BlockType.Expression) { Fail = true }
            },
            options: options);
        var firstRun = await firstRunner.RunAsync(recipe);
        Assert.That(firstRun.Status, Is.EqualTo(RunStatus.Failed));
        Assert.That(Node(firstRun, seed).Status, Is.EqualTo(ExecutionStatus.Succeeded));
        Assert.That(Node(firstRun, expression).Status, Is.EqualTo(ExecutionStatus.Failed));

        recipe.Name = "changed recipe";
        var resumeRunner = new RecipeRunner(
            new RecipeValidationService(),
            new IBlockExecutor[]
            {
                new CountingRowSourceExecutor(BlockType.Seed, rows: 4),
                new FailThenSucceedExecutor(BlockType.Expression)
            },
            options: options);

        Assert.ThrowsAsync<InvalidOperationException>(
            () => resumeRunner.RunAsync(recipe, resumeFrom: firstRun));
    }

    [Test]
    public async Task RunAsync_ResumeWithTamperedCheckpoint_RerunsBlock()
    {
        var (recipe, seed, _) = CreateTwoBlockRecipe();
        var seedExecutor = new CountingRowSourceExecutor(BlockType.Seed, rows: 4);
        var expressionExecutor = new FailThenSucceedExecutor(BlockType.Expression) { Fail = true };
        var options = new RecipeRunOptions(
            SpillThresholdRows: 1,
            SpillDirectory: Path.Combine(_directory, "spill"),
            OutputDirectory: Path.Combine(_directory, "runs"));
        var firstRunner = new RecipeRunner(
            new RecipeValidationService(),
            new IBlockExecutor[] { seedExecutor, expressionExecutor },
            options: options);
        var firstRun = await firstRunner.RunAsync(recipe);

        var checkpointDirectory = Path.Combine(firstRun.OutputDirectory!, "nodes");
        var checkpoint = GetFiles(checkpointDirectory)
            .Single(path => path.EndsWith(".rows", StringComparison.Ordinal));
        await File.AppendAllTextAsync(checkpoint, "999\t{}\n");

        expressionExecutor.Fail = false;
        var resumeRunner = new RecipeRunner(
            new RecipeValidationService(),
            new IBlockExecutor[] { seedExecutor, expressionExecutor },
            options: options);
        var resumed = await resumeRunner.RunAsync(recipe, resumeFrom: firstRun);

        Assert.That(resumed.Status, Is.EqualTo(RunStatus.Succeeded), string.Join('\n', resumed.Logs));
        Assert.That(seedExecutor.Attempts, Is.EqualTo(2));
        Assert.That(resumed.Logs.Any(log => log.Contains("failed hash verification")), Is.True);
        Assert.That(Node(resumed, seed).Status, Is.EqualTo(ExecutionStatus.Succeeded));
    }

    [Test]
    public async Task RunAsync_RetryAfterSpilledAttempt_CleansAbandonedBuffer()
    {
        var spillDirectory = Path.Combine(_directory, "spill");
        var recipe = CreateSingleSeedRecipe();
        var executor = new SpillThenFailOnceExecutor(BlockType.Seed);
        var runner = new RecipeRunner(
            new RecipeValidationService(),
            new IBlockExecutor[] { executor },
            options: new RecipeRunOptions(
                NodeRetryCount: 1,
                SpillThresholdRows: 1,
                SpillDirectory: spillDirectory));

        var run = await runner.RunAsync(recipe);

        Assert.That(run.Status, Is.EqualTo(RunStatus.Succeeded), string.Join('\n', run.Logs));
        Assert.That(executor.Attempts, Is.EqualTo(2));
        Assert.That(run.Nodes.Single().OutputRows, Is.EqualTo(3));
        Assert.That(GetFiles(spillDirectory), Is.Empty);
    }

    [Test]
    public async Task RunAsync_Cancel_StopsWithinOneSecondAndCleansSpillFiles()
    {
        var spillDirectory = Path.Combine(_directory, "spill");
        var recipe = CreateSingleSeedRecipe();
        var runner = new RecipeRunner(
            new RecipeValidationService(),
            new IBlockExecutor[] { new SlowExecutor(BlockType.Seed) },
            options: new RecipeRunOptions(
                SpillThresholdRows: 1,
                SpillDirectory: spillDirectory));
        using var cts = new CancellationTokenSource();

        var runTask = runner.RunAsync(recipe, cancellationToken: cts.Token);
        await Task.Delay(120);

        var stopwatch = Stopwatch.StartNew();
        cts.Cancel();
        var run = await runTask.WaitAsync(TimeSpan.FromSeconds(5));
        stopwatch.Stop();

        Assert.That(run.Status, Is.EqualTo(RunStatus.Cancelled));
        Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(1)),
            $"Cancellation took {stopwatch.Elapsed.TotalMilliseconds:F0}ms.");
        Assert.That(GetFiles(spillDirectory), Is.Empty);
    }

private Recipe CreateProbeRecipe()
{
    var recipe = new Recipe { Name = "parallel" };
    var first = CreateBlock("seed1", BlockType.Seed, "seed_a",
        new SeedBlockConfig(_dataPath, DataFileType.Jsonl, Streaming: false, UseIndexCache: true));
    first.OutputPorts.Add(CreatePort("seed1_out", "rows", PortDirection.Output, "rows"));
    var second = CreateBlock("seed2", BlockType.Seed, "seed_b",
        new SeedBlockConfig(_dataPath, DataFileType.Jsonl, Streaming: false, UseIndexCache: true));
    second.OutputPorts.Add(CreatePort("seed2_out", "rows", PortDirection.Output, "rows"));
    recipe.Blocks.AddRange(new[] { first, second });
    return recipe;
}

private Recipe CreateSingleSeedRecipe()
{
    var recipe = new Recipe { Name = "single" };
    var seed = CreateBlock("seed1", BlockType.Seed, "seed_a",
        new SeedBlockConfig(_dataPath, DataFileType.Jsonl, Streaming: false, UseIndexCache: true));
    seed.OutputPorts.Add(CreatePort("seed1_out", "rows", PortDirection.Output, "rows"));
    recipe.Blocks.Add(seed);
    return recipe;
}

private async Task WriteRowsAsync(int count)
    {
        var lines = Enumerable.Range(0, count)
            .Select(i => $"{{\"messages\":[{{\"role\":\"user\",\"content\":\"row {i}\"}}]}}");
        await File.WriteAllLinesAsync(_dataPath, lines);
    }

    private RecipeRunner CreateRunner(RecipeRunOptions? options = null) => new(
        new RecipeValidationService(),
        new IBlockExecutor[]
        {
            new SeedBlockExecutor(_settings),
            new ExpressionBlockExecutor(),
            new ValidatorBlockExecutor(),
            new SamplerBlockExecutor()
        },
        options: options);

    private (Recipe Recipe, RecipeBlock Seed, RecipeBlock Validator, RecipeBlock Sampler) CreatePipeline(long sampleCount)
    {
        var recipe = new Recipe { Name = "pipeline" };

        var seed = CreateBlock("seed", BlockType.Seed, "seed_data",
            new SeedBlockConfig(_dataPath, DataFileType.Jsonl, Streaming: false, UseIndexCache: true));
        seed.OutputPorts.Add(CreatePort("seed_out", "rows", PortDirection.Output, "rows"));

        var validator = CreateBlock("val", BlockType.Validator, "check",
            new ValidatorBlockConfig("expression", "messages.length > 0", RejectOnFailure: true));
        validator.InputPorts.Add(CreatePort("val_in", "input", PortDirection.Input, "rows"));
        validator.OutputPorts.Add(CreatePort("val_out", "valid", PortDirection.Output, "rows"));

        var sampler = CreateBlock("sample", BlockType.Sampler, "sample",
            new SamplerBlockConfig(sampleCount, Ratio: null, Seed: 42, Splits: Array.Empty<string>()));
        sampler.InputPorts.Add(CreatePort("sample_in", "input", PortDirection.Input, "rows"));
        sampler.OutputPorts.Add(CreatePort("sample_out", "output", PortDirection.Output, "rows"));

        recipe.Blocks.AddRange(new[] { seed, validator, sampler });
        recipe.Edges.Add(CreateEdge("e1", seed, seed.OutputPorts[0], validator, validator.InputPorts[0]));
        recipe.Edges.Add(CreateEdge("e2", validator, validator.OutputPorts[0], sampler, sampler.InputPorts[0]));

        return (recipe, seed, validator, sampler);
    }

    private static RecipeBlock CreateBlock(string id, BlockType type, string name, BlockConfig config) => new()
    {
        Id = id,
        Type = type,
        Name = name,
        DisplayName = name,
        Config = config
    };

    private static BlockPort CreatePort(string id, string name, PortDirection direction, string dataType) => new()
    {
        Id = id,
        Name = name,
        Direction = direction,
        DataType = dataType
    };

    private static RecipeEdge CreateEdge(
        string id, RecipeBlock source, BlockPort sourcePort, RecipeBlock target, BlockPort targetPort) => new()
    {
        Id = id,
        SourceBlockId = source.Id,
        SourcePortId = sourcePort.Id,
        TargetBlockId = target.Id,
        TargetPortId = targetPort.Id
    };

    private static NodeExecutionResult Node(ExecutionRun run, RecipeBlock block) =>
        run.Nodes.Single(node => node.BlockId == block.Id);

    private sealed class CollectingProgress<T>(ICollection<T> items) : IProgress<T>
    {
        public void Report(T value) => items.Add(value);
    }

    private sealed class CancellingExecutor(BlockType type) : IBlockExecutor
    {
        public BlockType Type => type;

        public Task<BlockExecutionResult> ExecuteAsync(
            BlockExecutionContext context, CancellationToken cancellationToken) =>
            throw new OperationCanceledException();
    }

    private sealed class ProbeExecutor : IBlockExecutor
    {
        private readonly TaskCompletionSource _bothStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _running;

        public ProbeExecutor(BlockType type) => Type = type;

        public BlockType Type { get; }

        public int MaxConcurrency { get; private set; }

        public async Task<BlockExecutionResult> ExecuteAsync(
            BlockExecutionContext context, CancellationToken cancellationToken)
        {
            var running = Interlocked.Increment(ref _running);
            MaxConcurrency = Math.Max(MaxConcurrency, running);
            if (running >= 2)
            {
                _bothStarted.TrySetResult();
            }

            try
            {
                await _bothStarted.Task.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken);
                return new BlockExecutionResult(NoRows());
            }
            finally
            {
                Interlocked.Decrement(ref _running);
            }
        }

        private static async IAsyncEnumerable<DatasetRow> NoRows()
        {
            await Task.Yield();
            yield break;
        }
    }

    private sealed class EmptyExecutor : IBlockExecutor
    {
        public EmptyExecutor(BlockType type) => Type = type;

        public BlockType Type { get; }

        public Task<BlockExecutionResult> ExecuteAsync(
            BlockExecutionContext context, CancellationToken cancellationToken) =>
            Task.FromResult(new BlockExecutionResult(NoRows()));

        private static async IAsyncEnumerable<DatasetRow> NoRows()
        {
            await Task.Yield();
            yield break;
        }
    }

    private sealed class FlakyExecutor : IBlockExecutor
    {
        private readonly int _failuresBeforeSuccess;
        private int _attempts;

        public FlakyExecutor(BlockType type, int failuresBeforeSuccess)
        {
            Type = type;
            _failuresBeforeSuccess = failuresBeforeSuccess;
        }

        public BlockType Type { get; }

        public int Attempts => _attempts;

        public Task<BlockExecutionResult> ExecuteAsync(
            BlockExecutionContext context, CancellationToken cancellationToken)
        {
            var attempt = Interlocked.Increment(ref _attempts);
            if (attempt <= _failuresBeforeSuccess)
            {
                throw new InvalidOperationException($"Synthetic failure {attempt}.");
            }

            return Task.FromResult(new BlockExecutionResult(NoRows()));
        }

        private static async IAsyncEnumerable<DatasetRow> NoRows()
        {
            await Task.Yield();
            yield break;
        }
    }

    private sealed class CountingRowSourceExecutor : IBlockExecutor
    {
        private readonly int _rows;
        private int _attempts;

        public CountingRowSourceExecutor(BlockType type, int rows)
        {
            Type = type;
            _rows = rows;
        }

        public BlockType Type { get; }

        public int Attempts => _attempts;

        public Task<BlockExecutionResult> ExecuteAsync(
            BlockExecutionContext context, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _attempts);
            return Task.FromResult(new BlockExecutionResult(GenerateAsync(_rows, cancellationToken)));
        }

        private static async IAsyncEnumerable<DatasetRow> GenerateAsync(
            int rows, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            for (var i = 0; i < rows; i++)
            {
                ct.ThrowIfCancellationRequested();
                yield return new DatasetRow(i, $"{{\"value\":{i}}}");
            }
        }
    }

    private sealed class FailThenSucceedExecutor : IBlockExecutor
    {
        private int _attempts;

        public FailThenSucceedExecutor(BlockType type) => Type = type;

        public BlockType Type { get; }

        public bool Fail { get; set; }

        public int Attempts => _attempts;

        public Task<BlockExecutionResult> ExecuteAsync(
            BlockExecutionContext context, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _attempts);
            if (Fail)
            {
                throw new InvalidOperationException("Synthetic resume failure.");
            }

            return Task.FromResult(new BlockExecutionResult(PassthroughAsync(context.Input!, cancellationToken)));
        }

        private static async IAsyncEnumerable<DatasetRow> PassthroughAsync(
            IAsyncEnumerable<DatasetRow> input,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (var row in input.WithCancellation(ct))
            {
                yield return row;
            }
        }
    }

    private sealed class SpillThenFailOnceExecutor : IBlockExecutor
    {
        private int _attempts;

        public SpillThenFailOnceExecutor(BlockType type) => Type = type;

        public BlockType Type { get; }

        public int Attempts => _attempts;

        public Task<BlockExecutionResult> ExecuteAsync(
            BlockExecutionContext context, CancellationToken cancellationToken)
        {
            var attempt = Interlocked.Increment(ref _attempts);
            return Task.FromResult(new BlockExecutionResult(
                GenerateAsync(attempt == 1, cancellationToken)));
        }

        private static async IAsyncEnumerable<DatasetRow> GenerateAsync(
            bool failAtEnd, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            for (var i = 0; i < 3; i++)
            {
                ct.ThrowIfCancellationRequested();
                yield return new DatasetRow(i, $"{{\"value\":{i}}}");
            }

            if (failAtEnd)
            {
                throw new InvalidOperationException("Synthetic failure after partial output.");
            }
        }
    }

    private sealed class SlowExecutor : IBlockExecutor
    {
        public SlowExecutor(BlockType type) => Type = type;

        public BlockType Type { get; }

        public Task<BlockExecutionResult> ExecuteAsync(
            BlockExecutionContext context, CancellationToken cancellationToken) =>
            Task.FromResult(new BlockExecutionResult(GenerateAsync(cancellationToken)));

        private static async IAsyncEnumerable<DatasetRow> GenerateAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            for (var i = 0; i < 100_000; i++)
            {
                await Task.Delay(25, ct);
                yield return new DatasetRow(i, "{\"value\":1}");
            }
        }
    }

    private (Recipe Recipe, RecipeBlock Seed, RecipeBlock Expression) CreateTwoBlockRecipe()
    {
        var recipe = new Recipe { Name = "resume" };
        var seed = CreateBlock("seed", BlockType.Seed, "seed_data",
            new SeedBlockConfig(_dataPath, DataFileType.Jsonl, Streaming: false, UseIndexCache: true));
        seed.OutputPorts.Add(CreatePort("seed_out", "rows", PortDirection.Output, "rows"));

        var expression = CreateBlock("expr", BlockType.Expression, "map",
            new ExpressionBlockConfig("""{"value":{{ value }}}""", new Dictionary<string, string>()));
        expression.InputPorts.Add(CreatePort("expr_in", "input", PortDirection.Input, "rows"));
        expression.OutputPorts.Add(CreatePort("expr_out", "output", PortDirection.Output, "rows"));

        recipe.Blocks.AddRange(new[] { seed, expression });
        recipe.Edges.Add(CreateEdge("e1", seed, seed.OutputPorts[0], expression, expression.InputPorts[0]));
        return (recipe, seed, expression);
    }

    private static string[] GetFiles(string directory) =>
        Directory.Exists(directory)
            ? Directory.GetFileSystemEntries(directory, "*", SearchOption.AllDirectories)
            : Array.Empty<string>();
}