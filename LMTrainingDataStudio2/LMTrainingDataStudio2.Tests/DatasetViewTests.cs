using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services.Datasets;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class DatasetViewTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "lmtds-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_directory, true); } catch { /* best effort */ }
    }

    [Test]
    public async Task Filter_Contains_IsCaseInsensitiveByDefault()
    {
        var reader = new MemoryReader(
            """{"name":"Alpha"}""",
            """{"name":"beta"}""",
            """{"name":"ALP"}""",
            """{"name":"gamma"}""");

        var index = await DatasetViewBuilder.BuildAsync(
            reader, 4,
            new DatasetViewSpec(
                FilterField: "name",
                FilterOperator: DatasetFilterOperator.Contains,
                FilterValue: "al"));

        Assert.That(index.SourceRows, Is.EqualTo(new long[] { 0, 2 }));
    }

    [Test]
    public async Task Filter_Contains_CanBeCaseSensitive()
    {
        var reader = new MemoryReader(
            """{"name":"Alpha"}""",
            """{"name":"alpha"}""");

        var index = await DatasetViewBuilder.BuildAsync(
            reader, 2,
            new DatasetViewSpec(
                FilterField: "name",
                FilterOperator: DatasetFilterOperator.Contains,
                FilterValue: "Al",
                FilterCaseSensitive: true));

        Assert.That(index.SourceRows, Is.EqualTo(new long[] { 0 }));
    }

    [Test]
    public async Task Filter_GreaterThan_ComparesNumbersNumerically()
    {
        var reader = new MemoryReader(
            """{"score":5}""",
            """{"score":20}""",
            """{"score":3}""",
            """{"score":100}""");

        var index = await DatasetViewBuilder.BuildAsync(
            reader, 4,
            new DatasetViewSpec(
                FilterField: "score",
                FilterOperator: DatasetFilterOperator.GreaterThan,
                FilterValue: "10"));

        Assert.That(index.SourceRows, Is.EqualTo(new long[] { 1, 3 }));
    }

    [Test]
    public async Task Filter_IsEmpty_MatchesMissingField()
    {
        var reader = new MemoryReader(
            """{"name":"a"}""",
            """{"id":1}""",
            """{"name":""}""",
            "not json");

        var index = await DatasetViewBuilder.BuildAsync(
            reader, 4,
            new DatasetViewSpec(
                FilterField: "name",
                FilterOperator: DatasetFilterOperator.IsEmpty));

        Assert.That(index.SourceRows, Is.EqualTo(new long[] { 1, 2, 3 }));
    }

    [Test]
    public async Task Filter_Regex_MatchesAndReportsInvalidPattern()
    {
        var reader = new MemoryReader(
            """{"text":"abc"}""",
            """{"text":"xyz"}""");

        var index = await DatasetViewBuilder.BuildAsync(
            reader, 2,
            new DatasetViewSpec(
                FilterField: "text",
                FilterOperator: DatasetFilterOperator.MatchesRegex,
                FilterValue: "^a.c$"));

        Assert.That(index.SourceRows, Is.EqualTo(new long[] { 0 }));

        Assert.ThrowsAsync<ArgumentException>(async () => await DatasetViewBuilder.BuildAsync(
            reader, 2,
            new DatasetViewSpec(
                FilterField: "text",
                FilterOperator: DatasetFilterOperator.MatchesRegex,
                FilterValue: "[unclosed")));
    }

    [Test]
    public async Task Sort_Numeric_OrdersByValueNotText()
    {
        var reader = new MemoryReader(
            """{"id":10,"text":"ten"}""",
            """{"id":2,"text":"two"}""",
            """{"id":1,"text":"one"}""");

        var index = await DatasetViewBuilder.BuildAsync(
            reader, 3,
            new DatasetViewSpec(SortField: "id"));

        Assert.That(index.SourceRows, Is.EqualTo(new long[] { 2, 1, 0 }));
        Assert.That(index.IsSorted, Is.True);
    }

    [Test]
    public async Task Sort_TextDescending_UsesOrdinalIgnoreCase()
    {
        var reader = new MemoryReader(
            """{"name":"beta"}""",
            """{"name":"Alpha"}""",
            """{"name":"gamma"}""");

        var index = await DatasetViewBuilder.BuildAsync(
            reader, 3,
            new DatasetViewSpec(SortField: "name", SortDescending: true));

        Assert.That(index.SourceRows, Is.EqualTo(new long[] { 2, 0, 1 }));
    }

    [Test]
    public async Task Sort_TiesKeepSourceOrder()
    {
        var reader = new MemoryReader(
            """{"group":"b"}""",
            """{"group":"a"}""",
            """{"group":"b"}""",
            """{"group":"a"}""");

        var index = await DatasetViewBuilder.BuildAsync(
            reader, 4,
            new DatasetViewSpec(SortField: "group"));

        Assert.That(index.SourceRows, Is.EqualTo(new long[] { 1, 3, 0, 2 }));
    }

    [Test]
    public async Task Build_DiscoversFieldsInFirstSeenOrder()
    {
        var reader = new MemoryReader(
            """{"id":1,"text":"a"}""",
            """{"text":"b","extra":true}""");

        var index = await DatasetViewBuilder.BuildAsync(
            reader, 2,
            new DatasetViewSpec(FilterField: "text", FilterValue: ""));

        Assert.That(index.FieldNames, Is.EqualTo(new[] { "id", "text", "extra" }));
    }

    [Test]
    public async Task Build_NoMatches_ReturnsEmptyIndex()
    {
        var reader = new MemoryReader("""{"name":"a"}""", """{"name":"b"}""");

        var index = await DatasetViewBuilder.BuildAsync(
            reader, 2,
            new DatasetViewSpec(FilterField: "name", FilterValue: "zzz"));

        Assert.That(index.Count, Is.Zero);
        Assert.That(index.GetSourceRows(0, 10), Is.Empty);
    }

    [Test]
    public void Build_ExceedingRowLimit_Throws()
    {
        var reader = new MemoryReader(
            """{"name":"a"}""",
            """{"name":"a"}""",
            """{"name":"a"}""");

        Assert.ThrowsAsync<DatasetViewLimitException>(async () => await DatasetViewBuilder.BuildAsync(
            reader, 3,
            new DatasetViewSpec(FilterField: "name", FilterValue: "a"),
            maxMatchedRows: 2));
    }

    [Test]
    public void Index_GetSourceRows_ReturnsRequestedSlice()
    {
        var index = new DatasetViewIndex(
            new long[] { 5, 3, 8, 1 }, new[] { "id" }, isSorted: true);

        Assert.That(index.GetSourceRows(1, 2), Is.EqualTo(new long[] { 3, 8 }));
        Assert.That(index.GetSourceRows(3, 10), Is.EqualTo(new long[] { 1 }));
        Assert.That(index.GetSourceRows(4, 10), Is.Empty);
        Assert.That(index.GetSourceRow(0), Is.EqualTo(5));
        Assert.Throws<ArgumentOutOfRangeException>(() => index.GetSourceRow(4));
    }

    [Test]
    public async Task Reader_ReadRowsAsync_GroupsConsecutiveRows()
    {
        var reader = new MemoryReader(
            """{"id":0}""", """{"id":1}""", """{"id":2}""",
            """{"id":3}""", """{"id":4}""", """{"id":5}""",
            """{"id":6}""", """{"id":7}""");

        var rows = await ((IDatasetReader)reader).ReadRowsAsync(new long[] { 0, 2, 3, 7 });

        Assert.That(rows.Select(row => row.LineNumber), Is.EqualTo(new long[] { 0, 2, 3, 7 }));
        Assert.That(reader.PageReads, Is.EqualTo(3));
    }

    [Test]
    public async Task ParquetReader_ReadRowsAsync_ReturnsScatteredRowsInOrder()
    {
        var path = Path.Combine(_directory, "scattered.parquet");
        await using (var writer = new ParquetDatasetWriter(path))
        {
            for (var id = 0; id < 10; id++)
            {
                await writer.WriteRowAsync(new DatasetRow(id, $$"""{"id":{{id}},"text":"row-{{id}}"}"""));
            }

            await writer.CompleteAsync();
        }

        await using var reader = new ParquetDatasetReader();
        await reader.OpenAsync(path);

        var rows = await reader.ReadRowsAsync(new long[] { 7, 1, 9 });

        Assert.That(rows.Select(row => row.LineNumber), Is.EqualTo(new long[] { 7, 1, 9 }));
        Assert.That(rows[0].Json, Does.Contain("\"row-7\""));
        Assert.That(rows[1].Json, Does.Contain("\"row-1\""));
        Assert.That(rows[2].Json, Does.Contain("\"row-9\""));
    }

    private sealed class MemoryReader : IDatasetReader
    {
        private readonly DatasetRow[] _rows;

        public MemoryReader(params string[] json)
        {
            _rows = json.Select((text, index) => new DatasetRow(index, text)).ToArray();
        }

        public string SourcePath => "memory";

        public DataFileType FileType => DataFileType.Jsonl;

        public long TotalRows => _rows.Length;

        public int PageReads { get; private set; }

        public Task OpenAsync(string path, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<DatasetRow>> ReadPageAsync(
            long startRow, int count, CancellationToken cancellationToken = default)
        {
            PageReads++;
            IReadOnlyList<DatasetRow> page = _rows
                .Skip((int)startRow)
                .Take(count)
                .ToArray();
            return Task.FromResult(page);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
