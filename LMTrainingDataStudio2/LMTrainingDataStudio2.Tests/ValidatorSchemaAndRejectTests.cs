using System.Text.Json;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services.Executors;
using LMTrainingDataStudio2.ViewModels;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class ValidatorSchemaAndRejectTests
{
    private const string MessageSchema = """
        {"type":"object","required":["messages"],"properties":{"messages":{"type":"array","minItems":1}}}
        """;

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
    public async Task SchemaRule_FiltersInvalidRows_AndLogsFailureRate()
    {
        var block = CreateBlock(new ValidatorBlockConfig("schema", MessageSchema, RejectOnFailure: true));
        var input = CreateInput();
        var logs = new List<string>();

        var executor = new ValidatorBlockExecutor();
        var result = await executor.ExecuteAsync(CreateContext(block, input, logs), CancellationToken.None);
        var rows = await CollectAsync(result.Output);

        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].Json, Does.Contain("\"user\""));
        Assert.That(logs.Any(log => log.Contains("failure rate")), Is.True);
    }

    [Test]
    public async Task RejectOutputPath_WritesRejectedRowsAtomically()
    {
        var rejectPath = Path.Combine(_directory, "rejected", "rows.jsonl");
        var block = CreateBlock(new ValidatorBlockConfig(
            "schema",
            MessageSchema,
            RejectOnFailure: true,
            RejectOutputPath: rejectPath));
        var executor = new ValidatorBlockExecutor();

        var result = await executor.ExecuteAsync(CreateContext(block, CreateInput()), CancellationToken.None);
        await CollectAsync(result.Output);

        Assert.That(File.Exists(rejectPath), Is.True);
        Assert.That(File.Exists(rejectPath + ".tmp"), Is.False);

        var lines = await File.ReadAllLinesAsync(rejectPath);
        Assert.That(lines, Has.Length.EqualTo(2));

        using var first = JsonDocument.Parse(lines[0]);
        Assert.That(first.RootElement.GetProperty("lineNumber").GetInt64(), Is.EqualTo(1));
        Assert.That(first.RootElement.GetProperty("reason").GetString(), Is.Not.Empty);
        Assert.That(first.RootElement.TryGetProperty("data", out _), Is.True);

        using var second = JsonDocument.Parse(lines[1]);
        Assert.That(second.RootElement.GetProperty("data").ValueKind, Is.EqualTo(JsonValueKind.String));
    }

    [Test]
    public async Task RejectOnFailureFalse_PassesInvalidRowsThrough()
    {
        var rejectPath = Path.Combine(_directory, "rejected.jsonl");
        var block = CreateBlock(new ValidatorBlockConfig(
            "schema",
            MessageSchema,
            RejectOnFailure: false,
            RejectOutputPath: rejectPath));
        var executor = new ValidatorBlockExecutor();

        var result = await executor.ExecuteAsync(CreateContext(block, CreateInput()), CancellationToken.None);
        var rows = await CollectAsync(result.Output);

        Assert.That(rows, Has.Count.EqualTo(3));
        Assert.That(File.Exists(rejectPath), Is.False);
    }

    [Test]
    public void InvalidSchema_IsRejectedWhenExecuting()
    {
        var block = CreateBlock(new ValidatorBlockConfig("schema", "{ not a schema", RejectOnFailure: true));
        var executor = new ValidatorBlockExecutor();

        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await executor.ExecuteAsync(CreateContext(block, Array.Empty<DatasetRow>()), CancellationToken.None));

        Assert.That(ex!.Message, Does.Contain("invalid JSON Schema"));
    }

    [Test]
    public void UnknownRuleKind_IsRejected()
    {
        var block = CreateBlock(new ValidatorBlockConfig("python", "x", RejectOnFailure: true));
        var executor = new ValidatorBlockExecutor();

        Assert.ThrowsAsync<NotSupportedException>(async () =>
            await executor.ExecuteAsync(CreateContext(block, Array.Empty<DatasetRow>()), CancellationToken.None));
    }

    [Test]
    public void Editor_ValidatesSchemaAndWritesRejectPath()
    {
        var applied = new List<ValidatorBlockConfig>();
        var editor = new ValidatorConfigEditorViewModel(
            new ValidatorBlockConfig("schema", MessageSchema, RejectOnFailure: true),
            applied.Add);

        editor.ValidateRuleCommand.Execute(null);
        Assert.That(editor.RuleError, Is.Null);

        editor.Rule = "{ not a schema";
        editor.ValidateRuleCommand.Execute(null);
        Assert.That(editor.RuleError, Is.Not.Null);

        editor.RejectOutputPath = "out/rejected.jsonl";
        Assert.That(applied[^1].RejectOutputPath, Is.EqualTo("out/rejected.jsonl"));
    }

    private static RecipeBlock CreateBlock(ValidatorBlockConfig config) => new()
    {
        Id = "val",
        Type = BlockType.Validator,
        Name = "val",
        DisplayName = "val",
        Config = config
    };

    private static DatasetRow[] CreateInput() => new[]
    {
        new DatasetRow(0, """{"messages":[{"role":"user"}]}"""),
        new DatasetRow(1, """{"messages":[]}"""),
        new DatasetRow(2, "not json")
    };

    private static BlockExecutionContext CreateContext(
        RecipeBlock block,
        IEnumerable<DatasetRow> input,
        List<string>? logs = null) => new()
    {
        Block = block,
        Input = ToAsync(input),
        Log = message => logs?.Add(message)
    };

    private static async IAsyncEnumerable<DatasetRow> ToAsync(IEnumerable<DatasetRow> rows)
    {
        await Task.Yield();
        foreach (var row in rows)
        {
            yield return row;
        }
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