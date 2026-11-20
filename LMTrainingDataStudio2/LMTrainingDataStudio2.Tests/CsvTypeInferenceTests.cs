using LMTrainingDataStudio2.Services.Datasets;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class CsvTypeInferenceTests
{
    [TestCase(new[] { "1", "2", "-3" }, "integer")]
    [TestCase(new[] { "1.5", "-2e3" }, "number")]
    [TestCase(new[] { "true", "FALSE" }, "bool")]
    [TestCase(new[] { "2024-01-02", "2024/03/04" }, "date")]
    [TestCase(new[] { "{\"a\":1}", "[1,2]" }, "json")]
    [TestCase(new[] { "1", "x" }, "string")]
    [TestCase(new[] { "", "   " }, "empty")]
    [TestCase(new[] { "007", "1" }, "integer")]
    public void Infer_ReturnsExpectedType(string[] values, string expected)
    {
        Assert.That(CsvTypeInference.Infer(values), Is.EqualTo(expected));
    }

    [Test]
    public void Infer_IgnoresEmptyValuesWhenOtherValuesShareType()
    {
        Assert.That(CsvTypeInference.Infer(new[] { "1", "", "3" }), Is.EqualTo("integer"));
    }

    [Test]
    public void Infer_SamplesOnlyBoundedPrefix()
    {
        var values = Enumerable.Range(0, CsvTypeInference.MaxSamples)
            .Select(_ => "1")
            .Append("not-a-number")
            .ToArray();

        Assert.That(CsvTypeInference.Infer(values), Is.EqualTo("integer"));
    }
}
