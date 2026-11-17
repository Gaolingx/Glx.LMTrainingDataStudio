using System.Text;
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
        Assert.That(result.Settings.Language, Is.EqualTo(defaults.Language));
        Assert.That(result.Warnings, Is.Empty);
    }

    [Test]
    public async Task SaveAsync_ThenLoad_RestoresRuntimeBufferSettings()
    {
        var settings = new AppSettings
        {
            SpillThresholdRows = 12_345,
            RunTempDirectory = Path.Combine(_directory, "run-temp")
        };

        await _store.SaveAsync(settings);
        var result = _store.Load();

        Assert.That(result.Warnings, Is.Empty);
        Assert.That(result.Settings.SpillThresholdRows, Is.EqualTo(12_345));
        Assert.That(result.Settings.RunTempDirectory, Is.EqualTo(settings.RunTempDirectory));
    }

    [Test]
    public void Load_InvalidRuntimeBufferSettings_FallBackToDefaults()
    {
        File.WriteAllText(_path, """{"spillThresholdRows":-1,"runTempDirectory":"  "}""");

        var result = _store.Load();
        var defaults = new AppSettings();

        Assert.That(result.Settings.SpillThresholdRows, Is.EqualTo(defaults.SpillThresholdRows));
        Assert.That(result.Settings.RunTempDirectory, Is.EqualTo(defaults.RunTempDirectory));
        Assert.That(result.Warnings, Has.Count.GreaterThanOrEqualTo(2));
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
        if (OperatingSystem.IsWindows())
        {
            Assert.That(result.Settings.OpenAiApiKey, Is.EqualTo("sk-openai-test"));
            Assert.That(result.Settings.AnthropicApiKey, Is.EqualTo("sk-ant-test"));
        }
        else
        {
            Assert.That(result.Settings.OpenAiApiKey, Is.Null);
            Assert.That(result.Settings.AnthropicApiKey, Is.Null);
        }

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
    public async Task SaveAsync_UsesDpapiForApiKeysOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("DPAPI is Windows-only.");
        }

        await _store.SaveAsync(new AppSettings { OpenAiApiKey = "sk-windows-secret" });

        var json = await File.ReadAllTextAsync(_path);
        Assert.That(json, Does.Not.Contain("sk-windows-secret"));

        using var document = JsonDocument.Parse(json);
        var storedKey = document.RootElement.GetProperty("openAiApiKey").GetString();
        Assert.That(storedKey, Does.StartWith(DpapiApiKeyProtector.Prefix));

        var result = _store.Load();
        Assert.That(result.Settings.OpenAiApiKey, Is.EqualTo("sk-windows-secret"));
        Assert.That(result.Warnings, Is.Empty);
    }

    [Test]
    public async Task SaveAsync_EncryptsBothApiKeysBeforeWriting()
    {
        var store = new SettingsStore(_path, new FakeApiKeyProtector());

        await store.SaveAsync(new AppSettings
        {
            OpenAiApiKey = "sk-openai-test",
            AnthropicApiKey = "sk-ant-test"
        });

        var json = await File.ReadAllTextAsync(_path);
        Assert.That(json, Does.Not.Contain("sk-openai-test"));
        Assert.That(json, Does.Not.Contain("sk-ant-test"));

        using var document = JsonDocument.Parse(json);
        Assert.That(
            document.RootElement.GetProperty("openAiApiKey").GetString(),
            Does.StartWith(FakeApiKeyProtector.Marker));
        Assert.That(
            document.RootElement.GetProperty("anthropicApiKey").GetString(),
            Does.StartWith(FakeApiKeyProtector.Marker));

        var result = store.Load();
        Assert.That(result.Settings.OpenAiApiKey, Is.EqualTo("sk-openai-test"));
        Assert.That(result.Settings.AnthropicApiKey, Is.EqualTo("sk-ant-test"));
        Assert.That(result.Warnings, Is.Empty);
    }

    [Test]
    public async Task Load_LegacyPlaintextApiKeys_MigratesFileToEncryptedValues()
    {
        await File.WriteAllTextAsync(_path, """
            {"openAiApiKey":"sk-legacy","anthropicApiKey":"sk-ant-legacy"}
            """);

        var store = new SettingsStore(_path, new FakeApiKeyProtector());
        var result = store.Load();

        Assert.That(result.Settings.OpenAiApiKey, Is.EqualTo("sk-legacy"));
        Assert.That(result.Settings.AnthropicApiKey, Is.EqualTo("sk-ant-legacy"));
        Assert.That(result.Warnings.Any(w => w.Contains("migrated")), Is.True);

        var json = await File.ReadAllTextAsync(_path);
        Assert.That(json, Does.Not.Contain("sk-legacy"));
        Assert.That(json, Does.Not.Contain("sk-ant-legacy"));

        using var document = JsonDocument.Parse(json);
        Assert.That(
            document.RootElement.GetProperty("openAiApiKey").GetString(),
            Does.StartWith(FakeApiKeyProtector.Marker));
    }

    [Test]
    public async Task Load_CorruptProtectedApiKey_WarnsAndIgnoresKey()
    {
        await File.WriteAllTextAsync(
            _path,
            "{\"openAiApiKey\":\"" + FakeApiKeyProtector.Marker + "not-base64!\"}");

        var store = new SettingsStore(_path, new FakeApiKeyProtector());
        var result = store.Load();

        Assert.That(result.Settings.OpenAiApiKey, Is.Null);
        Assert.That(result.Warnings.Any(w => w.Contains("could not be decrypted")), Is.True);
    }

    [Test]
    public async Task SaveAsync_WithoutProtectorSupport_DropsKeysInsteadOfWritingPlaintext()
    {
        var store = new SettingsStore(_path, new FakeApiKeyProtector { IsAvailable = false });

        await store.SaveAsync(new AppSettings { OpenAiApiKey = "sk-openai-test" });

        var json = await File.ReadAllTextAsync(_path);
        Assert.That(json, Does.Not.Contain("sk-openai-test"));
        Assert.That(store.Load().Settings.OpenAiApiKey, Is.Null);
    }

    [Test]
    public async Task Load_ProtectedKeyWithoutProtectorSupport_WarnsAndIgnoresKey()
    {
        await File.WriteAllTextAsync(
            _path,
            "{\"openAiApiKey\":\"" + FakeApiKeyProtector.Marker + "c2stc2VjcmV0\"}");

        var store = new SettingsStore(_path, new FakeApiKeyProtector { IsAvailable = false });
        var result = store.Load();

        Assert.That(result.Settings.OpenAiApiKey, Is.Null);
        Assert.That(
            result.Warnings.Any(w => w.Contains("cannot be read on this platform")),
            Is.True);
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
        if (OperatingSystem.IsWindows())
        {
            var storedKey = root.GetProperty("openAiApiKey").GetString();
            Assert.That(storedKey, Does.StartWith(DpapiApiKeyProtector.Prefix));
            Assert.That(storedKey, Does.Not.Contain("sk-test"));
        }
        else
        {
            Assert.That(root.TryGetProperty("openAiApiKey", out _), Is.False);
        }
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
    public async Task SaveAsync_ThenLoad_RestoresLanguage()
    {
        await _store.SaveAsync(new AppSettings { Language = "zh-CN" });
        var result = _store.Load();

        Assert.That(result.Settings.Language, Is.EqualTo("zh-CN"));
        Assert.That(result.Warnings, Is.Empty);
    }

    [Test]
    public async Task Load_UnsupportedLanguage_FallsBackToSystemWithWarning()
    {
        await File.WriteAllTextAsync(_path, """{"language":"fr-FR"}""");

        var result = _store.Load();

        Assert.That(result.Settings.Language, Is.EqualTo(new AppSettings().Language));
        Assert.That(result.Warnings, Has.Count.EqualTo(1));
        Assert.That(result.Warnings[0], Does.Contain("language"));
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

    private sealed class FakeApiKeyProtector : IApiKeyProtector
    {
        public const string Marker = "fake:v1:";

        public bool IsAvailable { get; init; } = true;

        public bool IsProtected(string value) => value.StartsWith(Marker, StringComparison.Ordinal);

        public string Protect(string plaintext) =>
            Marker + Convert.ToBase64String(Encoding.UTF8.GetBytes(plaintext));

        public bool TryUnprotect(string storedValue, out string? plaintext)
        {
            try
            {
                plaintext = Encoding.UTF8.GetString(
                    Convert.FromBase64String(storedValue[Marker.Length..]));
                return true;
            }
            catch (FormatException)
            {
                plaintext = null;
                return false;
            }
        }
    }
}
