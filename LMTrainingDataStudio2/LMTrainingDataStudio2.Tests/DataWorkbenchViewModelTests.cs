using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services;
using LMTrainingDataStudio2.Services.Datasets;
using LMTrainingDataStudio2.ViewModels;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class DataWorkbenchViewModelTests
{
    private string _directory = null!;
    private AppSettings _settings = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "lmtds-tests", Guid.NewGuid().ToString("N"));
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
    public async Task OpenJsonl_ShowsFirstPage_AndNavigates()
    {
        var path = CreateJsonl(250);
        var vm = new DataWorkbenchViewModel(_settings);

        await vm.OpenDatasetAsync(path);

        Assert.That(vm.IsDatasetOpen, Is.True, vm.StatusText);
        Assert.That(vm.TotalRows, Is.EqualTo(250));
        Assert.That(vm.TotalPages, Is.EqualTo(3));
        Assert.That(vm.CurrentPage, Is.EqualTo(1));
        Assert.That(vm.Rows, Has.Count.EqualTo(100));
        Assert.That(vm.Rows[0].Number, Is.EqualTo(1));
        Assert.That(vm.Rows[0].Preview, Does.Contain("\"id\": 1"));

        await vm.NextPageCommand.ExecuteAsync(null);
        Assert.That(vm.CurrentPage, Is.EqualTo(2));
        Assert.That(vm.Rows[0].Number, Is.EqualTo(101));

        vm.GoToRowText = "250";
        await vm.GoToRowCommand.ExecuteAsync(null);
        Assert.That(vm.CurrentPage, Is.EqualTo(3));
        Assert.That(vm.Rows, Has.Count.EqualTo(50));
        Assert.That(vm.Rows[^1].Number, Is.EqualTo(250));

        await vm.PreviousPageCommand.ExecuteAsync(null);
        Assert.That(vm.CurrentPage, Is.EqualTo(2));

        await vm.FirstPageCommand.ExecuteAsync(null);
        Assert.That(vm.CurrentPage, Is.EqualTo(1));

        await vm.LastPageCommand.ExecuteAsync(null);
        Assert.That(vm.CurrentPage, Is.EqualTo(3));
    }

    [Test]
    public async Task OpenJsonl_GoToRowOutsideRange_KeepsCurrentPage()
    {
        var path = CreateJsonl(250);
        var vm = new DataWorkbenchViewModel(_settings);
        await vm.OpenDatasetAsync(path);

        vm.GoToRowText = "9999";
        await vm.GoToRowCommand.ExecuteAsync(null);

        Assert.That(vm.CurrentPage, Is.EqualTo(1));
        Assert.That(vm.StatusText, Does.Contain("between 1 and 250"));
    }

    [Test]
    public async Task OpenCsv_ShowsRowsWithHeaderMapping()
    {
        var path = Path.Combine(_directory, "data.csv");
        await File.WriteAllTextAsync(path, "id,text\n1,alpha\n2,beta\n3,gamma\n");
        var vm = new DataWorkbenchViewModel(_settings);

        await vm.OpenDatasetAsync(path);

        Assert.That(vm.IsDatasetOpen, Is.True, vm.StatusText);
        Assert.That(vm.SourceFileTypeText, Is.EqualTo("CSV"));
        Assert.That(vm.TotalRows, Is.EqualTo(3));
        Assert.That(vm.Rows, Has.Count.EqualTo(3));
        Assert.That(vm.Rows[1].Preview, Does.Contain("\"text\": \"beta\""));
    }

    [Test]
    public async Task ConvertJsonlToCsv_ProducesTargetAndSummary()
    {
        var source = CreateJsonl(3, chatRows: true);
        var target = Path.Combine(_directory, "converted.csv");
        var vm = new DataWorkbenchViewModel(_settings);
        await vm.OpenDatasetAsync(source);

        vm.SelectedTargetFormat = vm.TargetFormats.First(option => option.Format == DatasetFormat.OpenAI);
        vm.TargetPath = target;
        await vm.ConvertCommand.ExecuteAsync(null);

        Assert.That(File.Exists(target), Is.True);
        Assert.That(vm.ConversionSummary, Does.Contain("3 / 3"));
        Assert.That(vm.ConversionErrors, Is.Empty);
        Assert.That(vm.StatusText, Does.Contain("complete"));

        await using var reader = new CsvDatasetReader();
        await reader.OpenAsync(target);
        Assert.That(reader.TotalRows, Is.EqualTo(3));
    }

    [Test]
    public async Task Convert_WithBadRows_ListsErrors()
    {
        var source = Path.Combine(_directory, "bad.jsonl");
        await File.WriteAllLinesAsync(source, new[]
        {
            """{"messages":[{"role":"user","content":"ok"}]}""",
            "{broken",
            """{"foo":"bar"}"""
        });
        var target = Path.Combine(_directory, "out.jsonl");
        var vm = new DataWorkbenchViewModel(_settings);
        await vm.OpenDatasetAsync(source);

        vm.TargetPath = target;
        await vm.ConvertCommand.ExecuteAsync(null);

        Assert.That(vm.ConversionErrors, Has.Count.EqualTo(2));
        Assert.That(vm.ConversionSummary, Does.Contain("1 / 3"));
        Assert.That(vm.ConversionSummary, Does.Contain("2 failed"));
        Assert.That(File.Exists(Path.Combine(_directory, "out.errors.jsonl")), Is.True);
    }

    [Test]
    public async Task Convert_UnsupportedPair_ReportsAllRowsFailed()
    {
        var source = CreateJsonl(2, chatRows: true);
        var target = Path.Combine(_directory, "dpo.jsonl");
        var vm = new DataWorkbenchViewModel(_settings);
        await vm.OpenDatasetAsync(source);

        vm.SelectedTargetFormat = vm.TargetFormats.First(option => option.Format == DatasetFormat.Dpo);
        vm.TargetPath = target;
        await vm.ConvertCommand.ExecuteAsync(null);

        Assert.That(vm.ConversionSummary, Does.Contain("0 / 2"));
        Assert.That(vm.ConversionSummary, Does.Contain("2 failed"));
        Assert.That(vm.ConversionErrors, Has.Count.EqualTo(2));
    }

    [Test]
    public void SuggestTargetPath_UsesSourceNameAndFormat()
    {
        var vm = new DataWorkbenchViewModel(_settings);

        var path = vm.SuggestTargetPath(Path.Combine(_directory, "train.jsonl"), DatasetFormat.ShareGPT);

        Assert.That(Path.GetFileName(path), Is.EqualTo("train.sharegpt.jsonl"));
        Assert.That(Path.GetDirectoryName(path), Is.EqualTo(_directory));
    }

    [Test]
    public async Task Validation_RunsAndIssueJumpMovesToPage()
    {
        var path = Path.Combine(_directory, "validate.jsonl");
        var lines = Enumerable.Range(1, 250).Select(index => index == 150
            ? "{broken"
            : $$"""{"messages":[{"role":"user","content":"q{{index}}"}]}""");
        await File.WriteAllLinesAsync(path, lines);

        var vm = new DataWorkbenchViewModel(_settings);
        await vm.OpenDatasetAsync(path);
        await vm.Validation.ValidateCommand.ExecuteAsync(null);

        Assert.That(vm.Validation.Issues.Any(issue => issue.LineNumber == 150), Is.True);
        Assert.That(vm.Validation.ErrorCount, Is.EqualTo(1));
        Assert.That(vm.Validation.HasIssues, Is.True);
        Assert.That(vm.Validation.Summary, Does.Contain("1 issue"));

        vm.Validation.RequestJump(150);
        await WaitForAsync(() => vm.CurrentPage == 2 && vm.GoToRowText == "150");
    }

    [Test]
    public async Task Validation_AutoFixPreviewAndWriteFixedCopy()
    {
        var source = Path.Combine(_directory, "fix.jsonl");
        var originalLines = new[] { """{"a":1""", "not json", """{"b":2}""" };
        await File.WriteAllLinesAsync(source, originalLines);

        var vm = new DataWorkbenchViewModel(_settings);
        await vm.OpenDatasetAsync(source);
        await vm.Validation.PreviewFixesCommand.ExecuteAsync(null);

        Assert.That(vm.Validation.FixSamples, Has.Count.EqualTo(1));
        Assert.That(vm.Validation.FixSummary, Does.Contain("1 fixable"));

        var target = Path.Combine(_directory, "fixed.jsonl");
        vm.Validation.FixTargetPath = target;
        await vm.Validation.WriteFixesCommand.ExecuteAsync(null);

        Assert.That(File.Exists(target), Is.True);
        Assert.That(await File.ReadAllLinesAsync(source), Is.EqualTo(originalLines));
        Assert.That(vm.Validation.StatusText, Does.Contain("Fixed copy written"));
    }

    [Test]
    public async Task Search_PreviewsPageAndWritesReplacement()
    {
        var path = CreateJsonl(250);
        var vm = new DataWorkbenchViewModel(_settings);
        await vm.OpenDatasetAsync(path);

        vm.Search.Query = "row-1";
        await vm.Search.PreviewCommand.ExecuteAsync(null);
        Assert.That(vm.Search.Matches, Is.Not.Empty);
        Assert.That(vm.Search.HasResults, Is.True);

        vm.Search.CurrentPageOnly = true;
        await vm.Search.PreviewCommand.ExecuteAsync(null);
        Assert.That(vm.Search.Summary, Does.Contain("100 scanned row(s)"));

        vm.Search.CurrentPageOnly = false;
        vm.Search.Query = "row-";
        vm.Search.Replacement = "line-";
        var target = Path.Combine(_directory, "search-out.jsonl");
        vm.Search.TargetPath = target;
        await vm.Search.ReplaceCommand.ExecuteAsync(null);

        Assert.That(File.Exists(target), Is.True);
        var lines = await File.ReadAllLinesAsync(target);
        Assert.That(lines, Has.Length.EqualTo(250));
        Assert.That(lines[0], Does.Contain("line-1"));
        Assert.That(vm.Search.StatusText, Does.Contain("Replacement written"));
    }

    [Test]
    public async Task Transform_DeduplicatesIntoOutput()
    {
        var path = Path.Combine(_directory, "transform.jsonl");
        await File.WriteAllLinesAsync(path, new[]
        {
            """{"messages":[{"role":"user","content":"q1"}]}""",
            """{"messages":[{"role":"user","content":"q1"}]}""",
            """{"messages":[{"role":"user","content":"q2"}]}"""
        });
        var vm = new DataWorkbenchViewModel(_settings);
        await vm.OpenDatasetAsync(path);

        var target = Path.Combine(_directory, "transform-out.jsonl");
        vm.Transform.Deduplicate = true;
        vm.Transform.OutputPath = target;
        await vm.Transform.RunCommand.ExecuteAsync(null);

        Assert.That(vm.Transform.HasResults, Is.True);
        Assert.That(vm.Transform.Summary, Does.Contain("1 duplicate"));
        Assert.That(await File.ReadAllLinesAsync(target), Has.Length.EqualTo(2));
    }

    [Test]
    public async Task Statistics_ComputesProfileAndRoles()
    {
        var path = CreateJsonl(4, chatRows: true);
        var vm = new DataWorkbenchViewModel(_settings);
        await vm.OpenDatasetAsync(path);

        await vm.Statistics.ComputeCommand.ExecuteAsync(null);

        Assert.That(vm.Statistics.HasResults, Is.True);
        Assert.That(vm.Statistics.Metrics.Any(metric =>
            metric.Name == "Rows" && metric.Value == "4"), Is.True);
        Assert.That(vm.Statistics.RoleCounts.Any(metric => metric.Name == "user"), Is.True);
        Assert.That(vm.Statistics.RoleCounts.Any(metric => metric.Name == "assistant"), Is.True);
        Assert.That(vm.Statistics.Summary, Does.Contain("profiled"));
    }

    [Test]
    public async Task OpeningAnotherDataset_CancelsInFlightPageLoad()
    {
        var slow = new SlowDatasetReader(TimeSpan.FromSeconds(5));
        var fast = new SlowDatasetReader(TimeSpan.Zero);
        var readers = new Queue<IDatasetReader>(new IDatasetReader[] { slow, fast });
        var vm = new DataWorkbenchViewModel(_settings, _ => readers.Dequeue());

        var first = vm.OpenDatasetAsync(Path.Combine(_directory, "slow.jsonl"));
        await Task.Delay(50);
        var second = vm.OpenDatasetAsync(Path.Combine(_directory, "fast.jsonl"));

        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.That(Path.GetFileName(vm.SourcePath), Is.EqualTo("fast.jsonl"));
        Assert.That(vm.Rows, Has.Count.EqualTo(1));
        Assert.That(vm.IsBusy, Is.False);
    }

    [Test]
    public async Task SettingsUpdate_RoutesNewReadersToUpdatedCacheDirectory()
    {
        var service = new AppSettingsService(_settings);
        var vm = new DataWorkbenchViewModel(service);

        await vm.OpenDatasetAsync(CreateJsonl(10));
        var firstCache = _settings.CacheDirectory;
        Assert.That(Directory.GetFiles(firstCache, "*.db"), Is.Not.Empty);

        var updatedCache = Path.Combine(_directory, "cache-updated");
        service.Update(new AppSettings { CacheDirectory = updatedCache, MaxCacheSizeMb = 16 });

        await vm.OpenDatasetAsync(CreateJsonl(10));

        Assert.That(Directory.Exists(updatedCache), Is.True);
        Assert.That(Directory.GetFiles(updatedCache, "*.db"), Is.Not.Empty);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail("Timed out waiting for the expected state.");
    }

    private string CreateJsonl(int rows, bool chatRows = false)
    {
        var path = Path.Combine(_directory, $"data-{Guid.NewGuid():N}.jsonl");
        var lines = Enumerable.Range(1, rows).Select(id => chatRows
            ? $$"""{"messages":[{"role":"user","content":"q{{id}}"},{"role":"assistant","content":"a{{id}}"}]}"""
            : $$"""{"id":{{id}},"text":"row-{{id}}"}""");
        File.WriteAllLines(path, lines);
        return path;
    }

    private sealed class SlowDatasetReader : IDatasetReader
    {
        private readonly TimeSpan _delay;

        public SlowDatasetReader(TimeSpan delay)
        {
            _delay = delay;
        }

        public string SourcePath { get; private set; } = string.Empty;

        public DataFileType FileType => DataFileType.Jsonl;

        public long TotalRows => 1;

        public Task OpenAsync(string path, CancellationToken cancellationToken = default)
        {
            SourcePath = path;
            return Task.CompletedTask;
        }

        public async Task<IReadOnlyList<DatasetRow>> ReadPageAsync(
            long startRow,
            int count,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(_delay, cancellationToken);
            return new[] { new DatasetRow(0, """{"id":1}""") };
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
