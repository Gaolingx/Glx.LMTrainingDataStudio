using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services.Datasets;

namespace LMTrainingDataStudio2.ViewModels;

/// <summary>
/// Dataset profile: role/length distribution, empty and duplicate samples, tool calls.
/// </summary>
public partial class DatasetStatisticsViewModel : ObservableObject
{
    private DatasetStatisticsService _service;
    private string _sourcePath = string.Empty;
    private DataFileType _fileType;
    private bool _hasSource;
    private CancellationTokenSource? _operation;

    [ObservableProperty]
    private bool _includeDuplicates = true;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isCancellable;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _statusText = "Open a dataset to compute statistics.";

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private bool _hasResults;

    public ObservableCollection<DatasetMetric> Metrics { get; } = new();
    public ObservableCollection<DatasetMetric> RoleCounts { get; } = new();
    public ObservableCollection<DatasetMetric> LengthBuckets { get; } = new();
    public ObservableCollection<DatasetMetric> FormatCounts { get; } = new();

    public DatasetStatisticsViewModel(AppSettings settings)
    {
        _service = new DatasetStatisticsService(settings);
    }

    public void ApplySettings(AppSettings settings)
    {
        _service = new DatasetStatisticsService(settings);
    }

    public void SetSource(string path, DataFileType fileType)
    {
        _sourcePath = path;
        _fileType = fileType;
        _hasSource = true;
        ClearResults();
        StatusText = $"Ready to profile '{Path.GetFileName(path)}'.";
    }

    [RelayCommand]
    private async Task ComputeAsync()
    {
        if (!_hasSource)
        {
            StatusText = "Open a dataset first.";
            return;
        }

        if (IsBusy) return;

        BeginOperation("Computing statistics...");
        try
        {
            var progress = new Progress<double>(value => Progress = value);
            var result = await _service.ComputeAsync(
                _sourcePath, _fileType, IncludeDuplicates, progress, _operation!.Token);

            ClearResults();

            AddMetric("Rows", $"{result.TotalRows:N0}");
            AddMetric("Malformed rows", $"{result.MalformedRows:N0}");
            AddMetric("Empty samples", $"{result.EmptyRows:N0}");
            AddMetric("Empty content messages", $"{result.EmptyContentMessages:N0}");
            AddMetric("Duplicate rows", IncludeDuplicates ? $"{result.DuplicateRows:N0}" : "not computed");
            AddMetric("Duplicate rate", IncludeDuplicates ? $"{result.DuplicateRate:P1}" : "–");
            AddMetric("Total characters", $"{result.TotalCharacters:N0}");
            AddMetric("Average characters/row", $"{result.AverageCharacters:N0}");
            AddMetric("Tool calls", $"{result.ToolCallCount:N0}");
            AddMetric("Tool messages", $"{result.ToolMessageCount:N0}");

            foreach (var pair in result.RoleCounts.OrderByDescending(pair => pair.Value))
            {
                RoleCounts.Add(new DatasetMetric(pair.Key, $"{pair.Value:N0}"));
            }

            foreach (var bucket in result.LengthBuckets)
            {
                LengthBuckets.Add(new DatasetMetric(bucket.Label, $"{bucket.Count:N0}"));
            }

            foreach (var pair in result.FormatCounts.OrderByDescending(pair => pair.Value))
            {
                FormatCounts.Add(new DatasetMetric(pair.Key, $"{pair.Value:N0}"));
            }

            HasResults = true;
            Summary =
                $"{result.TotalRows:N0} row(s) profiled in {result.Elapsed.TotalSeconds:F1}s.";
            StatusText = "Statistics complete.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Statistics cancelled.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or ArgumentException or DatasetFormatException)
        {
            StatusText = $"Statistics failed: {ex.Message}";
        }
        finally
        {
            EndOperation();
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _operation?.Cancel();

    private bool CanCancel() => IsCancellable;

    private void AddMetric(string name, string value) => Metrics.Add(new DatasetMetric(name, value));

    private void ClearResults()
    {
        Metrics.Clear();
        RoleCounts.Clear();
        LengthBuckets.Clear();
        FormatCounts.Clear();
        HasResults = false;
        Summary = string.Empty;
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
}

public sealed record DatasetMetric(string Name, string Value);
