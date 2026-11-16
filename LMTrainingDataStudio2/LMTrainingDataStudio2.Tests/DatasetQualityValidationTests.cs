using System.Text;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class DatasetQualityValidationTests
{
    private DatasetValidationService _service = null!;
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _service = new DatasetValidationService();
        _directory = Path.Combine(Path.GetTempPath(), "lmtds-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_directory, true); } catch { /* best effort */ }
    }

    [Test]
    public async Task ValidateAsync_ReportsSyntaxShapeAndFormatIssuesWithCodesAndPaths()
    {
        var path = Path.Combine(_directory, "mixed.jsonl");
        File.WriteAllLines(path, new[]
        {
            """{"messages":[{"role":"user","content":"hi"}]}""",
            "{broken",
            "[1,2]",
            "",
            """{"messages":[{"content":"no role"}]}""",
            """{"custom":"x"}"""
        });

        var report = await _service.ValidateAsync(path, DataFileType.Jsonl);

        Assert.That(report.ScannedRows, Is.EqualTo(6));
        Assert.That(report.Issues.Select(issue => issue.Code), Does.Contain("json-syntax"));
        Assert.That(report.Issues.Select(issue => issue.Code), Does.Contain("not-object"));
        Assert.That(report.Issues.Select(issue => issue.Code), Does.Contain("empty-line"));
        Assert.That(report.Issues.Select(issue => issue.Code), Does.Contain("missing-role"));
        Assert.That(report.Issues.Select(issue => issue.Code), Does.Contain("unrecognized-format"));

        var syntax = report.Issues.Single(issue => issue.Code == "json-syntax");
        Assert.That(syntax.LineNumber, Is.EqualTo(2));
        Assert.That(syntax.Severity, Is.EqualTo(ValidationSeverity.Error));

        var missingRole = report.Issues.Single(issue => issue.Code == "missing-role");
        Assert.That(missingRole.Path, Is.EqualTo("$.messages[0].role"));
        Assert.That(missingRole.LineNumber, Is.EqualTo(5));

        Assert.That(report.ErrorCount, Is.EqualTo(3));
        Assert.That(report.WarningCount, Is.EqualTo(2));
        Assert.That(report.IsValid, Is.False);
    }

    [Test]
    public async Task ValidateAsync_ReportsInvalidUtf8AndOversizedLines()
    {
        var path = Path.Combine(_directory, "bad-bytes.jsonl");
        var bytes = new List<byte>();
        bytes.AddRange(Encoding.UTF8.GetBytes("""{"text":"ok"}""" + "\n"));
        bytes.AddRange(Encoding.UTF8.GetBytes("""{"text":"bad-"""));
        bytes.Add(0xFF);
        bytes.Add(0xFE);
        bytes.AddRange(Encoding.UTF8.GetBytes("\"}\n"));
        bytes.AddRange(Encoding.UTF8.GetBytes(
            "{\"text\":\"" + new string('x', 70 * 1024) + "\"}\n"));
        await File.WriteAllBytesAsync(path, bytes.ToArray());

        var report = await _service.ValidateAsync(
            path, DataFileType.Jsonl, new DatasetValidationOptions(MaxLineBytes: 64 * 1024));

        Assert.That(report.ScannedRows, Is.EqualTo(3));
        Assert.That(report.Issues.Any(issue => issue.Code == "invalid-utf8" && issue.LineNumber == 2),
            Is.True);
        Assert.That(report.Issues.Any(issue => issue.Code == "line-too-long" && issue.LineNumber == 3),
            Is.True);
    }

    [Test]
    public async Task ValidateAsync_IssueLimitKeepsCountingTotals()
    {
        var path = Path.Combine(_directory, "many-bad.jsonl");
        await File.WriteAllLinesAsync(path, Enumerable.Repeat("{broken", 20));

        var report = await _service.ValidateAsync(
            path, DataFileType.Jsonl, new DatasetValidationOptions(MaxIssues: 5));

        Assert.That(report.Issues, Has.Count.EqualTo(5));
        Assert.That(report.IssuesTruncated, Is.True);
        Assert.That(report.ErrorCount, Is.EqualTo(20));
        Assert.That(report.ScannedRows, Is.EqualTo(20));
    }

    [Test]
    public async Task ValidateAsync_ChatFormatsReportStructuralWarnings()
    {
        var path = Path.Combine(_directory, "formats.jsonl");
        File.WriteAllLines(path, new[]
        {
            """{"conversations":[{"from":"human"}]}""",
            """{"instruction":"do it"}""",
            """{"prompt":"p","chosen":"c","rejected":""}""",
            """{"text":"   "}""",
            """{"messages":[{"role":"user","content":"hi"}],"tools":"bad"}"""
        });

        var report = await _service.ValidateAsync(path, DataFileType.Jsonl);

        Assert.That(report.Issues.Any(issue =>
            issue.Code == "empty-content" && issue.Path == "$.conversations[0].value"), Is.True);
        Assert.That(report.Issues.Any(issue =>
            issue.Code == "empty-output" && issue.Path == "$.output"), Is.True);
        Assert.That(report.Issues.Any(issue =>
            issue.Code == "empty-rejected" && issue.Path == "$.rejected"), Is.True);
        Assert.That(report.Issues.Any(issue =>
            issue.Code == "empty-text" && issue.Path == "$.text"), Is.True);
        Assert.That(report.Issues.Any(issue =>
            issue.Code == "invalid-tools" && issue.Path == "$.tools"), Is.True);
        Assert.That(report.IsValid, Is.True);
    }

    [Test]
    public async Task PreviewAutoFix_CountsFixableAndUnfixableRows()
    {
        var path = Path.Combine(_directory, "preview.jsonl");
        await File.WriteAllLinesAsync(path, new[]
        {
            """{"a":1""",
            "not json",
            """{"b":2}"""
        });

        var preview = await _service.PreviewAutoFixAsync(path);

        Assert.That(preview.ScannedRows, Is.EqualTo(3));
        Assert.That(preview.InvalidRows, Is.EqualTo(2));
        Assert.That(preview.FixableRows, Is.EqualTo(1));
        Assert.That(preview.UnfixableRows, Is.EqualTo(1));
        Assert.That(preview.Samples, Has.Count.EqualTo(1));
        Assert.That(preview.Samples[0].LineNumber, Is.EqualTo(1));
        Assert.That(preview.Samples[0].Fixed, Is.EqualTo("""{"a":1}"""));
    }

    [Test]
    public async Task WriteAutoFix_WritesNewFileAndLeavesSourceUnchanged()
    {
        var source = Path.Combine(_directory, "source.jsonl");
        var target = Path.Combine(_directory, "fixed.jsonl");
        var originalBytes = Encoding.UTF8.GetBytes(
            "{\"a\":1\nnot json\n{\"b\":2}\n");
        await File.WriteAllBytesAsync(source, originalBytes);

        var result = await _service.WriteAutoFixAsync(source, target);

        Assert.That(result.FixedRows, Is.EqualTo(1));
        Assert.That(result.UnfixableRows, Is.EqualTo(1));
        Assert.That(result.ScannedRows, Is.EqualTo(3));
        Assert.That(await File.ReadAllBytesAsync(source), Is.EqualTo(originalBytes));

        var lines = await File.ReadAllLinesAsync(target);
        Assert.That(lines, Has.Length.EqualTo(3));
        Assert.That(lines[0], Is.EqualTo("""{"a":1}"""));
        Assert.That(lines[1], Is.EqualTo("not json"));
        Assert.That(lines[2], Is.EqualTo("""{"b":2}"""));
    }

    [Test]
    public async Task WriteAutoFix_PreservesInvalidUtf8BytesAndHandlesOversizedLine()
    {
        var source = Path.Combine(_directory, "bytes.jsonl");
        var target = Path.Combine(_directory, "bytes.fixed.jsonl");
        var bytes = new List<byte>();
        bytes.AddRange(Encoding.UTF8.GetBytes("{\"a\":1\n"));
        bytes.AddRange(Encoding.UTF8.GetBytes("{\"text\":\"bad-"));
        bytes.Add(0xFF);
        bytes.AddRange(Encoding.UTF8.GetBytes("\"}\n"));
        bytes.AddRange(Encoding.UTF8.GetBytes(
            "{\"text\":\"" + new string('y', 70 * 1024) + "\"}\n"));
        await File.WriteAllBytesAsync(source, bytes.ToArray());

        var result = await _service.WriteAutoFixAsync(
            source, target, new DatasetValidationOptions(MaxLineBytes: 64 * 1024));

        Assert.That(result.FixedRows, Is.EqualTo(1));
        Assert.That(result.UnfixableRows, Is.EqualTo(2));
        var targetBytes = await File.ReadAllBytesAsync(target);
        Assert.That(targetBytes, Does.Contain((byte)0xFF));
        Assert.That(Encoding.UTF8.GetString(targetBytes), Does.Contain("""{"a":1}"""));
    }

    [Test]
    public void WriteAutoFix_SamePath_Throws()
    {
        var path = Path.Combine(_directory, "same.jsonl");
        File.WriteAllText(path, "{}\n");

        Assert.ThrowsAsync<ArgumentException>(() => _service.WriteAutoFixAsync(path, path));
    }

    [Test]
    public async Task ValidateAsync_BomFile_IsValid()
    {
        var path = Path.Combine(_directory, "bom.jsonl");
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }
            .Concat(Encoding.UTF8.GetBytes("""{"messages":[{"role":"user","content":"hi"}]}""" + "\n"))
            .ToArray();
        await File.WriteAllBytesAsync(path, bytes);

        var report = await _service.ValidateAsync(path, DataFileType.Jsonl);

        Assert.That(report.ErrorCount, Is.Zero);
    }
}