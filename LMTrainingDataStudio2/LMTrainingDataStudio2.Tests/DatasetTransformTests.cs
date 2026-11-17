using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services.Datasets;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class DatasetTransformTests
{
    private DatasetTransformService _service = null!;
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "lmtds-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _service = new DatasetTransformService(new AppSettings
        {
            CacheDirectory = Path.Combine(_directory, "cache")
        });
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_directory, true); } catch { /* best effort */ }
    }

    [Test]
    public async Task Deduplicate_RemovesExactDuplicatesAndConservesRows()
    {
        var source = CreateJsonl(
            """{"id":1}""",
            """{"id":2}""",
            """{"id":1}""",
            """{"id":3}""",
            """{"id":2}""");
        var target = Path.Combine(_directory, "dedup.jsonl");

        var result = await _service.TransformAsync(
            source, DataFileType.Jsonl,
            new DatasetTransformOptions(Deduplicate: true, Shuffle: false, OutputPath: target));

        Assert.That(result.TotalRows, Is.EqualTo(5));
        Assert.That(result.DuplicateRows, Is.EqualTo(2));
        Assert.That(result.OutputRows, Is.EqualTo(3));
        Assert.That(result.OutputRows + result.DuplicateRows, Is.EqualTo(result.TotalRows));

        var lines = await File.ReadAllLinesAsync(target);
        Assert.That(lines, Has.Length.EqualTo(3));
        Assert.That(lines[0], Is.EqualTo("""{"id":1}"""));
        Assert.That(lines[1], Is.EqualTo("""{"id":2}"""));
        Assert.That(lines[2], Is.EqualTo("""{"id":3}"""));
    }

    [Test]
    public async Task Shuffle_IsDeterministicForSameSeedAndConservesRows()
    {
        var source = CreateNumberedJsonl(50);
        var first = Path.Combine(_directory, "first.jsonl");
        var second = Path.Combine(_directory, "second.jsonl");
        var different = Path.Combine(_directory, "different.jsonl");

        await _service.TransformAsync(source, DataFileType.Jsonl,
            new DatasetTransformOptions(Shuffle: true, Seed: 7, OutputPath: first));
        await _service.TransformAsync(source, DataFileType.Jsonl,
            new DatasetTransformOptions(Shuffle: true, Seed: 7, OutputPath: second));
        await _service.TransformAsync(source, DataFileType.Jsonl,
            new DatasetTransformOptions(Shuffle: true, Seed: 8, OutputPath: different));

        var firstLines = await File.ReadAllLinesAsync(first);
        var secondLines = await File.ReadAllLinesAsync(second);
        var differentLines = await File.ReadAllLinesAsync(different);

        Assert.That(firstLines, Has.Length.EqualTo(50));
        Assert.That(secondLines, Is.EqualTo(firstLines));
        Assert.That(differentLines, Is.Not.EqualTo(firstLines));
        Assert.That(firstLines.OrderBy(line => line), Is.EqualTo(secondLines.OrderBy(line => line)));
        Assert.That(firstLines, Is.Not.EqualTo(File.ReadAllLines(source)));
    }

    [Test]
    public async Task Split_ByRatio_IsDeterministicAndConservesRows()
    {
        var source = CreateNumberedJsonl(100);
        var firstDirectory = Path.Combine(_directory, "split-a");
        var secondDirectory = Path.Combine(_directory, "split-b");

        var first = await _service.TransformAsync(source, DataFileType.Jsonl,
            new DatasetTransformOptions(
                Seed: 11,
                Splits: new[] { "train:0.8", "valid:0.2" },
                OutputDirectory: firstDirectory));
        var second = await _service.TransformAsync(source, DataFileType.Jsonl,
            new DatasetTransformOptions(
                Seed: 11,
                Splits: new[] { "train:0.8", "valid:0.2" },
                OutputDirectory: secondDirectory));

        Assert.That(first.Splits.Sum(split => split.Rows), Is.EqualTo(100));
        Assert.That(first.Splits.All(split => split.Rows > 0), Is.True);
        Assert.That(second.Splits.Select(split => split.Rows),
            Is.EqualTo(first.Splits.Select(split => split.Rows)));

        foreach (var split in first.Splits)
        {
            var expected = await File.ReadAllLinesAsync(split.Path);
            var actual = await File.ReadAllLinesAsync(
                Path.Combine(secondDirectory, Path.GetFileName(split.Path)));
            Assert.That(actual, Is.EqualTo(expected));
        }
    }

    [Test]
    public async Task Split_ByCount_FillsSplitsInOrder()
    {
        var source = CreateNumberedJsonl(6);
        var directory = Path.Combine(_directory, "count-split");

        var result = await _service.TransformAsync(source, DataFileType.Jsonl,
            new DatasetTransformOptions(
                Splits: new[] { "first:2", "second:3" },
                OutputDirectory: directory));

        Assert.That(result.Splits.Single(split => split.Name == "first").Rows, Is.EqualTo(2));
        Assert.That(result.Splits.Single(split => split.Name == "second").Rows, Is.EqualTo(4));
        Assert.That(result.Splits.Sum(split => split.Rows), Is.EqualTo(6));
    }

    [Test]
    public async Task DeduplicateShuffleAndSplit_CombineAndConserveRows()
    {
        var source = Path.Combine(_directory, "combined.jsonl");
        var lines = Enumerable.Range(0, 10).Select(index => $$"""{"id":{{index % 9}}}""").ToArray();
        await File.WriteAllLinesAsync(source, lines);
        var directory = Path.Combine(_directory, "combined-split");

        var result = await _service.TransformAsync(source, DataFileType.Jsonl,
            new DatasetTransformOptions(
                Deduplicate: true,
                Shuffle: true,
                Seed: 3,
                Splits: new[] { "train:0.7", "valid:0.3" },
                OutputDirectory: directory));

        Assert.That(result.TotalRows, Is.EqualTo(10));
        Assert.That(result.DuplicateRows, Is.EqualTo(1));
        Assert.That(result.OutputRows, Is.EqualTo(9));
        Assert.That(result.Splits.Sum(split => split.Rows), Is.EqualTo(9));
        Assert.That(result.OutputRows + result.DuplicateRows, Is.EqualTo(result.TotalRows));
    }

    [Test]
    public async Task CsvSource_CanProduceCsvOutput()
    {
        var source = Path.Combine(_directory, "data.csv");
        await File.WriteAllTextAsync(source, "id,text\n1,foo\n1,foo\n2,bar\n");
        var target = Path.Combine(_directory, "dedup.csv");

        var result = await _service.TransformAsync(source, DataFileType.Csv,
            new DatasetTransformOptions(Deduplicate: true, OutputPath: target));

        Assert.That(result.DuplicateRows, Is.EqualTo(1));
        Assert.That(result.OutputRows, Is.EqualTo(2));
        await using var reader = new CsvDatasetReader();
        await reader.OpenAsync(target);
        Assert.That(reader.TotalRows, Is.EqualTo(2));
    }

    [Test]
    public async Task Split_CsvSource_ProducesCsvSplits()
    {
        var source = Path.Combine(_directory, "split-source.csv");
        await File.WriteAllTextAsync(source, "id,text\n1,a\n2,b\n3,c\n4,d\n");
        var directory = Path.Combine(_directory, "csv-splits");

        var result = await _service.TransformAsync(source, DataFileType.Csv,
            new DatasetTransformOptions(
                Splits: new[] { "train:0.5", "valid:0.5" },
                Seed: 2,
                OutputDirectory: directory));

        Assert.That(result.Splits.Sum(split => split.Rows), Is.EqualTo(4));
        Assert.That(result.Splits.All(split => split.Path.EndsWith(".csv")), Is.True);
    }

    [Test]
    public void Transform_SourceEqualsTarget_Throws()
    {
        var source = CreateJsonl("""{"a":1}""");

        Assert.ThrowsAsync<ArgumentException>(() => _service.TransformAsync(
            source, DataFileType.Jsonl,
            new DatasetTransformOptions(OutputPath: source)));
    }

    [Test]
    public void Transform_InvalidSplits_Throws()
    {
        var source = CreateJsonl("""{"a":1}""");
        var directory = Path.Combine(_directory, "invalid-splits");

        Assert.ThrowsAsync<ArgumentException>(() => _service.TransformAsync(
            source, DataFileType.Jsonl,
            new DatasetTransformOptions(
                Splits: new[] { "train" },
                OutputDirectory: directory)));
    }

    [Test]
    public void Transform_CancellationLeavesNoTarget()
    {
        var source = CreateNumberedJsonl(3000);
        var target = Path.Combine(_directory, "cancelled.jsonl");
        using var cts = new CancellationTokenSource();
        var progress = new CancellingProgress(cts);

        Assert.ThrowsAsync<OperationCanceledException>(() => _service.TransformAsync(
            source, DataFileType.Jsonl,
            new DatasetTransformOptions(Deduplicate: true, Shuffle: true, OutputPath: target),
            progress, cts.Token));

        Assert.That(File.Exists(target), Is.False);
        Assert.That(Directory.GetFiles(_directory, "*.tmp-*"), Is.Empty);
    }

    private string CreateJsonl(params string[] lines)
    {
        var path = Path.Combine(_directory, $"data-{Guid.NewGuid():N}.jsonl");
        File.WriteAllLines(path, lines);
        return path;
    }

    private string CreateNumberedJsonl(int rows)
    {
        var path = Path.Combine(_directory, $"numbered-{Guid.NewGuid():N}.jsonl");
        File.WriteAllLines(path,
            Enumerable.Range(0, rows).Select(index => $$"""{"id":{{index}},"text":"row-{{index}}"}"""));
        return path;
    }

    private sealed class CancellingProgress : IProgress<double>
    {
        private readonly CancellationTokenSource _cts;

        public CancellingProgress(CancellationTokenSource cts) => _cts = cts;

        public void Report(double value)
        {
            if (value > 0)
            {
                _cts.Cancel();
            }
        }
    }
}
