using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services.Datasets;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class DatasetSearchReplaceTests
{
    private DatasetSearchReplaceService _service = null!;
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _service = new DatasetSearchReplaceService(new AppSettings
        {
            CacheDirectory = Path.Combine(Path.GetTempPath(), "lmtds-tests", Guid.NewGuid().ToString("N"))
        });
        _directory = Path.Combine(Path.GetTempPath(), "lmtds-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_directory, true); } catch { /* best effort */ }
    }

    [Test]
    public async Task Preview_LiteralIsCaseSensitiveWhenRequested()
    {
        var path = CreateJsonl(
            """{"text":"foo"}""",
            """{"text":"foo"}""",
            """{"text":"Foo"}""",
            """{"text":"bar"}""",
            """{"text":"foofoo"}""");

        var insensitive = await _service.PreviewAsync(
            path, DataFileType.Jsonl, new DatasetSearchRequest("foo", "bar", false, false));
        var sensitive = await _service.PreviewAsync(
            path, DataFileType.Jsonl, new DatasetSearchRequest("foo", "bar", false, true));

        Assert.That(insensitive.ScannedRows, Is.EqualTo(5));
        Assert.That(insensitive.MatchingRows, Is.EqualTo(4));
        Assert.That(insensitive.TotalMatches, Is.EqualTo(5));
        Assert.That(insensitive.Matches[0].LineNumber, Is.EqualTo(1));
        Assert.That(insensitive.Matches[0].Snippet, Does.Contain("foo"));

        Assert.That(sensitive.MatchingRows, Is.EqualTo(3));
        Assert.That(sensitive.TotalMatches, Is.EqualTo(4));
    }

    [Test]
    public async Task Preview_CurrentPageOnly_ScansRequestedWindow()
    {
        var path = CreateJsonl(
            """{"text":"hit"}""",
            """{"text":"miss"}""",
            """{"text":"hit"}""",
            """{"text":"miss"}""");

        var preview = await _service.PreviewAsync(
            path, DataFileType.Jsonl,
            new DatasetSearchRequest("hit", string.Empty, false, false, StartRow: 2, RowCount: 2));

        Assert.That(preview.ScannedRows, Is.EqualTo(2));
        Assert.That(preview.MatchingRows, Is.EqualTo(1));
        Assert.That(preview.Matches.Single().LineNumber, Is.EqualTo(3));
    }

    [Test]
    public async Task Preview_RegexReportsGroupMatchesAndTruncates()
    {
        var path = CreateJsonl(
            """{"text":"one"}""",
            """{"text":"two"}""",
            """{"text":"three"}""");

        var preview = await _service.PreviewAsync(
            path, DataFileType.Jsonl,
            new DatasetSearchRequest("\"text\":\"(\\w+)\"", "x", UseRegex: true,
                CaseSensitive: true),
            maxMatches: 1);

        Assert.That(preview.MatchingRows, Is.EqualTo(3));
        Assert.That(preview.TotalMatches, Is.EqualTo(3));
        Assert.That(preview.Matches, Has.Count.EqualTo(1));
        Assert.That(preview.Truncated, Is.True);
    }

    [Test]
    public async Task Replace_WritesTargetConservingRowCountAndSource()
    {
        var source = CreateJsonl(
            """{"text":"foo"}""",
            """{"text":"bar"}""",
            """{"text":"foo"}""");
        var original = await File.ReadAllTextAsync(source);
        var target = Path.Combine(_directory, "replaced.jsonl");

        var result = await _service.ReplaceAsync(
            source, DataFileType.Jsonl, target,
            new DatasetSearchRequest("foo", "baz", false, true));

        Assert.That(result.ScannedRows, Is.EqualTo(3));
        Assert.That(result.ChangedRows, Is.EqualTo(2));
        Assert.That(result.TotalMatches, Is.EqualTo(2));
        Assert.That(await File.ReadAllTextAsync(source), Is.EqualTo(original));

        var lines = await File.ReadAllLinesAsync(target);
        Assert.That(lines, Has.Length.EqualTo(3));
        Assert.That(lines[0], Does.Contain("baz"));
        Assert.That(lines[1], Does.Contain("bar"));
        Assert.That(lines[2], Does.Contain("baz"));
    }

    [Test]
    public async Task Replace_RegexExpandsGroups()
    {
        var source = CreateJsonl(
            """{"text":"one"}""",
            """{"text":"two"}""");
        var target = Path.Combine(_directory, "groups.jsonl");

        await _service.ReplaceAsync(
            source, DataFileType.Jsonl, target,
            new DatasetSearchRequest("(\"text\":\")(\\w+)\"", "$1prefix-$2\"", true, true));

        var lines = await File.ReadAllLinesAsync(target);
        Assert.That(lines[0], Is.EqualTo("""{"text":"prefix-one"}"""));
        Assert.That(lines[1], Is.EqualTo("""{"text":"prefix-two"}"""));
    }

    [Test]
    public async Task Replace_CancellationDoesNotPublishTarget()
    {
        var source = Path.Combine(_directory, "big.jsonl");
        await File.WriteAllLinesAsync(source,
            Enumerable.Range(0, 2000).Select(index => $$"""{"id":{{index}},"text":"foo"}"""));
        var target = Path.Combine(_directory, "cancelled.jsonl");

        using var cts = new CancellationTokenSource();
        var progress = new CancellingProgress(cts);

        Assert.ThrowsAsync<OperationCanceledException>(() => _service.ReplaceAsync(
            source, DataFileType.Jsonl, target,
            new DatasetSearchRequest("foo", "bar", false, true),
            progress, cts.Token));

        Assert.That(File.Exists(target), Is.False);
        Assert.That(Directory.GetFiles(_directory, "*.tmp-*"), Is.Empty);
    }

    [Test]
    public async Task Preview_CsvSource_Works()
    {
        var path = Path.Combine(_directory, "data.csv");
        await File.WriteAllTextAsync(path, "id,text\n1,foo\n2,bar\n");

        var preview = await _service.PreviewAsync(
            path, DataFileType.Csv, new DatasetSearchRequest("foo", "x", false, true));

        Assert.That(preview.ScannedRows, Is.EqualTo(2));
        Assert.That(preview.MatchingRows, Is.EqualTo(1));
        Assert.That(preview.Matches[0].LineNumber, Is.EqualTo(1));
    }

    [Test]
    public void Preview_EmptyQuery_Throws()
    {
        var path = CreateJsonl("""{"a":1}""");

        Assert.ThrowsAsync<ArgumentException>(() => _service.PreviewAsync(
            path, DataFileType.Jsonl, new DatasetSearchRequest(string.Empty, "x", false, true)));
    }

    [Test]
    public void Preview_InvalidRegex_Throws()
    {
        var path = CreateJsonl("""{"a":1}""");

        Assert.ThrowsAsync<ArgumentException>(() => _service.PreviewAsync(
            path, DataFileType.Jsonl, new DatasetSearchRequest("(", "x", true, true)));
    }

    private string CreateJsonl(params string[] lines)
    {
        var path = Path.Combine(_directory, $"data-{Guid.NewGuid():N}.jsonl");
        File.WriteAllLines(path, lines);
        return path;
    }

    private sealed class CancellingProgress : IProgress<double>
    {
        private readonly CancellationTokenSource _cts;

        public CancellingProgress(CancellationTokenSource cts) => _cts = cts;

        public void Report(double value) => _cts.Cancel();
    }
}
