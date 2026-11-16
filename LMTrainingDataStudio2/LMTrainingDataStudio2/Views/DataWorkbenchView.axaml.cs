using Avalonia.Controls;
using Avalonia.Input;
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

    private async void OnBrowseSearchTargetClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DataWorkbenchViewModel vm) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var extension = Path.GetExtension(vm.SourcePath).TrimStart('.');
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Choose replacement target",
            SuggestedFileName = Path.GetFileName(vm.Search.TargetPath),
            DefaultExtension = string.IsNullOrEmpty(extension) ? "jsonl" : extension,
            ShowOverwritePrompt = true,
            FileTypeChoices = DatasetFileTypes()
        });

        var path = file?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            vm.Search.TargetPath = path;
        }
    }

    private async void OnBrowseTransformOutputClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DataWorkbenchViewModel vm) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Choose transform output",
            SuggestedFileName = Path.GetFileName(vm.Transform.OutputPath),
            DefaultExtension = "jsonl",
            ShowOverwritePrompt = true,
            FileTypeChoices = DatasetFileTypes()
        });

        var path = file?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            vm.Transform.OutputPath = path;
        }
    }

    private async void OnBrowseTransformDirectoryClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DataWorkbenchViewModel vm) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose split output directory",
            AllowMultiple = false
        });

        var path = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        if (!string.IsNullOrWhiteSpace(path))
        {
            vm.Transform.OutputDirectory = path;
        }
    }

    private async void OnBrowseFixTargetClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DataWorkbenchViewModel vm) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Choose fixed copy target",
            SuggestedFileName = Path.GetFileName(vm.Validation.FixTargetPath),
            DefaultExtension = "jsonl",
            ShowOverwritePrompt = true,
            FileTypeChoices = new[]
            {
                new FilePickerFileType("JSONL") { Patterns = new[] { "*.jsonl" } }
            }
        });

        var path = file?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            vm.Validation.FixTargetPath = path;
        }
    }

    private void OnIssueDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not DataWorkbenchViewModel vm) return;
        if (vm.Validation.SelectedIssue?.LineNumber is { } line)
        {
            vm.Validation.RequestJump(line);
        }
    }

    private void OnSearchMatchDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not DataWorkbenchViewModel vm) return;
        if (vm.Search.SelectedMatch is { } match)
        {
            vm.Search.RequestJump(match.LineNumber);
        }
    }

    private static FilePickerFileType[] DatasetFileTypes() => new[]
    {
        new FilePickerFileType("JSONL") { Patterns = new[] { "*.jsonl" } },
        new FilePickerFileType("CSV") { Patterns = new[] { "*.csv" } },
        new FilePickerFileType("Parquet") { Patterns = new[] { "*.parquet" } }
    };
}
