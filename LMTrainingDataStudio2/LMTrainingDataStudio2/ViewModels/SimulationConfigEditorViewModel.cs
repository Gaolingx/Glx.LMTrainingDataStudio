using CommunityToolkit.Mvvm.ComponentModel;
using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.ViewModels;

public sealed record SimulationModelModeOption(SimulationModelMode Mode, string DisplayName);

public sealed record ToolExecutionModeOption(ToolExecutionMode Mode, string DisplayName);

/// <summary>
/// Edits the main simulation block settings. Tools, harness rules and script
/// remain available through the generic Config JSON editor.
/// </summary>
public partial class SimulationConfigEditorViewModel : ObservableObject
{
    private readonly SimulationBlockConfig _base;
    private readonly Action<SimulationBlockConfig> _apply;

    [ObservableProperty]
    private LlmProviderOption _selectedProvider;

    [ObservableProperty]
    private SimulationModelModeOption _selectedModelMode;

    [ObservableProperty]
    private ToolExecutionModeOption _selectedToolMode;

    [ObservableProperty]
    private string _modelAlias;

    [ObservableProperty]
    private string _endpoint;

    [ObservableProperty]
    private int _maxRounds;

    [ObservableProperty]
    private int _maxConcurrency;

    [ObservableProperty]
    private double _temperature;

    [ObservableProperty]
    private int _maxTokens;

    [ObservableProperty]
    private int _timeoutSeconds;

    [ObservableProperty]
    private int _modelMaxRetries;

    [ObservableProperty]
    private bool _emitIncompleteTrajectories;

    [ObservableProperty]
    private string _systemPrompt;

    [ObservableProperty]
    private string _finalContent;

    [ObservableProperty]
    private string _failureOutputPath;

    [ObservableProperty]
    private int _maxFailureSamples;

    [ObservableProperty]
    private double _inputCostPerMillionTokens;

    [ObservableProperty]
    private double _outputCostPerMillionTokens;

    [ObservableProperty]
    private int _sandboxTimeoutMs;

    [ObservableProperty]
    private int _sandboxMaxOutputBytes;

    [ObservableProperty]
    private int _sandboxMaxMemoryMb;

    [ObservableProperty]
    private int _sandboxMaxProcesses;

    [ObservableProperty]
    private bool _sandboxAllowNetwork;

    [ObservableProperty]
    private bool _sandboxAllowPrivateNetwork;

    [ObservableProperty]
    private string _pythonExecutable;

    [ObservableProperty]
    private string _powerShellExecutable;

    [ObservableProperty]
    private string _bashExecutable;

    [ObservableProperty]
    private string _toolProfileName;

    public SimulationConfigEditorViewModel(
        SimulationBlockConfig config,
        Action<SimulationBlockConfig> apply)
    {
        _base = config;
        _apply = apply;

        var sandbox = config.Sandbox ?? new SandboxLimits();
        _modelAlias = config.ModelAlias;
        _endpoint = config.Endpoint ?? string.Empty;
        _maxRounds = config.MaxRounds;
        _maxConcurrency = config.MaxConcurrency;
        _temperature = config.Temperature;
        _maxTokens = config.MaxTokens;
        _timeoutSeconds = config.TimeoutSeconds;
        _modelMaxRetries = config.ModelMaxRetries;
        _emitIncompleteTrajectories = config.EmitIncompleteTrajectories;
        _systemPrompt = config.SystemPrompt ?? string.Empty;
        _finalContent = config.FinalContent ?? string.Empty;
        _failureOutputPath = config.FailureOutputPath ?? string.Empty;
        _maxFailureSamples = config.MaxFailureSamples;
        _inputCostPerMillionTokens = config.InputCostPerMillionTokens;
        _outputCostPerMillionTokens = config.OutputCostPerMillionTokens;
        _sandboxTimeoutMs = sandbox.TimeoutMs;
        _sandboxMaxOutputBytes = sandbox.MaxOutputBytes;
        _sandboxMaxMemoryMb = sandbox.MaxMemoryMb;
        _sandboxMaxProcesses = sandbox.MaxProcesses;
        _sandboxAllowNetwork = sandbox.AllowNetwork;
        _sandboxAllowPrivateNetwork = sandbox.AllowPrivateNetwork;
        _pythonExecutable = sandbox.PythonExecutable ?? string.Empty;
        _powerShellExecutable = sandbox.PowerShellExecutable ?? string.Empty;
        _bashExecutable = sandbox.BashExecutable ?? string.Empty;
        _toolProfileName = config.ToolProfileName ?? string.Empty;

        _selectedProvider = Providers.FirstOrDefault(option =>
            option.Id == LlmProviderIds.Normalize(config.Provider)) ?? Providers[0];
        _selectedModelMode = ModelModes.FirstOrDefault(option => option.Mode == config.ModelMode) ?? ModelModes[0];
        _selectedToolMode = ToolModes.FirstOrDefault(option => option.Mode == config.ToolMode) ?? ToolModes[0];
    }

