using LMTrainingDataStudio2.Localization;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
[NonParallelizable]
public sealed class LocalizationTests
{
    [TearDown]
    public void TearDown() => LocalizationService.Instance.SetLanguage(LocalizationService.English);

    [Test]
    public void SetLanguage_UpdatesStringsAndVersion()
    {
        var service = LocalizationService.Instance;
        var version = service.Version;

        service.SetLanguage(LocalizationService.Chinese);
        Assert.That(service.CurrentLanguage, Is.EqualTo(LocalizationService.Chinese));
        Assert.That(service["node.seed.name"], Is.EqualTo("Seed 数据源"));
        Assert.That(service.Version, Is.GreaterThan(version));

        var chineseVersion = service.Version;
        service.SetLanguage(LocalizationService.English);
        Assert.That(service.CurrentLanguage, Is.EqualTo(LocalizationService.English));
        Assert.That(service["node.seed.name"], Is.EqualTo("Seed Source"));
        Assert.That(service.Version, Is.GreaterThan(chineseVersion));
    }

    [Test]
    public void MissingKey_ReturnsKey()
    {
        Assert.That(LocalizationService.L("missing.key.for.test"), Is.EqualTo("missing.key.for.test"));
    }

    [Test]
    public void ResolveLanguage_MapsCulturesAndSystem()
    {
        Assert.That(LocalizationService.ResolveLanguage("zh-CN"), Is.EqualTo(LocalizationService.Chinese));
        Assert.That(LocalizationService.ResolveLanguage("zh-Hans"), Is.EqualTo(LocalizationService.Chinese));
        Assert.That(LocalizationService.ResolveLanguage("en-GB"), Is.EqualTo(LocalizationService.English));
        Assert.That(
            LocalizationService.ResolveLanguage("system"),
            Is.AnyOf(LocalizationService.English, LocalizationService.Chinese));
    }

    [Test]
    public void BlockCatalog_AllNodesHaveLocalizedCopy()
    {
        foreach (var language in new[] { LocalizationService.English, LocalizationService.Chinese })
        {
            LocalizationService.Instance.SetLanguage(language);
            foreach (var type in BlockCatalog.Types)
            {
                var definition = BlockCatalog.Get(type);
                Assert.That(LocalizationService.L(definition.NameKey), Is.Not.EqualTo(definition.NameKey), type.ToString());
                Assert.That(LocalizationService.L(definition.SummaryKey), Is.Not.EqualTo(definition.SummaryKey), type.ToString());
                Assert.That(LocalizationService.L(definition.HelpKey), Is.Not.EqualTo(definition.HelpKey), type.ToString());
                Assert.That(BlockCatalog.GetName(type), Is.Not.Empty);
                Assert.That(BlockCatalog.GetSummary(type), Is.Not.Empty);
                Assert.That(BlockCatalog.GetHelp(type), Does.Contain("\n"));
                Assert.That(BlockCatalog.DescribePorts(type), Is.Not.Empty);
            }
        }
    }

    [Test]
    public void BlockCatalog_PortSummaryMatchesPortContract()
    {
        LocalizationService.Instance.SetLanguage(LocalizationService.English);

        Assert.That(BlockCatalog.DescribePorts(BlockType.Seed), Does.Contain("none"));
        Assert.That(BlockCatalog.DescribePorts(BlockType.Seed), Does.Contain("rows"));
        Assert.That(BlockCatalog.DescribePorts(BlockType.ToolProfile), Does.Contain("none"));
        Assert.That(BlockCatalog.DescribePorts(BlockType.SftExport), Does.Contain("input"));
    }

    [Test]
    public void Presets_AllHaveLocalizedNamesAndSummaries()
    {
        foreach (var language in new[] { LocalizationService.English, LocalizationService.Chinese })
        {
            LocalizationService.Instance.SetLanguage(language);
            foreach (var preset in RecipePresetCatalog.All)
            {
                Assert.That(LocalizationService.L(preset.NameKey), Is.Not.EqualTo(preset.NameKey));
                Assert.That(LocalizationService.L(preset.SummaryKey), Is.Not.EqualTo(preset.SummaryKey));
            }
        }
    }
}
