using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Services;

/// <summary>
/// Limits for dataset quality checks. <see cref="MaxIssues"/> caps stored issues;
/// issue totals keep counting after the cap is reached.
/// </summary>
public sealed record DatasetValidationOptions(
    int MaxIssues = 1000,
    int MaxLineBytes = 2 * 1024 * 1024)
{
    public int EffectiveMaxIssues => Math.Max(1, MaxIssues);

    public int EffectiveMaxLineBytes => Math.Clamp(MaxLineBytes, 64 * 1024, 256 * 1024 * 1024);
}

public sealed class DatasetValidationReport
{
    public long ScannedRows { get; internal set; }
    public long ErrorCount { get; internal set; }
    public long WarningCount { get; internal set; }
    public long InfoCount { get; internal set; }
    public long TotalIssueCount => ErrorCount + WarningCount + InfoCount;
    public bool IssuesTruncated { get; internal set; }
    public List<ValidationIssue> Issues { get; } = new();
    public TimeSpan Elapsed { get; internal set; }

    public bool IsValid => ErrorCount == 0;
}

public sealed record DatasetFixSample(
    long LineNumber,
    string Original,
    string Fixed);

public sealed class DatasetAutoFixPreview
{
    public long ScannedRows { get; internal set; }
    public long InvalidRows { get; internal set; }
    public long FixableRows { get; internal set; }
    public long UnfixableRows { get; internal set; }
    public bool SamplesTruncated { get; internal set; }
    public List<DatasetFixSample> Samples { get; } = new();
    public TimeSpan Elapsed { get; internal set; }
}

public sealed record DatasetAutoFixResult(
    long ScannedRows,
    long FixedRows,
    long UnfixableRows,
    string TargetPath,
    TimeSpan Elapsed);
