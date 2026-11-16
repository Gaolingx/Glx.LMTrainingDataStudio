using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services.Sandbox;
using LMTrainingDataStudio2.Services.Simulation;

namespace LMTrainingDataStudio2.Services.Executors;

/// <summary>
/// Runs the mini harness per query row and emits OpenAI chat-message samples.
/// Rows can run concurrently while output order follows input order.
/// </summary>
public sealed class SimulationBlockExecutor : IBlockExecutor
{
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly ILlmCredentials? _credentials;
    private readonly HttpClient _httpClient;
    private readonly ISandboxProcessRunner _sandboxRunner;
    private readonly Func<SimulationBlockConfig, IChatCompletionClient>? _chatClientFactory;
    private readonly Func<SimulationBlockConfig, IToolRuntime>? _toolRuntimeFactory;

    public SimulationBlockExecutor(
        ILlmCredentials? credentials = null,
        HttpClient? httpClient = null,
        ISandboxProcessRunner? sandboxRunner = null,
        Func<SimulationBlockConfig, IChatCompletionClient>? chatClientFactory = null,
        Func<SimulationBlockConfig, IToolRuntime>? toolRuntimeFactory = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _credentials = credentials;
        _httpClient = httpClient ?? new HttpClient();
        _sandboxRunner = sandboxRunner ?? new SandboxProcessRunner();
        _chatClientFactory = chatClientFactory;
        _toolRuntimeFactory = toolRuntimeFactory;
        _delay = delay ?? ((duration, token) => Task.Delay(duration, token));
    }

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

        if (config.MaxConcurrency < 1)
        {
            throw new InvalidOperationException(
                $"Simulation block '{context.Block.Name}' max concurrency must be at least 1; got {config.MaxConcurrency}.");
        }

        if (config.MaxFailureSamples < 0)
        {
            throw new InvalidOperationException(
                $"Simulation block '{context.Block.Name}' max failure samples must not be negative.");
        }

        if (config.ModelMode == SimulationModelMode.Provider &&
            _chatClientFactory is null &&
            _credentials is null)
        {
            throw new InvalidOperationException(
                $"Simulation block '{context.Block.Name}' uses the provider mode but no LLM credentials are available.");
        }

