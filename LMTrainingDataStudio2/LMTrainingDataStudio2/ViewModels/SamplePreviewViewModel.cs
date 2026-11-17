using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LMTrainingDataStudio2.Localization;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services.Datasets;

namespace LMTrainingDataStudio2.ViewModels;

/// <summary>
/// Unified read-only sample preview for node outputs and cleaner diffs.
/// </summary>
public partial class SamplePreviewViewModel : ObservableObject
{
    private readonly SamplePreviewService _service;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _pathText = string.Empty;

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isDiff;

    [ObservableProperty]
    private bool _hasRows;

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

    public async Task OpenInDataAsync()
    {
        if (OpenInDataRequested is not null && CanOpenInData)
        {
            await OpenInDataRequested(FilePath);
        }
    }

    private async Task LoadAsync(
        string path,
        string title,
        DatasetCleanupRules? rules,
        CancellationToken cancellationToken)
    {
        Title = title;
        FilePath = path;
        PathText = path;
        StatusText = LocalizationService.L("common.loading");
        IsBusy = true;
        try
        {
            var document = rules is null
                ? await _service.LoadFileAsync(path, cancellationToken: cancellationToken)
                : await _service.BuildCleanerDiffAsync(path, rules, cancellationToken: cancellationToken);

            IsDiff = document.IsDiff;
            Rows.Clear();
            foreach (var row in document.Rows)
            {
                Rows.Add(new SamplePreviewRowViewModel(row, document.IsDiff));
            }

            HasRows = Rows.Count > 0;
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
