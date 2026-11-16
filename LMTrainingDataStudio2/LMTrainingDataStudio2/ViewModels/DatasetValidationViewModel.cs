using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services;
using LMTrainingDataStudio2.Services.Datasets;

namespace LMTrainingDataStudio2.ViewModels;

/// <summary>
/// Dataset quality validation, issue navigation and auto-fix preview/write.
/// </summary>
public partial class DatasetValidationViewModel : ObservableObject
{
    private DatasetValidationService _service;
    private string _sourcePath = string.Empty;
    private DataFileType _fileType;
    private bool _hasSource;
    private CancellationTokenSource? _operation;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isCancellable;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _statusText = "Open a dataset to validate it.";

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private string _maxIssuesText = "1000";

    [ObservableProperty]
    private DatasetIssueViewModel? _selectedIssue;

    [ObservableProperty]
    private bool _issuesTruncated;

    [ObservableProperty]
    private bool _hasIssues;

    [ObservableProperty]
    private int _errorCount;

    [ObservableProperty]
    private int _warningCount;

    [ObservableProperty]
    private string _fixTargetPath = string.Empty;

    [ObservableProperty]
    private string _fixSummary = string.Empty;

    [ObservableProperty]
    private bool _hasFixPreview;

    public ObservableCollection<DatasetIssueViewModel> Issues { get; } = new();
    public ObservableCollection<DatasetFixSample> FixSamples { get; } = new();

    public event Action<long>? JumpRequested;

    public DatasetValidationViewModel(AppSettings settings)
    {
        _service = new DatasetValidationService(settings);
    }

    public void ApplySettings(AppSettings settings)
    {
        _service = new DatasetValidationService(settings);
    }

    public void SetSource(string path, DataFileType fileType)
    {
        _sourcePath = path;
        _fileType = fileType;
        _hasSource = true;
        Issues.Clear();
        FixSamples.Clear();
        HasIssues = false;
        HasFixPreview = false;
        IssuesTruncated = false;
        ErrorCount = 0;
        WarningCount = 0;
        Summary = string.Empty;
        FixSummary = string.Empty;
        StatusText = $"Ready to validate '{Path.GetFileName(path)}'.";
        FixTargetPath = SuggestFixPath(path);
    }

    public void RequestJump(long lineNumber) => JumpRequested?.Invoke(lineNumber);