    public IReadOnlyList<LlmProviderOption> Providers { get; } = new[]
    {
        new LlmProviderOption(LlmProviderIds.OpenAiChat, "OpenAI Chat Completions"),
        new LlmProviderOption(LlmProviderIds.OpenAiResponses, "OpenAI Responses"),
        new LlmProviderOption(LlmProviderIds.Anthropic, "Anthropic Messages")
    };

    public IReadOnlyList<SimulationModelModeOption> ModelModes { get; } = new[]
    {
        new SimulationModelModeOption(SimulationModelMode.Scripted, "Scripted (offline)"),
        new SimulationModelModeOption(SimulationModelMode.Provider, "Provider (real HTTP)")
    };

    public IReadOnlyList<ToolExecutionModeOption> ToolModes { get; } = new[]
    {
        new ToolExecutionModeOption(ToolExecutionMode.Simulated, "Simulated tools"),
        new ToolExecutionModeOption(ToolExecutionMode.Real, "Real sandbox tools")
    };

    partial void OnSelectedProviderChanged(LlmProviderOption value) => Push();

    partial void OnSelectedModelModeChanged(SimulationModelModeOption value) => Push();

    partial void OnSelectedToolModeChanged(ToolExecutionModeOption value) => Push();

    partial void OnModelAliasChanged(string value) => Push();

    partial void OnEndpointChanged(string value) => Push();

    partial void OnMaxRoundsChanged(int value) => Push();

    partial void OnMaxConcurrencyChanged(int value) => Push();

    partial void OnTemperatureChanged(double value) => Push();

    partial void OnMaxTokensChanged(int value) => Push();

    partial void OnTimeoutSecondsChanged(int value) => Push();

    partial void OnModelMaxRetriesChanged(int value) => Push();

    partial void OnEmitIncompleteTrajectoriesChanged(bool value) => Push();

    partial void OnSystemPromptChanged(string value) => Push();

    partial void OnFinalContentChanged(string value) => Push();

    partial void OnFailureOutputPathChanged(string value) => Push();

    partial void OnMaxFailureSamplesChanged(int value) => Push();

    partial void OnInputCostPerMillionTokensChanged(double value) => Push();

    partial void OnOutputCostPerMillionTokensChanged(double value) => Push();

    partial void OnSandboxTimeoutMsChanged(int value) => Push();

    partial void OnSandboxMaxOutputBytesChanged(int value) => Push();

    partial void OnSandboxMaxMemoryMbChanged(int value) => Push();

    partial void OnSandboxMaxProcessesChanged(int value) => Push();

    partial void OnSandboxAllowNetworkChanged(bool value) => Push();

    partial void OnSandboxAllowPrivateNetworkChanged(bool value) => Push();

    partial void OnPythonExecutableChanged(string value) => Push();

    partial void OnPowerShellExecutableChanged(string value) => Push();

    partial void OnBashExecutableChanged(string value) => Push();

    partial void OnToolProfileNameChanged(string value) => Push();

    private void Push()
    {
        var sandbox = (_base.Sandbox ?? new SandboxLimits()) with
        {
            TimeoutMs = SandboxTimeoutMs,
            MaxOutputBytes = SandboxMaxOutputBytes,
            MaxMemoryMb = SandboxMaxMemoryMb,
            MaxProcesses = SandboxMaxProcesses,
            AllowNetwork = SandboxAllowNetwork,
            AllowPrivateNetwork = SandboxAllowPrivateNetwork,
            PythonExecutable = NullIfBlank(PythonExecutable),
            PowerShellExecutable = NullIfBlank(PowerShellExecutable),
            BashExecutable = NullIfBlank(BashExecutable)
        };

        _apply(_base with
        {
            Provider = SelectedProvider.Id,
            ModelMode = SelectedModelMode.Mode,
            ToolMode = SelectedToolMode.Mode,
            ModelAlias = ModelAlias ?? string.Empty,
            Endpoint = NullIfBlank(Endpoint),
            MaxRounds = MaxRounds,
            MaxConcurrency = MaxConcurrency,
            Temperature = Temperature,
            MaxTokens = MaxTokens,
            TimeoutSeconds = TimeoutSeconds,
            ModelMaxRetries = ModelMaxRetries,
            EmitIncompleteTrajectories = EmitIncompleteTrajectories,
            SystemPrompt = NullIfBlank(SystemPrompt),
            FinalContent = NullIfBlank(FinalContent),
            FailureOutputPath = NullIfBlank(FailureOutputPath),
            MaxFailureSamples = MaxFailureSamples,
            InputCostPerMillionTokens = InputCostPerMillionTokens,
            OutputCostPerMillionTokens = OutputCostPerMillionTokens,
            Sandbox = sandbox,
            Tools = _base.Tools,
            Rules = _base.Rules,
            Script = _base.Script,
            ToolProfileName = NullIfBlank(ToolProfileName)
        });
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
