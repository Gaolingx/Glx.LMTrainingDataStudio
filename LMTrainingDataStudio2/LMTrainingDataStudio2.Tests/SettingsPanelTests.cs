using LMTrainingDataStudio2.Services;
using LMTrainingDataStudio2.ViewModels;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
[NonParallelizable]
public sealed class SettingsPanelTests
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

    private MainWindowViewModel CreateViewModel(out SettingsStore store)
    {
        store = new SettingsStore(Path.Combine(_directory, "settings.json"));
        return new MainWindowViewModel(store);
    }

    private void UseWritableDirectories(MainWindowViewModel vm)
    {
        vm.CacheDirectory = Path.Combine(_directory, "cache");
        vm.RunOutputDirectory = Path.Combine(_directory, "runs");
        vm.RunTempDirectory = Path.Combine(_directory, "temp");
    }

    [Test]
    public async Task ApplySettings_RejectsUnwritableDirectory()
    {
        var vm = CreateViewModel(out var store);
        vm.OpenSettingsCommand.Execute(null);
        UseWritableDirectories(vm);

        var blockingFile = Path.Combine(_directory, "not-a-directory");
        await File.WriteAllTextAsync(blockingFile, "x");
        vm.CacheDirectory = Path.Combine(blockingFile, "cache");

        await vm.ApplySettingsCommand.ExecuteAsync(null);

        Assert.That(vm.IsSettingsVisible, Is.True);
        Assert.That(vm.HasSettingsValidationMessage, Is.True);
        Assert.That(vm.SettingsValidationMessage, Does.Contain("Cache directory"));
        Assert.That(File.Exists(store.FilePath), Is.False);
    }

    [Test]
    public async Task ApplySettings_RejectsOutOfRangeValues()
    {
        var vm = CreateViewModel(out var store);
        vm.OpenSettingsCommand.Execute(null);
        UseWritableDirectories(vm);
        vm.MaxParallelism = 0;

        await vm.ApplySettingsCommand.ExecuteAsync(null);

        Assert.That(vm.IsSettingsVisible, Is.True);
        Assert.That(vm.SettingsValidationMessage, Does.Contain("Parallelism"));
        Assert.That(File.Exists(store.FilePath), Is.False);
    }

    [Test]
    public async Task ApplySettings_SavesValidValuesAndClosesPanel()
    {
        var vm = CreateViewModel(out var store);
        vm.OpenSettingsCommand.Execute(null);
        UseWritableDirectories(vm);
        vm.MaxParallelism = 8;
        vm.NodeRetryCount = 2;
        vm.SpillThresholdRows = 20_000;
        vm.GridSnapSize = 24;

        await vm.ApplySettingsCommand.ExecuteAsync(null);

        Assert.That(vm.IsSettingsVisible, Is.False);
        Assert.That(vm.SettingsValidationMessage, Is.Empty);
        Assert.That(File.Exists(store.FilePath), Is.True);

        var saved = store.Load().Settings;
        Assert.That(saved.MaxParallelism, Is.EqualTo(8));
        Assert.That(saved.NodeRetryCount, Is.EqualTo(2));
        Assert.That(saved.SpillThresholdRows, Is.EqualTo(20_000));
        Assert.That(saved.GridSnapSize, Is.EqualTo(24));
        Assert.That(vm.SettingsService.Current.MaxParallelism, Is.EqualTo(8));
    }

    [Test]
    public async Task ApplySettings_NotifiesSubscribers()
    {
        var vm = CreateViewModel(out _);
        vm.OpenSettingsCommand.Execute(null);
        UseWritableDirectories(vm);
        vm.MaxParallelism = 6;
        SettingsChangedEventArgs? captured = null;
        vm.SettingsService.Changed += (_, args) => captured = args;

        await vm.ApplySettingsCommand.ExecuteAsync(null);

        Assert.That(captured, Is.Not.Null);
        Assert.That(captured!.Current.MaxParallelism, Is.EqualTo(6));
    }
}
