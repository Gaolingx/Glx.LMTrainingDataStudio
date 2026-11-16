using CommunityToolkit.Mvvm.ComponentModel;
using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.ViewModels;

/// <summary>
/// Edits the SFT export output path.
/// </summary>
public partial class SftExportConfigEditorViewModel : ObservableObject
{
    private readonly Action<SftExportBlockConfig> _apply;

    [ObservableProperty]
    private string _outputPath;

    public SftExportConfigEditorViewModel(
        SftExportBlockConfig config,
        Action<SftExportBlockConfig> apply)
    {
        _apply = apply;
        _outputPath = config.OutputPath;
    }

    partial void OnOutputPathChanged(string value) => Push();

    private void Push() => _apply(new SftExportBlockConfig(OutputPath ?? string.Empty));
}
