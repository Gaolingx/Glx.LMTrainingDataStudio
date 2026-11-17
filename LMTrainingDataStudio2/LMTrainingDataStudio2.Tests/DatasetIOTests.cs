using System.Text;
using System.Text.Json.Nodes;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services.Datasets;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class DatasetIOTests
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
    public async Task CsvReader_ReadsQuotedNewlinesAndNestedJson()
    {
        var path = Path.Combine(_directory, "data.csv");
        await File.WriteAllTextAsync(path, """
            name,payload,note
            alpha,"{""messages"":[{""role"":""user"",""content"":""hi""}]}","line1
            line2"
            beta,"[1,2,3]",plain
            """.ReplaceLineEndings("\n"));

        await using var reader = new CsvDatasetReader();
        await reader.OpenAsync(path);

        Assert.That(reader.TotalRows, Is.EqualTo(2));

        var page = await reader.ReadPageAsync(0, 10);
        Assert.That(page, Has.Count.EqualTo(2));
        Assert.That(page[0].LineNumber, Is.EqualTo(0));

        var first = Parse(page[0].Json);
        Assert.That(first["name"]!.GetValue<string>(), Is.EqualTo("alpha"));
        Assert.That(first["payload"], Is.InstanceOf<JsonObject>());
        Assert.That(first["payload"]!.AsObject()["messages"]!.AsArray(), Has.Count.EqualTo(1));
        Assert.That(first["note"]!.GetValue<string>(), Is.EqualTo("line1\nline2"));

        var second = Parse(page[1].Json);
        Assert.That(second["name"]!.GetValue<string>(), Is.EqualTo("beta"));
        Assert.That(second["payload"], Is.InstanceOf<JsonArray>());
    }

    [Test]
    public async Task CsvReader_ReadsRandomPage()
    {
        var path = Path.Combine(_directory, "data.csv");
        var lines = new StringBuilder("id,value\n");
        for (var i = 0; i < 250; i++)
        {
            lines.Append(i).Append(",value-").Append(i).Append('\n');
        }

        await File.WriteAllTextAsync(path, lines.ToString());
        await using var reader = new CsvDatasetReader();
        await reader.OpenAsync(path);

        var page = await reader.ReadPageAsync(240, 20);

        Assert.That(reader.TotalRows, Is.EqualTo(250));
        Assert.That(page, Has.Count.EqualTo(10));
        Assert.That(Parse(page[0].Json)["id"]!.GetValue<string>(), Is.EqualTo("240"));
        Assert.That(Parse(page[9].Json)["id"]!.GetValue<string>(), Is.EqualTo("249"));
    }

    [Test]
    public async Task CsvWriter_RoundTrip_PreservesStructuredValues()
    {
        var path = Path.Combine(_directory, "roundtrip.csv");
        var rows = new[]
        {
            new DatasetRow(0, """{"name":"alpha","tags":["a","b"],"meta":{"score":1}}"""),
            new DatasetRow(1, """{"name":"beta","tags":[],"meta":{}}""")
        };

        await using (var writer = new CsvDatasetWriter(path))
        {
            foreach (var row in rows)
            {
                await writer.WriteRowAsync(row);
            }

            await writer.CompleteAsync();
        }

        await using var reader = new CsvDatasetReader();
        await reader.OpenAsync(path);
        var page = await reader.ReadPageAsync(0, 10);

        Assert.That(page, Has.Count.EqualTo(2));
        Assert.That(Parse(page[0].Json)["tags"], Is.InstanceOf<JsonArray>());
        Assert.That(Parse(page[0].Json)["meta"], Is.InstanceOf<JsonObject>());
        Assert.That(Parse(page[1].Json)["tags"]!.AsArray(), Is.Empty);
    }

    [Test]
    public async Task ParquetWriterAndReader_RoundTripScalarsAndNestedJson()
    {
        var path = Path.Combine(_directory, "roundtrip.parquet");
        var rows = new[]
        {
            new DatasetRow(0, """{"id":1,"score":0.5,"ok":true,"messages":[{"role":"user","content":"hi"}],"note":null}"""),
            new DatasetRow(1, """{"id":2,"score":1.25,"ok":false,"messages":[],"note":"second"}""")
        };

        await using (var writer = new ParquetDatasetWriter(path))
        {
            foreach (var row in rows)
            {
                await writer.WriteRowAsync(row);
            }

            await writer.CompleteAsync();
        }

        await using var reader = new ParquetDatasetReader();
        await reader.OpenAsync(path);

        Assert.That(reader.TotalRows, Is.EqualTo(2));

        var page = await reader.ReadPageAsync(0, 10);
        Assert.That(page, Has.Count.EqualTo(2));

        var first = Parse(page[0].Json);
        Assert.That(first["id"]!.GetValue<long>(), Is.EqualTo(1));
        Assert.That(first["score"]!.GetValue<double>(), Is.EqualTo(0.5));
        Assert.That(first["ok"]!.GetValue<bool>(), Is.True);
        Assert.That(first["messages"], Is.InstanceOf<JsonArray>());
        Assert.That(first["note"], Is.Null);

        var second = Parse(page[1].Json);
        Assert.That(second["id"]!.GetValue<long>(), Is.EqualTo(2));
        Assert.That(second["messages"]!.AsArray(), Is.Empty);
        Assert.That(second["note"]!.GetValue<string>(), Is.EqualTo("second"));
    }

    [Test]
    public async Task ParquetReader_ReadsAcrossRowGroups()
    {
        var path = Path.Combine(_directory, "many.parquet");

        await using (var writer = new ParquetDatasetWriter(path))
        {
            for (var i = 0; i < 9000; i++)
            {
                await writer.WriteRowAsync(new DatasetRow(i, $$"""{"id":{{i}},"text":"row-{{i}}"}"""));
            }

            await writer.CompleteAsync();
        }

        await using var reader = new ParquetDatasetReader();
        await reader.OpenAsync(path);

        Assert.That(reader.TotalRows, Is.EqualTo(9000));

        var page = await reader.ReadPageAsync(8180, 40);
        Assert.That(page, Has.Count.EqualTo(40));
        Assert.That(Parse(page[0].Json)["id"]!.GetValue<long>(), Is.EqualTo(8180));
        Assert.That(Parse(page[^1].Json)["id"]!.GetValue<long>(), Is.EqualTo(8219));
    }

    [Test]
    public async Task BatchConverter_JsonlToCsv_ReportsBadRows()
    {
        var source = Path.Combine(_directory, "source.jsonl");
        var target = Path.Combine(_directory, "out.csv");
        await File.WriteAllLinesAsync(source, new[]
        {
            """{"messages":[{"role":"user","content":"a"}]}""",
            """{"messages":[{"role":"user","content":"b"}]}""",
            "{broken json",
            """{"foo":"bar"}""",
            """{"messages":[{"role":"assistant","content":"c"}]}"""
        });

        var converter = new DatasetBatchConverter(_settings);
        var result = await converter.ConvertAsync(new BatchConversionRequest(
            source, DataFileType.Jsonl, DatasetFormat.Auto,
            target, DataFileType.Csv, DatasetFormat.OpenAI));

        Assert.That(result.TotalRows, Is.EqualTo(5));
        Assert.That(result.ConvertedRows, Is.EqualTo(3));
        Assert.That(result.FailedRows, Is.EqualTo(2));
        Assert.That(result.SampleErrors, Has.Count.EqualTo(2));
        Assert.That(result.SampleErrors[0], Does.Contain("Row 3"));
        Assert.That(result.ErrorFilePath, Is.Not.Null);
        Assert.That(File.Exists(result.ErrorFilePath), Is.True);

        var errors = await File.ReadAllLinesAsync(result.ErrorFilePath!);
        Assert.That(errors, Has.Length.EqualTo(2));
        Assert.That(Parse(errors[0])["lineNumber"]!.GetValue<long>(), Is.EqualTo(3));

        await using var reader = new CsvDatasetReader();
        await reader.OpenAsync(target);
        Assert.That(reader.TotalRows, Is.EqualTo(3));

        var page = await reader.ReadPageAsync(0, 10);
        Assert.That(Parse(page[0].Json)["messages"], Is.InstanceOf<JsonArray>());
    }

    [Test]
    public async Task BatchConverter_JsonlParquetRoundTrip_KeepsMessages()
    {
        var source = Path.Combine(_directory, "source.jsonl");
        var parquet = Path.Combine(_directory, "converted.parquet");
        var back = Path.Combine(_directory, "back.jsonl");
        await File.WriteAllLinesAsync(source, new[]
        {
            """{"messages":[{"role":"system","content":"be brief"},{"role":"user","content":"hi"},{"role":"assistant","content":"hello"}]}""",
            """{"messages":[{"role":"user","content":"second"},{"role":"assistant","content":"answer"}]}"""
        });

        var converter = new DatasetBatchConverter(_settings);
        var first = await converter.ConvertAsync(new BatchConversionRequest(
            source, DataFileType.Jsonl, DatasetFormat.Auto,
            parquet, DataFileType.Parquet, DatasetFormat.OpenAI));
        Assert.That(first.ConvertedRows, Is.EqualTo(2),
            first.SampleErrors.Count > 0 ? first.SampleErrors[0] : string.Empty);

        var second = await converter.ConvertAsync(new BatchConversionRequest(
            parquet, DataFileType.Parquet, DatasetFormat.Auto,
            back, DataFileType.Jsonl, DatasetFormat.OpenAI));
        Assert.That(second.ConvertedRows, Is.EqualTo(2),
            second.SampleErrors.Count > 0 ? second.SampleErrors[0] : string.Empty);

        var lines = await File.ReadAllLinesAsync(back);
        Assert.That(lines, Has.Length.EqualTo(2));
        Assert.That(Parse(lines[0])["messages"]!.AsArray(), Has.Count.EqualTo(3));
        Assert.That(Parse(lines[1])["messages"]!.AsArray()[1]!["content"]!.GetValue<string>(), Is.EqualTo("answer"));
    }

    [Test]
    public void ConversionService_DpoNormalizesAndRejectsUnsupportedTargets()
    {
        var service = new DatasetConversionService();
        var dpo = Parse("""{"prompt":"q","chosen":"good","rejected":"bad","extra":1}""");

        var normalized = service.Convert(dpo, DatasetFormat.Auto, DatasetFormat.Dpo, 1);

        Assert.That(normalized["prompt"]!.GetValue<string>(), Is.EqualTo("q"));
        Assert.That(normalized["chosen"]!.GetValue<string>(), Is.EqualTo("good"));
        Assert.That(normalized.ContainsKey("extra"), Is.False);

        var chat = Parse("""{"messages":[{"role":"user","content":"q"},{"role":"assistant","content":"a"}]}""");
        Assert.Throws<DatasetFormatException>(() =>
            service.Convert(chat, DatasetFormat.OpenAI, DatasetFormat.Dpo, 2));
        Assert.Throws<DatasetFormatException>(() =>
            service.Convert(Parse("""{"text":"corpus"}"""), DatasetFormat.Pretrain, DatasetFormat.OpenAI, 3));
    }

    [Test]
    public void ConversionService_ChatToPretrain_FlattensMessages()
    {
        var service = new DatasetConversionService();
        var chat = Parse("""
            {"messages":[
              {"role":"system","content":"be brief"},
              {"role":"user","content":"hi"},
              {"role":"assistant","content":"hello"}
            ]}
            """);

        var result = service.Convert(chat, DatasetFormat.OpenAI, DatasetFormat.Pretrain, 1);
        var text = result["text"]!.GetValue<string>();

        Assert.That(text, Does.Contain("system: be brief"));
        Assert.That(text, Does.Contain("user: hi"));
        Assert.That(text, Does.Contain("assistant: hello"));
        Assert.That(text, Does.Contain("\n\n"));
    }

    [Test]
    public async Task BatchConverter_Cancellation_LeavesNoTargetOrTempFiles()
    {
        var source = Path.Combine(_directory, "cancel.jsonl");
        var target = Path.Combine(_directory, "cancel.out.jsonl");
        await File.WriteAllLinesAsync(source, Enumerable.Range(1, 5000)
            .Select(id => $$"""{"messages":[{"role":"user","content":"q{{id}}"}]}"""));

        var converter = new DatasetBatchConverter(_settings);
        using var cancellation = new CancellationTokenSource();
        var progress = new InlineProgress(value =>
        {
            if (value > 0) cancellation.Cancel();
        });

        Assert.CatchAsync<OperationCanceledException>(async () =>
            await converter.ConvertAsync(
                new BatchConversionRequest(
                    source, DataFileType.Jsonl, DatasetFormat.Auto,
                    target, DataFileType.Jsonl, DatasetFormat.OpenAI),
                progress,
                cancellation.Token));

        Assert.That(File.Exists(target), Is.False);
        Assert.That(Directory.GetFiles(_directory, "*.tmp-*"), Is.Empty);
    }

    [Test]
    public void DetectFileType_MapsKnownExtensions()
    {
        Assert.That(DatasetIO.DetectFileType("a.jsonl"), Is.EqualTo(DataFileType.Jsonl));
        Assert.That(DatasetIO.DetectFileType("a.csv"), Is.EqualTo(DataFileType.Csv));
        Assert.That(DatasetIO.DetectFileType("a.parquet"), Is.EqualTo(DataFileType.Parquet));
        Assert.Throws<DatasetFormatException>(() => DatasetIO.DetectFileType("a.txt"));
    }

    private static JsonObject Parse(string json) => JsonNode.Parse(json)!.AsObject();

    private sealed class InlineProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
