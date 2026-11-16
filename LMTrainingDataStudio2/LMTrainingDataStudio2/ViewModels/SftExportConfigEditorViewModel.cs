using CommunityToolkit.Mvvm.ComponentModel;
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

    partial void OnFieldsTextChanged(string value) => Push();

    private void Push()
    {
        var fields = FieldsText?
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        _apply(new SftExportBlockConfig(
            OutputPath ?? string.Empty,
            fields is { Length: > 0 } ? fields : null));
    }
}
