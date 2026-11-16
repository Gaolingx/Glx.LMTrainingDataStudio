using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.ViewModels;

namespace LMTrainingDataStudio2.Views;

public partial class DataWorkbenchView : UserControl
{
    public DataWorkbenchView()
    {
        InitializeComponent();
    }

    private async void OnOpenDatasetClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DataWorkbenchViewModel vm) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open dataset",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Supported datasets")
                {
                    Patterns = new[] { "*.jsonl", "*.json", "*.csv", "*.parquet" }
                },
                new FilePickerFileType("JSONL") { Patterns = new[] { "*.jsonl", "*.json" } },
                new FilePickerFileType("CSV") { Patterns = new[] { "*.csv" } },
                new FilePickerFileType("Parquet") { Patterns = new[] { "*.parquet" } },
                FilePickerFileTypes.All
            }
        });

        if (files.Count == 0) return;

        var path = files[0].TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) return;

        await vm.OpenDatasetAsync(path);
    }

    private async void OnBrowseTargetClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DataWorkbenchViewModel vm) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var formatName = vm.SelectedTargetFormat.Format.ToString().ToLowerInvariant();
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Choose conversion target",
            SuggestedFileName = $"{Path.GetFileNameWithoutExtension(vm.SourcePath)}.{formatName}.jsonl",
            DefaultExtension = "jsonl",
            ShowOverwritePrompt = true,
            FileTypeChoices = new[]
            {
                new FilePickerFileType("JSONL") { Patterns = new[] { "*.jsonl" } },
                new FilePickerFileType("CSV") { Patterns = new[] { "*.csv" } },
                new FilePickerFileType("Parquet") { Patterns = new[] { "*.parquet" } }
            }
        });

        var path = file?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            vm.TargetPath = path;
        }
    }
}
