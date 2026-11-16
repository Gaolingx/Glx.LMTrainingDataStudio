using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services;
using System.Collections.ObjectModel;

namespace LMTrainingDataStudio2.ViewModels;

/// <summary>
/// Execution history list with lazily loaded run details.
/// </summary>
public partial class ExecutionsViewModel : ObservableObject
{
    private const string AllStatuses = "All";

    private readonly ExecutionStore _store;
    private readonly List<ExecutionRunSummary> _allRuns = new();

    [ObservableProperty]
    private string _statusFilter = AllStatuses;

    [ObservableProperty]
    private ExecutionRunSummary? _selectedRunSummary;

    [ObservableProperty]
    private ExecutionRun? _selectedRun;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "Executions not loaded.";

    public ExecutionsViewModel(ExecutionStore store)
    {
        _store = store;
    }

    public ObservableCollection<ExecutionRunSummary> Runs { get; } = new();

    public ObservableCollection<string> StatusFilters { get; } = new()
    {
        AllStatuses, "Succeeded", "Failed", "Cancelled", "Running"
    };

    public Task RefreshAsync() => RefreshExecutionsAsync();

    public async Task RefreshAndSelectAsync(string runId)
    {
        StatusFilter = AllStatuses;
        await RefreshExecutionsAsync();

        var selected = Runs.FirstOrDefault(run => run.RunId == runId);
        if (selected is not null)
        {
            SelectedRunSummary = selected;
        }
    }

    public async Task SelectRunAsync(ExecutionRunSummary? summary)
    {
        if (summary is null)
        {
            SelectedRun = null;
            return;
        }

        try
        {
            var run = await _store.LoadAsync(summary.RunId);
            if (SelectedRunSummary?.RunId != summary.RunId) return;
            SelectedRun = run;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            if (SelectedRunSummary?.RunId != summary.RunId) return;
            SelectedRun = null;
            StatusText = $"Failed to load run '{summary.RunId}': {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RefreshExecutionsAsync()
    {
        IsBusy = true;
        try
        {
            var recent = await _store.GetRecentAsync(100);
            _allRuns.Clear();
            _allRuns.AddRange(recent);
            ApplyFilter();
            StatusText = recent.Count == 0 ? "No execution runs yet." : $"{recent.Count} run(s)";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            StatusText = $"Failed to load executions: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnStatusFilterChanged(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            StatusFilter = AllStatuses;
            return;
        }

        ApplyFilter();
    }

    partial void OnSelectedRunSummaryChanged(ExecutionRunSummary? value) => _ = SelectRunAsync(value);

    private void ApplyFilter()
    {
        var selectedRunId = SelectedRunSummary?.RunId;
        Runs.Clear();

        foreach (var run in _allRuns)
        {
            if (StatusFilter != AllStatuses &&
                !string.Equals(run.Status.ToString(), StatusFilter, StringComparison.Ordinal))
            {
                continue;
            }

            Runs.Add(run);
        }

        SelectedRunSummary = Runs.FirstOrDefault(run => run.RunId == selectedRunId) ?? Runs.FirstOrDefault();
    }
}