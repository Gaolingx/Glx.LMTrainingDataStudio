using CommunityToolkit.Mvvm.ComponentModel;
using LMTrainingDataStudio2.Localization;
using LMTrainingDataStudio2.Services;

namespace LMTrainingDataStudio2.ViewModels;

/// <summary>
/// One bundled workflow preset shown in the Block Library and the File menu.
/// </summary>
public sealed class RecipePresetViewModel : ObservableObject
{
    public RecipePresetViewModel(RecipePreset preset) => Preset = preset;

    public RecipePreset Preset { get; }

    public string Id => Preset.Id;

    public string Name => LocalizationService.L(Preset.NameKey);

    public string Description => LocalizationService.L(Preset.SummaryKey);

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Description));
    }
}
