using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Services.Simulation;

/// <summary>
/// Executes one tool attempt. Retry and timeout policy live in the harness.
/// </summary>
public interface IToolRuntime
{
    Task<string> ExecuteAsync(
        SimulatedToolRule rule,
        ToolCall call,
        CancellationToken cancellationToken);
}

/// <summary>
/// Deterministic tool runtime driven by <see cref="SimulatedToolRule"/>.
/// Failure counters reset per trajectory because one instance is created per run.
/// </summary>
public sealed class SimulatedToolRuntime : IToolRuntime
{
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Dictionary<string, int> _attempts = new(StringComparer.OrdinalIgnoreCase);

    public SimulatedToolRuntime(Func<TimeSpan, CancellationToken, Task>? delay = null) =>
        _delay = delay ?? ((duration, token) => Task.Delay(duration, token));

    public async Task<string> ExecuteAsync(
        SimulatedToolRule rule,
        ToolCall call,
        CancellationToken cancellationToken)
    {
        var attempt = _attempts.GetValueOrDefault(rule.Name) + 1;
        _attempts[rule.Name] = attempt;

        if (rule.DelayMs > 0)
        {
            await _delay(TimeSpan.FromMilliseconds(rule.DelayMs), cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (rule.Behavior == SimulatedToolBehavior.Timeout)
        {
            throw new TimeoutException($"Tool '{rule.Name}' timed out.");
        }

        if (rule.Behavior == SimulatedToolBehavior.Failure || attempt <= rule.FailuresBeforeSuccess)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(rule.ErrorMessage)
                    ? $"Tool '{rule.Name}' failed."
                    : rule.ErrorMessage);
        }

        var arguments = call.Function.Arguments?.ToString() ?? "{}";
        return rule.ResponseTemplate
            .Replace("{{name}}", rule.Name, StringComparison.Ordinal)
            .Replace("{{arguments}}", arguments, StringComparison.Ordinal);
    }
}
