using System.Text.Json;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class RecipeStoreTests
{
    private string _directory = null!;
    private string _path = null!;
    private RecipeStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "lmtds-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "recipe.json");
        _store = new RecipeStore();
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_directory, true); } catch { /* best effort */ }
    }

    [Test]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsRecipe()
    {
        var recipe = CreateRecipe();

        await _store.SaveAsync(recipe, _path);
        var loaded = await _store.LoadAsync(_path);

        Assert.That(loaded.Id, Is.EqualTo(recipe.Id));
        Assert.That(loaded.Name, Is.EqualTo(recipe.Name));
        Assert.That(loaded.SchemaVersion, Is.EqualTo(Recipe.CurrentSchemaVersion));
        Assert.That(loaded.CreatedAt, Is.EqualTo(recipe.CreatedAt));
        Assert.That(loaded.ModifiedAt, Is.EqualTo(recipe.ModifiedAt));
        Assert.That(loaded.Blocks, Has.Count.EqualTo(2));
        Assert.That(loaded.Edges, Has.Count.EqualTo(1));

        var seed = loaded.Blocks[0];
        Assert.That(seed.Id, Is.EqualTo("seed1"));
        Assert.That(seed.Type, Is.EqualTo(BlockType.Seed));
        Assert.That(seed.Name, Is.EqualTo("seed_data"));
        Assert.That(seed.DisplayName, Is.EqualTo("Training Data"));
        Assert.That(seed.X, Is.EqualTo(80));
        Assert.That(seed.Y, Is.EqualTo(150));
        Assert.That(seed.Width, Is.EqualTo(240));
        Assert.That(seed.Height, Is.EqualTo(96));
        Assert.That(seed.Config, Is.EqualTo(recipe.Blocks[0].Config));
        Assert.That(seed.OutputPorts, Has.Count.EqualTo(1));
        Assert.That(seed.OutputPorts[0].Direction, Is.EqualTo(PortDirection.Output));
        Assert.That(seed.OutputPorts[0].DataType, Is.EqualTo("jsonl"));

        var validator = loaded.Blocks[1];
        Assert.That(validator.Type, Is.EqualTo(BlockType.Validator));
        Assert.That(validator.Config, Is.EqualTo(recipe.Blocks[1].Config));
        Assert.That(validator.InputPorts, Has.Count.EqualTo(1));

        var edge = loaded.Edges[0];
        Assert.That(edge.SourceBlockId, Is.EqualTo("seed1"));
        Assert.That(edge.SourcePortId, Is.EqualTo("seed1_out"));
        Assert.That(edge.TargetBlockId, Is.EqualTo("val1"));
        Assert.That(edge.TargetPortId, Is.EqualTo("val1_in"));
        Assert.That(edge.Label, Is.EqualTo("jsonl"));
    }

    [Test]
    public async Task SaveAsync_WritesCamelCasePolymorphicJson()
    {
        var recipe = new Recipe { Id = "recipe1", Name = "JSON format" };
        recipe.Blocks.Add(new RecipeBlock
        {
            Id = "seed1",
            Type = BlockType.Seed,
            Name = "seed_data",
            DisplayName = "Training Data",
            Config = new SeedBlockConfig("data/train.jsonl", DataFileType.Jsonl, Streaming: false, UseIndexCache: true)
        });
        recipe.Blocks.Add(new RecipeBlock
        {
            Id = "llm1",
            Type = BlockType.Llm,
            Name = "generator",
            DisplayName = "Generator",
            Config = new LlmBlockConfig("openai", "gpt-4o", Temperature: 0.7, MaxTokens: 2048, Prompt: null)
        });

        await _store.SaveAsync(recipe, _path);

        var json = await File.ReadAllTextAsync(_path);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.That(root.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(Recipe.CurrentSchemaVersion));
        Assert.That(root.GetProperty("name").GetString(), Is.EqualTo("JSON format"));

        var seedConfig = root.GetProperty("blocks")[0].GetProperty("config");
        Assert.That(seedConfig.GetProperty("kind").GetString(), Is.EqualTo("seed"));
        Assert.That(seedConfig.GetProperty("fileType").GetString(), Is.EqualTo("Jsonl"));

        var llmConfig = root.GetProperty("blocks")[1].GetProperty("config");
        Assert.That(llmConfig.GetProperty("kind").GetString(), Is.EqualTo("llm"));
        Assert.That(llmConfig.TryGetProperty("prompt", out _), Is.False);
    }

    [Test]
    public async Task SaveAsync_OverwritesExistingFile_AndLeavesNoTempFile()
    {
        await File.WriteAllTextAsync(_path, "not a recipe");

        await _store.SaveAsync(CreateRecipe(), _path);
        var loaded = await _store.LoadAsync(_path);

        Assert.That(loaded.Name, Is.EqualTo("Round trip"));
        Assert.That(File.Exists(_path + ".tmp"), Is.False);
    }

    [Test]
    public void SaveAsync_CancelledToken_DoesNotCreateFiles()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(async () => await _store.SaveAsync(new Recipe(), _path, cts.Token));
        Assert.That(File.Exists(_path), Is.False);
        Assert.That(File.Exists(_path + ".tmp"), Is.False);
    }

    [Test]
    public async Task LoadAsync_MalformedJson_ThrowsWithPathAndPosition()
    {
        await File.WriteAllTextAsync(_path, """{ "name": "broken", """);

        var ex = Assert.ThrowsAsync<RecipeFormatException>(async () => await _store.LoadAsync(_path));

        Assert.That(ex!.FilePath, Is.EqualTo(_path));
        Assert.That(ex.LineNumber, Is.Not.Null);
        Assert.That(ex.BytePositionInLine, Is.Not.Null);
        Assert.That(ex.Message, Does.Contain(_path));
    }

    [Test]
    public async Task LoadAsync_MissingRequiredConfig_ThrowsRecipeFormatException()
    {
        await File.WriteAllTextAsync(
            _path,
            """{"schemaVersion":1,"blocks":[{"id":"b1","type":"Seed","name":"seed_data"}]}""");

        var ex = Assert.ThrowsAsync<RecipeFormatException>(async () => await _store.LoadAsync(_path));

        Assert.That(ex!.InnerException, Is.TypeOf<JsonException>());
    }

    [Test]
    public async Task LoadAsync_UnsupportedSchemaVersion_ThrowsRecipeFormatException()
    {
        await File.WriteAllTextAsync(_path, """{"schemaVersion":99}""");

        var ex = Assert.ThrowsAsync<RecipeFormatException>(async () => await _store.LoadAsync(_path));

        Assert.That(ex!.Message, Does.Contain("schema version 99"));
        Assert.That(ex.LineNumber, Is.Null);
    }

    [Test]
    public async Task LoadAsync_NullJson_ThrowsRecipeFormatException()
    {
        await File.WriteAllTextAsync(_path, "null");

        Assert.ThrowsAsync<RecipeFormatException>(async () => await _store.LoadAsync(_path));
    }

    [Test]
    public void LoadAsync_MissingFile_ThrowsFileNotFoundException()
    {
        Assert.ThrowsAsync<FileNotFoundException>(
            async () => await _store.LoadAsync(Path.Combine(_directory, "missing.json")));
    }

    private static Recipe CreateRecipe()
    {
        var recipe = new Recipe
        {
            Id = "recipe1",
            Name = "Round trip",
            CreatedAt = new DateTime(2026, 11, 16, 8, 30, 0, DateTimeKind.Utc),
            ModifiedAt = new DateTime(2026, 11, 16, 9, 45, 0, DateTimeKind.Utc)
        };

        var seed = new RecipeBlock
        {
            Id = "seed1",
            Type = BlockType.Seed,
            Name = "seed_data",
            DisplayName = "Training Data",
            X = 80,
            Y = 150,
            Width = 240,
            Height = 96,
            Config = new SeedBlockConfig("data/train.jsonl", DataFileType.Jsonl, Streaming: false, UseIndexCache: true)
        };
        seed.OutputPorts.Add(new BlockPort
        {
            Id = "seed1_out",
            Name = "rows",
            DataType = "jsonl",
            Direction = PortDirection.Output
        });

        var validator = new RecipeBlock
        {
            Id = "val1",
            Type = BlockType.Validator,
            Name = "code_check",
            DisplayName = "Python Validator",
            X = 680,
            Y = 150,
            Config = new ValidatorBlockConfig("expression", "len(messages) > 0", RejectOnFailure: true)
        };
        validator.InputPorts.Add(new BlockPort
        {
            Id = "val1_in",
            Name = "input",
            DataType = "messages[]",
            Direction = PortDirection.Input
        });

        recipe.Blocks.Add(seed);
        recipe.Blocks.Add(validator);
        recipe.Edges.Add(new RecipeEdge
        {
            Id = "edge1",
            SourceBlockId = "seed1",
            SourcePortId = "seed1_out",
            TargetBlockId = "val1",
            TargetPortId = "val1_in",
            Label = "jsonl"
        });

        return recipe;
    }
}
