using CommunityToolkit.Mvvm.ComponentModel;
using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.ViewModels;

/// <summary>
/// Edits a seed block configuration and writes changes back to the owning block.
/// </summary>
public partial class SeedConfigEditorViewModel : ObservableObject
{
    private readonly Action<SeedBlockConfig> _apply;
    private readonly bool _streaming;

    [ObservableProperty]
    private string _sourcePath;

    [ObservableProperty]
    private DataFileType _fileType;

    [ObservableProperty]
    private bool _useIndexCache;

    public SeedConfigEditorViewModel(SeedBlockConfig config, Action<SeedBlockConfig> apply)
    {
        _apply = apply;
        _streaming = config.Streaming;
        _sourcePath = config.SourcePath;
        _fileType = config.FileType;
        _useIndexCache = config.UseIndexCache;
    }

    public IReadOnlyList<DataFileType> FileTypes { get; } = Enum.GetValues<DataFileType>();

    partial void OnSourcePathChanged(string value) => Push();

    partial void OnFileTypeChanged(DataFileType value) => Push();

    partial void OnUseIndexCacheChanged(bool value) => Push();

    private void Push() =>
        _apply(new SeedBlockConfig(SourcePath ?? string.Empty, FileType, _streaming, UseIndexCache));
}