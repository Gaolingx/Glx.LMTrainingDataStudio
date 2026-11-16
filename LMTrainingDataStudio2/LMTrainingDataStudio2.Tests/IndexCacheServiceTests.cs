using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class IndexCacheServiceTests
{
    private string _directory = null!;
    private AppSettings _settings = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "lmtds-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _settings = new AppSettings
        {
            CacheDirectory = Path.Combine(_directory, "cache"),
            MaxCacheSizeMb = 16
        };
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_directory, true); } catch { /* best effort */ }
    }

    [Test]
    public async Task BuildIndex_ThenReadLines_RoundTrips()
    {
        var dataPath = Path.Combine(_directory, "data.jsonl");
        await File.WriteAllTextAsync(dataPath, "{\"a\":1}\n{\"b\":2}\n");

        using var service = new IndexCacheService(_settings);
        await using var connection = await service.OpenCacheAsync(dataPath);

        await service.BuildIndexAsync(dataPath, connection);

        Assert.That(await service.GetTotalLinesAsync(connection), Is.EqualTo(2));
        Assert.That(await service.ReadLineAsync(dataPath, connection, 0), Is.EqualTo("{\"a\":1}"));
        Assert.That(await service.ReadLineAsync(dataPath, connection, 1), Is.EqualTo("{\"b\":2}"));
        Assert.That(await service.ReadLineAsync(dataPath, connection, 2), Is.Null);
    }

    [Test]
    public async Task BuildIndex_LastLineWithoutNewline_IsReadable()
    {
        var dataPath = Path.Combine(_directory, "no-newline.jsonl");
        await File.WriteAllTextAsync(dataPath, "first\nlast");

        using var service = new IndexCacheService(_settings);
        await using var connection = await service.OpenCacheAsync(dataPath);

        await service.BuildIndexAsync(dataPath, connection);

        Assert.That(await service.GetTotalLinesAsync(connection), Is.EqualTo(2));
        Assert.That(await service.ReadLineAsync(dataPath, connection, 1), Is.EqualTo("last"));
    }

    [Test]
    public async Task GetTotalCacheSize_AfterIndexing_IsPositive()
    {
        var dataPath = Path.Combine(_directory, "data.jsonl");
        await File.WriteAllTextAsync(dataPath, "{}\n");

        using var service = new IndexCacheService(_settings);
        await using (var connection = await service.OpenCacheAsync(dataPath))
        {
            await service.BuildIndexAsync(dataPath, connection);
        }

        Assert.That(service.GetTotalCacheSize(), Is.GreaterThan(0));
    }

    [Test]
    public void CleanCache_WhenDirectoryHasNoDatabases_DoesNotThrow()
    {
        using var service = new IndexCacheService(_settings);

        Assert.DoesNotThrow(() => service.CleanCache());
        Assert.That(service.GetTotalCacheSize(), Is.EqualTo(0));
    }
}
