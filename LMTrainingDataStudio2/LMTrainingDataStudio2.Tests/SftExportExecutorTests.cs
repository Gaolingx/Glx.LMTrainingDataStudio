using System.Text.Json;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services.Executors;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class SftExportExecutorTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "lmtds-sft-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_directory, true); } catch { /* best effort */ }
    }

    [Test]
    public async Task Export_PassesRowsThroughAndWritesFile()
    {
        var target = Path.Combine(_directory, "out.jsonl");

        var rows = await CollectAsync(ExecuteAsync(
            new SftExportBlockConfig(target),
            RowsAsync("""{"messages":[{"role":"user","content":"a"}]}""", """{"messages":[]}""")));

        Assert.That(rows.Select(row => row.Json), Is.EqualTo(new[]
        {
            """{"messages":[{"role":"user","content":"a"}]}""",
            """{"messages":[]}"""
        }));
        Assert.That(await File.ReadAllLinesAsync(target), Has.Length.EqualTo(2));
    }

    [Test]
    public async Task Export_AppendMode_PreservesExistingRowsAndAddsNewline()
    {
        var target = Path.Combine(_directory, "append.jsonl");
        await File.WriteAllTextAsync(target, """{"old":true}""");

        await CollectAsync(ExecuteAsync(
            new SftExportBlockConfig(target, Append: true),
            RowsAsync("""{"new":1}""", """{"new":2}""")));

        var lines = await File.ReadAllLinesAsync(target);
        Assert.That(lines, Has.Length.EqualTo(3));
        Assert.That(lines[0], Is.EqualTo("""{"old":true}"""));
        Assert.That(JsonDocument.Parse(lines[1]).RootElement.GetProperty("new").GetInt32(), Is.EqualTo(1));
        Assert.That(JsonDocument.Parse(lines[2]).RootElement.GetProperty("new").GetInt32(), Is.EqualTo(2));
    }

    [Test]
    public async Task Export_AppendMode_KeepsOriginalWhenInputFails()
    {
        var target = Path.Combine(_directory, "append-fail.jsonl");
        await File.WriteAllTextAsync(target, "{\"old\":true}\n");

        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await CollectAsync(ExecuteAsync(
                new SftExportBlockConfig(target, Append: true),
                FailingRowsAsync())));

        Assert.That(await File.ReadAllLinesAsync(target), Is.EqualTo(new[] { "{\"old\":true}" }));
        Assert.That(Directory.GetFiles(_directory, "*.tmp"), Is.Empty);
    }

    [Test]
    public async Task Export_TargetFormatAlpaca_ConvertsMessages()
    {
        var target = Path.Combine(_directory, "alpaca.jsonl");

        await CollectAsync(ExecuteAsync(
            new SftExportBlockConfig(target, TargetFormat: DatasetFormat.Alpaca),
            RowsAsync("""
                {"messages":[
                  {"role":"system","content":"be nice"},
                  {"role":"user","content":"hi"},
                  {"role":"assistant","content":"hello"}
                ]}
                """)));

        var line = (await File.ReadAllLinesAsync(target)).Single();
        var root = JsonDocument.Parse(line).RootElement;
        Assert.That(root.GetProperty("system").GetString(), Is.EqualTo("be nice"));
        Assert.That(root.GetProperty("instruction").GetString(), Is.EqualTo("hi"));
        Assert.That(root.GetProperty("output").GetString(), Is.EqualTo("hello"));
        Assert.That(root.TryGetProperty("messages", out _), Is.False);
    }

    [Test]
    public async Task Export_TargetFormatShareGPT_ConvertsMessages()
    {
        var target = Path.Combine(_directory, "sharegpt.jsonl");

        await CollectAsync(ExecuteAsync(
            new SftExportBlockConfig(target, TargetFormat: DatasetFormat.ShareGPT),
            RowsAsync("""{"messages":[{"role":"user","content":"hi"},{"role":"assistant","content":"hello"}]}""")));

        var root = JsonDocument.Parse((await File.ReadAllLinesAsync(target)).Single()).RootElement;
        var conversations = root.GetProperty("conversations");
        Assert.That(conversations.GetArrayLength(), Is.EqualTo(2));
        Assert.That(conversations[0].GetProperty("from").GetString(), Is.EqualTo("human"));
        Assert.That(conversations[1].GetProperty("from").GetString(), Is.EqualTo("gpt"));
    }

    [Test]
    public async Task Export_FieldWhitelist_AppliesToConvertedRow()
    {
        var target = Path.Combine(_directory, "filtered.jsonl");

        await CollectAsync(ExecuteAsync(
            new SftExportBlockConfig(
                target,
                Fields: new[] { "output" },
                TargetFormat: DatasetFormat.Alpaca),
            RowsAsync("""{"messages":[{"role":"user","content":"hi"},{"role":"assistant","content":"hello"}]}""")));

        var root = JsonDocument.Parse((await File.ReadAllLinesAsync(target)).Single()).RootElement;
        Assert.That(root.EnumerateObject().Select(property => property.Name), Is.EqualTo(new[] { "output" }));
        Assert.That(root.GetProperty("output").GetString(), Is.EqualTo("hello"));
    }

    [Test]
    public async Task Export_SplitMaxRows_WritesNumberedParts()
    {
        var target = Path.Combine(_directory, "split.jsonl");

        await CollectAsync(ExecuteAsync(
            new SftExportBlockConfig(target, SplitMaxRows: 2),
            RowsAsync(Enumerable.Range(1, 5).Select(index => $$"""{"n":{{index}}}""").ToArray())));

        Assert.That(File.Exists(target), Is.False);
        var parts = Directory.GetFiles(_directory, "split.part*.jsonl").OrderBy(path => path).ToArray();
        Assert.That(parts.Select(Path.GetFileName), Is.EqualTo(new[]
        {
            "split.part001.jsonl",
            "split.part002.jsonl",
            "split.part003.jsonl"
        }));
        Assert.That(await File.ReadAllLinesAsync(parts[0]), Has.Length.EqualTo(2));
        Assert.That(await File.ReadAllLinesAsync(parts[1]), Has.Length.EqualTo(2));
        Assert.That(await File.ReadAllLinesAsync(parts[2]), Has.Length.EqualTo(1));
    }

    [Test]
    public void Export_SplitFailure_LeavesNoTempOrPartialFiles()
    {
        var target = Path.Combine(_directory, "split-fail.jsonl");

        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await CollectAsync(ExecuteAsync(
                new SftExportBlockConfig(target, SplitMaxRows: 1),
                FailingRowsAsync())));

        Assert.That(Directory.GetFiles(_directory, "split-fail*"), Is.Empty);
    }

    [Test]
    public void Export_AppendWithSplit_Throws()
    {
        var executor = new SftExportBlockExecutor();
        var block = CreateBlock(new SftExportBlockConfig(
            Path.Combine(_directory, "bad.jsonl"), Append: true, SplitMaxRows: 10));

        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await executor.ExecuteAsync(CreateContext(block), CancellationToken.None));
    }

    [TestCase(DatasetFormat.Dpo)]
    [TestCase(DatasetFormat.Pretrain)]
    public void Export_UnsupportedTargetFormat_Throws(DatasetFormat format)
    {
        var executor = new SftExportBlockExecutor();
        var block = CreateBlock(new SftExportBlockConfig(
            Path.Combine(_directory, "bad.jsonl"), TargetFormat: format));

        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await executor.ExecuteAsync(CreateContext(block), CancellationToken.None));
    }

    [Test]
    public void Export_NegativeSplit_Throws()
    {
        var executor = new SftExportBlockExecutor();
        var block = CreateBlock(new SftExportBlockConfig(
            Path.Combine(_directory, "bad.jsonl"), SplitMaxRows: -1));

        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await executor.ExecuteAsync(CreateContext(block), CancellationToken.None));
    }

    private static async IAsyncEnumerable<DatasetRow> ExecuteAsync(
        SftExportBlockConfig config,
        IAsyncEnumerable<DatasetRow> rows)
    {
        var executor = new SftExportBlockExecutor();
        var block = CreateBlock(config);
        var context = CreateContext(block, rows);
        var result = await executor.ExecuteAsync(context, CancellationToken.None);
        await foreach (var row in result.Output)
        {
            yield return row;
        }
    }

    private static RecipeBlock CreateBlock(SftExportBlockConfig config) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Type = BlockType.SftExport,
        Name = "export",
        DisplayName = "export",
        Config = config
    };

    private static BlockExecutionContext CreateContext(
        RecipeBlock block,
        IAsyncEnumerable<DatasetRow>? input = null) => new()
        {
            Block = block,
            Input = input,
            Log = _ => { }
        };

    private static async IAsyncEnumerable<DatasetRow> RowsAsync(params string[] rows)
    {
        await Task.Yield();
        for (var index = 0; index < rows.Length; index++)
        {
            yield return new DatasetRow(index, rows[index]);
        }
    }

    private static async IAsyncEnumerable<DatasetRow> FailingRowsAsync()
    {
        await Task.Yield();
        yield return new DatasetRow(0, """{"messages":[]}""");
        throw new InvalidOperationException("simulated upstream failure");
    }

    private static async Task<List<DatasetRow>> CollectAsync(IAsyncEnumerable<DatasetRow> rows)
    {
        var result = new List<DatasetRow>();
        await foreach (var row in rows)
        {
            result.Add(row);
        }

        return result;
    }
}
