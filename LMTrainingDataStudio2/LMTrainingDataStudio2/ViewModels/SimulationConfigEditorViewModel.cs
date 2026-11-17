using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.ViewModels;

public sealed record SimulationModelModeOption(SimulationModelMode Mode, string DisplayName);

public sealed record ToolExecutionModeOption(ToolExecutionMode Mode, string DisplayName);

/// <summary>
/// One simulated tool row in the visual tools editor.
/// </summary>
public partial class SimulationToolEditorViewModel : ObservableObject
{
    private readonly Action _changed;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _description;

    [ObservableProperty]
    private SimulatedToolBehavior _behavior;

    [ObservableProperty]
    private string _parametersJson;

    [ObservableProperty]
    private string _responseTemplate;

    [ObservableProperty]
    private string _errorMessage;

    [ObservableProperty]
    private int _delayMs;

    [ObservableProperty]
    private int _failuresBeforeSuccess;

    [ObservableProperty]
    private int _timeoutMs;

    public SimulationToolEditorViewModel(SimulatedToolRule rule, Action changed)
    {
        _changed = changed;
        _name = rule.Name;
        _description = rule.Description;
        _behavior = rule.Behavior;
        _parametersJson = rule.ParametersJson;
        _responseTemplate = rule.ResponseTemplate;
        _errorMessage = rule.ErrorMessage;
        _delayMs = rule.DelayMs;
        _failuresBeforeSuccess = rule.FailuresBeforeSuccess;
        _timeoutMs = rule.TimeoutMs;
    }

    public IReadOnlyList<SimulatedToolBehavior> Behaviors { get; } = Enum.GetValues<SimulatedToolBehavior>();

    public SimulatedToolRule ToRule() => new()
    {
        Name = Name ?? string.Empty,
        Description = Description ?? string.Empty,
        Behavior = Behavior,
        ParametersJson = string.IsNullOrWhiteSpace(ParametersJson)
            ? """{"type":"object","properties":{}}"""
            : ParametersJson,
        ResponseTemplate = ResponseTemplate ?? "ok",
        ErrorMessage = string.IsNullOrWhiteSpace(ErrorMessage) ? "Simulated tool failure." : ErrorMessage,
        DelayMs = DelayMs,
        FailuresBeforeSuccess = FailuresBeforeSuccess,
        TimeoutMs = TimeoutMs
    };

    partial void OnNameChanged(string value) => _changed();

    partial void OnDescriptionChanged(string value) => _changed();

    partial void OnBehaviorChanged(SimulatedToolBehavior value) => _changed();

    partial void OnParametersJsonChanged(string value) => _changed();

    partial void OnResponseTemplateChanged(string value) => _changed();

    partial void OnErrorMessageChanged(string value) => _changed();

    partial void OnDelayMsChanged(int value) => _changed();

    partial void OnFailuresBeforeSuccessChanged(int value) => _changed();

    partial void OnTimeoutMsChanged(int value) => _changed();
}

/// <summary>
/// One scripted assistant turn in the visual script editor.
/// </summary>
public partial class SimulationScriptTurnEditorViewModel : ObservableObject
{
    private readonly Action _changed;

    [ObservableProperty]
    private string _content;

    [ObservableProperty]
    private string _toolCallsJson;

    [ObservableProperty]
    private string? _toolCallsError;

    public SimulationScriptTurnEditorViewModel(SimulatedTurn turn, Action changed)
    {
        _changed = changed;
        _content = turn.Content ?? string.Empty;
        _toolCallsJson = turn.ToolCalls is { Length: > 0 }
            ? JsonSerializer.Serialize(turn.ToolCalls)
            : "[]";
    }

    public SimulatedTurn ToTurn()
    {
        ToolCallsError = null;
        if (string.IsNullOrWhiteSpace(ToolCallsJson))
        {
            return new SimulatedTurn(NullIfBlank(Content), null);
        }

        try
        {
            var calls = JsonSerializer.Deserialize<SimulatedToolCallSpec[]>(ToolCallsJson);
            return new SimulatedTurn(NullIfBlank(Content), calls);
        }
        catch (JsonException ex)
        {
            ToolCallsError = $"Invalid tool calls JSON: {ex.Message}";
            return new SimulatedTurn(NullIfBlank(Content), null);
        }
    }

    partial void OnContentChanged(string value) => _changed();

    partial void OnToolCallsJsonChanged(string value) => _changed();

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}

/// <summary>
/// Edits the main simulation block settings. Tools, harness rules and script
/// remain available through the generic Config JSON editor.
/// </summary>
public partial class SimulationConfigEditorViewModel : ObservableObject
{
    private readonly SimulationBlockConfig _base;
    private readonly Action<SimulationBlockConfig> _apply;
    private bool _suppressPush;

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
    private ModelPricePreset _selectedPricePreset;

