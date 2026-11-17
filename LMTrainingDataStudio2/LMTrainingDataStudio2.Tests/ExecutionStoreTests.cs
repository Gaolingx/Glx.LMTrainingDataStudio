using System.Text.Json;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class ExecutionStoreTests
{
    private string _directory = null!;
    private ExecutionStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "lmtds-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _store = new ExecutionStore(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_directory, true); } catch { /* best effort */ }
    }

    [Test]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsRun()
    {
        var started = new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);
        var run = CreateRun("run1", started, "started", "completed");

        await _store.SaveAsync(run);
        var loaded = await _store.LoadAsync("run1");

        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.RunId, Is.EqualTo("run1"));
        Assert.That(loaded.RecipeId, Is.EqualTo("recipe1"));
        Assert.That(loaded.Status, Is.EqualTo(RunStatus.Succeeded));
        Assert.That(loaded.StartedAt, Is.EqualTo(started));
        Assert.That(loaded.FinishedAt, Is.EqualTo(started.AddSeconds(2)));
        Assert.That(loaded.Nodes, Has.Count.EqualTo(1));
        Assert.That(loaded.Nodes[0].BlockId, Is.EqualTo("seed"));
        Assert.That(loaded.Nodes[0].Status, Is.EqualTo(ExecutionStatus.Succeeded));
        Assert.That(loaded.Nodes[0].OutputRows, Is.EqualTo(5));
        Assert.That(loaded.Nodes[0].Duration, Is.EqualTo(TimeSpan.FromSeconds(1)));
        Assert.That(loaded.Logs, Is.EqualTo(new[] { "started", "completed" }));
    }

    [Test]
    public async Task SaveAsync_WritesJsonAndSeparateLogFile()
    {
        await _store.SaveAsync(CreateRun("run1", DateTimeOffset.UtcNow, "first line", "second line"));

        var jsonPath = Path.Combine(_directory, "run1.json");
        var logPath = Path.Combine(_directory, "run1.log");
        Assert.That(File.Exists(jsonPath), Is.True);
        Assert.That(File.Exists(logPath), Is.True);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(jsonPath));
        var root = document.RootElement;
        Assert.That(root.GetProperty("runId").GetString(), Is.EqualTo("run1"));
        Assert.That(root.GetProperty("status").GetString(), Is.EqualTo("Succeeded"));
        Assert.That(root.GetProperty("nodes")[0].GetProperty("status").GetString(), Is.EqualTo("Succeeded"));
        Assert.That(root.TryGetProperty("logs", out _), Is.False);

        Assert.That(
            await File.ReadAllLinesAsync(logPath),
            Is.EqualTo(new[] { "first line", "second line" }));
    }

    [Test]
    public async Task GetRecentAsync_ReturnsNewestFirstAndHonorsCount()
    {
        var start = new DateTimeOffset(2026, 11, 16, 8, 0, 0, TimeSpan.Zero);
        await _store.SaveAsync(CreateRun("old", start));
        await _store.SaveAsync(CreateRun("mid", start.AddMinutes(5)));
        await _store.SaveAsync(CreateRun("new", start.AddMinutes(10)));

        var recent = await _store.GetRecentAsync(2);

        Assert.That(recent.Select(summary => summary.RunId), Is.EqualTo(new[] { "new", "mid" }));
    }

    [Test]
    public async Task GetRecentAsync_WhenEmpty_ReturnsEmpty()
    {
        Assert.That(await _store.GetRecentAsync(5), Is.Empty);
        Assert.That(await _store.GetRecentAsync(0), Is.Empty);
    }

    [Test]
    public async Task LoadAsync_MissingRun_ReturnsNull()
    {
        Assert.That(await _store.LoadAsync("missing"), Is.Null);
    }

    [Test]
    public async Task LoadAsync_MalformedJson_ThrowsInvalidDataException()
    {
        var path = Path.Combine(_directory, "bad.json");
        await File.WriteAllTextAsync(path, "{ not valid");

        var ex = Assert.ThrowsAsync<InvalidDataException>(async () => await _store.LoadAsync("bad"));

        Assert.That(ex!.Message, Does.Contain(path));
    }

    [Test]
    public void InvalidRunId_Throws()
    {
        Assert.ThrowsAsync<ArgumentException>(
            async () => await _store.SaveAsync(CreateRun("../evil", DateTimeOffset.UtcNow)));
        Assert.ThrowsAsync<ArgumentException>(async () => await _store.LoadAsync("../evil"));
    }

    [Test]
    public void SaveAsync_CancelledToken_DoesNotCreateFiles()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(
            async () => await _store.SaveAsync(CreateRun("run1", DateTimeOffset.UtcNow), cts.Token));
        Assert.That(Directory.GetFiles(_directory), Is.Empty);
    }

    [Test]
    public async Task SaveAsync_OverwritesExistingRun_WithoutTempFiles()
    {
        await _store.SaveAsync(CreateRun("run1", DateTimeOffset.UtcNow, "first"));
        await _store.SaveAsync(CreateRun("run1", DateTimeOffset.UtcNow, "second", "line"));

        var loaded = await _store.LoadAsync("run1");

        Assert.That(loaded!.Logs, Is.EqualTo(new[] { "second", "line" }));
        Assert.That(Directory.GetFiles(_directory, "*.tmp"), Is.Empty);
    }

    [Test]
    public async Task LoadAsync_WithoutLogFile_ReturnsEmptyLogs()
    {
        await _store.SaveAsync(CreateRun("run1", DateTimeOffset.UtcNow, "only line"));
        File.Delete(Path.Combine(_directory, "run1.log"));

        var loaded = await _store.LoadAsync("run1");

        Assert.That(loaded!.Logs, Is.Empty);
    }

    [Test]
    public async Task Prune_ByMaxRuns_DeletesOldestRecordsAndLogs()
    {
        await _store.SaveAsync(CreateRun("old", DateTimeOffset.UtcNow.AddHours(-3)));
        await _store.SaveAsync(CreateRun("mid", DateTimeOffset.UtcNow.AddHours(-2)));
        await _store.SaveAsync(CreateRun("new", DateTimeOffset.UtcNow.AddHours(-1)));
        SetLastWrite("old", DateTimeOffset.UtcNow.AddHours(-3));
        SetLastWrite("mid", DateTimeOffset.UtcNow.AddHours(-2));
        SetLastWrite("new", DateTimeOffset.UtcNow.AddHours(-1));

        var removed = _store.Prune(maxRuns: 2, maxAgeDays: 0);

        Assert.That(removed, Is.EqualTo(1));
        Assert.That(File.Exists(Path.Combine(_directory, "old.json")), Is.False);
        Assert.That(File.Exists(Path.Combine(_directory, "old.log")), Is.False);
        Assert.That(File.Exists(Path.Combine(_directory, "mid.json")), Is.True);
        Assert.That(File.Exists(Path.Combine(_directory, "new.json")), Is.True);
    }

    [Test]
    public async Task Prune_ByAge_DeletesExpiredRuns()
    {
        await _store.SaveAsync(CreateRun("expired", DateTimeOffset.UtcNow.AddDays(-20)));
        await _store.SaveAsync(CreateRun("fresh", DateTimeOffset.UtcNow));
        SetLastWrite("expired", DateTimeOffset.UtcNow.AddDays(-20));
        SetLastWrite("fresh", DateTimeOffset.UtcNow);

        var removed = _store.Prune(maxRuns: 0, maxAgeDays: 7);

        Assert.That(removed, Is.EqualTo(1));
        Assert.That(File.Exists(Path.Combine(_directory, "expired.json")), Is.False);
        Assert.That(File.Exists(Path.Combine(_directory, "fresh.json")), Is.True);
    }

    [Test]
    public async Task Prune_ProtectsSelectedRun()
    {
        await _store.SaveAsync(CreateRun("protected", DateTimeOffset.UtcNow.AddHours(-3)));
        await _store.SaveAsync(CreateRun("mid", DateTimeOffset.UtcNow.AddHours(-2)));
        await _store.SaveAsync(CreateRun("new", DateTimeOffset.UtcNow.AddHours(-1)));
        SetLastWrite("protected", DateTimeOffset.UtcNow.AddHours(-3));
        SetLastWrite("mid", DateTimeOffset.UtcNow.AddHours(-2));
        SetLastWrite("new", DateTimeOffset.UtcNow.AddHours(-1));

        var removed = _store.Prune(maxRuns: 1, maxAgeDays: 0, protectedRunId: "protected");

        Assert.That(removed, Is.EqualTo(1));
        Assert.That(File.Exists(Path.Combine(_directory, "protected.json")), Is.True);
        Assert.That(File.Exists(Path.Combine(_directory, "mid.json")), Is.False);
        Assert.That(File.Exists(Path.Combine(_directory, "new.json")), Is.True);
    }

    [Test]
    public void Prune_WithNoLimits_IsNoOp()
    {
        Assert.That(_store.Prune(maxRuns: 0, maxAgeDays: 0), Is.EqualTo(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => _store.Prune(maxRuns: -1, maxAgeDays: 0));
    }

    private void SetLastWrite(string runId, DateTimeOffset value) =>
        File.SetLastWriteTimeUtc(
            Path.Combine(_directory, $"{runId}.json"), value.UtcDateTime);

    private static ExecutionRun CreateRun(string runId, DateTimeOffset startedAt, params string[] logs)
    {
        var run = new ExecutionRun
        {
            RunId = runId,
            RecipeId = "recipe1",
            Status = RunStatus.Succeeded,
            StartedAt = startedAt,
            FinishedAt = startedAt.AddSeconds(2)
        };

        run.Nodes.Add(new NodeExecutionResult
        {
            BlockId = "seed",
            BlockName = "seed_data",
            Status = ExecutionStatus.Succeeded,
            StartedAt = startedAt,
            FinishedAt = startedAt.AddSeconds(1),
            OutputRows = 5
        });

        run.Logs.AddRange(logs);
        return run;
    }
}
