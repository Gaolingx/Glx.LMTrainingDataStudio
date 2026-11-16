using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;
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
    public void ThemeManager_LightAndDark_AppliesRequestedVariant()
    {
        ThemeManager.Apply(dark: false);
        Assert.That(global::Avalonia.Application.Current?.RequestedThemeVariant?.Key,
            Is.EqualTo("Light"));

        ThemeManager.Apply(dark: true);
        Assert.That(global::Avalonia.Application.Current?.RequestedThemeVariant?.Key,
            Is.EqualTo("Dark"));
    }
}