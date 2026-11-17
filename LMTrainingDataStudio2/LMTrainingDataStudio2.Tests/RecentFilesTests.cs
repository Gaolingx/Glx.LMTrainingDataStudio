using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services;
using LMTrainingDataStudio2.ViewModels;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class RecentFilesTests
{
    private string _directory = null!;
    private SettingsStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "lmtds-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _store = new SettingsStore(Path.Combine(_directory, "settings.json"));
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_directory, true); } catch { /* best effort */ }
    }

    private string CreateRecipe(string name)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, "{}");
        return path;
    }

    [Test]
    public async Task PruneRecentFiles_RemovesDeletedFiles()
    {
        var first = CreateRecipe("first.recipe.json");
        var second = CreateRecipe("second.recipe.json");
        await _store.SaveAsync(new AppSettings { RecentFiles = [first, second] });

        var vm = new MainWindowViewModel(_store);
        Assert.That(vm.RecentFiles, Has.Count.EqualTo(2));

        File.Delete(first);
        vm.PruneRecentFiles();

        Assert.That(vm.RecentFiles, Is.EqualTo(new[] { second }));
    }

    [Test]
    public async Task ClearRecentFiles_EmptiesListAndPersists()
    {
        var recipe = CreateRecipe("recipe.recipe.json");
        await _store.SaveAsync(new AppSettings { RecentFiles = [recipe] });

        var vm = new MainWindowViewModel(_store);
        Assert.That(vm.RecentFiles, Has.Count.EqualTo(1));

        await vm.ClearRecentFilesCommand.ExecuteAsync(null);

        Assert.That(vm.RecentFiles, Is.Empty);
        Assert.That(_store.Load().Settings.RecentFiles, Is.Empty);
    }
}
