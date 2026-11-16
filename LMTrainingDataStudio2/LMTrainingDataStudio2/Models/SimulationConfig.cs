namespace LMTrainingDataStudio2.Models;

/// <summary>
/// How a query source expands its query list.
/// </summary>
public enum QuerySetMode
{
    /// <summary>Use the first query and repeat it.</summary>
    SameQuery,

    /// <summary>Emit every configured query (repeated when <see cref="QuerySourceBlockConfig.Repeat"/> is greater than 1).</summary>
    MultipleQueries
}

/// <summary>
/// Produces user queries for simulated API runs.
/// </summary>
public sealed record QuerySourceBlockConfig(
    string[] Queries,
    QuerySetMode Mode = QuerySetMode.MultipleQueries,
    int Repeat = 1,
    string? SystemPrompt = null) : BlockConfig;

/// <summary>
/// Deterministic behavior of a simulated tool.
/// </summary>
public enum SimulatedToolBehavior
{
    /// <summary>Return <see cref="SimulatedToolRule.ResponseTemplate"/>.</summary>
    Success,

    /// <summary>Always fail with <see cref="SimulatedToolRule.ErrorMessage"/>.</summary>
    Failure,

    /// <summary>Always time out; the harness retry policy decides what happens next.</summary>
    Timeout
}

/// <summary>
/// Declares one simulated tool: its OpenAI schema plus failure/latency behavior.
/// </summary>
public sealed record SimulatedToolRule
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string ParametersJson { get; init; } = """{"type":"object","properties":{}}""";
    public SimulatedToolBehavior Behavior { get; init; } = SimulatedToolBehavior.Success;

    /// <summary>Supports <c>{{name}}</c> and <c>{{arguments}}</c> placeholders.</summary>
    public string ResponseTemplate { get; init; } = "ok";

    public string ErrorMessage { get; init; } = "Simulated tool failure.";

    /// <summary>Simulated execution latency before the tool answers (or fails).</summary>
    public int DelayMs { get; init; }

    /// <summary>Fail this many attempts before succeeding; ignored when <see cref="Behavior"/> is not Success.</summary>
    public int FailuresBeforeSuccess { get; init; }

    /// <summary>Per-tool timeout in milliseconds; 0 uses the harness default.</summary>
    public int TimeoutMs { get; init; }
}

/// <summary>
/// Rules enforced by the mini harness around tool calls and model calls.
/// </summary>
public sealed record ToolHarnessRules
{
    /// <summary>
    /// Whitelist of tool names exposed to the model. Null means all declared tools;
    /// an empty array means no tools are exposed.
    /// </summary>
    public string[]? AllowedTools { get; init; }

    /// <summary>Timeout applied to a single tool attempt.</summary>
    public int DefaultTimeoutMs { get; init; } = 30_000;

    /// <summary>Retries after the first failed tool attempt.</summary>
    public int MaxRetries { get; init; } = 1;

    public int RetryDelayMs { get; init; }

    /// <summary>When true the whole trajectory fails instead of feeding the tool error back to the model.</summary>
    public bool AbortOnToolFailure { get; init; }
}

/// <summary>
/// A scripted assistant turn for the simulated API.
/// </summary>
public sealed record SimulatedTurn(string? Content, SimulatedToolCallSpec[]? ToolCalls);

/// <summary>
/// A scripted tool call; arguments must be a JSON object encoded as a string.
/// </summary>
public sealed record SimulatedToolCallSpec(string Name, string ArgumentsJson = "{}");

/// <summary>
/// Selects the chat-completion transport used by a simulation block.
/// </summary>
public enum SimulationModelMode
{
    /// <summary>Deterministic scripted turns; no network access.</summary>
    Scripted,

    /// <summary>Real provider HTTP calls with tool calling.</summary>
    Provider
}

/// <summary>
/// Simulated API + mini harness node. Runs a multi-round model/tool loop per query
/// and emits OpenAI chat messages ready for SFT export.
/// </summary>
public sealed record SimulationBlockConfig(
    string ModelAlias,
    int MaxRounds = 8,
    string? SystemPrompt = null,
    SimulatedToolRule[]? Tools = null,
    ToolHarnessRules? Rules = null,
    SimulatedTurn[]? Script = null,
    string? FinalContent = null,
    string Provider = LlmProviderIds.OpenAiChat,
    double Temperature = 0.7,
    int MaxTokens = 2048,
    int TimeoutSeconds = 100,
    int ModelMaxRetries = 0,
    bool EmitIncompleteTrajectories = false,
    SimulationModelMode ModelMode = SimulationModelMode.Scripted,
    ToolExecutionMode ToolMode = ToolExecutionMode.Simulated,
    SandboxLimits? Sandbox = null,
    string? Endpoint = null,
    int MaxConcurrency = 1,
    string? FailureOutputPath = null,
    int MaxFailureSamples = 100,
    double InputCostPerMillionTokens = 0,
    double OutputCostPerMillionTokens = 0,
    string? ToolProfileName = null) : BlockConfig;

/// <summary>
/// Standalone cleanup node for OAI message rows.
/// </summary>
public sealed record CleanerBlockConfig(DatasetCleanupRules Rules) : BlockConfig;

/// <summary>
/// Writes rows to a JSONL file in OpenAI messages format while passing them downstream.
/// When <see cref="Fields"/> is non-empty, only those top-level fields are written.
/// </summary>
public sealed record SftExportBlockConfig(string OutputPath, string[]? Fields = null) : BlockConfig;

/// <summary>
/// Prebuilt deterministic tool rules for the tools most agents use in traces.
/// These are simulated responses, not real process or network execution.
/// </summary>
public static class SimulatedToolCatalog
{
    public static SimulatedToolRule Python() => new()
    {
        Name = "python",
        Description = "Execute Python source code and return its output.",
        ParametersJson =
            """{"type":"object","properties":{"code":{"type":"string","description":"Python source to execute."}},"required":["code"]}""",
        ResponseTemplate = "[simulated python] executed: {{arguments}}"
    };

    public static SimulatedToolRule WebFetch() => new()
    {
        Name = "web_fetch",
        Description = "Fetch a URL and return its text content.",
        ParametersJson =
            """{"type":"object","properties":{"url":{"type":"string","description":"Absolute URL to fetch."}},"required":["url"]}""",
        ResponseTemplate = "[simulated web_fetch] fetched: {{arguments}}"
    };

    public static SimulatedToolRule PowerShell() => new()
    {
        Name = "powershell",
        Description = "Run a PowerShell command.",
        ParametersJson =
            """{"type":"object","properties":{"command":{"type":"string","description":"Command to execute."}},"required":["command"]}""",
        ResponseTemplate = "[simulated powershell] completed: {{arguments}}"
    };

    public static SimulatedToolRule Bash() => new()
    {
        Name = "bash",
        Description = "Run a bash command.",
        ParametersJson =
            """{"type":"object","properties":{"command":{"type":"string","description":"Command to execute."}},"required":["command"]}""",
        ResponseTemplate = "[simulated bash] completed: {{arguments}}"
    };

    public static SimulatedToolRule[] Common() => new[] { Python(), WebFetch(), PowerShell(), Bash() };
}
