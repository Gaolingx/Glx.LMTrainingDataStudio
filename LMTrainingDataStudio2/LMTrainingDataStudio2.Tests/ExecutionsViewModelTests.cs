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
