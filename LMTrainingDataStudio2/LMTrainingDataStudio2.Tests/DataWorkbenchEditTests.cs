using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services;
using LMTrainingDataStudio2.ViewModels;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class DataWorkbenchEditTests
{
    private string _directory = null!;
    private AppSettings _settings = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "lmtds-edit-vm", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
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
    public async Task Edit_ApplyUndoRedo_TracksPendingChanges()
    {
        var path = CreateJsonl(3);
        var vm = new DataWorkbenchViewModel(_settings);
        await vm.OpenDatasetAsync(path);

        vm.SelectedRow = vm.Rows[0];
        vm.SelectedJsonText = """{"id":1,"text":"edited"}""";
        await vm.ApplyRowEditCommand.ExecuteAsync(null);

        Assert.That(vm.Rows[0].IsModified, Is.True);
        Assert.That(vm.Rows[0].Preview, Does.Contain("edited"));
        Assert.That(vm.HasPendingEdits, Is.True);
        Assert.That(vm.PendingEditText, Does.Contain("+0"));
        Assert.That(vm.UndoEditCommand.CanExecute(null), Is.True);

        await vm.UndoEditCommand.ExecuteAsync(null);
        Assert.That(vm.HasPendingEdits, Is.False);
        Assert.That(vm.Rows[0].IsModified, Is.False);
        Assert.That(vm.Rows[0].Preview, Does.Contain("row-1"));

        await vm.RedoEditCommand.ExecuteAsync(null);
        Assert.That(vm.HasPendingEdits, Is.True);
        Assert.That(vm.Rows[0].Preview, Does.Contain("edited"));
    }

    [Test]
    public async Task Edit_InsertDuplicateDelete_SavesConservedRows()
    {
        var source = CreateJsonl(4);
        var target = Path.Combine(_directory, "edited.jsonl");
        var vm = new DataWorkbenchViewModel(_settings);
        await vm.OpenDatasetAsync(source);

        vm.SelectedRow = vm.Rows[1];
        await vm.DuplicateRowCommand.ExecuteAsync(null);
        Assert.That(vm.Rows.Any(row => row.IsInserted), Is.True);
        Assert.That(vm.PendingEditText, Does.Contain("+1"));

        vm.SelectedRow = vm.Rows.First(row => row.OriginalIndex == 2);
        await vm.DeleteRowCommand.ExecuteAsync(null);
        Assert.That(vm.Rows.Any(row => row.IsDeleted), Is.True);
        Assert.That(vm.PendingEditText, Does.Contain("-1"));

        var saved = await vm.SaveEditsAsync(target);

        Assert.That(saved, Is.True, vm.EditStatusText);
        Assert.That(vm.HasPendingEdits, Is.False);
        Assert.That(vm.Rows.Any(row => row.IsInserted || row.IsDeleted || row.IsModified), Is.False);

        var lines = await File.ReadAllLinesAsync(target);
        Assert.That(lines, Has.Length.EqualTo(4));
        Assert.That(await File.ReadAllLinesAsync(source), Has.Length.EqualTo(4));
    }

    [Test]
    public async Task Edit_OverwriteSource_ReopensAndClearsPending()
    {
        var source = CreateJsonl(2);
        var vm = new DataWorkbenchViewModel(_settings);
        await vm.OpenDatasetAsync(source);

        vm.SelectedRow = vm.Rows[0];
        vm.SelectedJsonText = """{"id":1,"text":"overwritten"}""";
        await vm.ApplyRowEditCommand.ExecuteAsync(null);

        var saved = await vm.SaveEditsAsync(source);

        Assert.That(saved, Is.True, vm.EditStatusText);
        Assert.That(vm.HasPendingEdits, Is.False);
        Assert.That(vm.TotalRows, Is.EqualTo(2));
        Assert.That(vm.Rows[0].Preview, Does.Contain("overwritten"));
        Assert.That(await File.ReadAllTextAsync(source), Does.Contain("overwritten"));
    }

    [Test]
    public async Task Edit_InvalidJson_ReportsErrorWithoutPendingEdit()
    {
        var vm = new DataWorkbenchViewModel(_settings);
        await vm.OpenDatasetAsync(CreateJsonl(2));

        vm.SelectedRow = vm.Rows[0];
        vm.SelectedJsonText = "{broken";
        await vm.ApplyRowEditCommand.ExecuteAsync(null);

        Assert.That(vm.RowEditError, Does.Contain("Invalid JSON"));
        Assert.That(vm.HasPendingEdits, Is.False);
    }

    [Test]
    public async Task Edit_InsertedRowCanBeUpdatedAndDeleted()
    {
        var vm = new DataWorkbenchViewModel(_settings);
        await vm.OpenDatasetAsync(CreateJsonl(2));

        await vm.InsertRowBelowCommand.ExecuteAsync(null);
        var inserted = vm.Rows.First(row => row.IsInserted);
        Assert.That(vm.SelectedRow, Is.SameAs(inserted));

        vm.SelectedJsonText = """{"text":"updated"}""";
        await vm.ApplyRowEditCommand.ExecuteAsync(null);
        Assert.That(vm.Rows.First(row => row.IsInserted).Preview, Does.Contain("updated"));

        vm.SelectedRow = vm.Rows.First(row => row.IsInserted);
        await vm.DeleteRowCommand.ExecuteAsync(null);
        Assert.That(vm.Rows.Any(row => row.IsInserted), Is.False);
        Assert.That(vm.HasPendingEdits, Is.False);
    }

    [Test]
    public async Task SearchQuery_PropagatesHighlightToRows()
    {
        var vm = new DataWorkbenchViewModel(_settings);
        await vm.OpenDatasetAsync(CreateJsonl(3));

        vm.Search.Query = "row-2";

        Assert.That(vm.Rows[1].IsSearchMatch, Is.True);
        Assert.That(vm.Rows[0].IsSearchMatch, Is.False);
        Assert.That(vm.Rows[1].HighlightQuery, Is.EqualTo("row-2"));

        vm.Search.Query = string.Empty;
        Assert.That(vm.Rows[1].IsSearchMatch, Is.False);
    }

    [Test]
    public async Task Validation_FiltersBySeverityAndCode()
    {
        var path = Path.Combine(_directory, "filter.jsonl");
        await File.WriteAllLinesAsync(path, new[]
        {
            """{"messages":[{"role":"user","content":"ok"}]}""",
            "{broken",
            """{"foo":"bar"}"""
        });

        var vm = new DataWorkbenchViewModel(_settings);
        await vm.OpenDatasetAsync(path);
        await vm.Validation.ValidateCommand.ExecuteAsync(null);

        var total = vm.Validation.Issues.Count;
        Assert.That(total, Is.GreaterThanOrEqualTo(2));
        Assert.That(vm.Validation.FilteredIssues.Count, Is.EqualTo(total));
        Assert.That(vm.Validation.CodeOptions.Count, Is.GreaterThan(1));

        vm.Validation.SelectedSeverityOption = vm.Validation.SeverityOptions
            .First(option => option.Key == ValidationSeverityOption.ErrorKey);
        Assert.That(vm.Validation.FilteredIssues.All(issue => issue.IsError), Is.True);
        Assert.That(vm.Validation.FilteredIssues.Count, Is.LessThan(total));

        vm.Validation.SelectedSeverityOption = vm.Validation.SeverityOptions
            .First(option => option.Key == ValidationSeverityOption.WarningKey);
        Assert.That(vm.Validation.FilteredIssues.All(issue => issue.IsWarning), Is.True);

        vm.Validation.SelectedSeverityOption = vm.Validation.SeverityOptions[0];
        vm.Validation.SelectedCodeOption = vm.Validation.CodeOptions
            .First(option => option.Key == "json-syntax");
        Assert.That(vm.Validation.FilteredIssues, Has.Count.EqualTo(1));
        Assert.That(vm.Validation.FilteredIssues[0].LineNumber, Is.EqualTo(2));
        Assert.That(vm.Validation.FilterSummary, Does.Contain("1 /"));
    }

    private string CreateJsonl(int rows)
    {
        var path = Path.Combine(_directory, $"source-{Guid.NewGuid():N}.jsonl");
        File.WriteAllLines(path, Enumerable.Range(1, rows)
            .Select(id => $$"""{"id":{{id}},"text":"row-{{id}}"}"""));
        return path;
    }
}