        return Task.FromResult(new BlockExecutionResult(
            GenerateAsync(context, config, cancellationToken)));
    }

    private async IAsyncEnumerable<DatasetRow> GenerateAsync(
        BlockExecutionContext context,
        SimulationBlockConfig config,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var maxConcurrency = Math.Clamp(config.MaxConcurrency, 1, 64);
        var blockName = context.Block.Name;
        var logLock = new object();
        void Log(string message)
        {
            lock (logLock)
            {
                context.Log(message);
            }
        }

        var pending = new Queue<Task<RowOutcome>>();
        var failures = new List<FailureSample>();
        var inputIndex = 0L;
        var outputIndex = 0L;
        var consumed = 0L;
        var emitted = 0L;
        var skipped = 0L;
        var failedTrajectories = 0L;
        var modelCalls = 0L;
        var promptTokens = 0L;
        var completionTokens = 0L;
        var latency = TimeSpan.Zero;
        var cost = 0m;

        using var pipelineCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        try
        {
            await foreach (var row in context.Input!.WithCancellation(ct))
            {
                ct.ThrowIfCancellationRequested();
                var index = inputIndex++;
                pending.Enqueue(ProcessRowAsync(
                    index, row, blockName, config, Log, pipelineCts.Token));

                while (pending.Count >= maxConcurrency)
                {
                    var outcome = await pending.Dequeue();
                    consumed++;
                    Accumulate(
                        outcome, ref modelCalls, ref promptTokens, ref completionTokens,
                        ref latency, ref cost, ref failedTrajectories);
                    TrackFailure(outcome, config, failures);

                    if (outcome.Json is null)
                    {
                        skipped++;
                    }
                    else
                    {
                        emitted++;
                        if (outcome.Trajectory.ModelCalls > 0)
                        {
                            Log(FormatUsage(outcome));
                        }

                        yield return new DatasetRow(outputIndex++, outcome.Json);
                    }

                    context.RowsProcessed?.Report(consumed);
                }
            }

            while (pending.Count > 0)
            {
                var outcome = await pending.Dequeue();
                consumed++;
                Accumulate(
                    outcome, ref modelCalls, ref promptTokens, ref completionTokens,
                    ref latency, ref cost, ref failedTrajectories);
                TrackFailure(outcome, config, failures);

                if (outcome.Json is null)
                {
                    skipped++;
                }
                else
                {
                    emitted++;
                    if (outcome.Trajectory.ModelCalls > 0)
                    {
                        Log(FormatUsage(outcome));
                    }

                    yield return new DatasetRow(outputIndex++, outcome.Json);
                }

                context.RowsProcessed?.Report(consumed);
            }
        }
        finally
        {
            pipelineCts.Cancel();
            while (pending.Count > 0)
            {
                try
                {
                    await pending.Dequeue();
                }
                catch
                {
                    // Cancelled or aborted rows are not part of the output.
                }
            }
        }

        if (failedTrajectories > 0)
        {
            if (failures.Count < failedTrajectories)
            {
                Log($"Kept {failures.Count} of {failedTrajectories} failed trajectory sample(s) " +
                    $"because MaxFailureSamples is {config.MaxFailureSamples}.");
            }

            if (string.IsNullOrWhiteSpace(config.FailureOutputPath))
            {
                Log($"{failures.Count} failed trajectory sample(s) were not written; no failure path configured.");
            }
            else
            {
                await WriteFailuresAsync(config.FailureOutputPath, failures, ct);
                Log($"Wrote {failures.Count} failed trajectory sample(s) to '{config.FailureOutputPath}'.");
            }
        }

        Log(string.Create(
            CultureInfo.InvariantCulture,
            $"Simulated {consumed} query row(s), emitted {emitted} trajectory row(s), skipped {skipped}. " +
            $"Model calls: {modelCalls}, tokens: {promptTokens}+{completionTokens}, " +
            $"latency: {latency.TotalSeconds:F2}s, estimated cost: {cost:F6}."));
    }

    private async Task<RowOutcome> ProcessRowAsync(
        long inputIndex,
        DatasetRow row,
        string blockName,
        SimulationBlockConfig config,
        Action<string> log,
        CancellationToken ct)
    {
        var (query, systemPrompt) = ReadQuery(row, blockName, config.SystemPrompt);

        log($"Row {row.LineNumber}: simulating query '{Truncate(query)}'.");

        var harness = new SimulationHarness(
            CreateChatClient(config),
            CreateToolRuntime(config),
            _delay);

        var trajectory = await harness.RunAsync(
            query,
            config,
            systemPrompt,
            message => log($"Row {row.LineNumber}: {message}"),
            ct);

        var failed = trajectory.Status != TrajectoryStatus.Completed;
        FailureSample? failure = failed
            ? new FailureSample(TrajectorySerializer.ToFailureJson(query, trajectory))
            : null;

        if (failed && !config.EmitIncompleteTrajectories)
        {
            log($"Row {row.LineNumber}: skipped {trajectory.Status} trajectory: {trajectory.Error}");
            return new RowOutcome(inputIndex, trajectory, null, failure);
        }

        if (failed)
        {
            log($"Row {row.LineNumber}: emitting {trajectory.Status} trajectory: {trajectory.Error}");
        }

        return new RowOutcome(
            inputIndex,
            trajectory,
            TrajectorySerializer.ToOaiJson(trajectory),
            failure);
    }

    private static void Accumulate(
        RowOutcome outcome,
        ref long modelCalls,
        ref long promptTokens,
        ref long completionTokens,
        ref TimeSpan latency,
        ref decimal cost,
        ref long failedTrajectories)
    {
        modelCalls += outcome.Trajectory.ModelCalls;
        promptTokens += outcome.Trajectory.PromptTokens;
        completionTokens += outcome.Trajectory.CompletionTokens;
        latency += outcome.Trajectory.ModelLatency;
        cost += outcome.Trajectory.EstimatedCost;

        if (outcome.Failure is not null)
        {
            failedTrajectories++;
        }
    }

    private static void TrackFailure(
        RowOutcome outcome,
        SimulationBlockConfig config,
        List<FailureSample> failures)
    {
        if (outcome.Failure is null)
        {
            return;
        }

        if (config.MaxFailureSamples == 0 || failures.Count < config.MaxFailureSamples)
        {
            failures.Add(outcome.Failure);
        }
    }

    private IChatCompletionClient CreateChatClient(SimulationBlockConfig config)
    {
        if (_chatClientFactory is not null)
        {
            return _chatClientFactory(config);
        }

        if (config.ModelMode == SimulationModelMode.Provider)
        {
            var credentials = _credentials
                ?? throw new InvalidOperationException(
                    "Provider simulation requires LLM credentials from application settings or environment variables.");
            return new ProviderChatCompletionClient(_httpClient, credentials, config);
        }

        return new ScriptedChatClient(config.Script, config.FinalContent);
    }

    private IToolRuntime CreateToolRuntime(SimulationBlockConfig config)
    {
        if (_toolRuntimeFactory is not null)
        {
            return _toolRuntimeFactory(config);
        }

        return config.ToolMode == ToolExecutionMode.Real
            ? new RealToolRuntime(config.Sandbox, _sandboxRunner)
            : new SimulatedToolRuntime(_delay);
    }

    private static string FormatUsage(RowOutcome outcome)
    {
        var trajectory = outcome.Trajectory;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Row {outcome.InputIndex}: {trajectory.ModelCalls} model call(s), " +
            $"tokens {trajectory.PromptTokens}+{trajectory.CompletionTokens}, " +
            $"latency {trajectory.ModelLatency.TotalSeconds:F2}s, cost {trajectory.EstimatedCost:F6}.");
    }

    private static async Task WriteFailuresAsync(
        string path,
        IReadOnlyList<FailureSample> failures,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = fullPath + ".tmp";
        var completed = false;
        try
        {
            await using (var stream = new FileStream(
                tempPath, FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: 64 * 1024, useAsync: true))
            await using (var writer = new StreamWriter(
                stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                foreach (var failure in failures)
                {
                    await writer.WriteLineAsync(failure.Json.AsMemory(), ct);
                }
            }

            File.Move(tempPath, fullPath, overwrite: true);
            completed = true;
        }
        finally
        {
            if (!completed)
            {
                TryDelete(tempPath);
            }
        }
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

    private static void TryDelete(string path)
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

    private static string Truncate(string value) =>
        value.Length <= 60 ? value : value[..57] + "...";

    private sealed record RowOutcome(
        long InputIndex,
        TrajectoryResult Trajectory,
        string? Json,
        FailureSample? Failure);

    private sealed record FailureSample(string Json);
}
