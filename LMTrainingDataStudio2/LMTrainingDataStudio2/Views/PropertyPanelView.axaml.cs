using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LMTrainingDataStudio2.ViewModels;

namespace LMTrainingDataStudio2.Views;

public partial class PropertyPanelView : UserControl
{
    public PropertyPanelView()
    {
        InitializeComponent();
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
}