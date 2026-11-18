using System.Text.Json;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services;
using LMTrainingDataStudio2.ViewModels;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class MainWindowViewModelDocumentTests
{
    private string _directory = null!;
    private FakeSettingsStore _settingsStore = null!;
    private RecipePresetStore _presetStore = null!;
    private MainWindowViewModel _viewModel = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "lmtds-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);

        _settingsStore = new FakeSettingsStore(Path.Combine(_directory, "settings.json"), new AppSettings
        {
            CacheDirectory = Path.Combine(_directory, "cache"),
            MaxCacheSizeMb = 16
        });
        _presetStore = new RecipePresetStore(Path.Combine(_directory, "presets"));

        _viewModel = new MainWindowViewModel(_settingsStore, presetStore: _presetStore);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_directory, true); } catch { /* best effort */ }
    }

    [Test]
    public void Constructor_StartsWithBlankDocument()
    {
        Assert.That(_viewModel.Blocks, Is.Empty);
        Assert.That(_viewModel.Edges, Is.Empty);
        Assert.That(_viewModel.IsDirty, Is.False);
        Assert.That(_viewModel.CurrentFilePath, Is.Empty);
        Assert.That(_viewModel.DocumentTitle, Is.EqualTo("Untitled Recipe"));
    }

    [Test]
    public async Task NewFromPreset_LoadsWorkflowAndMarksDirty()
    {
        Assert.That(_viewModel.Presets.Any(preset => preset.Id == "llm_batch"), Is.True);

        await _viewModel.NewRecipeFromPresetAsync("llm_batch");

        Assert.That(_viewModel.Blocks, Has.Count.EqualTo(3));
        Assert.That(_viewModel.Edges, Has.Count.EqualTo(2));
        Assert.That(_viewModel.IsDirty, Is.True);
        Assert.That(_viewModel.DocumentTitle, Does.EndWith("*"));

        _viewModel.ValidateRecipeCommand.Execute(null);

        Assert.That(_viewModel.ValidationIssues.Any(issue => issue.IsError), Is.False);
    }

    [Test]
    public void NewRecipeFromPreset_UnknownId_Throws()
    {
        Assert.ThrowsAsync<ArgumentException>(
            async () => await _viewModel.NewRecipeFromPresetAsync("does_not_exist"));
    }

    [Test]
    public async Task SaveAsPreset_AddsCustomPresetAndPersists()
    {
        await _viewModel.NewRecipeAsync(withSample: true);

        var saved = await _viewModel.SaveAsPresetAsync("My Agent Flow", "Reusable sample pipeline");

        Assert.That(saved.IsCustom, Is.True);
        Assert.That(saved.Name, Is.EqualTo("My Agent Flow"));
        Assert.That(_viewModel.Presets, Has.Count.EqualTo(RecipePresetCatalog.All.Count + 1));
        Assert.That(_presetStore.LoadAll().Presets, Has.Count.EqualTo(1));

        var reloaded = new MainWindowViewModel(_settingsStore, presetStore: _presetStore);
        Assert.That(reloaded.Presets.Any(preset => preset.IsCustom && preset.Name == "My Agent Flow"), Is.True);
    }

    [Test]
    public void SaveAsPreset_EmptyDocument_Throws()
    {
        Assert.ThrowsAsync<ArgumentException>(
            async () => await _viewModel.SaveAsPresetAsync("Empty", null));
    }

    [Test]
    public async Task ImportPreset_AddsCustomPresetToLibrary()
    {
        var sourceDirectory = Path.Combine(_directory, "source");
        var sourceStore = new RecipePresetStore(sourceDirectory);
        var source = await sourceStore.SaveAsync(
            "Shared Cleanup", "Imported description", RecipePresetCatalog.Create("oai_clean"));
        var sourcePath = Path.Combine(sourceDirectory, source.Id + ".json");

        var imported = await _viewModel.ImportPresetAsync(sourcePath);

        Assert.That(imported.IsCustom, Is.True);
        Assert.That(imported.Name, Is.EqualTo("Shared Cleanup"));
        Assert.That(imported.Description, Is.EqualTo("Imported description"));
        Assert.That(_viewModel.Presets, Has.Count.EqualTo(RecipePresetCatalog.All.Count + 1));
        Assert.That(_presetStore.LoadAll().Presets, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task ImportPreset_InvalidFile_DoesNotChangeLibrary()
    {
        var before = _viewModel.Presets.Count;
        var path = Path.Combine(_directory, "broken.json");
        await File.WriteAllTextAsync(path, "not json");

        Assert.ThrowsAsync<RecipePresetFormatException>(
            async () => await _viewModel.ImportPresetAsync(path));
        Assert.That(_viewModel.Presets, Has.Count.EqualTo(before));
    }

    [Test]
    public async Task DeleteCustomPreset_RemovesOnlyUserPresets()
    {
        await _viewModel.NewRecipeAsync(withSample: true);
        var saved = await _viewModel.SaveAsPresetAsync("Delete Me", null);

        Assert.That(await _viewModel.DeleteCustomPresetAsync("llm_batch"), Is.False);
        Assert.That(_viewModel.Presets, Has.Count.EqualTo(RecipePresetCatalog.All.Count + 1));

        Assert.That(await _viewModel.DeleteCustomPresetAsync(saved.Id), Is.True);
        Assert.That(_viewModel.Presets, Has.Count.EqualTo(RecipePresetCatalog.All.Count));
        Assert.That(_presetStore.LoadAll().Presets, Is.Empty);
    }

    [Test]
    public async Task NewFromCustomPreset_LoadsSavedSnapshot()
    {
        await _viewModel.NewRecipeAsync(withSample: true);
        var saved = await _viewModel.SaveAsPresetAsync("My Snapshot", null);

        await _viewModel.NewRecipeAsync();
        Assert.That(_viewModel.Blocks, Is.Empty);

        await _viewModel.NewRecipeFromPresetAsync(saved.Id);

        Assert.That(_viewModel.RecipeName, Is.EqualTo("My Snapshot"));
        Assert.That(_viewModel.Blocks, Has.Count.EqualTo(4));
        Assert.That(_viewModel.Edges, Has.Count.EqualTo(3));
        Assert.That(_viewModel.IsDirty, Is.True);
    }

    [Test]
    public async Task NewFromSample_CreatesValidPipeline_AndMarksDirty()
    {
        await _viewModel.NewRecipeAsync(withSample: true);

        Assert.That(_viewModel.Blocks, Has.Count.EqualTo(4));
        Assert.That(_viewModel.Edges, Has.Count.EqualTo(3));
        Assert.That(_viewModel.IsDirty, Is.True);
        Assert.That(_viewModel.DocumentTitle, Does.EndWith("*"));

        foreach (var block in _viewModel.Blocks)
        {
            Assert.That(block.InputPorts, Has.Count.EqualTo(BlockPortSchema.CreateInputPorts(block.Type).Count));
            Assert.That(block.OutputPorts, Has.Count.EqualTo(BlockPortSchema.CreateOutputPorts(block.Type).Count));
            foreach (var port in block.InputPorts.Concat(block.OutputPorts))
            {
                Assert.That(port.DataType, Is.EqualTo(BlockPortSchema.RowsDataType));
            }
        }

        _viewModel.ValidateRecipeCommand.Execute(null);

        Assert.That(_viewModel.ValidationIssues.Any(issue => issue.IsError), Is.False);
        Assert.That(_viewModel.IsDirty, Is.True, "Validation must not mark the document dirty.");
    }

    [Test]
    public async Task SaveThenOpen_RoundTripsDocumentContents()
    {
        await _viewModel.NewRecipeAsync(withSample: true);
        var path = Path.Combine(_directory, "recipe.json");

        await _viewModel.SaveRecipeAsync(path);

        Assert.That(File.Exists(path), Is.True);
        Assert.That(_viewModel.IsDirty, Is.False);
        Assert.That(_viewModel.CurrentFilePath, Is.EqualTo(Path.GetFullPath(path)));
        Assert.That(_viewModel.RecentFiles[0], Is.EqualTo(Path.GetFullPath(path)));
        Assert.That(_settingsStore.Current.LastRecipePath, Is.EqualTo(Path.GetFullPath(path)));

        var saved = _viewModel.ToRecipe();

        await _viewModel.NewRecipeAsync(withSample: false);
        Assert.That(_viewModel.Blocks, Is.Empty);
        Assert.That(_settingsStore.Current.LastRecipePath, Is.Null);

        await _viewModel.OpenRecipeAsync(path);

        Assert.That(_viewModel.IsDirty, Is.False);
        Assert.That(_viewModel.RecipeName, Is.EqualTo(saved.Name));
        Assert.That(_viewModel.Blocks, Has.Count.EqualTo(saved.Blocks.Count));
        Assert.That(_viewModel.Edges, Has.Count.EqualTo(saved.Edges.Count));

        for (var i = 0; i < saved.Blocks.Count; i++)
        {
            var expected = saved.Blocks[i];
            var actual = _viewModel.ToRecipe().Blocks[i];

            Assert.That(actual.Id, Is.EqualTo(expected.Id));
            Assert.That(actual.Type, Is.EqualTo(expected.Type));
            Assert.That(actual.Name, Is.EqualTo(expected.Name));
            Assert.That(actual.DisplayName, Is.EqualTo(expected.DisplayName));
            Assert.That(actual.X, Is.EqualTo(expected.X));
            Assert.That(actual.Y, Is.EqualTo(expected.Y));
            Assert.That(actual.Width, Is.EqualTo(expected.Width));
            Assert.That(actual.Height, Is.EqualTo(expected.Height));
            Assert.That(
                JsonSerializer.Serialize(actual.Config, RecipeStore.SerializerOptions),
                Is.EqualTo(JsonSerializer.Serialize(expected.Config, RecipeStore.SerializerOptions)));
            Assert.That(actual.InputPorts.Select(p => (p.Id, p.Name, p.DataType, p.Direction)),
                Is.EqualTo(expected.InputPorts.Select(p => (p.Id, p.Name, p.DataType, p.Direction))));
            Assert.That(actual.OutputPorts.Select(p => (p.Id, p.Name, p.DataType, p.Direction)),
                Is.EqualTo(expected.OutputPorts.Select(p => (p.Id, p.Name, p.DataType, p.Direction))));
        }

        var loaded = _viewModel.ToRecipe();
        for (var i = 0; i < saved.Edges.Count; i++)
        {
            Assert.That(loaded.Edges[i].Id, Is.EqualTo(saved.Edges[i].Id));
            Assert.That(loaded.Edges[i].SourceBlockId, Is.EqualTo(saved.Edges[i].SourceBlockId));
            Assert.That(loaded.Edges[i].SourcePortId, Is.EqualTo(saved.Edges[i].SourcePortId));
            Assert.That(loaded.Edges[i].TargetBlockId, Is.EqualTo(saved.Edges[i].TargetBlockId));
            Assert.That(loaded.Edges[i].TargetPortId, Is.EqualTo(saved.Edges[i].TargetPortId));
        }
    }

    [Test]
    public async Task ModifyingDocument_MarksDirty_AndSaveClearsIt()
    {
        _viewModel.AddBlockCommand.Execute(BlockType.Seed);

        Assert.That(_viewModel.Blocks, Has.Count.EqualTo(1));
        Assert.That(_viewModel.IsDirty, Is.True);
        Assert.That(_viewModel.Blocks[0].OutputPorts, Has.Count.EqualTo(1));

        var path = Path.Combine(_directory, "dirty.json");
        await _viewModel.SaveRecipeAsync(path);
        Assert.That(_viewModel.IsDirty, Is.False);

        _viewModel.Blocks[0].DisplayName = "Renamed seed";

        Assert.That(_viewModel.IsDirty, Is.True);
        Assert.That(_viewModel.DocumentTitle, Does.EndWith("*"));
    }

    [Test]
    public async Task OpenInvalidFile_LeavesCurrentDocumentUntouched()
    {
        await _viewModel.NewRecipeAsync(withSample: true);
        var path = Path.Combine(_directory, "broken.json");
        await File.WriteAllTextAsync(path, """{ "name": "broken", """);

        Assert.ThrowsAsync<RecipeFormatException>(async () => await _viewModel.OpenRecipeAsync(path));

        Assert.That(_viewModel.Blocks, Has.Count.EqualTo(4));
        Assert.That(_viewModel.RecipeName, Is.EqualTo("Sample SFT Trajectory"));
        Assert.That(_viewModel.CurrentFilePath, Is.Empty);
    }

    [Test]
    public async Task RestoreLastRecipeAsync_LoadsPersistedDocument()
    {
        await _viewModel.NewRecipeAsync(withSample: true);
        var path = Path.Combine(_directory, "restore.json");
        await _viewModel.SaveRecipeAsync(path);

        var restored = new MainWindowViewModel(_settingsStore);
        var loaded = await restored.RestoreLastRecipeAsync();

        Assert.That(loaded, Is.True);
        Assert.That(restored.RecipeName, Is.EqualTo("Sample SFT Trajectory"));
        Assert.That(restored.Blocks, Has.Count.EqualTo(4));
        Assert.That(restored.IsDirty, Is.False);
    }

    [Test]
    public async Task TryConnect_RejectsSecondIncomingConnection()
    {
        await _viewModel.NewRecipeAsync(withSample: false);
        _viewModel.AddBlockCommand.Execute(BlockType.QuerySource);
        _viewModel.AddBlockCommand.Execute(BlockType.Simulation);

        var source = _viewModel.Blocks[0];
        var target = _viewModel.Blocks[1];

        var connected = _viewModel.TryConnect(source, source.OutputPorts[0], target, target.InputPorts[0]);

        Assert.That(connected, Is.True);
        Assert.That(_viewModel.Edges, Has.Count.EqualTo(1));

        var duplicate = _viewModel.TryConnect(source, source.OutputPorts[0], target, target.InputPorts[0]);

        Assert.That(duplicate, Is.False);
        Assert.That(_viewModel.Edges, Has.Count.EqualTo(1));
        Assert.That(_viewModel.StatusText, Does.Contain("already exists"));
        Assert.That(
            _viewModel.LogMessages.Any(message => message.StartsWith("[Error]", StringComparison.Ordinal)),
            Is.True);
    }

    private sealed class FakeSettingsStore(string filePath, AppSettings settings) : ISettingsStore
    {
        public string FilePath { get; } = filePath;

        public AppSettings Current { get; private set; } = Clone(settings);

        public SettingsLoadResult Load() => new(Clone(Current), Array.Empty<string>());

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Current = Clone(settings);
            return Task.CompletedTask;
        }

        private static AppSettings Clone(AppSettings source) => new()
        {
            MaxCacheSizeMb = source.MaxCacheSizeMb,
            CacheDirectory = source.CacheDirectory,
            IsDarkTheme = source.IsDarkTheme,
            Language = source.Language,
            GridSnapSize = source.GridSnapSize,
            EnableGridSnap = source.EnableGridSnap,
            OpenAiApiKey = source.OpenAiApiKey,
            AnthropicApiKey = source.AnthropicApiKey,
            RecentFiles = source.RecentFiles.ToList(),
            LastRecipePath = source.LastRecipePath
        };
    }
}
