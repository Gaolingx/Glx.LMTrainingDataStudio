using System.Text;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class JsonlDatasetReaderTests
{
    private string _directory = null!;
    private string _dataPath = null!;
    private AppSettings _settings = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "lmtds-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _dataPath = Path.Combine(_directory, "data.jsonl");
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
    public async Task OpenAsync_BuildsIndex_AndReportsTotalLines()
    {
        await File.WriteAllTextAsync(_dataPath, "{\"a\":1}\n{\"b\":2}\n{\"c\":3}");
        await using var reader = new JsonlDatasetReader(_settings);

        await reader.OpenAsync(_dataPath);

        Assert.That(reader.TotalLines, Is.EqualTo(3));
        Assert.That(reader.SourcePath, Is.EqualTo(Path.GetFullPath(_dataPath)));
    }

    [Test]
    public async Task ReadPageAsync_ReturnsRequestedSlice()
    {
        await File.WriteAllTextAsync(_dataPath, "{\"a\":1}\n{\"b\":2}\n{\"c\":3}\n{\"d\":4}");
        await using var reader = new JsonlDatasetReader(_settings);
        await reader.OpenAsync(_dataPath);

        var page = await reader.ReadPageAsync(1, 2);

        Assert.That(page, Has.Count.EqualTo(2));
        Assert.That(page[0].LineNumber, Is.EqualTo(1));
        Assert.That(page[0].Json, Is.EqualTo("{\"b\":2}"));
        Assert.That(page[1].LineNumber, Is.EqualTo(2));
        Assert.That(page[1].Json, Is.EqualTo("{\"c\":3}"));
    }

    [Test]
    public async Task ReadPageAsync_PastEnd_ClampsOrReturnsEmpty()
    {
        await File.WriteAllTextAsync(_dataPath, "a\nb\nc");
        await using var reader = new JsonlDatasetReader(_settings);
        await reader.OpenAsync(_dataPath);

        Assert.That(await reader.ReadPageAsync(2, 10), Has.Count.EqualTo(1));
        Assert.That(await reader.ReadPageAsync(5, 2), Is.Empty);
        Assert.That(await reader.ReadPageAsync(0, 0), Is.Empty);
    }

    [Test]
    public async Task ReadLineAsync_OutOfRange_ReturnsNull()
    {
        await File.WriteAllTextAsync(_dataPath, "a\nb");
        await using var reader = new JsonlDatasetReader(_settings);
        await reader.OpenAsync(_dataPath);

        Assert.That(await reader.ReadLineAsync(-1), Is.Null);
        Assert.That(await reader.ReadLineAsync(2), Is.Null);
    }

    [Test]
    public async Task ReadLineAsync_LastLineWithoutNewline_IsReadable()
    {
        await File.WriteAllTextAsync(_dataPath, "first\nlast");
        await using var reader = new JsonlDatasetReader(_settings);
        await reader.OpenAsync(_dataPath);

        var row = await reader.ReadLineAsync(1);

        Assert.That(row, Is.Not.Null);
        Assert.That(row!.Json, Is.EqualTo("last"));
    }

    [Test]
    public async Task OpenAsync_CrlfWithBom_ReadsCleanLines()
    {
        var preamble = Encoding.UTF8.GetPreamble();
        var content = Encoding.UTF8.GetBytes("{\"a\":1}\r\n{\"b\":2}\r\n");
        await File.WriteAllBytesAsync(_dataPath, [.. preamble, .. content]);
        await using var reader = new JsonlDatasetReader(_settings);

        await reader.OpenAsync(_dataPath);
        var page = await reader.ReadPageAsync(0, 2);

        Assert.That(page, Has.Count.EqualTo(2));
        Assert.That(page[0].Json, Is.EqualTo("{\"a\":1}"));
        Assert.That(page[1].Json, Is.EqualTo("{\"b\":2}"));
    }

    [Test]
    public async Task ReadLineAsync_LongLine_IsReadCompletely()
    {
        var longJson = "{\"text\":\"" + new string('x', 200_000) + "\"}";
        await File.WriteAllTextAsync(_dataPath, longJson + "\nshort");
        await using var reader = new JsonlDatasetReader(_settings);
        await reader.OpenAsync(_dataPath);

        var row = await reader.ReadLineAsync(0);

        Assert.That(row, Is.Not.Null);
        Assert.That(row!.Json, Is.EqualTo(longJson));
    }

    [Test]
    public async Task OpenAsync_AfterFileChanges_RebuildsIndex()
    {
        await File.WriteAllTextAsync(_dataPath, "a\nb\n");
        await using (var first = new JsonlDatasetReader(_settings))
        {
            await first.OpenAsync(_dataPath);
            Assert.That(first.TotalLines, Is.EqualTo(2));
        }

        await File.WriteAllTextAsync(_dataPath, "a\nb\nc\n");
        File.SetLastWriteTimeUtc(_dataPath, DateTime.UtcNow.AddSeconds(1));

        await using var second = new JsonlDatasetReader(_settings);
        await second.OpenAsync(_dataPath);

        Assert.That(second.TotalLines, Is.EqualTo(3));
        var row = await second.ReadLineAsync(2);
        Assert.That(row!.Json, Is.EqualTo("c"));
    }

    [Test]
    public async Task OpenAsync_EmptyFile_ReadsZeroRows()
    {
        await File.WriteAllTextAsync(_dataPath, string.Empty);
        await using var reader = new JsonlDatasetReader(_settings);

        await reader.OpenAsync(_dataPath);

        Assert.That(reader.TotalLines, Is.EqualTo(0));
        Assert.That(await reader.ReadPageAsync(0, 10), Is.Empty);
        Assert.That(await reader.ReadLineAsync(0), Is.Null);
    }

    [Test]
    public async Task OpenAsync_CalledTwice_Throws()
    {
        await File.WriteAllTextAsync(_dataPath, "a\n");
        await using var reader = new JsonlDatasetReader(_settings);
        await reader.OpenAsync(_dataPath);

        Assert.ThrowsAsync<InvalidOperationException>(async () => await reader.OpenAsync(_dataPath));
    }

    [Test]
    public async Task ReadLineAsync_BeforeOpen_ThrowsInvalidOperationException()
    {
        await using var reader = new JsonlDatasetReader(_settings);

        Assert.ThrowsAsync<InvalidOperationException>(async () => await reader.ReadLineAsync(0));
    }

    [Test]
    public async Task OpenAsync_MissingFile_ThrowsFileNotFoundException()
    {
        await using var reader = new JsonlDatasetReader(_settings);

        Assert.ThrowsAsync<FileNotFoundException>(
            async () => await reader.OpenAsync(Path.Combine(_directory, "missing.jsonl")));
    }
}
