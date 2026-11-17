using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services.Executors;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class RowSpillBufferTests
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

    [Test]
    public async Task Append_BelowThreshold_StaysInMemoryAndReadsBack()
    {
        await using var buffer = new RowSpillBuffer(memoryLimitRows: 10, _directory);

        for (var i = 0; i < 5; i++)
        {
            await buffer.AppendAsync(new DatasetRow(i, $"{{\"value\":{i}}}"));
        }

        await buffer.SealAsync();

        Assert.That(buffer.IsSpilled, Is.False);
        Assert.That(buffer.Count, Is.EqualTo(5));
        Assert.That(GetFiles(_directory), Is.Empty);

        var rows = await CollectAsync(buffer.ReadAsync());
        Assert.That(rows, Has.Count.EqualTo(5));
        Assert.That(rows.Select(row => row.LineNumber), Is.EqualTo(new long[] { 0, 1, 2, 3, 4 }));
        Assert.That(rows[3].Json, Is.EqualTo("{\"value\":3}"));
    }

    [Test]
    public async Task Append_BeyondThreshold_SpillsToDiskAndPreservesLineNumbers()
    {
        await using var buffer = new RowSpillBuffer(memoryLimitRows: 3, _directory);

        for (var i = 0; i < 8; i++)
        {
            await buffer.AppendAsync(new DatasetRow(100 + i, $"{{\"value\":{i}}}"));
        }

        await buffer.SealAsync();

        Assert.That(buffer.IsSpilled, Is.True);
        Assert.That(buffer.FilePath, Is.Not.Null);
        Assert.That(File.Exists(buffer.FilePath), Is.True);
        Assert.That(buffer.Count, Is.EqualTo(8));

        var rows = await CollectAsync(buffer.ReadAsync());
        Assert.That(rows, Has.Count.EqualTo(8));
        Assert.That(rows[0].LineNumber, Is.EqualTo(100));
        Assert.That(rows[7].LineNumber, Is.EqualTo(107));
        Assert.That(rows[7].Json, Is.EqualTo("{\"value\":7}"));
    }

    [Test]
    public async Task Append_ExceedingByteBudget_SpillsBelowRowLimit()
    {
        var payload = new string('x', 100);
        await using var buffer = new RowSpillBuffer(
            memoryLimitRows: 100, _directory, memoryLimitBytes: 220);

        for (var i = 0; i < 3; i++)
        {
            await buffer.AppendAsync(new DatasetRow(i, $"{{\"value\":\"{payload}\"}}"));
        }

        await buffer.SealAsync();

        Assert.That(buffer.IsSpilled, Is.True);
        Assert.That(buffer.Count, Is.EqualTo(3));
        var rows = await CollectAsync(buffer.ReadAsync());
        Assert.That(rows, Has.Count.EqualTo(3));
        Assert.That(rows[2].Json, Does.Contain(payload));
    }

    [Test]
    public async Task Append_UnderByteAndRowBudgets_StaysInMemory()
    {
        await using var buffer = new RowSpillBuffer(
            memoryLimitRows: 100, _directory, memoryLimitBytes: 10_000);

        for (var i = 0; i < 5; i++)
        {
            await buffer.AppendAsync(new DatasetRow(i, $"{{\"value\":{i}}}"));
        }

        await buffer.SealAsync();

        Assert.That(buffer.IsSpilled, Is.False);
        Assert.That(buffer.Count, Is.EqualTo(5));
    }

    [Test]
    public async Task Append_ZeroThreshold_SpillsImmediately()
    {
        await using var buffer = new RowSpillBuffer(memoryLimitRows: 0, _directory);

        await buffer.AppendAsync(new DatasetRow(1, "{\"value\":1}"));

        Assert.That(buffer.IsSpilled, Is.True);
        Assert.That(File.Exists(buffer.FilePath), Is.True);
    }

    [Test]
    public async Task Dispose_DeletesSpillFileButKeepsExternallyOwnedFile()
    {
        var owned = new RowSpillBuffer(memoryLimitRows: 0, _directory);
        await owned.AppendAsync(new DatasetRow(0, "{\"value\":0}"));
        await owned.SealAsync();
        var ownedPath = owned.FilePath!;
        await owned.DisposeAsync();

        Assert.That(File.Exists(ownedPath), Is.False);

        var externalPath = Path.Combine(_directory, "external.rows");
        await File.WriteAllTextAsync(externalPath, "0\t{\"value\":0}\n");

        var external = RowSpillBuffer.FromFile(externalPath, count: 1);
        Assert.That(external.Count, Is.EqualTo(1));
        var rows = await CollectAsync(external.ReadAsync());
        Assert.That(rows, Has.Count.EqualTo(1));
        await external.DisposeAsync();

        Assert.That(File.Exists(externalPath), Is.True);
    }

    [Test]
    public async Task ReadAsync_TransformedJsonWithTabWhitespace_RoundTrips()
    {
        await using var buffer = new RowSpillBuffer(memoryLimitRows: 0, _directory);

        await buffer.AppendAsync(new DatasetRow(7, "\t{\"value\":\"a\\tb\"}"));
        await buffer.SealAsync();

        var rows = await CollectAsync(buffer.ReadAsync());
        Assert.That(rows.Single().Json, Is.EqualTo("\t{\"value\":\"a\\tb\"}"));
    }

    private static string[] GetFiles(string directory) =>
        Directory.Exists(directory)
            ? Directory.GetFileSystemEntries(directory, "*", SearchOption.AllDirectories)
            : Array.Empty<string>();

    private static async Task<List<DatasetRow>> CollectAsync(IAsyncEnumerable<DatasetRow> rows)
    {
        var result = new List<DatasetRow>();
        await foreach (var row in rows)
        {
            result.Add(row);
        }

        return result;
    }
}
