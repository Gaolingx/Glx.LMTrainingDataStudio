using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services.Datasets;
using LMTrainingDataStudio2.ViewModels;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class SftExportFieldSelectorTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "lmtds-field-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_directory, true); } catch { /* best effort */ }
    }

    [Test]
    public void Editor_PushesAppendFormatAndSplit()
    {
        var applied = new List<SftExportBlockConfig>();
        var editor = new SftExportConfigEditorViewModel(
            new SftExportBlockConfig("out/sft.jsonl"), applied.Add);

        editor.Append = true;
        editor.SelectedTargetFormat = editor.TargetFormats.Single(
            option => option.Format == DatasetFormat.Alpaca);
        editor.SplitMaxRows = 500;

        var config = applied[^1];
        Assert.That(config.Append, Is.True);
        Assert.That(config.TargetFormat, Is.EqualTo(DatasetFormat.Alpaca));
        Assert.That(config.SplitMaxRows, Is.EqualTo(500));
    }

    [Test]
    public void Editor_PushesRatioSplitsAndSeed()
    {
        var applied = new List<SftExportBlockConfig>();
        var editor = new SftExportConfigEditorViewModel(
            new SftExportBlockConfig("out/sft.jsonl"), applied.Add);

        editor.SplitsText = "train:0.8\nvalid:0.2";
        editor.SplitSeed = 7;

        var config = applied[^1];
        Assert.That(config.Splits, Is.EqualTo(new[] { "train:0.8", "valid:0.2" }));
        Assert.That(config.SplitSeed, Is.EqualTo(7));
        Assert.That(editor.SplitsError, Is.Null);
    }

    [Test]
    public void Editor_FlagsInvalidRatioSplits()
    {
        var editor = new SftExportConfigEditorViewModel(
            new SftExportBlockConfig("out/sft.jsonl"), _ => { });

        editor.SplitsText = "train:0";

        Assert.That(editor.SplitsError, Is.Not.Null);
        Assert.That(editor.SplitsError, Does.Contain("train"));
    }

    [Test]
    public void Editor_FlagsCountSplitInRatioMode()
    {
        var editor = new SftExportConfigEditorViewModel(
            new SftExportBlockConfig("out/sft.jsonl"), _ => { });

        editor.SplitsText = "train:100";

        Assert.That(editor.SplitsError, Is.Not.Null);
        Assert.That(editor.SplitsError, Does.Contain("name:ratio"));
    }

    [Test]
    public void Editor_InitializesSplitsFromConfig()
    {
        var editor = new SftExportConfigEditorViewModel(
            new SftExportBlockConfig(
                "out/sft.jsonl",
                Splits: new[] { "train:0.8", "valid:0.2" },
                SplitSeed: 9),
            _ => { });

        Assert.That(editor.SplitsText, Is.EqualTo("train:0.8\nvalid:0.2"));
        Assert.That(editor.SplitSeed, Is.EqualTo(9));
    }

    [Test]
    public void Editor_InitializesOptionsFromConfig()
    {
        var editor = new SftExportConfigEditorViewModel(
            new SftExportBlockConfig(
                "out/sft.jsonl",
                Fields: new[] { "messages" },
                Append: true,
                TargetFormat: DatasetFormat.ShareGPT,
                SplitMaxRows: 100),
            _ => { });

        Assert.That(editor.Append, Is.True);
        Assert.That(editor.SplitMaxRows, Is.EqualTo(100));
        Assert.That(editor.SelectedTargetFormat.Format, Is.EqualTo(DatasetFormat.ShareGPT));
        Assert.That(editor.TargetFormats.Select(option => option.Format), Is.EqualTo(new[]
        {
            DatasetFormat.Auto,
            DatasetFormat.OpenAI,
            DatasetFormat.ShareGPT,
            DatasetFormat.Alpaca
        }));
    }

    [Test]
    public void Editor_LoadFields_SelectsExistingWhitelist()
    {
        var applied = new List<SftExportBlockConfig>();
        var editor = new SftExportConfigEditorViewModel(
            new SftExportBlockConfig("out/sft.jsonl", Fields: new[] { "messages" }), applied.Add);

        editor.LoadFields(new[] { "messages", "tools", "metadata" });

        Assert.That(editor.AvailableFields.Select(option => option.Name),
            Is.EqualTo(new[] { "messages", "tools", "metadata" }));
        Assert.That(editor.AvailableFields.Single(option => option.Name == "messages").IsSelected, Is.True);
        Assert.That(editor.AvailableFields.Single(option => option.Name == "tools").IsSelected, Is.False);
    }

    [Test]
    public void Editor_TogglingOptionsUpdatesWhitelist()
    {
        var applied = new List<SftExportBlockConfig>();
        var editor = new SftExportConfigEditorViewModel(
            new SftExportBlockConfig("out/sft.jsonl"), applied.Add);
        editor.LoadFields(new[] { "messages", "tools" });

        editor.AvailableFields.Single(option => option.Name == "tools").IsSelected = true;
        Assert.That(editor.FieldsText, Is.EqualTo("tools"));
        Assert.That(applied[^1].Fields, Is.EqualTo(new[] { "tools" }));

        editor.AvailableFields.Single(option => option.Name == "messages").IsSelected = true;
        editor.AvailableFields.Single(option => option.Name == "tools").IsSelected = false;
        Assert.That(editor.FieldsText, Is.EqualTo("messages"));
        Assert.That(applied[^1].Fields, Is.EqualTo(new[] { "messages" }));
    }

    [Test]
    public void Editor_ManualTextField_SyncsOptionSelection()
    {
        var editor = new SftExportConfigEditorViewModel(
            new SftExportBlockConfig("out/sft.jsonl"), _ => { });
        editor.LoadFields(new[] { "messages", "tools" });

        editor.FieldsText = "tools, custom";

        Assert.That(editor.AvailableFields.Single(option => option.Name == "tools").IsSelected, Is.True);
        Assert.That(editor.AvailableFields.Single(option => option.Name == "messages").IsSelected, Is.False);
        Assert.That(editor.FieldsError, Is.Null);

        editor.AvailableFields.Single(option => option.Name == "messages").IsSelected = true;
        Assert.That(editor.FieldsText, Is.EqualTo("messages, tools, custom"));
    }

    [Test]
    public void Editor_SelectAllAndClear()
    {
        var applied = new List<SftExportBlockConfig>();
        var editor = new SftExportConfigEditorViewModel(
            new SftExportBlockConfig("out/sft.jsonl"), applied.Add);
        editor.LoadFields(new[] { "messages", "tools" });

        editor.SelectAllFieldsCommand.Execute(null);
        Assert.That(editor.FieldsText, Is.EqualTo("messages, tools"));
        Assert.That(applied[^1].Fields, Is.EqualTo(new[] { "messages", "tools" }));

        editor.ClearFieldsCommand.Execute(null);
        Assert.That(editor.FieldsText, Is.Empty);
        Assert.That(applied[^1].Fields, Is.Null);
    }

    [Test]
    public async Task Editor_LoadFieldsFromFile_ReadsJsonlAndCsv()
    {
        var jsonl = Path.Combine(_directory, "sample.jsonl");
        await File.WriteAllTextAsync(jsonl, """
            {"messages":[],"tools":[],"id":"x"}
            {"messages":[]}
            """);

        var csv = Path.Combine(_directory, "sample.csv");
        await File.WriteAllTextAsync(csv, "messages,tools,id\n[],,\"x\"\n");

        var editor = new SftExportConfigEditorViewModel(
            new SftExportBlockConfig("out/sft.jsonl"), _ => { });

        await editor.LoadFieldsFromFileAsync(jsonl);
        Assert.That(editor.AvailableFields.Select(option => option.Name),
            Is.EqualTo(new[] { "messages", "tools", "id" }));
        Assert.That(editor.FieldsSourceError, Is.Null);
        Assert.That(editor.FieldsSourceInfo, Is.Not.Null.And.Not.Empty);

        await editor.LoadFieldsFromFileAsync(csv);
        Assert.That(editor.AvailableFields.Select(option => option.Name),
            Is.EqualTo(new[] { "messages", "tools", "id" }));
    }

    [Test]
    public async Task Discovery_ReturnsEmptyForEmptyOrScalarSamples()
    {
        var empty = Path.Combine(_directory, "empty.jsonl");
        await File.WriteAllTextAsync(empty, string.Empty);

        var scalar = Path.Combine(_directory, "scalar.jsonl");
        await File.WriteAllTextAsync(scalar, "42\n");

        Assert.That(await DatasetFieldDiscovery.ReadTopLevelFieldsAsync(empty), Is.Empty);
        Assert.That(await DatasetFieldDiscovery.ReadTopLevelFieldsAsync(scalar), Is.Empty);
    }

    [Test]
    public async Task Editor_LoadFieldsFromMissingFile_ReportsError()
    {
        var editor = new SftExportConfigEditorViewModel(
            new SftExportBlockConfig("out/sft.jsonl"), _ => { });

        await editor.LoadFieldsFromFileAsync(Path.Combine(_directory, "missing.jsonl"));

        Assert.That(editor.FieldsSourceError, Is.Not.Null.And.Not.Empty);
        Assert.That(editor.AvailableFields, Is.Empty);
    }
}
