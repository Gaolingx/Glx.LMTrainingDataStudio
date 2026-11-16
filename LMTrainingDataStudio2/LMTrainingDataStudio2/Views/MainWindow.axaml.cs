using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LMTrainingDataStudio2.Localization;
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
            vm.ConfirmOverwriteAsync = ConfirmExportOverwriteAsync;
            await vm.RestoreLastRecipeAsync();
        }
    }

    private async Task<bool> ConfirmExportOverwriteAsync(IReadOnlyList<string> paths)
    {
        var message = string.Join(Environment.NewLine, paths) +
                      Environment.NewLine + Environment.NewLine +
                      LocalizationService.L("dialog.overwrite_exports_message");
        var result = await MessageDialog.ShowAsync(
            this,
            LocalizationService.L("dialog.overwrite_exports_title"),
            message,
            MessageDialogButtons.OkCancel);
        return result == MessageDialogResult.Ok;
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

    private void OnPresetsSubmenuOpened(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || sender is not MenuItem menu) return;

        menu.Items.Clear();
        foreach (var preset in vm.Presets)
        {
            var item = new MenuItem { Header = preset.Name };
            ToolTip.SetTip(item, preset.Description);
            item.Click += async (_, _) => await LoadPresetAsync(vm, preset);
            menu.Items.Add(item);
        }
    }

    private async void OnPresetPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: RecipePresetViewModel preset }) return;
        if (e.Source is Button) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        if (DataContext is MainWindowViewModel vm)
        {
            await LoadPresetAsync(vm, preset);
        }
    }

    private async void OnSaveAsPresetClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        if (vm.Blocks.Count == 0)
        {
            await MessageDialog.ShowAsync(
                this,
                LocalizationService.L("preset.save_empty.title"),
                LocalizationService.L("preset.save_empty.message"),
                MessageDialogButtons.Ok);
            return;
        }

        var input = await PresetSaveDialog.ShowAsync(this, vm.RecipeName, string.Empty);
        if (input is null) return;

        try
        {
            await vm.SaveAsPresetAsync(input.Value.Name, input.Value.Description);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await MessageDialog.ShowAsync(
                this,
                LocalizationService.L("preset.save_failed.title"),
                ex.Message,
                MessageDialogButtons.Ok);
        }
    }

    private async void OnImportPresetClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationService.L("dialog.picker.import_preset"),
            AllowMultiple = false,
            FileTypeFilter = new[] { PresetFileType() }
        });

        var path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (path is null) return;

        try
        {
            await vm.ImportPresetAsync(path);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await MessageDialog.ShowAsync(
                this,
                LocalizationService.L("preset.import_failed.title"),
                ex.Message,
                MessageDialogButtons.Ok);
        }
    }

    private void OnDeletePresetPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
    }

    private async void OnDeletePresetClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: RecipePresetViewModel preset }) return;
        if (DataContext is not MainWindowViewModel vm || !preset.IsCustom) return;

        var result = await MessageDialog.ShowAsync(
            this,
            LocalizationService.L("preset.delete.title"),
            string.Format(LocalizationService.L("preset.delete.message_format"), preset.Name),
            MessageDialogButtons.OkCancel);

        if (result == MessageDialogResult.Ok)
        {
            await vm.DeleteCustomPresetAsync(preset.Id);
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
            menu.Items.Add(new MenuItem
            {
                Header = LocalizationService.L("dialog.no_recent_files"),
                IsEnabled = false
            });
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
                LocalizationService.L("dialog.unsaved_changes.title"),
                string.Format(LocalizationService.L("dialog.close.message_format"), vm.RecipeName),
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
        var actionKey = withSample ? "dialog.action.new_from_sample" : "dialog.action.new_recipe";
        if (!await EnsureCanDiscardAsync(vm, actionKey)) return;
        await vm.NewRecipeAsync(withSample);
    }

    private async Task LoadPresetAsync(MainWindowViewModel vm, RecipePresetViewModel preset)
    {
        if (!await EnsureCanDiscardAsync(vm, "dialog.action.new_from_preset")) return;
        await vm.NewRecipeFromPresetAsync(preset.Id);
    }

    private async Task OpenFromDialogAsync(MainWindowViewModel vm)
    {
        if (!await EnsureCanDiscardAsync(vm, "dialog.action.open_recipe")) return;

        var path = await PickOpenPathAsync();
        if (path is not null)
        {
            await OpenRecipeAsync(vm, path);
        }
    }

    private async Task OpenRecentAsync(MainWindowViewModel vm, string path)
    {
        if (!await EnsureCanDiscardAsync(vm, "dialog.action.open_recent_recipe")) return;
        await OpenRecipeAsync(vm, path);
    }

    private async Task<bool> EnsureCanDiscardAsync(MainWindowViewModel vm, string actionKey)
    {
        if (!vm.IsDirty) return true;

        var result = await MessageDialog.ShowAsync(
            this,
            LocalizationService.L(actionKey),
            string.Format(LocalizationService.L("dialog.discard.message_format"), vm.RecipeName),
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
            await MessageDialog.ShowAsync(
                this,
                LocalizationService.L("dialog.save_failed.title"),
                ex.Message,
                MessageDialogButtons.Ok);
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
            await MessageDialog.ShowAsync(
                this,
                LocalizationService.L("dialog.open_failed.title"),
                ex.Message,
                MessageDialogButtons.Ok);
        }
    }

    private async Task<string?> PickOpenPathAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationService.L("dialog.picker.open_recipe"),
            AllowMultiple = false,
            FileTypeFilter = new[] { RecipeFileType() }
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    private async Task<string?> PickSavePathAsync(MainWindowViewModel vm)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = LocalizationService.L("dialog.picker.save_recipe"),
            SuggestedFileName = GetSuggestedFileName(vm.RecipeName),
            DefaultExtension = "json",
            ShowOverwritePrompt = true,
            FileTypeChoices = new[] { RecipeFileType() }
        });

        return file?.TryGetLocalPath();
    }

    private static FilePickerFileType RecipeFileType() => new(LocalizationService.L("dialog.file_type.recipe_json"))
    {
        Patterns = new[] { "*.json" },
        MimeTypes = new[] { "application/json" }
    };

    private static FilePickerFileType PresetFileType() => new(LocalizationService.L("dialog.file_type.recipe_preset"))
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
