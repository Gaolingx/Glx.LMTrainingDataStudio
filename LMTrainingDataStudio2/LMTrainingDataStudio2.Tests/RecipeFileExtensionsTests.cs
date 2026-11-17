using LMTrainingDataStudio2.Services;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class RecipeFileExtensionsTests
{
    [TestCase("recipe.json", true)]
    [TestCase("recipe.JSON", true)]
    [TestCase("recipe.recipe", true)]
    [TestCase("recipe.RECIPE", true)]
    [TestCase("recipe.recipe.json", true)]
    [TestCase("recipe.jsonl", false)]
    [TestCase("recipe.txt", false)]
    [TestCase("recipe", false)]
    [TestCase("", false)]
    [TestCase(null, false)]
    public void IsRecipePath_RecognizesSupportedExtensions(string? path, bool expected)
    {
        Assert.That(RecipeFileExtensions.IsRecipePath(path), Is.EqualTo(expected));
    }

    [TestCase("out.recipe", "out.recipe")]
    [TestCase("out.json", "out.json")]
    [TestCase("out.recipe.json", "out.recipe.json")]
    [TestCase("out", "out.json")]
    [TestCase(" out ", "out.json")]
    public void EnsureRecipeExtension_PreservesOrAddsJson(string path, string expected)
    {
        Assert.That(RecipeFileExtensions.EnsureRecipeExtension(path), Is.EqualTo(expected));
    }

    [Test]
    public void GetSuggestedFileName_KeepsExplicitExtensionAndSanitizes()
    {
        Assert.That(RecipeFileExtensions.GetSuggestedFileName("sample.recipe"), Is.EqualTo("sample.recipe"));
        Assert.That(RecipeFileExtensions.GetSuggestedFileName("sample"), Is.EqualTo("sample.json"));
        Assert.That(RecipeFileExtensions.GetSuggestedFileName("a/b"), Is.EqualTo("a_b.json"));
        Assert.That(RecipeFileExtensions.GetSuggestedFileName("  "), Is.EqualTo("recipe.json"));
    }

    [Test]
    public void Patterns_IncludeJsonAndRecipe()
    {
        Assert.That(RecipeFileExtensions.Patterns, Is.EquivalentTo(new[] { "*.json", "*.recipe" }));
    }
}
