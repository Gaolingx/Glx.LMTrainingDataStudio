using System.Text.Json;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class SettingsStoreTests
{
    private string _directory = null!;
    private string _path = null!;
    private SettingsStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "lmtds-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "settings.json");
        _store = new SettingsStore(_path);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_directory, true); } catch { /* best effort */ }
    }

    [Test]
    public void GetDefaultFilePath_UsesApplicationDataFolder()
    {
        var path = SettingsStore.GetDefaultFilePath();

        Assert.That(Path.GetFileName(path), Is.EqualTo("settings.json"));
        Assert.That(path, Does.Contain("LMTrainingDataStudio"));
        Assert.That(Path.IsPathRooted(path), Is.True);
    }

    [Test]
    public void Load_MissingFile_ReturnsDefaultsWithoutWarnings()
    {
        var result = _store.Load();
        var defaults = new AppSettings();

        Assert.That(result.Settings.MaxCacheSizeMb, Is.EqualTo(defaults.MaxCacheSizeMb));
        Assert.That(result.Settings.CacheDirectory, Is.EqualTo(defaults.CacheDirectory));
        Assert.That(result.Settings.EnableGridSnap, Is.EqualTo(defaults.EnableGridSnap));
        Assert.That(result.Settings.GridSnapSize, Is.EqualTo(defaults.GridSnapSize));
        Assert.That(result.Settings.IsDarkTheme, Is.EqualTo(defaults.IsDarkTheme));
        Assert.That(result.Warnings, Is.Empty);
    }

    [Test]
    public async Task SaveAsync_ThenLoad_RestoresValues()
    {
        var settings = new AppSettings
        {
            MaxCacheSizeMb = 1024,
            CacheDirectory = Path.Combine(_directory, "cache"),
            EnableGridSnap = false,
            GridSnapSize = 32,
            IsDarkTheme = false,
            OpenAiApiKey = "sk-openai-test",
            AnthropicApiKey = "sk-ant-test"
        };

        await _store.SaveAsync(settings);
        var result = _store.Load();

        Assert.That(result.Settings.MaxCacheSizeMb, Is.EqualTo(1024));
        Assert.That(result.Settings.CacheDirectory, Is.EqualTo(settings.CacheDirectory));
        Assert.That(result.Settings.EnableGridSnap, Is.False);
        Assert.That(result.Settings.GridSnapSize, Is.EqualTo(32));
        Assert.That(result.Settings.IsDarkTheme, Is.False);
        Assert.That(result.Settings.OpenAiApiKey, Is.EqualTo("sk-openai-test"));
        Assert.That(result.Settings.AnthropicApiKey, Is.EqualTo("sk-ant-test"));
        Assert.That(result.Warnings, Is.Empty);
    }

    [Test]
    public async Task SaveAsync_ThenLoad_RestoresExecutionSettings()
    {
        var settings = new AppSettings
        {
            MaxParallelism = 8,
            NodeRetryCount = 2,
            RunOutputDirectory = Path.Combine(_directory, "runs")
        };

        await _store.SaveAsync(settings);
        var result = _store.Load();

        Assert.That(result.Settings.MaxParallelism, Is.EqualTo(8));
        Assert.That(result.Settings.NodeRetryCount, Is.EqualTo(2));
        Assert.That(result.Settings.RunOutputDirectory, Is.EqualTo(settings.RunOutputDirectory));
        Assert.That(result.Warnings, Is.Empty);
    }

    [Test]
    public async Task Load_InvalidExecutionSettings_FallBackWithWarnings()
    {
        await File.WriteAllTextAsync(_path, """
            {
              "maxParallelism": 0,
              "nodeRetryCount": 99,
              "runOutputDirectory": "   "
            }
            """);

        var result = _store.Load();
        var defaults = new AppSettings();

        Assert.That(result.Settings.MaxParallelism, Is.EqualTo(defaults.MaxParallelism));
        Assert.That(result.Settings.NodeRetryCount, Is.EqualTo(defaults.NodeRetryCount));
        Assert.That(result.Settings.RunOutputDirectory, Is.EqualTo(defaults.RunOutputDirectory));
        Assert.That(result.Warnings, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task SaveAsync_WritesCamelCaseJson()
    {
        await _store.SaveAsync(new AppSettings
        {
            MaxCacheSizeMb = 256,
            CacheDirectory = "C:/cache",
            EnableGridSnap = true,
            GridSnapSize = 8,
            IsDarkTheme = false,
            OpenAiApiKey = "sk-test"
        });

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(_path));
        var root = document.RootElement;

        Assert.That(root.GetProperty("maxCacheSizeMb").GetInt64(), Is.EqualTo(256));
        Assert.That(root.GetProperty("cacheDirectory").GetString(), Is.EqualTo("C:/cache"));
        Assert.That(root.GetProperty("enableGridSnap").GetBoolean(), Is.True);
        Assert.That(root.GetProperty("gridSnapSize").GetInt32(), Is.EqualTo(8));
        Assert.That(root.GetProperty("isDarkTheme").GetBoolean(), Is.False);
        Assert.That(root.GetProperty("openAiApiKey").GetString(), Is.EqualTo("sk-test"));
    }

    [Test]
    public async Task Load_InvalidValues_FallBackToDefaultsWithWarnings()
    {
        await File.WriteAllTextAsync(
            _path,
            """{"maxCacheSizeMb":8,"cacheDirectory":"  ","gridSnapSize":512,"enableGridSnap":false,"isDarkTheme":false}""");

        var result = _store.Load();
        var defaults = new AppSettings();

        Assert.That(result.Settings.MaxCacheSizeMb, Is.EqualTo(defaults.MaxCacheSizeMb));
        Assert.That(result.Settings.CacheDirectory, Is.EqualTo(defaults.CacheDirectory));
        Assert.That(result.Settings.GridSnapSize, Is.EqualTo(defaults.GridSnapSize));
        Assert.That(result.Settings.EnableGridSnap, Is.False);
        Assert.That(result.Settings.IsDarkTheme, Is.False);
        Assert.That(result.Warnings, Has.Count.EqualTo(3));
        Assert.That(result.Warnings.Any(w => w.Contains("maxCacheSizeMb")), Is.True);
        Assert.That(result.Warnings.Any(w => w.Contains("gridSnapSize")), Is.True);
        Assert.That(result.Warnings.Any(w => w.Contains("cacheDirectory")), Is.True);
    }

    [Test]
    public async Task Load_BoundaryValues_AreAccepted()
    {
        await _store.SaveAsync(new AppSettings
        {
            MaxCacheSizeMb = SettingsStore.MinCacheSizeMb,
            GridSnapSize = SettingsStore.MaxGridSnapSize,
            CacheDirectory = Path.Combine(_directory, "cache")
        });

        var result = _store.Load();

        Assert.That(result.Settings.MaxCacheSizeMb, Is.EqualTo(SettingsStore.MinCacheSizeMb));
        Assert.That(result.Settings.GridSnapSize, Is.EqualTo(SettingsStore.MaxGridSnapSize));
        Assert.That(result.Warnings, Is.Empty);
    }

    [Test]
    public async Task Load_MalformedJson_ReturnsDefaultsWithWarning()
    {
        await File.WriteAllTextAsync(_path, "{ not valid json");

        var result = _store.Load();
        var defaults = new AppSettings();

        Assert.That(result.Settings.MaxCacheSizeMb, Is.EqualTo(defaults.MaxCacheSizeMb));
        Assert.That(result.Warnings, Has.Count.EqualTo(1));
        Assert.That(result.Warnings[0], Does.Contain(_path));
    }

    [Test]
    public async Task SaveAsync_OverwritesExistingFile_AndLeavesNoTempFile()
    {
        await File.WriteAllTextAsync(_path, "garbage");

        await _store.SaveAsync(new AppSettings { GridSnapSize = 24 });
        var result = _store.Load();

        Assert.That(result.Settings.GridSnapSize, Is.EqualTo(24));
        Assert.That(File.Exists(_path + ".tmp"), Is.False);
    }

    [Test]
    public void SaveAsync_CancelledToken_DoesNotCreateFiles()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(async () => await _store.SaveAsync(new AppSettings(), cts.Token));
        Assert.That(File.Exists(_path), Is.False);
        Assert.That(File.Exists(_path + ".tmp"), Is.False);
    }
}