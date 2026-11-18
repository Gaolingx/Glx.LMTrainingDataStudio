using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services;
using LMTrainingDataStudio2.ViewModels;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class ExecutionsViewModelTests
{
    private string _directory = null!;
    private ExecutionStore _store = null!;
    private ExecutionsViewModel _viewModel = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "lmtds-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _store = new ExecutionStore(_directory);
        _viewModel = new ExecutionsViewModel(_store);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_directory, true); } catch { /* best effort */ }
    }

    [Test]
    public void CanResumeSelectedRun_RequiresFailedOrCancelledRunWithOutputDirectory()
    {
        _viewModel.SelectedRun = new ExecutionRun
        {
            Status = RunStatus.Failed,
            OutputDirectory = Path.Combine(_directory, "run")
        };
        Assert.That(_viewModel.CanResumeSelectedRun, Is.True);

        _viewModel.SelectedRun = new ExecutionRun
        {
            Status = RunStatus.Succeeded,
            OutputDirectory = Path.Combine(_directory, "run")
        };
        Assert.That(_viewModel.CanResumeSelectedRun, Is.False);

        _viewModel.SelectedRun = new ExecutionRun
        {
            Status = RunStatus.Cancelled,
            OutputDirectory = null
        };
        Assert.That(_viewModel.CanResumeSelectedRun, Is.False);
    }

    [Test]
    public void CanOpenOutputFolder_RequiresExistingDirectory()
    {
        var output = Path.Combine(_directory, "run-output");
        _viewModel.SelectedRun = new ExecutionRun { OutputDirectory = output };
        Assert.That(_viewModel.CanOpenOutputFolder, Is.False);

        Directory.CreateDirectory(output);
        _viewModel.SelectedRun = new ExecutionRun { OutputDirectory = output };
        Assert.That(_viewModel.CanOpenOutputFolder, Is.True);

        _viewModel.SelectedRun = new ExecutionRun { OutputDirectory = null };
        Assert.That(_viewModel.CanOpenOutputFolder, Is.False);
    }

    [Test]
    public async Task RefreshAsync_LoadsRunsNewestFirst()
    {
        var start = new DateTimeOffset(2026, 11, 16, 8, 0, 0, TimeSpan.Zero);
        await _store.SaveAsync(CreateRun("older", start, RunStatus.Succeeded));
        await _store.SaveAsync(CreateRun("newer", start.AddMinutes(1), RunStatus.Failed));

        await _viewModel.RefreshAsync();

        Assert.That(_viewModel.Runs.Select(run => run.RunId), Is.EqualTo(new[] { "newer", "older" }));
        Assert.That(_viewModel.StatusText, Does.Contain("2 run"));
        Assert.That(_viewModel.SelectedRunSummary?.RunId, Is.EqualTo("newer"));
    }

    [Test]
    public async Task StatusFilter_FiltersRuns()
    {
        var start = new DateTimeOffset(2026, 11, 16, 8, 0, 0, TimeSpan.Zero);
        await _store.SaveAsync(CreateRun("ok", start, RunStatus.Succeeded));
        await _store.SaveAsync(CreateRun("bad", start.AddMinutes(1), RunStatus.Failed));
        await _viewModel.RefreshAsync();

        _viewModel.StatusFilter = "Failed";

        Assert.That(_viewModel.Runs.Select(run => run.RunId), Is.EqualTo(new[] { "bad" }));
        Assert.That(_viewModel.SelectedRunSummary?.RunId, Is.EqualTo("bad"));
    }

    [Test]
    public async Task SelectRunAsync_LoadsNodeResultsAndLogs()
    {
        await _store.SaveAsync(CreateRun(
            "run1", new DateTimeOffset(2026, 11, 16, 8, 0, 0, TimeSpan.Zero), RunStatus.Succeeded,
            "started", "completed"));
        await _viewModel.RefreshAsync();
        var summary = _viewModel.Runs.Single();

        await _viewModel.SelectRunAsync(summary);

        Assert.That(_viewModel.SelectedRun, Is.Not.Null);
        Assert.That(_viewModel.SelectedRun!.Nodes, Has.Count.EqualTo(1));
        Assert.That(_viewModel.SelectedRun.Nodes[0].BlockName, Is.EqualTo("seed_data"));
        Assert.That(_viewModel.SelectedRun.Logs, Is.EqualTo(new[] { "started", "completed" }));
    }

    [Test]
    public async Task RefreshAndSelectAsync_SelectsRequestedRun()
    {
        var start = new DateTimeOffset(2026, 11, 16, 8, 0, 0, TimeSpan.Zero);
        await _store.SaveAsync(CreateRun("first", start, RunStatus.Succeeded));
        await _store.SaveAsync(CreateRun("second", start.AddMinutes(1), RunStatus.Succeeded));

        await _viewModel.RefreshAndSelectAsync("first");

        Assert.That(_viewModel.SelectedRunSummary?.RunId, Is.EqualTo("first"));
    }

    [Test]
    public async Task CleanupExecutionsCommand_RemovesRunsOutsideRetention()
    {
        var settingsPath = CreateSettingsFile(historyLimit: 1, retentionDays: 0, checkpointQuotaMb: 0);
        var start = DateTimeOffset.UtcNow;
        await _store.SaveAsync(CreateRun("old", start.AddHours(-2), RunStatus.Succeeded));
        await _store.SaveAsync(CreateRun("new", start.AddHours(-1), RunStatus.Succeeded));
        SetLastWrite("old", start.AddHours(-2));
        SetLastWrite("new", start.AddHours(-1));
        var viewModel = new MainWindowViewModel(
            new SettingsStore(settingsPath), executionStore: _store);

        await viewModel.CleanupExecutionsCommand.ExecuteAsync(null);

        var remaining = await _store.GetRecentAsync(10);
        Assert.That(remaining.Select(run => run.RunId), Is.EqualTo(new[] { "new" }));
        Assert.That(viewModel.StatusText, Does.Contain("1"));
    }

    [Test]
    public async Task RunStartupMaintenance_PrunesHistoryAndOldCheckpoints()
    {
        var runs = Path.Combine(_directory, "runs");
        var settingsPath = CreateSettingsFile(historyLimit: 1, retentionDays: 0, checkpointQuotaMb: 1);
        var start = DateTimeOffset.UtcNow;
        await _store.SaveAsync(CreateRun("old", start.AddHours(-2), RunStatus.Succeeded));
        await _store.SaveAsync(CreateRun("new", start.AddHours(-1), RunStatus.Succeeded));
        SetLastWrite("old", start.AddHours(-2));
        SetLastWrite("new", start.AddHours(-1));
        var oldCheckpoint = CreateCheckpoint(runs, "old", 1024 * 1024, start.AddHours(-2));
        var newCheckpoint = CreateCheckpoint(runs, "new", 1024 * 1024, start.AddHours(-1));
        var viewModel = new MainWindowViewModel(
            new SettingsStore(settingsPath), executionStore: _store);

        await viewModel.RunStartupMaintenanceAsync();

        Assert.That(File.Exists(Path.Combine(_directory, "old.json")), Is.False);
        Assert.That(File.Exists(Path.Combine(_directory, "new.json")), Is.True);
        Assert.That(Directory.Exists(oldCheckpoint), Is.False);
        Assert.That(Directory.Exists(newCheckpoint), Is.True);
    }

    [Test]
    public async Task RunStartupMaintenance_RemovesStaleSpillDirectories()
    {
        var settingsPath = CreateSettingsFile(historyLimit: 0, retentionDays: 0, checkpointQuotaMb: 0);
        var spillRoot = Path.Combine(_directory, "temp");
        var stale = Path.Combine(spillRoot, "run-1-abc");
        Directory.CreateDirectory(stale);
        File.WriteAllText(Path.Combine(stale, "rows.tmp"), "stale");
        var viewModel = new MainWindowViewModel(
            new SettingsStore(settingsPath), executionStore: _store);

        await viewModel.RunStartupMaintenanceAsync();

        Assert.That(Directory.Exists(stale), Is.False);
        Assert.That(Directory.Exists(spillRoot), Is.True);
    }

    private string CreateSettingsFile(int historyLimit, int retentionDays, long checkpointQuotaMb)
    {
        var path = Path.Combine(_directory, "config", "settings.json");
        new SettingsStore(path).SaveAsync(new AppSettings
        {
            CacheDirectory = Path.Combine(_directory, "cache"),
            RunOutputDirectory = Path.Combine(_directory, "runs"),
            RunTempDirectory = Path.Combine(_directory, "temp"),
            ExecutionHistoryLimit = historyLimit,
            ExecutionHistoryRetentionDays = retentionDays,
            CheckpointQuotaMb = checkpointQuotaMb
        }).GetAwaiter().GetResult();
        return path;
    }

    private static string CreateCheckpoint(
        string runsDirectory, string runId, int bytes, DateTimeOffset lastWrite)
    {
        var directory = Path.Combine(runsDirectory, runId, "nodes");
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, "block.rows"), new byte[bytes]);
        Directory.SetLastWriteTimeUtc(directory, lastWrite.UtcDateTime);
        return directory;
    }

    private void SetLastWrite(string runId, DateTimeOffset value) =>
        File.SetLastWriteTimeUtc(
            Path.Combine(_directory, $"{runId}.json"), value.UtcDateTime);

    private static ExecutionRun CreateRun(
        string runId, DateTimeOffset startedAt, RunStatus status, params string[] logs)
    {
        var run = new ExecutionRun
        {
            RunId = runId,
            RecipeId = "recipe1",
            Status = status,
            StartedAt = startedAt,
            FinishedAt = startedAt.AddSeconds(1)
        };

        run.Nodes.Add(new NodeExecutionResult
        {
            BlockId = "seed",
            BlockName = "seed_data",
            Status = ExecutionStatus.Succeeded,
            InputRows = 0,
            OutputRows = 5
        });

        run.Logs.AddRange(logs);
        return run;
    }
}
