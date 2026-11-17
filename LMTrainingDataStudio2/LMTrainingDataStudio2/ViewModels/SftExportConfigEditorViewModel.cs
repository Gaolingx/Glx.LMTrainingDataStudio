using CommunityToolkit.Mvvm.ComponentModel;
using LMTrainingDataStudio2.Localization;
using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.ViewModels;

/// <summary>
/// Edits the SFT export output path and optional top-level field whitelist.
/// </summary>
public partial class SftExportConfigEditorViewModel : ObservableObject
{
    private readonly Action<SftExportBlockConfig> _apply;

    [ObservableProperty]
    private string _outputPath;

    [ObservableProperty]
    private string _fieldsText;

    [ObservableProperty]
    private string? _fieldsError;

    public SftExportConfigEditorViewModel(
        SftExportBlockConfig config,
        Action<SftExportBlockConfig> apply)
    {
        _apply = apply;
        _outputPath = config.OutputPath;
        _fieldsText = config.Fields is { Length: > 0 }
            ? string.Join(", ", config.Fields)
            : string.Empty;
    }

    partial void OnOutputPathChanged(string value) => Push();

    partial void OnFieldsTextChanged(string value)
    {
        ValidateFields(value);
        Push();
    }

    private void ValidateFields(string? text)
    {
        var fields = ParseFields(text);
        var hasInvalidName = fields.Any(field => field.Any(char.IsWhiteSpace));
        var hasDuplicate = fields
            .GroupBy(field => field, StringComparer.Ordinal)
            .Any(group => group.Count() > 1);

        FieldsError = hasInvalidName || hasDuplicate
            ? LocalizationService.L("property.sft_fields_invalid")
            : null;
    }

    private void Push()
    {
        var fields = ParseFields(FieldsText);
        _apply(new SftExportBlockConfig(
            OutputPath ?? string.Empty,
            fields.Length > 0 ? fields : null));
    }

    private static string[] ParseFields(string? text) =>
        (text ?? string.Empty).Split(
            ',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}
