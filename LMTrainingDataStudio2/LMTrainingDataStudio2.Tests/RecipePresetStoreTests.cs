using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class RecipePresetStoreTests
{
    private string _directory = null!;
    private string _presetDirectory = null!;
    private RecipePresetStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "lmtds-tests", Guid.NewGuid().ToString("N"));
        _presetDirectory = Path.Combine(_directory, "presets");
        Directory.CreateDirectory(_directory);
        _store = new RecipePresetStore(_presetDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_directory, true); } catch { /* best effort */ }
    }

    [Test]
    public async Task SaveAsync_ThenLoadAll_RoundTrips()
    {
        var recipe = RecipePresetCatalog.Create("llm_batch");

        var saved = await _store.SaveAsync("My Workflow", "Reusable batch generation", recipe);
        var loaded = _store.LoadAll();

        Assert.That(File.Exists(Path.Combine(_presetDirectory, saved.Id + ".json")), Is.True);
        Assert.That(loaded.Warnings, Is.Empty);
        Assert.That(loaded.Presets, Has.Count.EqualTo(1));

        var document = loaded.Presets[0];
        Assert.That(document.Id, Is.EqualTo(saved.Id));
        Assert.That(document.Name, Is.EqualTo("My Workflow"));
        Assert.That(document.Description, Is.EqualTo("Reusable batch generation"));
        Assert.That(document.Recipe.Blocks, Has.Count.EqualTo(recipe.Blocks.Count));
        Assert.That(document.Recipe.Edges, Has.Count.EqualTo(recipe.Edges.Count));
        Assert.That(document.Recipe.Blocks.Select(block => block.Name),
            Is.EquivalentTo(recipe.Blocks.Select(block => block.Name)));
        Assert.That(document.Recipe.Blocks.Select(block => block.Config.GetBlockType()),
            Is.EquivalentTo(recipe.Blocks.Select(block => block.Config.GetBlockType())));
    }

    [Test]
    public async Task SaveAsync_TrimsMetadata_AndUsesPresetNameForSnapshot()
    {
        var recipe = RecipePresetCatalog.Create("llm_batch");
        recipe.Name = "Original Recipe";

        var saved = await _store.SaveAsync("  Trimmed Preset  ", "  description  ", recipe);
        var loaded = _store.LoadAll().Presets[0];

        Assert.That(saved.Name, Is.EqualTo("Trimmed Preset"));
        Assert.That(saved.Description, Is.EqualTo("description"));
        Assert.That(loaded.Recipe.Name, Is.EqualTo("Trimmed Preset"));
        Assert.That(recipe.Name, Is.EqualTo("Original Recipe"));
    }

    [Test]
    public void SaveAsync_RejectsEmptyNameAndEmptyRecipe()
    {
        var recipe = RecipePresetCatalog.Create("llm_batch");

        Assert.ThrowsAsync<ArgumentException>(async () => await _store.SaveAsync(" ", null, recipe));
        Assert.ThrowsAsync<ArgumentException>(async () => await _store.SaveAsync("Preset", null, new Recipe()));
    }

    [Test]
    public async Task SaveAsync_IsAtomic_AndLeavesNoTempFile()
    {
        var recipe = RecipePresetCatalog.Create("oai_clean");

        var saved = await _store.SaveAsync("Clean", null, recipe);
        var path = Path.Combine(_presetDirectory, saved.Id + ".json");

        Assert.That(File.Exists(path), Is.True);
        Assert.That(File.Exists(path + ".tmp"), Is.False);
    }

    [Test]
    public async Task ImportAsync_CopiesPresetWithNewLocalId()
    {
        var sourceDirectory = Path.Combine(_directory, "source");
        var sourceStore = new RecipePresetStore(sourceDirectory);
        var source = await sourceStore.SaveAsync("Shared Preset", "From a colleague",
            RecipePresetCatalog.Create("field_map"));
        var sourcePath = Path.Combine(sourceDirectory, source.Id + ".json");

        var imported = await _store.ImportAsync(sourcePath);
        var loaded = _store.LoadAll();

        Assert.That(imported.Id, Is.Not.EqualTo(source.Id));
        Assert.That(imported.Name, Is.EqualTo("Shared Preset"));
        Assert.That(imported.Description, Is.EqualTo("From a colleague"));
        Assert.That(loaded.Presets, Has.Count.EqualTo(1));
        Assert.That(loaded.Presets[0].Id, Is.EqualTo(imported.Id));
        Assert.That(File.Exists(sourcePath), Is.True, "Import must not move or delete the source file.");
    }

    [Test]
    public async Task ImportAsync_RejectsMalformedJson()
    {
        var path = Path.Combine(_directory, "broken.json");
        await File.WriteAllTextAsync(path, "{ not valid json");

        Assert.ThrowsAsync<RecipePresetFormatException>(async () => await _store.ImportAsync(path));
        Assert.That(_store.LoadAll().Presets, Is.Empty);
    }

    [Test]
    public async Task ImportAsync_RejectsUnsupportedSchemaEmptyNameAndEmptyRecipe()
    {
        var unsupportedSchema = await WritePresetJsonAsync(
            """{"schemaVersion":99,"id":"0123456789abcdef0123456789abcdef","name":"x","recipe":{}}""");
        var emptyName = await WritePresetJsonAsync(
            """{"schemaVersion":1,"id":"0123456789abcdef0123456789abcdef","name":" ","recipe":{}}""");
        var emptyRecipe = await WritePresetJsonAsync(
            """{"schemaVersion":1,"id":"0123456789abcdef0123456789abcdef","name":"x","recipe":{"blocks":[]}}""");

        Assert.ThrowsAsync<RecipePresetFormatException>(async () => await _store.ImportAsync(unsupportedSchema));
        Assert.ThrowsAsync<RecipePresetFormatException>(async () => await _store.ImportAsync(emptyName));
        Assert.ThrowsAsync<RecipePresetFormatException>(async () => await _store.ImportAsync(emptyRecipe));
    }

    [Test]
    public async Task LoadAll_SkipsInvalidFilesAndReportsWarnings()
    {
        await _store.SaveAsync("Valid", null, RecipePresetCatalog.Create("llm_batch"));
        await File.WriteAllTextAsync(Path.Combine(_presetDirectory, "broken.json"), "not json");

        var result = _store.LoadAll();

        Assert.That(result.Presets, Has.Count.EqualTo(1));
        Assert.That(result.Presets[0].Name, Is.EqualTo("Valid"));
        Assert.That(result.Warnings, Has.Count.EqualTo(1));
        Assert.That(result.Warnings[0], Does.Contain("broken.json"));
    }

    [Test]
    public async Task DeleteAsync_RemovesPresetFile()
    {
        var saved = await _store.SaveAsync("Temporary", null, RecipePresetCatalog.Create("oai_clean"));
        var path = Path.Combine(_presetDirectory, saved.Id + ".json");

        await _store.DeleteAsync(saved.Id);

        Assert.That(File.Exists(path), Is.False);
        Assert.That(_store.LoadAll().Presets, Is.Empty);
    }

    [Test]
    public async Task DeleteAsync_UnknownId_IsNoOp()
    {
        await _store.DeleteAsync(Guid.NewGuid().ToString("N"));
        await _store.DeleteAsync(Guid.NewGuid().ToString("N"));

        Assert.That(_store.LoadAll().Presets, Is.Empty);
    }

    [Test]
    public void DeleteAsync_InvalidId_Throws()
    {
        Assert.ThrowsAsync<ArgumentException>(async () => await _store.DeleteAsync("../escape"));
    }

    [Test]
    public async Task BuildRecipe_ReturnsIndependentCopyNamedAfterPreset()
    {
        var saved = await _store.SaveAsync("Frozen Snapshot", null, RecipePresetCatalog.Create("llm_batch"));
        var document = _store.LoadAll().Presets[0];

        var first = RecipePresetStore.BuildRecipe(document);
        var second = RecipePresetStore.BuildRecipe(document);
        first.Name = "Changed";
        first.Blocks[0].Name = "changed_name";

        Assert.That(second.Name, Is.EqualTo(saved.Name));
        Assert.That(second.Blocks[0].Name, Is.Not.EqualTo("changed_name"));
        Assert.That(document.Recipe.Name, Is.EqualTo(saved.Name));
    }

    private async Task<string> WritePresetJsonAsync(string json)
    {
        var path = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(path, json);
        return path;
    }
}