    [ObservableProperty]
    private int _requestsPerMinute;

    [ObservableProperty]
    private bool _streamDeltas;

    [ObservableProperty]
    private string _allowedToolsText;

    [ObservableProperty]
    private int _toolTimeoutMs;

    [ObservableProperty]
    private int _toolMaxRetries;

    [ObservableProperty]
    private int _toolRetryDelayMs;

    [ObservableProperty]
    private bool _abortOnToolFailure;

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
        _selectedPricePreset = ModelPriceCatalog.Find(
            config.InputCostPerMillionTokens, config.OutputCostPerMillionTokens);
        _requestsPerMinute = config.RequestsPerMinute;
        _streamDeltas = config.StreamDeltas;
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

        var rules = config.Rules ?? new ToolHarnessRules();
        _allowedToolsText = rules.AllowedTools is { Length: > 0 }
            ? string.Join(", ", rules.AllowedTools)
            : string.Empty;
        _toolTimeoutMs = rules.DefaultTimeoutMs;
        _toolMaxRetries = rules.MaxRetries;
        _toolRetryDelayMs = rules.RetryDelayMs;
        _abortOnToolFailure = rules.AbortOnToolFailure;

        foreach (var tool in config.Tools ?? Array.Empty<SimulatedToolRule>())
        {
            Tools.Add(new SimulationToolEditorViewModel(tool, Push));
        }

        foreach (var turn in config.Script ?? Array.Empty<SimulatedTurn>())
        {
            ScriptTurns.Add(new SimulationScriptTurnEditorViewModel(turn, Push));
        }

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

    public IReadOnlyList<ModelPricePreset> PricePresets { get; } = ModelPriceCatalog.Presets;

    public ObservableCollection<SimulationToolEditorViewModel> Tools { get; } = new();

    public ObservableCollection<SimulationScriptTurnEditorViewModel> ScriptTurns { get; } = new();

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

    partial void OnSelectedPricePresetChanged(ModelPricePreset value)
    {
        if (value is null || _suppressPush) return;

        _suppressPush = true;
        try
        {
            InputCostPerMillionTokens = value.InputPerMillionTokens;
            OutputCostPerMillionTokens = value.OutputPerMillionTokens;
        }
        finally
        {
            _suppressPush = false;
        }

        Push();
    }

    partial void OnRequestsPerMinuteChanged(int value) => Push();

    partial void OnStreamDeltasChanged(bool value) => Push();

    partial void OnAllowedToolsTextChanged(string value) => Push();

    partial void OnToolTimeoutMsChanged(int value) => Push();

    partial void OnToolMaxRetriesChanged(int value) => Push();

    partial void OnToolRetryDelayMsChanged(int value) => Push();

    partial void OnAbortOnToolFailureChanged(bool value) => Push();

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

    [RelayCommand]
    private void AddTool()
    {
        Tools.Add(new SimulationToolEditorViewModel(new SimulatedToolRule { Name = "new_tool" }, Push));
        Push();
    }

    [RelayCommand]
    private void RemoveTool(SimulationToolEditorViewModel? tool)
    {
        if (tool is null || !Tools.Remove(tool)) return;
        Push();
    }

    [RelayCommand]
    private void AddScriptTurn()
    {
        ScriptTurns.Add(new SimulationScriptTurnEditorViewModel(new SimulatedTurn(null, null), Push));
        Push();
    }

    [RelayCommand]
    private void RemoveScriptTurn(SimulationScriptTurnEditorViewModel? turn)
    {
        if (turn is null || !ScriptTurns.Remove(turn)) return;
        Push();
    }

    private void Push()
    {
        if (_suppressPush) return;

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

        var allowedTools = (AllowedToolsText ?? string.Empty)
            .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var rules = (_base.Rules ?? new ToolHarnessRules()) with
        {
            AllowedTools = allowedTools.Length == 0 ? null : allowedTools,
            DefaultTimeoutMs = ToolTimeoutMs,
            MaxRetries = ToolMaxRetries,
            RetryDelayMs = ToolRetryDelayMs,
            AbortOnToolFailure = AbortOnToolFailure
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
            Tools = Tools.Select(tool => tool.ToRule()).ToArray(),
            Rules = rules,
            Script = ScriptTurns.Select(turn => turn.ToTurn()).ToArray(),
            ToolProfileName = NullIfBlank(ToolProfileName),
            RequestsPerMinute = RequestsPerMinute,
            StreamDeltas = StreamDeltas
        });
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
