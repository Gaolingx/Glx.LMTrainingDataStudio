using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services.Datasets;

namespace LMTrainingDataStudio2.ViewModels;

/// <summary>
/// Deduplicate, shuffle and split a dataset into new output files.
/// </summary>
public partial class DatasetTransformViewModel : ObservableObject
{
    private DatasetTransformService _service;
    private string _sourcePath = string.Empty;
    private DataFileType _fileType;
    private bool _hasSource;
    private CancellationTokenSource? _operation;

    [ObservableProperty]
    private bool _deduplicate;

    [ObservableProperty]
    private bool _shuffle;

    [ObservableProperty]
    private string _seedText = "42";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSplits))]
    private string _splitSpecsText = string.Empty;

    [ObservableProperty]
    private string _outputPath = string.Empty;

    [ObservableProperty]
    private string _outputDirectory = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isCancellable;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _statusText = "Open a dataset to transform it.";

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private bool _hasResults;

    public ObservableCollection<DatasetTransformOutputLine> Results { get; } = new();

    public bool HasSplits => !string.IsNullOrWhiteSpace(SplitSpecsText);

    public DatasetTransformViewModel(AppSettings settings)
    {
        _service = new DatasetTransformService(settings);
    }

    public void ApplySettings(AppSettings settings)
    {
        _service = new DatasetTransformService(settings);
    }

    public void SetSource(string path, DataFileType fileType)
    {
        _sourcePath = path;
        _fileType = fileType;
        _hasSource = true;
        Results.Clear();
        HasResults = false;
        Summary = string.Empty;
        StatusText = $"Ready to transform '{Path.GetFileName(path)}'.";

        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        var fileName = $"{name}.clean{extension}";
        OutputPath = string.IsNullOrEmpty(directory) ? fileName : Path.Combine(directory, fileName);
        if (string.IsNullOrWhiteSpace(OutputDirectory))
        {
            OutputDirectory = directory;
        }
    }

    [RelayCommand]
    private async Task RunAsync()
    {
        if (!_hasSource)
        {
            StatusText = "Open a dataset first.";
            return;
        }

        if (!long.TryParse(SeedText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed))
        {
            StatusText = "Seed must be an integer.";
            return;
        }

        IReadOnlyList<string>? splits = null;
        if (HasSplits)
        {
            splits = SplitSpecsText
                .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Trim())
                .Where(part => part.Length > 0)
                .ToArray();
        }

        if (splits is { Count: > 0 } && string.IsNullOrWhiteSpace(OutputDirectory))
        {
            StatusText = "Choose an output directory for the splits.";
            return;
        }

        if ((splits is null or { Count: 0 }) && string.IsNullOrWhiteSpace(OutputPath))
        {
            StatusText = "Choose an output file.";
            return;
        }

        if (IsBusy) return;

        BeginOperation("Transforming...");
        try
        {
            var progress = new Progress<double>(value => Progress = value);
            var options = new DatasetTransformOptions(
                Deduplicate,
                Shuffle,
                seed,
                splits,
                string.IsNullOrWhiteSpace(OutputPath) ? null : OutputPath,
                string.IsNullOrWhiteSpace(OutputDirectory) ? null : OutputDirectory);

            var result = await _service.TransformAsync(
                _sourcePath, _fileType, options, progress, _operation!.Token);

            Results.Clear();
            Results.Add(new DatasetTransformOutputLine($"total rows: {result.TotalRows:N0}"));
            Results.Add(new DatasetTransformOutputLine($"output rows: {result.OutputRows:N0}"));
            Results.Add(new DatasetTransformOutputLine($"duplicates removed: {result.DuplicateRows:N0}"));

            if (result.OutputPath is not null)
            {
                Results.Add(new DatasetTransformOutputLine($"output: {result.OutputPath}"));
            }

            foreach (var split in result.Splits)
            {
                Results.Add(new DatasetTransformOutputLine(
                    $"{split.Name}: {split.Rows:N0} rows → {split.Path}"));
            }

            HasResults = true;
            Summary =
                $"{result.OutputRows:N0} / {result.TotalRows:N0} row(s) written, " +
                $"{result.DuplicateRows:N0} duplicate(s) removed in " +
                $"{result.Elapsed.TotalSeconds:F1}s.";
            StatusText = "Transform complete.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Transform cancelled; no target was replaced.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or ArgumentException or DatasetFormatException)
        {
            StatusText = $"Transform failed: {ex.Message}";
        }
        finally
        {
            EndOperation();
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _operation?.Cancel();

    private bool CanCancel() => IsCancellable;

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

public sealed record DatasetTransformOutputLine(string Text);