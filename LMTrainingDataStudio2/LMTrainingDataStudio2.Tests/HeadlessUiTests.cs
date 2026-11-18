using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.NUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LMTrainingDataStudio2.Controls;
using LMTrainingDataStudio2.Localization;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services;
using LMTrainingDataStudio2.ViewModels;
using LMTrainingDataStudio2.Views;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
[NonParallelizable]
public sealed class HeadlessUiTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "lmtds-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_directory, true); } catch { /* best effort */ }
    }

    [AvaloniaTest]
    public void MainWindow_LoadsCanvasAndDataWorkbench()
    {
        var vm = new MainWindowViewModel(new SettingsStore(Path.Combine(_directory, "settings.json")));
        var window = new MainWindow { DataContext = vm };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.That(window.FindControl<RecipeCanvasView>("RecipeCanvas"), Is.Not.Null);
        Assert.That(vm.Blocks, Is.Empty);
        Assert.That(vm.DataWorkbench, Is.Not.Null);

        window.Close();
    }

    [AvaloniaTest]
    public void Canvas_AddingAndSelectingBlock_RendersWithoutErrors()
    {
        var vm = new MainWindowViewModel(new SettingsStore(Path.Combine(_directory, "settings.json")));
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        vm.AddBlockCommand.Execute(BlockType.Seed);
        Dispatcher.UIThread.RunJobs();

        Assert.That(vm.Blocks, Has.Count.EqualTo(1));
        Assert.That(vm.Blocks[0].InputPorts, Is.Empty);
        Assert.That(vm.Blocks[0].OutputPorts, Has.Count.GreaterThan(0));

        window.Close();
    }

    [AvaloniaTest]
    public void LlmPanel_WiresModelListAndStreamingControls()
    {
        var vm = new MainWindowViewModel(new SettingsStore(Path.Combine(_directory, "settings.json")));
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            vm.AddBlockCommand.Execute(BlockType.Llm);
            Dispatcher.UIThread.RunJobs();

            var panel = window.FindControl<PropertyPanelView>("PropertyPanel")!;
            Assert.That(panel, Is.Not.Null);
            var models = panel.FindControl<ComboBox>("LlmModelListBox");
            var refresh = panel.FindControl<Button>("RefreshLlmModelsButton");
            var streaming = panel.FindControl<CheckBox>("LlmStreamingBox");
            Assert.That(models, Is.Not.Null);
            Assert.That(refresh, Is.Not.Null);
            Assert.That(streaming, Is.Not.Null);

            var editor = vm.SelectedBlock!.LlmEditor!;
            Assert.That(models!.ItemsSource, Is.SameAs(editor.Models));
            Assert.That(refresh!.Command, Is.SameAs(editor.RefreshModelsCommand));
            Assert.That(refresh.IsEnabled, Is.True, "the model catalog is available in the app");

            editor.Streaming = true;
            Dispatcher.UIThread.RunJobs();

            Assert.That(streaming!.IsChecked, Is.True);
            Assert.That(((LlmBlockConfig)vm.SelectedBlock.Config).Streaming, Is.True);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void LanguageSwitch_UpdatesStaticUiAndBlockLibrary()
    {
        var vm = new MainWindowViewModel(new SettingsStore(Path.Combine(_directory, "settings.json")));
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            LocalizationService.Instance.SetLanguage(LocalizationService.English);
            Dispatcher.UIThread.RunJobs();

            var header = window.FindControl<TextBlock>("BlocksHeader");
            Assert.That(header?.Text, Is.EqualTo("Block Library"));
            Assert.That(vm.BlockTemplates[0].Name, Is.EqualTo("Seed Source"));
            Assert.That(vm.Presets, Has.Count.EqualTo(RecipePresetCatalog.All.Count));

            LocalizationService.Instance.SetLanguage(LocalizationService.Chinese);
            Dispatcher.UIThread.RunJobs();

            Assert.That(header?.Text, Is.EqualTo("节点库"));
            Assert.That(vm.BlockTemplates[0].Name, Is.EqualTo("Seed 数据源"));
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(LocalizationService.English);
            window.Close();
        }
    }

    [AvaloniaTest]
    public void SelectingBlock_ShowsLocalizedNodeGuide()
    {
        var vm = new MainWindowViewModel(new SettingsStore(Path.Combine(_directory, "settings.json")));
        LocalizationService.Instance.SetLanguage(LocalizationService.Chinese);
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            vm.AddBlockCommand.Execute(BlockType.Seed);
            Dispatcher.UIThread.RunJobs();

            Assert.That(vm.SelectedBlockHelp, Does.Contain("入口节点"));
            Assert.That(vm.SelectedBlockPorts, Does.Contain("rows"));
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(LocalizationService.English);
            window.Close();
        }
    }

    [AvaloniaTest]
    public void LlmPanel_RendersToolRoundsAndExecutionMode()
    {
        var vm = new MainWindowViewModel(new SettingsStore(Path.Combine(_directory, "settings.json")));
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            vm.AddBlockCommand.Execute(BlockType.Llm);
            Dispatcher.UIThread.RunJobs();

            var editor = vm.SelectedBlock!.LlmEditor!;
            editor.MaxToolRounds = 3;
            Dispatcher.UIThread.RunJobs();

            Assert.That(((LlmBlockConfig)vm.SelectedBlock.Config).MaxToolRounds, Is.EqualTo(3));

            var names = window.GetVisualDescendants()
                .OfType<Control>()
                .Select(control => control.Name)
                .ToArray();
            Assert.That(names, Does.Contain("LlmMaxToolRoundsBox"));
            Assert.That(names, Does.Contain("LlmToolModeBox"));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void SftExportEditor_UpdatesFieldWhitelist()
    {
        var vm = new MainWindowViewModel(new SettingsStore(Path.Combine(_directory, "settings.json")));
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            vm.AddBlockCommand.Execute(BlockType.SftExport);
            Dispatcher.UIThread.RunJobs();

            var editor = vm.SelectedBlock?.SftExportEditor;
            Assert.That(editor, Is.Not.Null);

            editor!.FieldsText = "messages, tools";
            Assert.That(vm.SelectedBlock!.Config, Is.TypeOf<SftExportBlockConfig>());
            Assert.That(((SftExportBlockConfig)vm.SelectedBlock.Config).Fields,
                Is.EqualTo(new[] { "messages", "tools" }));

            editor.FieldsText = string.Empty;
            Assert.That(((SftExportBlockConfig)vm.SelectedBlock.Config).Fields, Is.Null);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void SftExportPanel_RendersFormatSplitFieldSelectorAndPathPickButtons()
    {
        var vm = new MainWindowViewModel(new SettingsStore(Path.Combine(_directory, "settings.json")));
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            vm.AddBlockCommand.Execute(BlockType.SftExport);
            Dispatcher.UIThread.RunJobs();

            var editor = vm.SelectedBlock!.SftExportEditor!;
            editor.LoadFields(new[] { "messages", "tools" });
            Dispatcher.UIThread.RunJobs();

            var buttonNames = window.GetVisualDescendants()
                .OfType<Button>()
                .Select(button => button.Name)
                .Where(name => !string.IsNullOrEmpty(name))
                .ToArray();
            Assert.That(buttonNames, Does.Contain("BrowseSftSampleFieldsButton"));
            Assert.That(buttonNames, Does.Contain("BrowseLlmFailureButton"));
            Assert.That(buttonNames, Does.Contain("BrowseValidatorRejectButton"));
            Assert.That(buttonNames, Does.Contain("BrowseSimulationFailureButton"));
            Assert.That(buttonNames, Does.Contain("BrowseSamplerOutputButton"));

            var fieldLabels = window.GetVisualDescendants()
                .OfType<CheckBox>()
                .Select(box => box.Content as string)
                .ToArray();
            Assert.That(fieldLabels, Does.Contain("messages"));
            Assert.That(fieldLabels, Does.Contain("tools"));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task DataWorkbench_MessagesTab_RendersStructuredEditor()
    {
        var vm = new MainWindowViewModel(new SettingsStore(Path.Combine(_directory, "settings.json")));
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            var path = Path.Combine(_directory, "chat.jsonl");
            await File.WriteAllTextAsync(path,
                """{"id":1,"messages":[{"role":"user","content":"hi"},{"role":"assistant","content":"hello"}]}""");

            vm.SwitchToDataCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            await vm.DataWorkbench.OpenDatasetAsync(path);
            vm.DataWorkbench.SelectedRow = vm.DataWorkbench.Rows[0];
            Dispatcher.UIThread.RunJobs();

            Assert.That(vm.DataWorkbench.HasMessages, Is.True);
            Assert.That(vm.DataWorkbench.SelectedEditorTabIndex, Is.EqualTo(1));

            var tabs = window.GetVisualDescendants()
                .OfType<TabControl>()
                .FirstOrDefault(control => control.Name == "RowEditorTabs");
            Assert.That(tabs, Is.Not.Null);
            Assert.That(tabs!.ItemCount, Is.EqualTo(3));

            var names = window.GetVisualDescendants()
                .OfType<Control>()
                .Select(control => control.Name)
                .ToArray();
            Assert.That(names, Does.Contain("MessageList"));
            Assert.That(names, Does.Contain("AddMessageButton"));
            Assert.That(names, Does.Contain("RemoveMessageButton"));
            Assert.That(names, Does.Contain("ApplyMessagesButton"));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void ToolProfilePanel_RendersEnumAndDefaultEditors()
    {
        var vm = new MainWindowViewModel(new SettingsStore(Path.Combine(_directory, "settings.json")));
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            vm.AddBlockCommand.Execute(BlockType.ToolProfile);
            Dispatcher.UIThread.RunJobs();

            var editor = vm.SelectedBlock?.ToolProfileEditor;
            Assert.That(editor, Is.Not.Null);

            editor!.AddToolCommand.Execute(null);
            editor.SelectedTool!.AddParameterCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            var names = window.GetVisualDescendants()
                .OfType<Control>()
                .Select(control => control.Name)
                .ToArray();
            Assert.That(names, Does.Contain("ToolEnumBox"));
            Assert.That(names, Does.Contain("ToolDefaultBox"));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void HighlightBehavior_RendersMatchedRuns()
    {
        var block = new TextBlock();
        TextHighlightBehavior.SetHighlightText(block, "row-1 and row-2");
        TextHighlightBehavior.SetHighlightQuery(block, "row");

        var window = new Window { Content = block };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var runs = block.Inlines!.OfType<Run>().ToArray();
        Assert.That(runs, Has.Length.EqualTo(4));
        Assert.That(runs.Count(run => run.Background is not null), Is.EqualTo(2));
        Assert.That(string.Concat(runs.Select(run => run.Text)), Is.EqualTo("row-1 and row-2"));

        window.Close();
    }

    [AvaloniaTest]
    public void ThemeManager_LightAndDark_AppliesRequestedVariant()
    {
        ThemeManager.Apply(dark: false);
        Assert.That(global::Avalonia.Application.Current?.RequestedThemeVariant?.Key,
            Is.EqualTo("Light"));

        ThemeManager.Apply(dark: true);
        Assert.That(global::Avalonia.Application.Current?.RequestedThemeVariant?.Key,
            Is.EqualTo("Dark"));
    }

    [AvaloniaTest]
    public void ThemeSwitch_UpdatesWindowBackgroundDynamicResource()
    {
        var vm = new MainWindowViewModel(new SettingsStore(Path.Combine(_directory, "settings.json")));
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            ThemeManager.Apply(dark: false);
            Dispatcher.UIThread.RunJobs();
            var light = (window.Background as ISolidColorBrush)?.Color;

            ThemeManager.Apply(dark: true);
            Dispatcher.UIThread.RunJobs();
            var dark = (window.Background as ISolidColorBrush)?.Color;

            Assert.That(light, Is.Not.Null);
            Assert.That(dark, Is.Not.Null);
            Assert.That(light, Is.Not.EqualTo(dark));
        }
        finally
        {
            ThemeManager.Apply(dark: true);
            window.Close();
        }
    }

    [AvaloniaTest]
    public void CleanerAndValidatorPanels_RenderSharedCleanupEditor()
    {
        var vm = new MainWindowViewModel(new SettingsStore(Path.Combine(_directory, "settings.json")));
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            vm.AddBlockCommand.Execute(BlockType.Cleaner);
            Dispatcher.UIThread.RunJobs();
            Assert.That(vm.SelectedBlock?.CleanerEditor?.Cleanup, Is.Not.Null);

            vm.AddBlockCommand.Execute(BlockType.Validator);
            Dispatcher.UIThread.RunJobs();
            Assert.That(vm.SelectedBlock?.ValidatorEditor?.Cleanup, Is.Not.Null);

            var cleanupEditors = window.GetVisualDescendants()
                .OfType<ContentControl>()
                .Count(control => control.Content is CleanupRulesEditorViewModel);
            Assert.That(cleanupEditors, Is.GreaterThan(0));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void SettingsPanel_UsesBoundedInputsAndDirectoryBrowsers()
    {
        var vm = new MainWindowViewModel(new SettingsStore(Path.Combine(_directory, "settings.json")));
        vm.OpenSettingsCommand.Execute(null);
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            var parallelism = window.FindControl<NumericUpDown>("MaxParallelismInput");
            var retries = window.FindControl<NumericUpDown>("NodeRetryCountInput");
            var spill = window.FindControl<NumericUpDown>("SpillThresholdInput");
            var spillBytes = window.FindControl<NumericUpDown>("SpillByteThresholdInput");
            var checkpointQuota = window.FindControl<NumericUpDown>("CheckpointQuotaInput");
            var historyLimit = window.FindControl<NumericUpDown>("ExecutionHistoryLimitInput");
            var historyDays = window.FindControl<NumericUpDown>("ExecutionHistoryRetentionInput");
            var snap = window.FindControl<NumericUpDown>("GridSnapSizeInput");
            var cacheSize = window.FindControl<NumericUpDown>("MaxCacheSizeInput");

            Assert.That(parallelism, Is.Not.Null);
            Assert.That(parallelism!.Minimum, Is.EqualTo(SettingsStore.MinParallelism));
            Assert.That(parallelism.Maximum, Is.EqualTo(SettingsStore.MaxParallelism));

            Assert.That(retries, Is.Not.Null);
            Assert.That(retries!.Minimum, Is.EqualTo(0));
            Assert.That(retries.Maximum, Is.EqualTo(SettingsStore.MaxNodeRetries));

            Assert.That(spill, Is.Not.Null);
            Assert.That(spill!.Maximum, Is.EqualTo(SettingsStore.MaxSpillThresholdRows));

            Assert.That(spillBytes, Is.Not.Null);
            Assert.That(spillBytes!.Minimum, Is.EqualTo(0));
            Assert.That(spillBytes.Maximum, Is.EqualTo(SettingsStore.MaxSpillThresholdMb));

            Assert.That(checkpointQuota, Is.Not.Null);
            Assert.That(checkpointQuota!.Minimum, Is.EqualTo(0));
            Assert.That(checkpointQuota.Maximum, Is.EqualTo(SettingsStore.MaxCheckpointQuotaMb));

            Assert.That(historyLimit, Is.Not.Null);
            Assert.That(historyLimit!.Maximum, Is.EqualTo(SettingsStore.MaxExecutionHistoryLimit));

            Assert.That(historyDays, Is.Not.Null);
            Assert.That(historyDays!.Maximum, Is.EqualTo(SettingsStore.MaxExecutionRetentionDays));

            Assert.That(snap, Is.Not.Null);
            Assert.That(snap!.Minimum, Is.EqualTo(SettingsStore.MinGridSnapSize));
            Assert.That(snap.Maximum, Is.EqualTo(SettingsStore.MaxGridSnapSize));

            Assert.That(cacheSize, Is.Not.Null);
            Assert.That(cacheSize!.Minimum, Is.EqualTo(SettingsStore.MinCacheSizeMb));

            Assert.That(window.FindControl<Button>("BrowseCacheDirectoryButton"), Is.Not.Null);
            Assert.That(window.FindControl<Button>("BrowseRunOutputDirectoryButton"), Is.Not.Null);
            Assert.That(window.FindControl<Button>("BrowseRunTempDirectoryButton"), Is.Not.Null);
        }
        finally
        {
            window.Close();
        }
    }
}
