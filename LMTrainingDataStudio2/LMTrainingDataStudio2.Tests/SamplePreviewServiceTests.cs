using LMTrainingDataStudio2.Localization;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services.Datasets;
using LMTrainingDataStudio2.ViewModels;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class SamplePreviewServiceTests
{
    private string _directory = null!;
    private AppSettings _settings = null!;
    private SamplePreviewService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "lmtds-preview-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _settings = new AppSettings
        {
            CacheDirectory = Path.Combine(_directory, "cache"),
            MaxCacheSizeMb = 16
        };
        _service = new SamplePreviewService(_settings);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_directory, true); } catch { /* best effort */ }
    }

    [Test]
    public async Task LoadFile_Jsonl_ReturnsFirstRowsAndTotal()
    {
        var path = Path.Combine(_directory, "preview.jsonl");
        await File.WriteAllLinesAsync(path, Enumerable.Range(1, 10)
            .Select(id => $$"""{"id":{{id}}}"""));

        var document = await _service.LoadFileAsync(path, maxRows: 3);

        Assert.That(document.FileType, Is.EqualTo(DataFileType.Jsonl));
        Assert.That(document.TotalRows, Is.EqualTo(10));
        Assert.That(document.Rows, Has.Count.EqualTo(3));
        Assert.That(document.Rows[0].LineNumber, Is.EqualTo(1));
        Assert.That(document.Rows[0].After, Is.EqualTo("""{"id":1}"""));
        Assert.That(document.IsDiff, Is.False);
    }

    [Test]
    public async Task LoadFile_Csv_WorksThroughDatasetReader()
    {
        var path = Path.Combine(_directory, "preview.csv");
        await File.WriteAllTextAsync(path, "id,text\n1,alpha\n2,beta\n");

        var document = await _service.LoadFileAsync(path);

        Assert.That(document.FileType, Is.EqualTo(DataFileType.Csv));
        Assert.That(document.Rows, Has.Count.EqualTo(2));
        Assert.That(document.Rows[1].After, Does.Contain("beta"));
    }

    [Test]
    public async Task BuildCleanerDiff_MarksChangedRowsOnly()
    {
        var path = Path.Combine(_directory, "clean.jsonl");
        await File.WriteAllLinesAsync(path, new[]
        {
            """{"messages":[{"role":"user","content":"hello   \n\n\nworld"}]}""",
            """{"messages":[{"role":"user","content":"clean"}]}"""
        });

        var rules = new DatasetCleanupRules { NormalizeWhitespace = true };
        var document = await _service.BuildCleanerDiffAsync(path, rules);

        Assert.That(document.IsDiff, Is.True);
        Assert.That(document.Rows, Has.Count.EqualTo(2));
        Assert.That(document.Rows[0].Changed, Is.True);
        Assert.That(document.Rows[0].Before, Does.Contain("   "));
        Assert.That(document.Rows[0].After, Does.Not.Contain("   "));
        Assert.That(document.Rows[1].Changed, Is.False);
        Assert.That(document.Rows[1].After, Is.EqualTo(document.Rows[1].Before));
    }

    [Test]
    public void BuildCleanerDiff_Csv_Throws()
    {
        var path = Path.Combine(_directory, "clean.csv");
        File.WriteAllText(path, "id,text\n1,a\n");

        Assert.ThrowsAsync<DatasetFormatException>(() =>
            _service.BuildCleanerDiffAsync(path, new DatasetCleanupRules { NormalizeWhitespace = true }));
    }

    [Test]
    public async Task LoadFile_StartRow_ReturnsRequestedWindow()
    {
        var path = Path.Combine(_directory, "paged.jsonl");
        await File.WriteAllLinesAsync(path, Enumerable.Range(1, 10)
            .Select(id => $$"""{"id":{{id}}}"""));

        var document = await _service.LoadFileAsync(path, maxRows: 3, startRow: 6);

        Assert.That(document.TotalRows, Is.EqualTo(10));
        Assert.That(document.Rows, Has.Count.EqualTo(3));
        Assert.That(document.Rows[0].LineNumber, Is.EqualTo(7));
        Assert.That(document.Rows[2].LineNumber, Is.EqualTo(9));
    }

    [Test]
    public async Task ViewModel_PagesThroughRows()
    {
        var path = Path.Combine(_directory, "paging.jsonl");
        await File.WriteAllLinesAsync(path, Enumerable.Range(1, 120)
            .Select(id => $$"""{"id":{{id}}}"""));
        var vm = new SamplePreviewViewModel(_settings);

        await vm.LoadFileAsync(path, "paging");

        Assert.That(vm.Rows, Has.Count.EqualTo(SamplePreviewViewModel.PageSize));
        Assert.That(vm.CanGoPreviousPage, Is.False);
        Assert.That(vm.CanGoNextPage, Is.True);

        await vm.NextPageCommand.ExecuteAsync(null);

        Assert.That(vm.Rows, Has.Count.EqualTo(SamplePreviewViewModel.PageSize));
        Assert.That(vm.Rows[0].Header, Is.EqualTo("51"));
        Assert.That(vm.CanGoPreviousPage, Is.True);
        Assert.That(vm.CanGoNextPage, Is.True);

        await vm.NextPageCommand.ExecuteAsync(null);

        Assert.That(vm.Rows, Has.Count.EqualTo(20));
        Assert.That(vm.Rows[0].Header, Is.EqualTo("101"));
        Assert.That(vm.CanGoNextPage, Is.False);
        Assert.That(vm.PageText, Is.EqualTo(string.Format(
            LocalizationService.L("data.position_format"), 3, 3)));

        await vm.PreviousPageCommand.ExecuteAsync(null);
        Assert.That(vm.Rows[0].Header, Is.EqualTo("51"));
        Assert.That(vm.CanGoNextPage, Is.True);
    }

    [Test]
    public void LoadFile_MissingFile_Throws()
    {
        Assert.ThrowsAsync<FileNotFoundException>(() =>
            _service.LoadFileAsync(Path.Combine(_directory, "missing.jsonl")));
    }
}
