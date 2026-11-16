using CommunityToolkit.Mvvm.ComponentModel;
using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.ViewModels;

public sealed record LlmProviderOption(string Id, string DisplayName);

/// <summary>
/// Edits an LLM block configuration and writes changes back to the owning block.
/// </summary>
public partial class LlmConfigEditorViewModel : ObservableObject
{
    private readonly Action<LlmBlockConfig> _apply;

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

    public LlmConfigEditorViewModel(LlmBlockConfig config, Action<LlmBlockConfig> apply)
    {
        _apply = apply;
        _modelAlias = config.ModelAlias;
        _temperature = config.Temperature;
        _maxTokens = config.MaxTokens;
        _prompt = config.Prompt ?? string.Empty;
        _endpoint = config.Endpoint ?? string.Empty;
        _timeoutSeconds = config.TimeoutSeconds;
        _maxRetries = config.MaxRetries;
        _selectedProvider = Providers.FirstOrDefault(option =>
            option.Id == LlmProviderIds.Normalize(config.Provider)) ?? Providers[0];
    }

    public IReadOnlyList<LlmProviderOption> Providers { get; } = new[]
    {
        new LlmProviderOption(LlmProviderIds.OpenAiChat, "OpenAI Chat Completions"),
        new LlmProviderOption(LlmProviderIds.OpenAiResponses, "OpenAI Responses"),
        new LlmProviderOption(LlmProviderIds.Anthropic, "Anthropic Messages")
    };

    partial void OnSelectedProviderChanged(LlmProviderOption value) => Push();

    partial void OnModelAliasChanged(string value) => Push();

    partial void OnTemperatureChanged(double value) => Push();

    partial void OnMaxTokensChanged(int value) => Push();

    partial void OnPromptChanged(string value) => Push();

    partial void OnEndpointChanged(string value) => Push();

    partial void OnTimeoutSecondsChanged(int value) => Push();

    partial void OnMaxRetriesChanged(int value) => Push();

    private void Push() => _apply(new LlmBlockConfig(
        SelectedProvider.Id,
        ModelAlias ?? string.Empty,
        Temperature,
        MaxTokens,
        string.IsNullOrWhiteSpace(Prompt) ? null : Prompt,
        string.IsNullOrWhiteSpace(Endpoint) ? null : Endpoint.Trim(),
        TimeoutSeconds,
        MaxRetries));
}