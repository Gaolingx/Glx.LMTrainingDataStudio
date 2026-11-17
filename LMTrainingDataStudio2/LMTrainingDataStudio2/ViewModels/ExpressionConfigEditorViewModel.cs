using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services.Executors;

namespace LMTrainingDataStudio2.ViewModels;

/// <summary>
/// Edits an Expression template, its field aliases and renders a preview against sample JSON.
/// </summary>
public partial class ExpressionConfigEditorViewModel : ObservableObject
{
    private const string DefaultSampleJson =
        """{"messages":[{"role":"user","content":"hello"}],"score":42}""";

    private readonly Action<ExpressionBlockConfig> _apply;
    private Dictionary<string, string> _fields;

    [ObservableProperty]
    private string _template;

    [ObservableProperty]
    private string _fieldsJson;

    [ObservableProperty]
    private string _sampleJson = DefaultSampleJson;

    [ObservableProperty]
    private string? _fieldsError;

    [ObservableProperty]
    private string? _templateError;

    [ObservableProperty]
    private string _previewOutput = string.Empty;

    [ObservableProperty]
    private string? _previewError;

    public ExpressionConfigEditorViewModel(ExpressionBlockConfig config, Action<ExpressionBlockConfig> apply)
    {
        _apply = apply;
        _fields = new Dictionary<string, string>(config.Fields);
        _template = config.Template;
        _fieldsJson = JsonSerializer.Serialize(
            config.Fields,
            new JsonSerializerOptions { WriteIndented = true });
        RefreshTemplateValidation();
    }

    partial void OnTemplateChanged(string value)
    {
        RefreshTemplateValidation();
        Push();
    }

    partial void OnFieldsJsonChanged(string value)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(value ?? "{}");
            _fields = parsed ?? new Dictionary<string, string>();
            FieldsError = null;
            Push();
        }
        catch (JsonException ex)
        {
            FieldsError = $"Fields must be a JSON object of alias -> path: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ValidateTemplate()
    {
        RefreshTemplateValidation();
        PreviewError = TemplateError;
    }

    [RelayCommand]
    private async Task RenderPreviewAsync()
    {
        PreviewOutput = string.Empty;
        PreviewError = null;

        try
        {
            using var document = JsonDocument.Parse(
                string.IsNullOrWhiteSpace(SampleJson) ? "{}" : SampleJson);
            var root = document.RootElement;

            if (!string.IsNullOrWhiteSpace(Template))
            {
                PreviewOutput = await TemplateRenderer.RenderAsync(Template, root, _fields);
            }
            else
            {
                PreviewOutput = JsonSerializer.Serialize(_fields.ToDictionary(
                    pair => pair.Key,
                    pair => TemplateRenderer.ResolveString(root, pair.Value)));
            }

            try
            {
                using var _ = JsonDocument.Parse(PreviewOutput);
            }
            catch (JsonException ex)
            {
                PreviewError = $"Rendered output is not valid JSON: {ex.Message}";
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            PreviewError = ex.Message;
        }
    }

    private void RefreshTemplateValidation()
    {
        var result = TemplateRenderer.Validate(Template);
        TemplateError = result.IsValid ? null : result.Error;
    }

    private void Push() => _apply(new ExpressionBlockConfig(
        Template ?? string.Empty,
        new Dictionary<string, string>(_fields)));
}
