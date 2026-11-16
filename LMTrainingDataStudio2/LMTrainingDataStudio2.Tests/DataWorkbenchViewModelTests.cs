using LMTrainingDataStudio2.Models;
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

    private string CreateJsonl(int rows, bool chatRows = false)
    {
        var path = Path.Combine(_directory, $"data-{Guid.NewGuid():N}.jsonl");
        var lines = Enumerable.Range(1, rows).Select(id => chatRows
            ? $$"""{"messages":[{"role":"user","content":"q{{id}}"},{"role":"assistant","content":"a{{id}}"}]}"""
            : $$"""{"id":{{id}},"text":"row-{{id}}"}""");
        File.WriteAllLines(path, lines);
        return path;
    }
}
