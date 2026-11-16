using System.Text.Json;
using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Services.Simulation;

/// <summary>
/// Mini harness that records a multi-round model/tool trajectory.
/// Enforces the tool whitelist, per-attempt timeouts, retry policy and max rounds,
/// then returns OpenAI-format messages ready for SFT export.
/// </summary>
public sealed class SimulationHarness
{
    private readonly IChatCompletionClient _client;
    private readonly IToolRuntime _toolRuntime;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public SimulationHarness(
        IChatCompletionClient client,
        IToolRuntime toolRuntime,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _client = client;
        _toolRuntime = toolRuntime;
        _delay = delay ?? ((duration, token) => Task.Delay(duration, token));
    }

    public async Task<TrajectoryResult> RunAsync(
        string query,
        SimulationBlockConfig config,
        string? systemPrompt = null,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentNullException.ThrowIfNull(config);

        if (string.IsNullOrWhiteSpace(config.ModelAlias))
        {
            throw new InvalidOperationException("Simulation block has no model alias configured.");
        }

        if (config.MaxRounds <= 0)
        {
            throw new InvalidOperationException(
                $"Simulation max rounds must be greater than 0; got {config.MaxRounds}.");
        }

        if (config.TimeoutSeconds <= 0)
        {
            throw new InvalidOperationException(
                $"Simulation model timeout must be greater than 0 seconds; got {config.TimeoutSeconds}.");
        }

        if (config.ModelMaxRetries < 0)
        {
            throw new InvalidOperationException(
                $"Simulation model max retries must not be negative; got {config.ModelMaxRetries}.");
        }

        var rules = config.Rules ?? new ToolHarnessRules();
        if (rules.DefaultTimeoutMs < 0 || rules.MaxRetries < 0 || rules.RetryDelayMs < 0)
        {
            throw new InvalidOperationException(
                "Simulation tool harness rules cannot use negative timeout, retries or retry delay.");
        }

        var declaredTools = ValidateTools(config.Tools ?? Array.Empty<SimulatedToolRule>());
        var exposedTools = FilterTools(declaredTools, rules);
        var toolRules = declaredTools.ToDictionary(tool => tool.Name, StringComparer.OrdinalIgnoreCase);

        var result = new TrajectoryResult { Status = TrajectoryStatus.Completed };
        result.Tools.AddRange(exposedTools.Select(ToDefinition));

        var effectiveSystemPrompt = string.IsNullOrWhiteSpace(systemPrompt)
            ? config.SystemPrompt
            : systemPrompt;

        if (!string.IsNullOrWhiteSpace(effectiveSystemPrompt))
        {
            result.Messages.Add(new ChatMessage { Role = "system", Content = effectiveSystemPrompt });
        }

        result.Messages.Add(new ChatMessage { Role = "user", Content = query });

        for (var round = 1; round <= config.MaxRounds; round++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Rounds = round;

            var request = new ChatCompletionRequest(
                config.ModelAlias,
                result.Messages.ToArray(),
                result.Tools.ToArray(),
                config.Temperature,
                config.MaxTokens,
                round);

            var response = await CompleteWithRetryAsync(request, config, rules, log, cancellationToken);
            result.Messages.Add(response.Message);

            var toolCalls = response.Message.ToolCalls;
            if (toolCalls is not { Count: > 0 })
            {
                log?.Invoke($"Round {round}: model finished.");
                result.Status = TrajectoryStatus.Completed;
                return result;
            }

            log?.Invoke($"Round {round}: model requested {toolCalls.Count} tool call(s).");

            foreach (var call in toolCalls)
            {
                cancellationToken.ThrowIfCancellationRequested();
                result.ToolCallCount++;

                var name = call.Function.Name;
                if (!exposedTools.Any(tool =>
                        string.Equals(tool.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    var error = $"Tool '{name}' is not available in this run.";
                    log?.Invoke(error);
                    result.Messages.Add(CreateToolMessage(call, new { error, attempts = 0 }));
                    continue;
                }

                var outcome = await ExecuteToolWithRetryAsync(
                    toolRules[name], call, rules, log, cancellationToken);
                result.ToolRetryCount += outcome.Attempts - 1;

                if (!outcome.Success && rules.AbortOnToolFailure)
                {
                    result.Status = TrajectoryStatus.Aborted;
                    result.Error = $"Tool '{name}' failed after {outcome.Attempts} attempt(s): {outcome.Error}";
                    log?.Invoke($"Trajectory aborted: {result.Error}");
                    return result;
                }

                result.Messages.Add(outcome.Success
                    ? CreateToolMessage(call, outcome.Content!)
                    : CreateToolMessage(call, new { error = outcome.Error, attempts = outcome.Attempts }));
            }
        }

        result.Status = TrajectoryStatus.MaxRoundsExceeded;
        result.Error = $"Trajectory exceeded the maximum of {config.MaxRounds} round(s).";
        log?.Invoke(result.Error);
        return result;
    }

    private async Task<ChatCompletionResponse> CompleteWithRetryAsync(
        ChatCompletionRequest request,
        SimulationBlockConfig config,
        ToolHarnessRules rules,
        Action<string>? log,
        CancellationToken ct)
    {
        var attempts = config.ModelMaxRetries + 1;
        Exception? lastFailure = null;

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(config.TimeoutSeconds));

            try
            {
                return await _client.CompleteAsync(request, timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                lastFailure = new TimeoutException(
                    $"Simulated model call timed out after {config.TimeoutSeconds}s.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastFailure = ex;
            }

            log?.Invoke($"Round {request.Round}: model attempt {attempt + 1} failed: {lastFailure.Message}");

            if (attempt + 1 < attempts && rules.RetryDelayMs > 0)
            {
                await _delay(TimeSpan.FromMilliseconds(rules.RetryDelayMs), ct);
            }
        }

        throw new InvalidOperationException(
            $"Simulated model call failed after {attempts} attempt(s): {lastFailure?.Message ?? "unknown error"}",
            lastFailure);
    }

    private async Task<ToolOutcome> ExecuteToolWithRetryAsync(
        SimulatedToolRule rule,
        ToolCall call,
        ToolHarnessRules rules,
        Action<string>? log,
        CancellationToken ct)
    {
        var attempts = rules.MaxRetries + 1;
        var timeout = rule.TimeoutMs > 0 ? rule.TimeoutMs : rules.DefaultTimeoutMs;
        Exception? lastFailure = null;

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (timeout > 0)
            {
                timeoutCts.CancelAfter(timeout);
            }

            try
            {
                var content = await _toolRuntime.ExecuteAsync(rule, call, timeoutCts.Token);
                if (attempt > 0)
                {
                    log?.Invoke($"Tool '{rule.Name}' succeeded on attempt {attempt + 1}.");
                }

                return new ToolOutcome(true, content, null, attempt + 1);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                lastFailure = new TimeoutException(
                    $"Tool '{rule.Name}' timed out after {timeout} ms.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastFailure = ex;
            }

            log?.Invoke(
                $"Tool '{rule.Name}' attempt {attempt + 1}/{attempts} failed: {lastFailure.Message}");

            if (attempt + 1 < attempts && rules.RetryDelayMs > 0)
            {
                await _delay(TimeSpan.FromMilliseconds(rules.RetryDelayMs), ct);
            }
        }

        return new ToolOutcome(false, null, lastFailure?.Message ?? "unknown error", attempts);
    }

    private static SimulatedToolRule[] ValidateTools(SimulatedToolRule[] tools)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tool in tools)
        {
            if (string.IsNullOrWhiteSpace(tool.Name))
            {
                throw new InvalidOperationException("Simulated tools must have a non-empty name.");
            }

            if (!seen.Add(tool.Name))
            {
                throw new InvalidOperationException($"Duplicate simulated tool name: '{tool.Name}'.");
            }

            if (tool.FailuresBeforeSuccess < 0 || tool.DelayMs < 0 || tool.TimeoutMs < 0)
            {
                throw new InvalidOperationException(
                    $"Tool '{tool.Name}' cannot use negative delay, timeout or failure counts.");
            }
        }

        return tools;
    }

