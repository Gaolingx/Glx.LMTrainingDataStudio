using LMTrainingDataStudio2.Services;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class DatasetValidationServiceTests
{
    private readonly DatasetValidationService _service = new();
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
    public async Task ValidateJsonlAsync_ValidFile_IsValid()
    {
        var path = WriteFile("valid.jsonl", """{"a":1}""", """{"b":2}""");

        var result = await _service.ValidateJsonlAsync(path);

        Assert.That(result.IsValid, Is.True);
        Assert.That(result.Issues, Is.Empty);
    }

    [Test]
    public async Task ValidateJsonlAsync_InvalidLine_ReportsLineNumberAndPath()
    {
        var path = WriteFile("invalid.jsonl", """{"a":1}""", """{bad json}""", """{"c":3}""");

        var result = await _service.ValidateJsonlAsync(path);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Issues, Has.Count.EqualTo(1));
        Assert.That(result.Issues[0].LineNumber, Is.EqualTo(2));
        Assert.That(result.Issues[0].FilePath, Is.EqualTo(path));
    }

    [Test]
    public async Task ValidateJsonlAsync_MissingFile_ReturnsError()
    {
        var result = await _service.ValidateJsonlAsync(Path.Combine(_directory, "missing.jsonl"));

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Issues, Has.Count.EqualTo(1));
        Assert.That(result.Issues[0].Message, Does.Contain("File not found"));
    }

    [TestCase("""{"a":1""", """{"a":1}""")]
    [TestCase("""[1,2""", """[1,2]""")]
    [TestCase("""{"a":1,}""", """{"a":1}""")]
    public void TryAutoFix_RepairsSimpleSyntaxErrors(string input, string expected)
    {
        Assert.That(_service.TryAutoFix(input), Is.EqualTo(expected));
    }

    [Test]
    public void TryAutoFix_UnfixableInput_ReturnsNull()
    {
        Assert.That(_service.TryAutoFix("not json"), Is.Null);
        Assert.That(_service.TryAutoFix("   "), Is.Null);
    }

    private string WriteFile(string name, params string[] lines)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllLines(path, lines);
        return path;
    }
}
