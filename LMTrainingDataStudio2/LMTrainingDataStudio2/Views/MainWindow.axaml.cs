using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LMTrainingDataStudio2.ViewModels;

namespace LMTrainingDataStudio2.Views;

public partial class MainWindow : Window
{
    private bool _closeConfirmed;
    private bool _closePromptInProgress;

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        Closing += OnWindowClosing;
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (DataContext is MainWindowViewModel vm)
        {
            await vm.RestoreLastRecipeAsync();
        }
    }

    private void OnFitCanvasClick(object? sender, RoutedEventArgs e)
    {
        this.FindControl<RecipeCanvasView>("RecipeCanvas")?.FitToView();
    }

    private async void OnBlockTemplatePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: BlockTemplateViewModel template }) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        var data = new DataObject();
        data.Set("application/x-lmtds-block-type", template.Type.ToString());
        await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy);
    }

    private async void OnNewRecipeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            await NewDocumentAsync(vm, withSample: false);
        }
    }

    private async void OnNewFromSampleClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            await NewDocumentAsync(vm, withSample: true);
        }
    }

    private async void OnOpenRecipeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            await OpenFromDialogAsync(vm);
        }
    }

    private async void OnSaveRecipeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            await SaveAsync(vm, forceSaveAs: false);
        }
    }

    private async void OnSaveAsRecipeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            await SaveAsync(vm, forceSaveAs: true);
        }
    }

    private void OnRecentFilesSubmenuOpened(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || sender is not MenuItem menu) return;

        menu.Items.Clear();
        if (vm.RecentFiles.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "No recent files", IsEnabled = false });
            return;
        }

        foreach (var path in vm.RecentFiles)
        {
            var item = new MenuItem
            {
                Header = Path.GetFileName(path)
            };
            ToolTip.SetTip(item, path);
            item.Click += async (_, _) => await OpenRecentAsync(vm, path);
            menu.Items.Add(item);
        }
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;

        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        switch (e.Key)
        {
            case Key.N:
                e.Handled = true;
                _ = NewDocumentAsync(vm, withSample: false);
                break;
            case Key.O:
                e.Handled = true;
                _ = OpenFromDialogAsync(vm);
                break;
            case Key.S:
                e.Handled = true;
                _ = SaveAsync(vm, forceSaveAs: shift);
                break;
        }
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed || DataContext is not MainWindowViewModel vm || !vm.IsDirty)
        {
            return;
        }

        e.Cancel = true;
        if (_closePromptInProgress) return;

        _closePromptInProgress = true;
        _ = ConfirmCloseAsync(vm);
    }

    private async Task ConfirmCloseAsync(MainWindowViewModel vm)
    {
        try
        {
            var result = await MessageDialog.ShowAsync(
                this,
                "Unsaved changes",
                $"Save changes to '{vm.RecipeName}' before closing?",
                MessageDialogButtons.SaveDiscardCancel);

            var canClose = result switch
            {
                MessageDialogResult.Save => await SaveAsync(vm, forceSaveAs: false),
                MessageDialogResult.Discard => true,
                _ => false
            };

            if (!canClose) return;

            _closeConfirmed = true;
            Close();
        }
        finally
        {
            _closePromptInProgress = false;
        }
    }

    private async Task NewDocumentAsync(MainWindowViewModel vm, bool withSample)
    {
        if (!await EnsureCanDiscardAsync(vm, withSample ? "New from Sample" : "New Recipe")) return;
        await vm.NewRecipeAsync(withSample);
    }

    private async Task OpenFromDialogAsync(MainWindowViewModel vm)
    {
        if (!await EnsureCanDiscardAsync(vm, "Open Recipe")) return;

        var path = await PickOpenPathAsync();
        if (path is not null)
        {
            await OpenRecipeAsync(vm, path);
        }
    }

    private async Task OpenRecentAsync(MainWindowViewModel vm, string path)
    {
        if (!await EnsureCanDiscardAsync(vm, "Open Recent Recipe")) return;
        await OpenRecipeAsync(vm, path);
    }

    private async Task<bool> EnsureCanDiscardAsync(MainWindowViewModel vm, string action)
    {
        if (!vm.IsDirty) return true;

        var result = await MessageDialog.ShowAsync(
            this,
            action,
            $"'{vm.RecipeName}' has unsaved changes. Save before continuing?",
            MessageDialogButtons.SaveDiscardCancel);

        return result switch
        {
            MessageDialogResult.Save => await SaveAsync(vm, forceSaveAs: false),
            MessageDialogResult.Discard => true,
            _ => false
        };
    }

    private async Task<bool> SaveAsync(MainWindowViewModel vm, bool forceSaveAs)
    {
        var path = forceSaveAs || string.IsNullOrWhiteSpace(vm.CurrentFilePath)
            ? await PickSavePathAsync(vm)
            : vm.CurrentFilePath;

        if (string.IsNullOrWhiteSpace(path)) return false;

        try
        {
            await vm.SaveRecipeAsync(path);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await MessageDialog.ShowAsync(this, "Save failed", ex.Message, MessageDialogButtons.Ok);
            return false;
        }
    }

    private async Task OpenRecipeAsync(MainWindowViewModel vm, string path)
    {
        try
        {
            await vm.OpenRecipeAsync(path);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await MessageDialog.ShowAsync(this, "Open failed", ex.Message, MessageDialogButtons.Ok);
        }
    }

    private async Task<string?> PickOpenPathAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Recipe",
            AllowMultiple = false,
            FileTypeFilter = new[] { RecipeFileType() }
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    private async Task<string?> PickSavePathAsync(MainWindowViewModel vm)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Recipe",
            SuggestedFileName = GetSuggestedFileName(vm.RecipeName),
            DefaultExtension = "json",
            ShowOverwritePrompt = true,
            FileTypeChoices = new[] { RecipeFileType() }
        });

        return file?.TryGetLocalPath();
    }

    private static FilePickerFileType RecipeFileType() => new("Recipe JSON")
    {
        Patterns = new[] { "*.json" },
        MimeTypes = new[] { "application/json" }
    };

    private static string GetSuggestedFileName(string recipeName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = new string(recipeName.Trim()
            .Select(character => invalid.Contains(character) ? '_' : character)
            .ToArray());

        if (string.IsNullOrWhiteSpace(name))
        {
            name = "recipe";
        }

        return name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? name : name + ".json";
    }
}