    [RelayCommand]
    private async Task ValidateAsync()
    {
        if (!_hasSource)
        {
            StatusText = "Open a dataset first.";
            return;
        }

        if (!int.TryParse(MaxIssuesText, out var maxIssues) ||
            maxIssues < 1 || maxIssues > 100_000)
        {
            StatusText = "Max issues must be an integer between 1 and 100000.";
            return;
        }

        if (IsBusy) return;

        BeginOperation("Validating...");
        try
        {
            var progress = new Progress<double>(value => Progress = value);
            var report = await _service.ValidateAsync(
                _sourcePath, _fileType,
                new DatasetValidationOptions(maxIssues),
                progress,
                _operation!.Token);

            Issues.Clear();
            foreach (var issue in report.Issues)
            {
                Issues.Add(new DatasetIssueViewModel(issue));
            }

            ErrorCount = (int)Math.Min(report.ErrorCount, int.MaxValue);
            WarningCount = (int)Math.Min(report.WarningCount, int.MaxValue);
            HasIssues = report.TotalIssueCount > 0;
            IssuesTruncated = report.IssuesTruncated;

            var truncated = report.IssuesTruncated
                ? $" Showing the first {report.Issues.Count:N0}."
                : string.Empty;
            Summary =
                $"{report.TotalIssueCount:N0} issue(s) in {report.ScannedRows:N0} row(s) " +
                $"({report.ErrorCount:N0} error(s), {report.WarningCount:N0} warning(s)) " +
                $"in {report.Elapsed.TotalSeconds:F1}s.{truncated}";

            StatusText = report.IsValid
                ? "Validation complete: no errors."
                : "Validation complete with errors.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Validation cancelled.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or ArgumentException or DatasetFormatException)
        {
            StatusText = $"Validation failed: {ex.Message}";
        }
        finally
        {
            EndOperation();
        }
    }

    [RelayCommand]
    private async Task PreviewFixesAsync()
    {
        if (!EnsureJsonl("Auto-fix")) return;
        if (IsBusy) return;

        BeginOperation("Scanning fixable lines...");
        try
        {
            var progress = new Progress<double>(value => Progress = value);
            var preview = await _service.PreviewAutoFixAsync(
                _sourcePath, progress: progress, cancellationToken: _operation!.Token);

            FixSamples.Clear();
            foreach (var sample in preview.Samples)
            {
                FixSamples.Add(sample);
            }

            HasFixPreview = preview.InvalidRows > 0;
            var truncated = preview.SamplesTruncated
                ? $" Showing {preview.Samples.Count:N0}."
                : string.Empty;
            FixSummary =
                $"{preview.InvalidRows:N0} invalid row(s): {preview.FixableRows:N0} fixable, " +
                $"{preview.UnfixableRows:N0} need manual review.{truncated}";
            StatusText = preview.FixableRows > 0
                ? "Auto-fix preview ready."
                : "No automatically fixable lines found.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Auto-fix preview cancelled.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or ArgumentException)
        {
            StatusText = $"Auto-fix preview failed: {ex.Message}";
        }
        finally
        {
            EndOperation();
        }
    }

    [RelayCommand]
    private async Task WriteFixesAsync()
    {
        if (!EnsureJsonl("Auto-fix")) return;
        if (string.IsNullOrWhiteSpace(FixTargetPath))
        {
            StatusText = "Choose a target file for the fixed copy.";
            return;
        }

        if (string.Equals(
                Path.GetFullPath(FixTargetPath), Path.GetFullPath(_sourcePath),
                StringComparison.OrdinalIgnoreCase))
        {
            StatusText = "The fixed copy must use a different path from the source.";
            return;
        }

        if (!int.TryParse(MaxIssuesText, out var maxIssues))
        {
            maxIssues = 1000;
        }

        if (IsBusy) return;

        BeginOperation("Writing fixed copy...");
        try
        {
            var progress = new Progress<double>(value => Progress = value);
            var result = await _service.WriteAutoFixAsync(
                _sourcePath,
                FixTargetPath,
                new DatasetValidationOptions(maxIssues),
                progress,
                _operation!.Token);

            FixSummary =
                $"{result.FixedRows:N0} row(s) fixed, {result.UnfixableRows:N0} copied unchanged " +
                $"({result.ScannedRows:N0} scanned) in {result.Elapsed.TotalSeconds:F1}s.";
            StatusText = $"Fixed copy written to '{result.TargetPath}'.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Auto-fix cancelled; the target was not replaced.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or ArgumentException)
        {
            StatusText = $"Auto-fix failed: {ex.Message}";
        }
        finally
        {
            EndOperation();
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _operation?.Cancel();

    private bool CanCancel() => IsCancellable;

    private bool EnsureJsonl(string operation)
    {
        if (!_hasSource)
        {
            StatusText = "Open a dataset first.";
            return false;
        }

        if (_fileType != DataFileType.Jsonl)
        {
            StatusText = $"{operation} is only available for JSONL files.";
            return false;
        }

        return true;
    }

    private void BeginOperation(string status)
    {
        _operation = new CancellationTokenSource();
        IsBusy = true;
        IsCancellable = true;
        Progress = 0;
        StatusText = status;
    }

    private void EndOperation()
    {
        _operation?.Dispose();
        _operation = null;
        IsBusy = false;
        IsCancellable = false;
    }

    private static string SuggestFixPath(string path)
    {
        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        var fileName = $"{name}.fixed{extension}";
        return string.IsNullOrEmpty(directory) ? fileName : Path.Combine(directory, fileName);
    }
}

public sealed class DatasetIssueViewModel
{
    public DatasetIssueViewModel(ValidationIssue issue)
    {
        Issue = issue;
    }

    public ValidationIssue Issue { get; }
    public long? LineNumber => Issue.LineNumber;
    public string LineText => LineNumber?.ToString("N0") ?? "–";
    public string SeverityText => Issue.Severity.ToString();
    public bool IsError => Issue.Severity == ValidationSeverity.Error;
    public bool IsWarning => Issue.Severity == ValidationSeverity.Warning;
    public string CodeText => string.IsNullOrWhiteSpace(Issue.Code) ? "issue" : Issue.Code;
    public string PathText => string.IsNullOrWhiteSpace(Issue.Path) ? string.Empty : Issue.Path;
    public string Message => Issue.Message;
}