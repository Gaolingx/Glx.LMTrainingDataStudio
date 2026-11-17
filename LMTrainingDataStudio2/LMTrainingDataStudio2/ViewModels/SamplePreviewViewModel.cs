using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LMTrainingDataStudio2.Localization;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services.Datasets;

namespace LMTrainingDataStudio2.ViewModels;

/// <summary>
/// Unified read-only sample preview for node outputs and cleaner diffs.
/// </summary>
public partial class SamplePreviewViewModel : ObservableObject
{
    public const int PageSize = 50;

    private readonly SamplePreviewService _service;
    private string _path = string.Empty;
    private DatasetCleanupRules? _rules;
    private int _pageIndex;
    private int _pageCount = 1;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _pathText = string.Empty;

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isDiff;

    [ObservableProperty]
    private bool _hasRows;

    [ObservableProperty]
    private string _pageText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
    private bool _canGoPreviousPage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    private bool _canGoNextPage;

    public SamplePreviewViewModel(AppSettings settings)
    {
        _service = new SamplePreviewService(settings);
    }

    public ObservableCollection<SamplePreviewRowViewModel> Rows { get; } = new();

    /// <summary>Opens the previewed file in the Data workbench; set by the owner.</summary>
    public Func<string, Task>? OpenInDataRequested { get; set; }

    public string FilePath { get; private set; } = string.Empty;

    public bool CanOpenInData => !IsDiff && !string.IsNullOrWhiteSpace(FilePath) && File.Exists(FilePath);

    partial void OnIsDiffChanged(bool value) => OnPropertyChanged(nameof(CanOpenInData));

    public Task LoadFileAsync(string path, string title, CancellationToken cancellationToken = default) =>
        LoadAsync(path, title, rules: null, cancellationToken);

    public Task LoadCleanerDiffAsync(
        string path,
        DatasetCleanupRules rules,
        string nodeName,
        CancellationToken cancellationToken = default) =>
        LoadAsync(
            path,
            string.Format(LocalizationService.L("preview.node_format"), nodeName),
            rules,
            cancellationToken);

    [RelayCommand(CanExecute = nameof(CanNavigatePrevious))]
    private Task PreviousPageAsync() => LoadPageAsync(_pageIndex - 1, CancellationToken.None);

    [RelayCommand(CanExecute = nameof(CanNavigateNext))]
    private Task NextPageAsync() => LoadPageAsync(_pageIndex + 1, CancellationToken.None);

    private bool CanNavigatePrevious() => CanGoPreviousPage && !IsBusy;

    private bool CanNavigateNext() => CanGoNextPage && !IsBusy;

    public async Task OpenInDataAsync()
    {
        if (OpenInDataRequested is not null && CanOpenInData)
        {
            await OpenInDataRequested(FilePath);
        }
    }

    private Task LoadAsync(
        string path,
        string title,
        DatasetCleanupRules? rules,
        CancellationToken cancellationToken)
    {
        _path = path;
        _rules = rules;
        Title = title;
        FilePath = path;
        PathText = path;
        return LoadPageAsync(0, cancellationToken);
    }

    private async Task LoadPageAsync(int pageIndex, CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        StatusText = LocalizationService.L("common.loading");
        IsBusy = true;
        try
        {
            var startRow = (long)Math.Max(0, pageIndex) * PageSize;
            var document = _rules is null
                ? await _service.LoadFileAsync(_path, PageSize, startRow, cancellationToken)
                : await _service.BuildCleanerDiffAsync(_path, _rules, PageSize, startRow, cancellationToken);

            _pageCount = Math.Max(1, (int)Math.Ceiling((double)document.TotalRows / PageSize));
            _pageIndex = Math.Clamp(pageIndex, 0, _pageCount - 1);

            IsDiff = document.IsDiff;
            Rows.Clear();
            foreach (var row in document.Rows)
            {
                Rows.Add(new SamplePreviewRowViewModel(row, document.IsDiff));
            }

            HasRows = Rows.Count > 0;
            PageText = string.Format(
                LocalizationService.L("data.position_format"), _pageIndex + 1, _pageCount);
            CanGoPreviousPage = _pageIndex > 0;
            CanGoNextPage = _pageIndex + 1 < _pageCount;
            Summary = string.Format(
                LocalizationService.L("preview.rows_format"), Rows.Count, document.TotalRows) +
                $" · {document.FileType}";
            StatusText = HasRows ? string.Empty : LocalizationService.L("preview.empty");
        }
        catch (OperationCanceledException)
        {
            StatusText = LocalizationService.L("common.cancelled");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or DatasetFormatException or ArgumentException or InvalidOperationException)
        {
            Rows.Clear();
            HasRows = false;
            CanGoPreviousPage = false;
            CanGoNextPage = false;
            PageText = string.Empty;
            Summary = string.Empty;
            StatusText = string.Format(LocalizationService.L("preview.load_failed"), ex.Message);
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanOpenInData));
        }
    }
}

public sealed class SamplePreviewRowViewModel
{
    public SamplePreviewRowViewModel(SamplePreviewRow row, bool isDiff)
    {
        IsDiff = isDiff;
        Header = row.LineNumber?.ToString("N0") ?? "–";
        Before = row.Before;
        After = row.After;
        ChangeText = row.Changed
            ? LocalizationService.L("preview.changed")
            : LocalizationService.L("preview.unchanged");
        IsChanged = row.Changed;
        Note = row.Note ?? string.Empty;
    }

    public string Header { get; }

    public string Before { get; }

    public string After { get; }

    public bool IsDiff { get; }

    public bool IsChanged { get; }

    public string ChangeText { get; }

    public string Note { get; }
}
