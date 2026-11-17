using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LMTrainingDataStudio2.Commands;
using LMTrainingDataStudio2.Localization;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.ViewModels;

namespace LMTrainingDataStudio2.Views;

public partial class PropertyPanelView : UserControl
{
    private BlockNodeViewModel? _nameEditBlock;
    private string? _nameEditOriginal;
    private BlockNodeViewModel? _displayNameEditBlock;
    private string? _displayNameEditOriginal;

    public PropertyPanelView()
    {
        InitializeComponent();
    }

    private void OnBlockNameGotFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || vm.SelectedBlock is not { } block) return;
        _nameEditBlock = block;
        _nameEditOriginal = block.Name;
    }

    private void OnBlockNameLostFocus(object? sender, RoutedEventArgs e)
    {
        var block = _nameEditBlock;
        var original = _nameEditOriginal;
        _nameEditBlock = null;
        _nameEditOriginal = null;

        if (block is null || original is null || DataContext is not MainWindowViewModel vm) return;
        var current = block.Name;
        if (string.IsNullOrWhiteSpace(current) || string.Equals(current, original, StringComparison.Ordinal))
        {
            return;
        }

        vm.CommandHistory.Execute(new RenameBlockCommand(block, original, current));
    }

    private void OnBlockDisplayNameGotFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || vm.SelectedBlock is not { } block) return;
        _displayNameEditBlock = block;
        _displayNameEditOriginal = block.DisplayName;
    }

    private void OnBlockDisplayNameLostFocus(object? sender, RoutedEventArgs e)
    {
        var block = _displayNameEditBlock;
        var original = _displayNameEditOriginal;
        _displayNameEditBlock = null;
        _displayNameEditOriginal = null;

        if (block is null || original is null || DataContext is not MainWindowViewModel vm) return;
        var current = block.DisplayName;
        if (string.IsNullOrWhiteSpace(current) || string.Equals(current, original, StringComparison.Ordinal))
        {
            return;
        }

        vm.CommandHistory.Execute(new RenameBlockCommand(block, original, current, isDisplayName: true));
    }

    private async void OnBrowseSeedSourceClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm ||
            vm.SelectedBlock?.SeedEditor is not { } editor)
        {
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select seed dataset",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Datasets")
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

        editor.SourcePath = path;
        vm.StatusText = $"Seed source selected: {path}";
    }

    private async void OnBrowseSftOutputClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm ||
            vm.SelectedBlock?.SftExportEditor is not { } editor)
        {
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Select SFT export target",
            SuggestedFileName = Path.GetFileName(editor.OutputPath),
            DefaultExtension = "jsonl",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("JSONL") { Patterns = new[] { "*.jsonl", "*.json" } },
                FilePickerFileTypes.All
            }
        });

        var path = file?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) return;

        editor.OutputPath = path;
        vm.StatusText = $"SFT export target selected: {path}";
    }

    private async void OnPreviewSftOutputClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm ||
            vm.SelectedBlock is not { } block ||
            block.SftExportEditor is not { } editor)
        {
            return;
        }

        await PreviewFileAsync(vm, block.Name, editor.OutputPath);
    }

    private async void OnPreviewRejectClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm ||
            vm.SelectedBlock is not { } block ||
            block.ValidatorEditor is not { } editor)
        {
            return;
        }

        await PreviewFileAsync(vm, block.Name, editor.RejectOutputPath);
    }

    private async void OnPreviewFailuresClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm ||
            vm.SelectedBlock is not { } block ||
            block.SimulationEditor is not { } editor)
        {
            return;
        }

        await PreviewFileAsync(vm, block.Name, editor.FailureOutputPath);
    }

    private async void OnPreviewCleanerDiffClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm ||
            vm.SelectedBlock is not { } block ||
            block.Config is not CleanerBlockConfig cleaner)
        {
            return;
        }

        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationService.L("preview.cleaner_pick_sample"),
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("JSONL") { Patterns = new[] { "*.jsonl", "*.json" } },
                FilePickerFileTypes.All
            }
        });

        var path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var preview = vm.CreateSamplePreview();
        await preview.LoadCleanerDiffAsync(path, cleaner.Rules, block.Name);
        await SamplePreviewWindow.ShowAsync(owner, preview);
    }

    private async Task PreviewFileAsync(MainWindowViewModel vm, string nodeName, string? path)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            await MessageDialog.ShowAsync(
                owner,
                LocalizationService.L("preview.title"),
                string.Format(
                    LocalizationService.L("property.preview_file_missing"),
                    string.IsNullOrWhiteSpace(path) ? "(empty)" : path),
                MessageDialogButtons.Ok);
            return;
        }

        var preview = vm.CreateSamplePreview();
        await preview.LoadFileAsync(
            path,
            string.Format(LocalizationService.L("preview.node_format"), nodeName));
        await SamplePreviewWindow.ShowAsync(owner, preview);
    }
}