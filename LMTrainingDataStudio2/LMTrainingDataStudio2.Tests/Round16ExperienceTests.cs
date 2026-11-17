using System.IO.Compression;
using System.Text.RegularExpressions;
using Avalonia;
using LMTrainingDataStudio2.Commands;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services;
using LMTrainingDataStudio2.ViewModels;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
[NonParallelizable]
public sealed class Round16ExperienceTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "lmtds-round16", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_directory, true); } catch { /* best effort */ }
    }

    [Test]
    public void ConfigEditorEdits_AreMergedIntoOneUndoEntry()
    {
        var vm = CreateViewModel();
        vm.AddBlockCommand.Execute(BlockType.Seed);
        var block = vm.SelectedBlock!;
        var original = ((SeedBlockConfig)block.Config).SourcePath;

        block.SeedEditor!.SourcePath = Path.Combine(_directory, "a.jsonl");
        block.SeedEditor.SourcePath = Path.Combine(_directory, "b.jsonl");

        Assert.That(vm.CanUndo, Is.True);
        Assert.That(vm.UndoDescription, Does.Contain("Seed"));
        Assert.That(((SeedBlockConfig)block.Config).SourcePath, Does.EndWith("b.jsonl"));

        vm.CommandHistory.Undo();
        Assert.That(((SeedBlockConfig)block.Config).SourcePath, Is.EqualTo(original));

        vm.CommandHistory.Redo();
        Assert.That(((SeedBlockConfig)block.Config).SourcePath, Does.EndWith("b.jsonl"));
    }

    [Test]
    public void ResizeBlocksCommand_RestoresSize()
    {
        var block = CreateBlock();
        var command = new ResizeBlocksCommand(new[] { (block, 200d, 80d, 320d, 140d) });

        command.Execute();
        Assert.That(block.Width, Is.EqualTo(320));
        Assert.That(block.Height, Is.EqualTo(140));

        command.Undo();
        Assert.That(block.Width, Is.EqualTo(200));
        Assert.That(block.Height, Is.EqualTo(80));
    }

    [Test]
    public void RenameBlockCommand_DisplayName_UsesExplicitOriginal()
    {
        var block = CreateBlock();
        block.DisplayName = "After edit";

        var command = new RenameBlockCommand(block, "Before edit", "After edit", isDisplayName: true);
        command.Undo();
        Assert.That(block.DisplayName, Is.EqualTo("Before edit"));

        command.Execute();
        Assert.That(block.DisplayName, Is.EqualTo("After edit"));
    }

    [Test]
    public void EdgeLabelLayout_AvoidsNodesAndOtherLabels()
    {
        var start = new Point(0, 100);
        var end = new Point(400, 100);
        var source = new Rect(0, 60, 160, 80);
        var target = new Rect(240, 60, 160, 80);

        var first = EdgeLabelLayout.FindBounds(start, end, 5, source, target, Array.Empty<Rect>());
        Assert.That(source.Intersects(first), Is.False);
        Assert.That(target.Intersects(first), Is.False);

        var second = EdgeLabelLayout.FindBounds(start, end, 5, source, target, new[] { first });
        Assert.That(second.Intersects(first), Is.False);
        Assert.That(source.Intersects(second), Is.False);
        Assert.That(target.Intersects(second), Is.False);
    }

    [Test]
    public void CrashReport_ContainsSummaryAndRedactedSettings()
    {
        AppLog.Initialize(Path.Combine(_directory, "logs"));
        AppLog.Info("diagnostics test entry");

        var path = Path.Combine(_directory, "diagnostics.zip");
        CrashReportService.CreateReport(path, "unit test", new InvalidOperationException("boom"));

        Assert.That(File.Exists(path), Is.True);
        using var archive = ZipFile.OpenRead(path);
        Assert.That(archive.GetEntry("summary.txt"), Is.Not.Null);
        Assert.That(archive.GetEntry("settings.redacted.json"), Is.Not.Null);
        Assert.That(archive.GetEntry("logs/app.log"), Is.Not.Null);

        var settings = new AppSettings { OpenAiApiKey = "sk-secret", AnthropicApiKey = "sk-ant-secret" };
        CrashReportService.RedactSettingsForDiagnostics(settings);
        Assert.That(settings.OpenAiApiKey, Is.Null);
        Assert.That(settings.AnthropicApiKey, Is.Null);
    }

    [Test]
    public void RefreshMemoryUsage_UpdatesStatusText()
    {
        var vm = CreateViewModel();
        vm.RefreshMemoryUsage();
        Assert.That(vm.MemoryUsage, Does.Match(@"^\d+(\.\d+)? MB$"));
    }

    [Test]
    public void ViewXaml_ContainsNoHardcodedColorLiterals()
    {
        var views = Path.Combine(GetRepositoryRoot(), "LMTrainingDataStudio2", "LMTrainingDataStudio2", "Views");
        var pattern = new Regex("#[0-9A-Fa-f]{6,8}");

        var offenders = Directory.GetFiles(views, "*.axaml")
            .SelectMany(file => File.ReadLines(file)
                .Select((line, index) => (File: Path.GetFileName(file), Line: index + 1, Text: line)))
            .Where(entry => pattern.IsMatch(entry.Text))
            .Select(entry => $"{entry.File}:{entry.Line}: {entry.Text.Trim()}")
            .ToArray();

        Assert.That(offenders, Is.Empty, string.Join(Environment.NewLine, offenders));
    }

    [Test]
    public void ThemeDictionaries_ProvideSemanticBrushesInBothVariants()
    {
        string[] keys =
        [
            "CanvasBackgroundBrush", "PanelBackgroundBrush", "PanelBorderBrush",
            "NodeBackgroundBrush", "NodeBorderBrush", "NodeBorderSelectedBrush",
            "TextPrimaryBrush", "TextBrightBrush", "TextMutedBrush", "TextDimBrush",
            "AccentBrush", "AccentForegroundBrush", "SuccessBrush", "WarningBrush", "DangerBrush",
            "InputBackgroundBrush", "ButtonSurfaceBrush", "ButtonSurfaceTextBrush",
            "EdgeDefaultBrush", "EdgeHoverBrush", "EdgeLabelBackgroundBrush",
            "MiniMapEdgeBrush", "ResizeGripBrush", "SelectionFillBrush"
        ];

        var colorsPath = Path.Combine(
            GetRepositoryRoot(), "LMTrainingDataStudio2", "LMTrainingDataStudio2", "Themes", "Colors.axaml");
        var document = System.Xml.Linq.XDocument.Load(colorsPath);
        var xNamespace = System.Xml.Linq.XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

        foreach (var variant in new[] { "Dark", "Light" })
        {
            var dictionary = document.Descendants()
                .Single(element => element.Name.LocalName == "ResourceDictionary" &&
                                   (string?)element.Attribute(xNamespace + "Key") == variant);
            var available = dictionary.Elements()
                .Select(element => (string?)element.Attribute(xNamespace + "Key"))
                .Where(key => key is not null)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var key in keys)
            {
                Assert.That(available, Does.Contain(key), $"Missing resource '{key}' in the {variant} dictionary.");
            }
        }
    }

    private MainWindowViewModel CreateViewModel() =>
        new(new SettingsStore(Path.Combine(_directory, "settings.json")));

    private static BlockNodeViewModel CreateBlock() => new()
    {
        Type = BlockType.Seed,
        Name = "seed",
        DisplayName = "Seed Source",
        Config = new SeedBlockConfig(string.Empty, DataFileType.Jsonl, Streaming: false, UseIndexCache: true)
    };

    private static string GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "LMTrainingDataStudio2.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.Parent?.FullName ?? Directory.GetCurrentDirectory();
    }
}