    private static SimulatedToolRule[] FilterTools(
        SimulatedToolRule[] tools,
        ToolHarnessRules rules)
    {
        if (rules.AllowedTools is null)
        {
            return tools;
        }

        var allowed = new HashSet<string>(rules.AllowedTools, StringComparer.OrdinalIgnoreCase);
        return tools.Where(tool => allowed.Contains(tool.Name)).ToArray();
    }

    private static ToolDefinition ToDefinition(SimulatedToolRule rule)
    {
        Dictionary<string, object>? parameters = null;
        if (!string.IsNullOrWhiteSpace(rule.ParametersJson))
        {
            try
            {
                parameters = JsonSerializer.Deserialize<Dictionary<string, object>>(rule.ParametersJson);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    $"Tool '{rule.Name}' has invalid parameters JSON: {ex.Message}", ex);
            }
        }

        parameters ??= new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object>()
        };

        return new ToolDefinition
        {
            Function = new ToolFunction
            {
                Name = rule.Name,
                Description = rule.Description,
                Parameters = parameters
            }
        };
    }

    private static ChatMessage CreateToolMessage(ToolCall call, object content) => new()
    {
        Role = "tool",
        ToolCallId = call.Id ?? string.Empty,
        Content = content is string text ? text : JsonSerializer.Serialize(content)
    };

    private sealed record ToolOutcome(bool Success, string? Content, string? Error, int Attempts);
}
