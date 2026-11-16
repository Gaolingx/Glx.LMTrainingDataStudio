using System.Text.Json;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services;
using LMTrainingDataStudio2.Services.Executors;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class ExecutorTests
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
    public async Task SeedExecutor_WithIndexCache_ReadsAllRows()
    {
        var dataPath = Path.Combine(_directory, "data.jsonl");
        await File.WriteAllTextAsync(dataPath, "{\"a\":1}\n{\"a\":2}");
        var block = CreateBlock(BlockType.Seed, "seed",
            new SeedBlockConfig(dataPath, DataFileType.Jsonl, Streaming: false, UseIndexCache: true));

        var executor = new SeedBlockExecutor(_settings);
        var result = await executor.ExecuteAsync(CreateContext(block), CancellationToken.None);
        var rows = await CollectAsync(result.Output);

        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows[0].LineNumber, Is.EqualTo(0));
        Assert.That(rows[1].Json, Is.EqualTo("{\"a\":2}"));
    }

    [Test]
    public async Task SeedExecutor_WithoutIndexCache_ReadsAllRows()
    {
        var dataPath = Path.Combine(_directory, "data.jsonl");
        await File.WriteAllTextAsync(dataPath, "first\nsecond\n");
        var block = CreateBlock(BlockType.Seed, "seed",
            new SeedBlockConfig(dataPath, DataFileType.Jsonl, Streaming: true, UseIndexCache: false));

        var executor = new SeedBlockExecutor(_settings);
        var result = await executor.ExecuteAsync(CreateContext(block), CancellationToken.None);
        var rows = await CollectAsync(result.Output);

        Assert.That(rows.Select(r => r.Json), Is.EqualTo(new[] { "first", "second" }));
    }

    [Test]
    public void SeedExecutor_WithoutSourcePath_Throws()
    {
        var block = CreateBlock(BlockType.Seed, "seed",
            new SeedBlockConfig(string.Empty, DataFileType.Jsonl, Streaming: false, UseIndexCache: true));

        var executor = new SeedBlockExecutor(_settings);

        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await executor.ExecuteAsync(CreateContext(block), CancellationToken.None));
    }

    [Test]
    public async Task ExpressionExecutor_RendersTemplateWithMappedFields()
    {
        var block = CreateBlock(BlockType.Expression, "expr",
            new ExpressionBlockConfig(
                "{\"input\":{{ prompt | json }}}",
                new Dictionary<string, string> { ["prompt"] = "content" }));
        var input = new[] { new DatasetRow(0, "{\"content\":\"hello \\\"world\\\"\"}") };

        var executor = new ExpressionBlockExecutor();
        var result = await executor.ExecuteAsync(CreateContext(block, input), CancellationToken.None);
        var rows = await CollectAsync(result.Output);

        Assert.That(rows, Has.Count.EqualTo(1));
        using var document = JsonDocument.Parse(rows[0].Json);
        Assert.That(document.RootElement.GetProperty("input").GetString(), Is.EqualTo("hello \"world\""));
    }

    [Test]
    public async Task ExpressionExecutor_WithoutTemplate_MapsFields()
    {
        var block = CreateBlock(BlockType.Expression, "expr",
            new ExpressionBlockConfig(
                string.Empty,
                new Dictionary<string, string> { ["text"] = "content" }));
        var input = new[] { new DatasetRow(0, "{\"content\":\"mapped\"}") };

        var executor = new ExpressionBlockExecutor();
        var result = await executor.ExecuteAsync(CreateContext(block, input), CancellationToken.None);
        var rows = await CollectAsync(result.Output);

        using var document = JsonDocument.Parse(rows[0].Json);
        Assert.That(document.RootElement.GetProperty("text").GetString(), Is.EqualTo("mapped"));
    }

    [Test]
    public async Task ValidatorExecutor_FiltersRowsByRule()
    {
        var block = CreateBlock(BlockType.Validator, "check",
            new ValidatorBlockConfig("expression", "messages.length > 0", RejectOnFailure: true));
        var input = new[]
        {
            new DatasetRow(0, "{\"messages\":[{\"role\":\"user\"}]}"),
            new DatasetRow(1, "{\"messages\":[]}"),
            new DatasetRow(2, "not json")
        };

        var executor = new ValidatorBlockExecutor();
        var result = await executor.ExecuteAsync(CreateContext(block, input), CancellationToken.None);
        var rows = await CollectAsync(result.Output);

        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].Json, Is.EqualTo("{\"messages\":[{\"role\":\"user\"}]}"));
    }

    [Test]
    public async Task ValidatorExecutor_InvalidRule_ThrowsDuringEnumeration()
    {
        var block = CreateBlock(BlockType.Validator, "check",
            new ValidatorBlockConfig("expression", "messages ?? 1", RejectOnFailure: true));
        var input = new[] { new DatasetRow(0, "{\"messages\":[]}") };

        var executor = new ValidatorBlockExecutor();
        var result = await executor.ExecuteAsync(CreateContext(block, input), CancellationToken.None);

        Assert.ThrowsAsync<InvalidOperationException>(async () => await CollectAsync(result.Output));
    }

    [Test]
    public async Task SamplerExecutor_Count_IsDeterministic()
    {
        var block = CreateBlock(BlockType.Sampler, "sample",
            new SamplerBlockConfig(Count: 3, Ratio: null, Seed: 42, Splits: Array.Empty<string>()));
        var input = Enumerable.Range(0, 10).Select(i => new DatasetRow(i, $"{{\"i\":{i}}}")).ToArray();

        var executor = new SamplerBlockExecutor();
        var first = await CollectAsync((await executor.ExecuteAsync(CreateContext(block, input), CancellationToken.None)).Output);
        var second = await CollectAsync((await executor.ExecuteAsync(CreateContext(block, input), CancellationToken.None)).Output);

        Assert.That(first, Has.Count.EqualTo(3));
        Assert.That(second.Select(r => r.Json), Is.EqualTo(first.Select(r => r.Json)));
    }

    [Test]
    public async Task SamplerExecutor_Ratio_IsDeterministic()
    {
        var block = CreateBlock(BlockType.Sampler, "sample",
            new SamplerBlockConfig(Count: null, Ratio: 0.5, Seed: 7, Splits: Array.Empty<string>()));
        var input = Enumerable.Range(0, 100).Select(i => new DatasetRow(i, $"{{\"i\":{i}}}")).ToArray();

        var executor = new SamplerBlockExecutor();
        var first = await CollectAsync((await executor.ExecuteAsync(CreateContext(block, input), CancellationToken.None)).Output);
        var second = await CollectAsync((await executor.ExecuteAsync(CreateContext(block, input), CancellationToken.None)).Output);

        Assert.That(first, Has.Count.InRange(1, 99));
        Assert.That(second.Select(r => r.Json), Is.EqualTo(first.Select(r => r.Json)));
    }

    [Test]
    public void SamplerExecutor_WithoutCountOrRatio_Throws()
    {
        var block = CreateBlock(BlockType.Sampler, "sample",
            new SamplerBlockConfig(Count: null, Ratio: null, Seed: 42, Splits: Array.Empty<string>()));

        var executor = new SamplerBlockExecutor();

        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await executor.ExecuteAsync(CreateContext(block, Array.Empty<DatasetRow>()), CancellationToken.None));
    }

    [Test]
    public async Task LlmExecutor_UsesPromptAndClient()
    {
        var client = new FakeLlmClient("done");
        var block = CreateBlock(BlockType.Llm, "llm",
            new LlmBlockConfig("openai", "gpt-4o", Temperature: 0.7, MaxTokens: 2048, Prompt: "Rewrite: {{ content }}"));
        var input = new[] { new DatasetRow(0, "{\"content\":\"hello\"}") };

        var executor = new LlmBlockExecutor(client);
        var result = await executor.ExecuteAsync(CreateContext(block, input), CancellationToken.None);
        var rows = await CollectAsync(result.Output);

        Assert.That(client.Prompts, Is.EqualTo(new[] { "Rewrite: hello" }));
        using var document = JsonDocument.Parse(rows[0].Json);
        var message = document.RootElement.GetProperty("messages")[0];
        Assert.That(message.GetProperty("role").GetString(), Is.EqualTo("assistant"));
        Assert.That(message.GetProperty("content").GetString(), Is.EqualTo("done"));
    }

    [Test]
    public void LlmExecutor_WithoutModelAlias_Throws()
    {
        var block = CreateBlock(BlockType.Llm, "llm",
            new LlmBlockConfig("openai", string.Empty, Temperature: 0.7, MaxTokens: 2048, Prompt: null));
        var executor = new LlmBlockExecutor(new FakeLlmClient("unused"));

        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await executor.ExecuteAsync(
                CreateContext(block, new[] { new DatasetRow(0, "{}") }), CancellationToken.None));
    }

    private static RecipeBlock CreateBlock(BlockType type, string name, BlockConfig config) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Type = type,
        Name = name,
        DisplayName = name,
        Config = config
    };

    private static BlockExecutionContext CreateContext(
        RecipeBlock block,
        IEnumerable<DatasetRow>? input = null,
        List<string>? logs = null) => new()
    {
        Block = block,
        Input = input is null ? null : ToAsync(input),
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

    private sealed class FakeLlmClient(string completion) : ILlmClient
    {
        public List<string> Prompts { get; } = new();

        public Task<string> CompleteAsync(
            string prompt, LlmBlockConfig config, CancellationToken cancellationToken = default)
        {
            Prompts.Add(prompt);
            return Task.FromResult(completion);
        }
    }
}