using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services.Datasets;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace LMTrainingDataStudio2.ViewModels;

/// <summary>
/// Paginated dataset preview and batch format conversion.
/// The view owns file pickers; this model only needs paths.
/// </summary>
public partial class DataWorkbenchViewModel : ObservableObject
{
    public static readonly int[] PageSizes = { 100, 200, 500, 1000 };
    public static readonly DatasetFormatOption[] Formats =
    {
        new(DatasetFormat.OpenAI, "OpenAI messages"),
        new(DatasetFormat.ShareGPT, "ShareGPT conversations"),
        new(DatasetFormat.Alpaca, "Alpaca instruction/output"),
        new(DatasetFormat.Dpo, "DPO prompt/chosen/rejected"),
        new(DatasetFormat.Pretrain, "Pretrain text")
    };

    private readonly DatasetBatchConverter _converter;
    private AppSettings _settings;
    private IDatasetReader? _reader;
    private DataFileType _sourceFileType;
    private CancellationTokenSource? _conversionCancellation;
    private int _pageLoadVersion;
    private bool _targetPathIsSuggested = true;
    private bool _updatingTargetPath;

    [ObservableProperty]
    private string _sourcePath = string.Empty;

    [ObservableProperty]
    private string _sourceFileTypeText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalPages), nameof(PositionText))]
    private long _totalRows;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalPages), nameof(PositionText))]
    private int _pageSize = PageSizes[0];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionText))]
    private long _currentPage = 1;

    [ObservableProperty]
    private string _goToRowText = "1";

    [ObservableProperty]
    private string _statusText = "Open a JSONL, CSV or Parquet dataset to preview it.";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionText))]
    private bool _isDatasetOpen;

    [ObservableProperty]
    private DatasetFormatOption _selectedTargetFormat = Formats[0];

    [ObservableProperty]
    private string _targetPath = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelConversionCommand))]
    private bool _isConverting;

    [ObservableProperty]
    private double _conversionProgress;

    [ObservableProperty]
    private string _conversionSummary = string.Empty;

    [ObservableProperty]
    private bool _hasConversionErrors;

    public ObservableCollection<DatasetRowViewModel> Rows { get; } = new();
    public ObservableCollection<string> ConversionErrors { get; } = new();
    public IReadOnlyList<int> AvailablePageSizes => PageSizes;
    public IReadOnlyList<DatasetFormatOption> TargetFormats => Formats;

    public string PositionText => IsDatasetOpen
        ? $"Page {CurrentPage:N0} / {TotalPages:N0}"
        : "No dataset";

    public long TotalPages => TotalRows == 0 ? 1 : (TotalRows + PageSize - 1) / PageSize;

    public DataWorkbenchViewModel(AppSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _converter = new DatasetBatchConverter(settings);
    }

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;
    }

    public string SuggestTargetPath(string sourcePath, DatasetFormat format)
    {
        var directory = Path.GetDirectoryName(sourcePath) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(sourcePath);
        var fileName = $"{name}.{format.ToString().ToLowerInvariant()}.jsonl";
        return string.IsNullOrEmpty(directory) ? fileName : Path.Combine(directory, fileName);
    }

    /// <summary>
    /// Opens a dataset, rebuilds the page index and shows the first page.
    /// </summary>
    public async Task OpenDatasetAsync(string path)
    {
        if (IsBusy) return;

        if (IsConverting)
        {
            StatusText = "Cancel the running conversion before opening another dataset.";
            return;
        }

        IsBusy = true;
        StatusText = "Opening dataset...";
        IDatasetReader? reader = null;
        try
        {
            var fileType = DatasetIO.DetectFileType(path);
            reader = DatasetIO.CreateReader(fileType, _settings);
            await reader.OpenAsync(path, CancellationToken.None);

            var openedPath = reader.SourcePath;
            var openedRows = reader.TotalRows;

            await CloseReaderAsync();
            _reader = reader;
            reader = null;
            _sourceFileType = fileType;
            SourcePath = openedPath;
            SourceFileTypeText = fileType.ToString().ToUpperInvariant();
            TotalRows = openedRows;
            IsDatasetOpen = true;
            GoToRowText = TotalRows == 0 ? "0" : "1";
            if (string.IsNullOrWhiteSpace(TargetPath) || _targetPathIsSuggested)
            {
                SetSuggestedTargetPath(openedPath, SelectedTargetFormat.Format);
            }

            await LoadPageAsync(1);
            StatusText = $"Opened '{openedPath}' ({openedRows:N0} rows).";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (reader is not null)
            {
                await reader.DisposeAsync();
            }

            StatusText = $"Open failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task FirstPageAsync() => LoadPageAsync(1);

    [RelayCommand]
    private Task PreviousPageAsync() => LoadPageAsync(CurrentPage - 1);

    [RelayCommand]
    private Task NextPageAsync() => LoadPageAsync(CurrentPage + 1);

    [RelayCommand]
    private Task LastPageAsync() => LoadPageAsync(TotalPages);

    [RelayCommand]
    private Task GoToRowAsync()
    {
        if (_reader is null)
        {
            StatusText = "Open a dataset first.";
            return Task.CompletedTask;
        }

        if (!long.TryParse(GoToRowText, out var row) || row < 1 || row > TotalRows)
        {
            StatusText = TotalRows == 0
                ? "The dataset is empty."
                : $"Row must be between 1 and {TotalRows:N0}.";
            return Task.CompletedTask;
        }

        var page = (row - 1) / PageSize + 1;
        return LoadPageAsync(page);
    }

    public async Task LoadPageAsync(long page)
    {
        if (_reader is null) return;

        var version = ++_pageLoadVersion;
        try
        {
            var totalPages = TotalPages;
            page = Math.Clamp(page, 1, totalPages);
            var startRow = (page - 1) * PageSize;
            var pageRows = await _reader.ReadPageAsync(startRow, PageSize, CancellationToken.None);

            if (version != _pageLoadVersion)
            {
                return;
            }

            Rows.Clear();
            foreach (var row in pageRows)
            {
                Rows.Add(new DatasetRowViewModel(row.LineNumber + 1, row.Json));
            }

            CurrentPage = page;
            GoToRowText = TotalRows == 0 ? "0" : (startRow + 1).ToString();
            var lastRow = startRow + pageRows.Count;
            StatusText = TotalRows == 0
                ? "The dataset is empty."
                : $"Rows {startRow + 1:N0}-{lastRow:N0} of {TotalRows:N0}.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (version == _pageLoadVersion)
            {
                StatusText = $"Page load failed: {ex.Message}";
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelConversion))]
    private void CancelConversion() => _conversionCancellation?.Cancel();

    private bool CanCancelConversion() => IsConverting;

    [RelayCommand]
    private async Task ConvertAsync()
    {
        if (IsBusy)
        {
            StatusText = "Wait for the dataset to finish opening.";
            return;
        }

        if (_reader is null)
        {
            StatusText = "Open a dataset first.";
            return;
        }

        if (string.IsNullOrWhiteSpace(TargetPath))
        {
            StatusText = "Choose a target file.";
            return;
        }

        DataFileType targetFileType;
        try
        {
            targetFileType = DatasetIO.DetectFileType(TargetPath);
        }
        catch (DatasetFormatException ex)
        {
            StatusText = ex.Message;
            return;
        }

        IsConverting = true;
        ConversionProgress = 0;
        ConversionSummary = string.Empty;
        ConversionErrors.Clear();
        HasConversionErrors = false;
        StatusText = "Converting...";
        _conversionCancellation = new CancellationTokenSource();

        try
        {
            var progress = new Progress<double>(value => ConversionProgress = value);
            var result = await _converter.ConvertAsync(
                new BatchConversionRequest(
                    SourcePath,
                    _sourceFileType,
                    DatasetFormat.Auto,
                    TargetPath,
                    targetFileType,
                    SelectedTargetFormat.Format),
                progress,
                _conversionCancellation.Token);

            ConversionSummary = result.ErrorFilePath is null
                ? $"{result.ConvertedRows:N0} / {result.TotalRows:N0} rows converted in {result.Elapsed.TotalSeconds:F1}s."
                : $"{result.ConvertedRows:N0} / {result.TotalRows:N0} rows converted, {result.FailedRows:N0} failed in {result.Elapsed.TotalSeconds:F1}s. Errors: {result.ErrorFilePath}";

            foreach (var error in result.SampleErrors.Take(20))
            {
                ConversionErrors.Add(error);
            }

            HasConversionErrors = ConversionErrors.Count > 0;

            StatusText = result.FailedRows == 0
                ? "Conversion complete ✓"
                : $"Conversion complete with {result.FailedRows:N0} failed row(s).";
        }
        catch (OperationCanceledException)
        {
            ConversionSummary = "Conversion cancelled. No partial target was written.";
            StatusText = "Conversion cancelled.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DatasetFormatException or ArgumentException)
        {
            ConversionSummary = $"Conversion failed: {ex.Message}";
            StatusText = "Conversion failed.";
        }
        finally
        {
            _conversionCancellation.Dispose();
            _conversionCancellation = null;
            IsConverting = false;
        }
    }

    private async Task CloseReaderAsync()
    {
        if (_reader is null) return;

        await _reader.DisposeAsync();
        _reader = null;
    }

    partial void OnPageSizeChanged(int value)
    {
        if (IsDatasetOpen)
        {
            _ = LoadPageAsync(1);
        }
    }

    partial void OnSelectedTargetFormatChanged(DatasetFormatOption value)
    {
        if (_targetPathIsSuggested && IsDatasetOpen)
        {
            SetSuggestedTargetPath(SourcePath, value.Format);
        }
    }

    partial void OnTargetPathChanged(string value)
    {
        if (!_updatingTargetPath)
        {
            _targetPathIsSuggested = false;
        }
    }

    private void SetSuggestedTargetPath(string sourcePath, DatasetFormat format)
    {
        _updatingTargetPath = true;
        TargetPath = SuggestTargetPath(sourcePath, format);
        _updatingTargetPath = false;
        _targetPathIsSuggested = true;
    }
}

public sealed record DatasetFormatOption(DatasetFormat Format, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// One preview row. Large rows are pretty-printed and truncated for display only.
/// </summary>
public sealed class DatasetRowViewModel
{
    private const int MaxPreviewLength = 4000;

    public DatasetRowViewModel(long number, string json)
    {
        Number = number;
        Json = json;
        Preview = BuildPreview(json);
    }

    public long Number { get; }

    public string Json { get; }

    public string Preview { get; }

    private static string BuildPreview(string json)
    {
        if (json.Length <= MaxPreviewLength)
        {
            try
            {
                var node = System.Text.Json.Nodes.JsonNode.Parse(json);
                if (node is not null)
                {
                    return node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
                }
            }
            catch (JsonException)
            {
            }
        }

        return json.Length <= MaxPreviewLength
            ? json
            : json[..MaxPreviewLength] + "\n…";
    }
}
