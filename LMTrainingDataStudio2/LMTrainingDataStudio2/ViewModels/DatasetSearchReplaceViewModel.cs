using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services.Datasets;

namespace LMTrainingDataStudio2.ViewModels;

/// <summary>
/// Literal/regex search with match preview, scoped to the current page or the whole file,
/// and replacement into a new target file.
/// </summary>
public partial class DatasetSearchReplaceViewModel : ObservableObject
{
    private DatasetSearchReplaceService _service;
    private string _sourcePath = string.Empty;
    private DataFileType _fileType;
    private bool _hasSource;
    private long _pageStartRow;
    private int _pageRowCount;
    private CancellationTokenSource? _operation;

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private string _replacement = string.Empty;

    [ObservableProperty]
    private bool _useRegex;

    [ObservableProperty]
    private bool _caseSensitive;

    [ObservableProperty]
    private bool _currentPageOnly;

    [ObservableProperty]
    private string _targetPath = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isCancellable;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _statusText = "Open a dataset to search it.";

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private bool _hasResults;

    [ObservableProperty]
    private DatasetSearchMatchViewModel? _selectedMatch;

    public ObservableCollection<DatasetSearchMatchViewModel> Matches { get; } = new();

    public event Action<long>? JumpRequested;

    public DatasetSearchReplaceViewModel(AppSettings settings)
    {
        _service = new DatasetSearchReplaceService(settings);
    }

    public void ApplySettings(AppSettings settings)
    {
        _service = new DatasetSearchReplaceService(settings);
    }

    public void SetSource(string path, DataFileType fileType)
    {
        _sourcePath = path;
        _fileType = fileType;
        _hasSource = true;
        Matches.Clear();
        HasResults = false;
        Summary = string.Empty;
        StatusText = $"Ready to search '{Path.GetFileName(path)}'.";
        SetSuggestedTargetPath(path);
    }

    public void SetPageWindow(long startRow, int rowCount)
    {
        _pageStartRow = startRow;
        _pageRowCount = rowCount;
    }

    public void RequestJump(long lineNumber) => JumpRequested?.Invoke(lineNumber);

    [RelayCommand]
    private async Task PreviewAsync()
    {
        if (!TryBuildRequest(out var request, out var error))
        {
            StatusText = error;
            return;
        }

        if (IsBusy) return;

        BeginOperation("Searching...");
        try
        {
            var progress = new Progress<double>(value => Progress = value);
            var preview = await _service.PreviewAsync(
                _sourcePath, _fileType, request, maxMatches: 200,
                progress: progress, cancellationToken: _operation!.Token);

            Matches.Clear();
            foreach (var match in preview.Matches)
            {
                Matches.Add(new DatasetSearchMatchViewModel(match));
            }

            HasResults = preview.MatchingRows > 0;
            var truncated = preview.Truncated ? " Showing the first 200 matches." : string.Empty;
            Summary =
                $"{preview.MatchingRows:N0} matching row(s), {preview.TotalMatches:N0} match(es) " +
                $"in {preview.ScannedRows:N0} scanned row(s) " +
                $"({preview.Elapsed.TotalSeconds:F1}s).{truncated}";
            StatusText = preview.MatchingRows > 0
                ? "Search complete."
                : "No matches found.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Search cancelled.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or ArgumentException or DatasetFormatException)
        {
            StatusText = $"Search failed: {ex.Message}";
        }
        finally
        {
            EndOperation();
        }
    }

    [RelayCommand]
    private async Task ReplaceAsync()
    {
        if (!TryBuildRequest(out var request, out var error))
        {
            StatusText = error;
            return;
        }

        if (string.IsNullOrWhiteSpace(TargetPath))
        {
            StatusText = "Choose a target file for the replaced dataset.";
            return;
        }

        if (IsBusy) return;

        BeginOperation("Replacing...");
        try
        {
            var progress = new Progress<double>(value => Progress = value);
            var result = await _service.ReplaceAsync(
                _sourcePath, _fileType, TargetPath, request,
                progress, _operation!.Token);

            Summary =
                $"{result.ChangedRows:N0} row(s) changed, {result.TotalMatches:N0} match(es) " +
                $"in {result.ScannedRows:N0} row(s) ({result.Elapsed.TotalSeconds:F1}s).";
            StatusText = $"Replacement written to '{result.TargetPath}'.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Replacement cancelled; the target was not replaced.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or ArgumentException or DatasetFormatException)
        {
            StatusText = $"Replacement failed: {ex.Message}";
        }
        finally
        {
            EndOperation();
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _operation?.Cancel();

    private bool CanCancel() => IsCancellable;

    private bool TryBuildRequest(out DatasetSearchRequest request, out string error)
    {
        request = new DatasetSearchRequest(Query, Replacement, UseRegex, CaseSensitive);
        error = string.Empty;

        if (!_hasSource)
        {
            error = "Open a dataset first.";
            return false;
        }

        if (string.IsNullOrEmpty(Query))
        {
            error = "Enter a search query.";
            return false;
        }

        request = new DatasetSearchRequest(
            Query,
            Replacement,
            UseRegex,
            CaseSensitive,
            CurrentPageOnly ? _pageStartRow : 0,
            CurrentPageOnly ? _pageRowCount : -1);
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

    private void SetSuggestedTargetPath(string sourcePath)
    {
        var directory = Path.GetDirectoryName(sourcePath) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(sourcePath);
        var extension = Path.GetExtension(sourcePath);
        var fileName = $"{name}.replaced{extension}";
        TargetPath = string.IsNullOrEmpty(directory) ? fileName : Path.Combine(directory, fileName);
    }
}

public sealed record DatasetSearchMatchViewModel(DatasetSearchMatch Match)
{
    public long LineNumber => Match.LineNumber;
    public string LineText => Match.LineNumber.ToString("N0");
    public string CountText => Match.MatchCount == 1 ? "1 match" : $"{Match.MatchCount:N0} matches";
    public string Snippet => Match.Snippet;
}