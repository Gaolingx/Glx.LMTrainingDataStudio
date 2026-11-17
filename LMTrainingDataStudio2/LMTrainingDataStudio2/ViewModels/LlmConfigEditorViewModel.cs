using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LMTrainingDataStudio2.Localization;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services.Executors;

namespace LMTrainingDataStudio2.ViewModels;

public sealed record LlmProviderOption(string Id, string DisplayName);

/// <summary>
/// Edits an LLM block configuration and writes changes back to the owning block.
/// </summary>
public partial class LlmConfigEditorViewModel : ObservableObject
{
    private readonly Action<LlmBlockConfig> _apply;
    private readonly LlmBlockConfig _base;

    [ObservableProperty]
    private LlmProviderOption _selectedProvider;

    [ObservableProperty]
    private string _modelAlias;

    [ObservableProperty]
    private double _temperature;

    [ObservableProperty]
    private int _maxTokens;

    [ObservableProperty]
    private string _prompt;

    [ObservableProperty]
    private string _endpoint;

    [ObservableProperty]
    private int _timeoutSeconds;

    [ObservableProperty]
    private int _maxRetries;

    [ObservableProperty]
    private string _toolProfileName;

    [ObservableProperty]
    private string _systemPrompt;

    [ObservableProperty]
    private bool _useRowMessages;

    [ObservableProperty]
    private int _maxConcurrency;

    [ObservableProperty]
    private string _responseSchema;

    [ObservableProperty]
    private string? _schemaError;

    [ObservableProperty]
    private string _failureOutputPath;

    [ObservableProperty]
    private int _maxFailureSamples;

    [ObservableProperty]
    private double _inputCostPerMillionTokens;

    [ObservableProperty]
    private double _outputCostPerMillionTokens;

    [ObservableProperty]
    private bool _continueOnError;

    [ObservableProperty]
    private int _maxToolRounds;

    [ObservableProperty]
    private ToolExecutionModeOption _selectedToolMode;

    public LlmConfigEditorViewModel(LlmBlockConfig config, Action<LlmBlockConfig> apply)
    {
        _apply = apply;
        _base = config;
        _modelAlias = config.ModelAlias;
        _temperature = config.Temperature;
        _maxTokens = config.MaxTokens;
        _prompt = config.Prompt ?? string.Empty;
        _endpoint = config.Endpoint ?? string.Empty;
        _timeoutSeconds = config.TimeoutSeconds;
        _maxRetries = config.MaxRetries;
        _toolProfileName = config.ToolProfileName ?? string.Empty;
        _systemPrompt = config.SystemPrompt ?? string.Empty;
        _useRowMessages = config.UseRowMessages;
        _maxConcurrency = config.MaxConcurrency;
        _responseSchema = config.ResponseSchema ?? string.Empty;
        _failureOutputPath = config.FailureOutputPath ?? string.Empty;
        _maxFailureSamples = config.MaxFailureSamples;
        _inputCostPerMillionTokens = config.InputCostPerMillionTokens;
        _outputCostPerMillionTokens = config.OutputCostPerMillionTokens;
        _continueOnError = config.ContinueOnError;
        _maxToolRounds = config.MaxToolRounds;
        _selectedToolMode = ToolModes.FirstOrDefault(option => option.Mode == config.ToolMode) ?? ToolModes[0];
        _selectedProvider = Providers.FirstOrDefault(option =>
            option.Id == LlmProviderIds.Normalize(config.Provider)) ?? Providers[0];
    }

    public IReadOnlyList<LlmProviderOption> Providers { get; } = new[]
    {
        new LlmProviderOption(LlmProviderIds.OpenAiChat, "OpenAI Chat Completions"),
        new LlmProviderOption(LlmProviderIds.OpenAiResponses, "OpenAI Responses"),
        new LlmProviderOption(LlmProviderIds.Anthropic, "Anthropic Messages")
    };

    public IReadOnlyList<ToolExecutionModeOption> ToolModes { get; } = new[]
    {
        new ToolExecutionModeOption(ToolExecutionMode.Simulated, "Simulated tools"),
        new ToolExecutionModeOption(ToolExecutionMode.Real, "Real sandbox tools")
    };

    partial void OnSelectedProviderChanged(LlmProviderOption value) => Push();

    partial void OnModelAliasChanged(string value) => Push();

    partial void OnTemperatureChanged(double value) => Push();

    partial void OnMaxTokensChanged(int value) => Push();

    partial void OnPromptChanged(string value) => Push();

    partial void OnEndpointChanged(string value) => Push();

    partial void OnTimeoutSecondsChanged(int value) => Push();

    partial void OnMaxRetriesChanged(int value) => Push();

    partial void OnToolProfileNameChanged(string value) => Push();

    partial void OnSystemPromptChanged(string value) => Push();

    partial void OnUseRowMessagesChanged(bool value) => Push();

    partial void OnMaxConcurrencyChanged(int value) => Push();

    partial void OnResponseSchemaChanged(string value)
    {
        SchemaError = null;
        Push();
    }

    partial void OnFailureOutputPathChanged(string value) => Push();

    partial void OnMaxFailureSamplesChanged(int value) => Push();

    partial void OnInputCostPerMillionTokensChanged(double value) => Push();

    partial void OnOutputCostPerMillionTokensChanged(double value) => Push();

    partial void OnContinueOnErrorChanged(bool value) => Push();

    partial void OnMaxToolRoundsChanged(int value) => Push();

    partial void OnSelectedToolModeChanged(ToolExecutionModeOption value) => Push();

    [RelayCommand]
    private void ValidateSchema()
    {
        SchemaError = null;
        if (string.IsNullOrWhiteSpace(ResponseSchema))
        {
            return;
        }

        try
        {
            JsonSchemaRuleEvaluator.Parse(ResponseSchema);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            SchemaError = string.Format(LocalizationService.L("property.invalid_json_schema_format"), ex.Message);
        }
    }

    private void Push() => _apply(_base with
    {
        Provider = SelectedProvider.Id,
        ModelAlias = ModelAlias ?? string.Empty,
        Temperature = Temperature,
        MaxTokens = MaxTokens,
        Prompt = string.IsNullOrWhiteSpace(Prompt) ? null : Prompt,
        Endpoint = string.IsNullOrWhiteSpace(Endpoint) ? null : Endpoint.Trim(),
        TimeoutSeconds = TimeoutSeconds,
        MaxRetries = MaxRetries,
        ToolProfileName = string.IsNullOrWhiteSpace(ToolProfileName) ? null : ToolProfileName.Trim(),
        SystemPrompt = string.IsNullOrWhiteSpace(SystemPrompt) ? null : SystemPrompt,
        UseRowMessages = UseRowMessages,
        MaxConcurrency = MaxConcurrency,
        ResponseSchema = string.IsNullOrWhiteSpace(ResponseSchema) ? null : ResponseSchema,
        FailureOutputPath = string.IsNullOrWhiteSpace(FailureOutputPath) ? null : FailureOutputPath.Trim(),
        MaxFailureSamples = MaxFailureSamples,
        InputCostPerMillionTokens = InputCostPerMillionTokens,
        OutputCostPerMillionTokens = OutputCostPerMillionTokens,
        ContinueOnError = ContinueOnError,
        MaxToolRounds = MaxToolRounds,
        ToolMode = SelectedToolMode.Mode
    });
}
