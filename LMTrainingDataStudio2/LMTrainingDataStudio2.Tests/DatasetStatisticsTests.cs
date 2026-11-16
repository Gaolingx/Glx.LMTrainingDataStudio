using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services.Datasets;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class DatasetStatisticsTests
{
    private DatasetStatisticsService _service = null!;
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "lmtds-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _service = new DatasetStatisticsService(new AppSettings
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
    public async Task Compute_ReportsRolesLengthsToolsAndDuplicates()
    {
        var path = Path.Combine(_directory, "stats.jsonl");
        var openAi =
            """{"messages":[{"role":"system","content":"sys"},{"role":"user","content":"hello"},{"role":"assistant","content":"hi","tool_calls":[{"id":"1","type":"function","function":{"name":"f","arguments":"{}"}}]},{"role":"tool","content":"result"}]}""";
        File.WriteAllLines(path, new[]
        {
            openAi,
            openAi,
            """{"text":"plain text"}""",
            """{"instruction":"do","output":"done"}""",
            """{"prompt":"p","chosen":"c","rejected":"r"}""",
            """{"conversations":[{"from":"human","value":"q"},{"from":"gpt","value":"a"}]}""",
            "{}",
            "{broken"
        });

        var result = await _service.ComputeAsync(path, DataFileType.Jsonl);

        Assert.That(result.TotalRows, Is.EqualTo(8));
        Assert.That(result.MalformedRows, Is.EqualTo(1));
        Assert.That(result.DuplicateRows, Is.EqualTo(1));
        Assert.That(result.DuplicateRate, Is.EqualTo(1d / 8));
        Assert.That(result.EmptyRows, Is.EqualTo(1));

        Assert.That(result.RoleCounts["system"], Is.EqualTo(2));
        Assert.That(result.RoleCounts["user"], Is.EqualTo(5));
        Assert.That(result.RoleCounts["assistant"], Is.EqualTo(6));
        Assert.That(result.RoleCounts["tool"], Is.EqualTo(2));
        Assert.That(result.ToolCallCount, Is.EqualTo(2));
        Assert.That(result.ToolMessageCount, Is.EqualTo(2));
        Assert.That(result.EmptyContentMessages, Is.Zero);

        Assert.That(result.FormatCounts["OpenAI"], Is.EqualTo(2));
        Assert.That(result.FormatCounts["ShareGPT"], Is.EqualTo(1));
        Assert.That(result.FormatCounts["Alpaca"], Is.EqualTo(1));
        Assert.That(result.FormatCounts["Dpo"], Is.EqualTo(1));
        Assert.That(result.FormatCounts["Pretrain"], Is.EqualTo(1));
        Assert.That(result.FormatCounts["Unknown"], Is.EqualTo(1));

        Assert.That(result.LengthBuckets.Sum(bucket => bucket.Count), Is.EqualTo(8));
        Assert.That(result.TotalCharacters, Is.GreaterThan(0));
        Assert.That(result.AverageCharacters, Is.GreaterThan(0));
    }

    [Test]
    public async Task Compute_SkipsDuplicateRateWhenDisabled()
    {
        var path = Path.Combine(_directory, "nodup.jsonl");
        File.WriteAllLines(path, new[]
        {
            """{"id":1}""",
            """{"id":2}"""
        });

        var result = await _service.ComputeAsync(
            path, DataFileType.Jsonl, computeDuplicates: false);

        Assert.That(result.TotalRows, Is.EqualTo(2));
        Assert.That(result.DuplicateRows, Is.Zero);
        Assert.That(result.DuplicateRate, Is.Zero);
    }

    [Test]
    public async Task Compute_CsvSource_ProfilesRows()
    {
        var path = Path.Combine(_directory, "data.csv");
        await File.WriteAllTextAsync(path, "id,text\n1,foo\n1,foo\n2,bar\n");

        var result = await _service.ComputeAsync(path, DataFileType.Csv);

        Assert.That(result.TotalRows, Is.EqualTo(3));
        Assert.That(result.DuplicateRows, Is.EqualTo(1));
        Assert.That(result.MalformedRows, Is.Zero);
    }

    [Test]
    public async Task Compute_LengthBucketsCoverBoundaries()
    {
        var path = Path.Combine(_directory, "lengths.jsonl");
        File.WriteAllLines(path, new[]
        {
            """{"text":""}""",
            $$"""{"text":"{{new string('a', 1)}}"}""",
            $$"""{"text":"{{new string('a', 101)}}"}""",
            $$"""{"text":"{{new string('a', 501)}}"}""",
            $$"""{"text":"{{new string('a', 8001)}}"}"""
        });

        var result = await _service.ComputeAsync(path, DataFileType.Jsonl);

        Assert.That(result.LengthBuckets.Single(bucket => bucket.Min == 0).Count, Is.EqualTo(1));
        Assert.That(result.LengthBuckets.Single(bucket => bucket.Min == 1).Count, Is.EqualTo(1));
        Assert.That(result.LengthBuckets.Single(bucket => bucket.Min == 101).Count, Is.EqualTo(1));
        Assert.That(result.LengthBuckets.Single(bucket => bucket.Min == 501).Count, Is.EqualTo(1));
        Assert.That(result.LengthBuckets.Single(bucket => bucket.Min == 8001).Count, Is.EqualTo(1));
    }
}