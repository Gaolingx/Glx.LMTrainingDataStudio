using System.Text.Json;
using System.Text.RegularExpressions;
using LMTrainingDataStudio2.Localization;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class LocalizationConsistencyTests
{
    private static readonly Regex PlaceholderPattern = new(@"\{(\d+)(?:[^}]*)\}", RegexOptions.Compiled);

    [Test]
    public void Dictionaries_HaveIdenticalKeySets()
    {
        var english = Load(LocalizationService.English, out var englishDuplicates);
        var chinese = Load(LocalizationService.Chinese, out var chineseDuplicates);

        Assert.That(englishDuplicates, Is.Empty, "duplicate en-US keys");
        Assert.That(chineseDuplicates, Is.Empty, "duplicate zh-CN keys");
        Assert.That(chinese.Keys, Is.EquivalentTo(english.Keys));
        Assert.That(english.Values, Is.All.Not.Empty, "empty en-US translations");
        Assert.That(chinese.Values, Is.All.Not.Empty, "empty zh-CN translations");
    }

    [Test]
    public void Dictionaries_UseMatchingPlaceholders()
    {
        var english = Load(LocalizationService.English, out _);
        var chinese = Load(LocalizationService.Chinese, out _);

        foreach (var (key, englishValue) in english)
        {
            var expected = Placeholders(englishValue);
            var actual = Placeholders(chinese[key]);
            Assert.That(actual, Is.EqualTo(expected), $"placeholders differ for '{key}'");
        }
    }

    [Test]
    public void StaticallyReferencedKeys_ExistInBothLanguages()
    {
        var english = Load(LocalizationService.English, out _);
        var chinese = Load(LocalizationService.Chinese, out _);
        var missing = new List<string>();

        foreach (var key in StaticKeys())
        {
            if (!english.ContainsKey(key)) missing.Add($"{key} (en-US)");
            if (!chinese.ContainsKey(key)) missing.Add($"{key} (zh-CN)");
        }

        Assert.That(missing, Is.Empty);
    }

    private static string[] Placeholders(string value) =>
        PlaceholderPattern.Matches(value)
            .Select(match => match.Groups[1].Value)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static Dictionary<string, string> Load(string language, out List<string> duplicates)
    {
        var assembly = typeof(LocalizationService).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith($".Localization.{language}.json", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var document = JsonDocument.Parse(stream);

        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        duplicates = [];
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (!entries.TryAdd(property.Name, property.Value.GetString() ?? string.Empty))
            {
                duplicates.Add(property.Name);
            }
        }

        return entries;
    }

    private static SortedSet<string> StaticKeys()
    {
        var project = TestRepository.PathOf("LMTrainingDataStudio2", "LMTrainingDataStudio2");
        var separator = Path.DirectorySeparatorChar;
        var keys = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var path in Directory.EnumerateFiles(project, "*.*", SearchOption.AllDirectories))
        {
            if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (path.Contains($"{separator}bin{separator}") || path.Contains($"{separator}obj{separator}"))
            {
                continue;
            }

            var text = File.ReadAllText(path);
            if (path.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase))
            {
                foreach (Match match in Regex.Matches(text, @"\{loc:Loc\s+([A-Za-z0-9_.]+)"))
                {
                    keys.Add(match.Groups[1].Value);
                }
            }
            else
            {
                foreach (Match match in Regex.Matches(text, @"LocalizationService\.L\(\s*""([A-Za-z0-9_.]+)"""))
                {
                    keys.Add(match.Groups[1].Value);
                }
            }
        }

        return keys;
    }
}
