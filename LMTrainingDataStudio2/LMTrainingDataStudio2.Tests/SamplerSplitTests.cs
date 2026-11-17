using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services.Executors;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class SamplerSplitTests
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
    public async Task RatioSplits_AreDeterministic_AndConserveRows()
    {
        var input = CreateRows(100);
        var block = CreateBlock(new SamplerBlockConfig(
            Count: null,
            Ratio: null,
            Seed: 7,
            Splits: new[] { "train:0.8", "valid:0.2" },
            OutputDirectory: _directory));

        var executor = new SamplerBlockExecutor();
        var first = await CollectAsync(
            (await executor.ExecuteAsync(CreateContext(block, input), CancellationToken.None)).Output);
        var firstTrain = await File.ReadAllLinesAsync(Path.Combine(_directory, "train.jsonl"));
        var firstValid = await File.ReadAllLinesAsync(Path.Combine(_directory, "valid.jsonl"));

        var second = await CollectAsync(
            (await executor.ExecuteAsync(CreateContext(block, input), CancellationToken.None)).Output);
        var secondTrain = await File.ReadAllLinesAsync(Path.Combine(_directory, "train.jsonl"));
        var secondValid = await File.ReadAllLinesAsync(Path.Combine(_directory, "valid.jsonl"));

        Assert.That(first, Has.Count.EqualTo(100));
        Assert.That(second.Select(row => row.Json), Is.EqualTo(first.Select(row => row.Json)));
        Assert.That(firstTrain, Is.EqualTo(secondTrain));
        Assert.That(firstValid, Is.EqualTo(secondValid));
        Assert.That(firstTrain.Length + firstValid.Length, Is.EqualTo(100));
        Assert.That(Directory.GetFiles(_directory, "*.tmp"), Is.Empty);
    }

    [Test]
    public async Task CountSplits_FillSequentially_AndGiveRemainderToLast()
    {
        var block = CreateBlock(new SamplerBlockConfig(
            Count: null,
            Ratio: null,
            Seed: 1,
            Splits: new[] { "first:3", "rest:2" },
            OutputDirectory: _directory));
        var executor = new SamplerBlockExecutor();

        var output = await CollectAsync(
            (await executor.ExecuteAsync(CreateContext(block, CreateRows(10)), CancellationToken.None)).Output);

        var first = await File.ReadAllLinesAsync(Path.Combine(_directory, "first.jsonl"));
        var rest = await File.ReadAllLinesAsync(Path.Combine(_directory, "rest.jsonl"));

        Assert.That(output, Has.Count.EqualTo(10));
        Assert.That(first, Has.Length.EqualTo(3));
        Assert.That(rest, Has.Length.EqualTo(7));
    }

    [Test]
    public void SplitsWithoutOutputDirectory_AreRejected()
    {
        var block = CreateBlock(new SamplerBlockConfig(
            Count: null,
            Ratio: null,
            Seed: 1,
            Splits: new[] { "train:1.0" },
            OutputDirectory: null));
        var executor = new SamplerBlockExecutor();

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await executor.ExecuteAsync(CreateContext(block, Array.Empty<DatasetRow>()), CancellationToken.None));
    }

    [Test]
    public void InvalidSplitSpec_IsRejected()
    {
        var block = CreateBlock(new SamplerBlockConfig(
            Count: null,
            Ratio: null,
            Seed: 1,
            Splits: new[] { "train" },
            OutputDirectory: _directory));
        var executor = new SamplerBlockExecutor();

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await executor.ExecuteAsync(CreateContext(block, Array.Empty<DatasetRow>()), CancellationToken.None));
    }

    [Test]
    public void SplitNameWithPathSeparator_IsRejected()
    {
        var block = CreateBlock(new SamplerBlockConfig(
            Count: null,
            Ratio: null,
            Seed: 1,
            Splits: new[] { "../escape:0.5" },
            OutputDirectory: _directory));
        var executor = new SamplerBlockExecutor();

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await executor.ExecuteAsync(CreateContext(block, Array.Empty<DatasetRow>()), CancellationToken.None));
    }

    private static DatasetRow[] CreateRows(int count) =>
        Enumerable.Range(0, count).Select(i => new DatasetRow(i, $"{{\"i\":{i}}}")).ToArray();

    private static RecipeBlock CreateBlock(SamplerBlockConfig config) => new()
    {
        Id = "sample",
        Type = BlockType.Sampler,
        Name = "sample",
        DisplayName = "sample",
        Config = config
    };

    private static BlockExecutionContext CreateContext(
        RecipeBlock block,
        IEnumerable<DatasetRow> input) => new()
        {
            Block = block,
            Input = ToAsync(input),
            Log = _ => { }
        };

    private static async IAsyncEnumerable<DatasetRow> ToAsync(IEnumerable<DatasetRow> rows)
    {
        await Task.Yield();
        foreach (var row in rows)
        {
            yield return row;
        }
    }

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
