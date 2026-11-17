using System.Text;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services;
using LMTrainingDataStudio2.Services.Datasets;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class DatasetEditServiceTests
{
    private string _directory = null!;
    private AppSettings _settings = null!;
    private DatasetEditService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "lmtds-edit-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _settings = new AppSettings
        {
            CacheDirectory = Path.Combine(_directory, "cache"),
            MaxCacheSizeMb = 16
        };
        _service = new DatasetEditService(_settings);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_directory, true); } catch { /* best effort */ }
    }

    [Test]
    public async Task Save_ReplaceInsertDelete_ConservesRowsAndKeepsUntouchedRows()
    {
        var source = CreateJsonl(5);
        var target = Path.Combine(_directory, "edited.jsonl");
        var session = new DatasetEditSession(5);
        session.SetRowJson(1, """{"id":2,"text":"changed"}""");
        session.DeleteSourceRow(3);
        session.InsertBefore(0, """{"id":0,"text":"prepended"}""");
        session.InsertBefore(2, """{"id":90,"text":"middle"}""");
        session.InsertBefore(5, """{"id":91,"text":"appended"}""");

        var result = await _service.SaveAsync(session.Snapshot(), source, DataFileType.Jsonl, target);

        var lines = await File.ReadAllLinesAsync(target);
        Assert.That(result.SourceRows, Is.EqualTo(5));
        Assert.That(result.WrittenRows, Is.EqualTo(7));
        Assert.That(result.ReplacedRows, Is.EqualTo(1));
        Assert.That(result.DeletedRows, Is.EqualTo(1));
        Assert.That(result.InsertedRows, Is.EqualTo(3));
        Assert.That(result.UnchangedRows, Is.EqualTo(3));
        Assert.That(lines, Has.Length.EqualTo(7));
        Assert.That(lines[0], Does.Contain("prepended"));
        Assert.That(lines[1], Is.EqualTo("""{"id":1,"text":"row-1"}"""));
        Assert.That(lines[2], Is.EqualTo("""{"id":2,"text":"changed"}"""));
        Assert.That(lines[3], Does.Contain("middle"));
        Assert.That(lines[4], Is.EqualTo("""{"id":3,"text":"row-3"}"""));
        Assert.That(lines[5], Is.EqualTo("""{"id":5,"text":"row-5"}"""));
        Assert.That(lines[6], Does.Contain("appended"));
        Assert.That(lines, Has.No.Member("""{"id":4,"text":"row-4"}"""));
        Assert.That(session.EffectiveRows, Is.EqualTo(7));
    }

    [Test]
    public async Task Save_OverwriteSourcePath_ReplacesFile()
    {
        var source = CreateJsonl(3);
        var session = new DatasetEditSession(3);
        session.SetRowJson(0, """{"id":1,"text":"overwritten"}""");

        var result = await _service.SaveAsync(
            session.Snapshot(), source, DataFileType.Jsonl, source);

        var lines = await File.ReadAllLinesAsync(source);
        Assert.That(result.WrittenRows, Is.EqualTo(3));
        Assert.That(lines[0], Does.Contain("overwritten"));
        Assert.That(lines, Has.Length.EqualTo(3));
        Assert.That(Directory.GetFiles(_directory, "*.tmp-*"), Is.Empty);
    }

    [Test]
    public void Save_TargetFormatDiffers_Throws()
    {
        var source = CreateJsonl(2);
        var target = Path.Combine(_directory, "edited.csv");
        var session = new DatasetEditSession(2);

        Assert.ThrowsAsync<DatasetFormatException>(() =>
            _service.SaveAsync(session.Snapshot(), source, DataFileType.Jsonl, target));
    }

    [Test]
    public async Task Save_InvalidJson_FailsBeforeWriting()
    {
        var source = CreateJsonl(2);
        var target = Path.Combine(_directory, "invalid.jsonl");
        var session = new DatasetEditSession(2);
        session.SetRowJson(1, "{not json");

        Assert.ThrowsAsync<DatasetFormatException>(() =>
            _service.SaveAsync(session.Snapshot(), source, DataFileType.Jsonl, target));

        Assert.That(File.Exists(target), Is.False);
        Assert.That(await File.ReadAllLinesAsync(source), Has.Length.EqualTo(2));
    }

    [Test]
    public async Task Save_Cancelled_LeavesSourceAndTargetUntouched()
    {
        var source = CreateJsonl(50);
        var original = await File.ReadAllBytesAsync(source);
        var target = Path.Combine(_directory, "cancelled.jsonl");
        var session = new DatasetEditSession(50);
        session.SetRowJson(0, """{"id":1,"text":"changed"}""");

        using var cancellation = new CancellationTokenSource();
        var progress = new CancelAfterFirstReport(cancellation);

        Assert.CatchAsync<OperationCanceledException>(() =>
            _service.SaveAsync(
                session.Snapshot(), source, DataFileType.Jsonl, target,
                chunkRows: 10, progress: progress, cancellationToken: cancellation.Token));

        Assert.That(File.Exists(target), Is.False);
        Assert.That(await File.ReadAllBytesAsync(source), Is.EqualTo(original));
        Assert.That(Directory.GetFiles(_directory, "*.tmp-*"), Is.Empty);
    }

    [Test]
    public async Task BuildSummary_ReportsCountsAndSamples()
    {
        var source = CreateJsonl(4);
        await using var reader = new JsonlDatasetReader(_settings);
        await reader.OpenAsync(source);

        var session = new DatasetEditSession(4);
        session.SetRowJson(0, """{"id":1,"text":"changed"}""");
        session.DeleteSourceRow(1);
        session.InsertBefore(2, """{"id":50,"text":"inserted"}""");

        var summary = await _service.BuildSummaryAsync(session.Snapshot(), reader, maxSamples: 5);

        Assert.That(summary.SourceRows, Is.EqualTo(4));
        Assert.That(summary.EffectiveRows, Is.EqualTo(4));
        Assert.That(summary.ReplacedRows, Is.EqualTo(1));
        Assert.That(summary.DeletedRows, Is.EqualTo(1));
        Assert.That(summary.InsertedRows, Is.EqualTo(1));
        Assert.That(summary.Samples, Has.Count.EqualTo(3));
        Assert.That(summary.Samples[0].Kind, Is.EqualTo("replace"));
        Assert.That(summary.Samples[0].Before, Is.EqualTo("""{"id":1,"text":"row-1"}"""));
        Assert.That(summary.Samples[0].After, Does.Contain("changed"));
        Assert.That(summary.Samples[1].Kind, Is.EqualTo("delete"));
        Assert.That(summary.Samples[2].Kind, Is.EqualTo("insert"));
        Assert.That(summary.Samples[2].SourceLine, Is.EqualTo(3));
    }

    [Test]
    public void Session_UndoRedo_RestoresExactly()
    {
        var session = new DatasetEditSession(3);
        session.SetRowJson(0, """{"id":1,"text":"one"}""");
        session.DeleteSourceRow(2);
        var insertId = session.InsertBefore(1, """{"id":9,"text":"nine"}""");
        session.UpdateInsertedRow(insertId, """{"id":9,"text":"nine-updated"}""");

        Assert.That(session.ReplacedRows, Is.EqualTo(1));
        Assert.That(session.DeletedRows, Is.EqualTo(1));
        Assert.That(session.InsertedRows, Is.EqualTo(1));
        Assert.That(session.GetInsertions(1).Single().Json, Does.Contain("nine-updated"));

        Assert.That(session.Undo(), Is.True);
        Assert.That(session.GetInsertions(1).Single().Json, Does.Contain("nine\""));
        Assert.That(session.Undo(), Is.True);
        Assert.That(session.InsertedRows, Is.Zero);
        Assert.That(session.Undo(), Is.True);
        Assert.That(session.DeletedRows, Is.Zero);
        Assert.That(session.Undo(), Is.True);
        Assert.That(session.HasChanges, Is.False);
        Assert.That(session.CanUndo, Is.False);

        Assert.That(session.Redo(), Is.True);
        Assert.That(session.GetReplacement(0), Is.Not.Null);
        Assert.That(session.Redo(), Is.True);
        Assert.That(session.DeletedRows, Is.EqualTo(1));
        Assert.That(session.Redo(), Is.True);
        Assert.That(session.InsertedRows, Is.EqualTo(1));
        Assert.That(session.Redo(), Is.True);
        Assert.That(session.GetInsertions(1).Single().Json, Does.Contain("nine-updated"));
    }

    [Test]
    public void Session_DeleteInsertedRow_RemovesIt()
    {
        var session = new DatasetEditSession(2);
        var id = session.InsertBefore(1, """{"id":9}""");
        session.DeleteInsertedRow(id);

        Assert.That(session.InsertedRows, Is.Zero);
        Assert.That(session.HasChanges, Is.False);
        Assert.That(session.CanUndo, Is.True);

        session.Undo();
        Assert.That(session.InsertedRows, Is.EqualTo(1));
    }

    [Test]
    public void Session_EditDeletedRow_Throws()
    {
        var session = new DatasetEditSession(2);
        session.DeleteSourceRow(0);

        Assert.Throws<InvalidOperationException>(() => session.SetRowJson(0, """{"id":1}"""));
    }

    private sealed class CancelAfterFirstReport(CancellationTokenSource cancellation) : IProgress<double>
    {
        private bool _cancelled;

        public void Report(double value)
        {
            if (_cancelled)
            {
                return;
            }

            _cancelled = true;
            cancellation.Cancel();
        }
    }

    private string CreateJsonl(int rows)
    {
        var path = Path.Combine(_directory, $"source-{Guid.NewGuid():N}.jsonl");
        var builder = new StringBuilder();
        for (var i = 1; i <= rows; i++)
        {
            builder.Append($"{{\"id\":{i},\"text\":\"row-{i}\"}}").Append('\n');
        }

        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
        return path;
    }
}
