using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LMTrainingDataStudio2.Localization;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services;
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
    private DatasetEditService _editService;
    private AppSettings _settings;
    private IDatasetReader? _reader;
    private DataFileType _sourceFileType;
    private CancellationTokenSource? _conversionCancellation;
    private int _pageLoadVersion;
    private bool _targetPathIsSuggested = true;
    private bool _updatingTargetPath;
    private DatasetEditSession? _editSession;

    private const string NewRowTemplate = """{"text":""}""";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SourcePathText))]
    private string _sourcePath = string.Empty;

    [ObservableProperty]
    private string _sourceFileTypeText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalPages), nameof(PositionText), nameof(TotalRowsText))]
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

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyRowEditCommand))]
    private DatasetRowViewModel? _selectedRow;

    [ObservableProperty]
    private string _selectedJsonText = string.Empty;

    [ObservableProperty]
    private string? _rowEditError;

    [ObservableProperty]
    private bool _hasPendingEdits;

    [ObservableProperty]
    private string _pendingEditText = string.Empty;

    [ObservableProperty]
    private bool _isSavingEdits;

    [ObservableProperty]
    private double _editProgress;

    [ObservableProperty]
    private string _editStatusText = string.Empty;

    [ObservableProperty]
    private string _highlightQuery = string.Empty;

    [ObservableProperty]
    private bool _highlightUseRegex;

    [ObservableProperty]
    private bool _highlightCaseSensitive;

    public ObservableCollection<DatasetRowViewModel> Rows { get; } = new();
    public ObservableCollection<string> ConversionErrors { get; } = new();
    public IReadOnlyList<int> AvailablePageSizes => PageSizes;
    public IReadOnlyList<DatasetFormatOption> TargetFormats => Formats;

    public DatasetValidationViewModel Validation { get; }
    public DatasetSearchReplaceViewModel Search { get; }
    public DatasetTransformViewModel Transform { get; }
    public DatasetStatisticsViewModel Statistics { get; }

    public string PositionText => IsDatasetOpen
        ? string.Format(LocalizationService.L("data.position_format"), CurrentPage, TotalPages)
        : LocalizationService.L("data.no_dataset");

    public string TotalRowsText => string.Format(LocalizationService.L("data.total_rows_format"), TotalRows);

    public string SourcePathText => string.IsNullOrWhiteSpace(SourcePath)
        ? LocalizationService.L("data.no_dataset_opened")
        : SourcePath;

    public long TotalPages => TotalRows == 0 ? 1 : (TotalRows + PageSize - 1) / PageSize;

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(PositionText));
        OnPropertyChanged(nameof(TotalRowsText));
        OnPropertyChanged(nameof(SourcePathText));
        Validation.RefreshLocalization();
        OnEditSessionChanged();
    }

    public DataWorkbenchViewModel(AppSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _converter = new DatasetBatchConverter(settings);
        _editService = new DatasetEditService(settings);

        Validation = new DatasetValidationViewModel(settings);
        Search = new DatasetSearchReplaceViewModel(settings);
        Transform = new DatasetTransformViewModel(settings);
        Statistics = new DatasetStatisticsViewModel(settings);

        Validation.JumpRequested += line => _ = JumpToLineAsync(line);
        Search.JumpRequested += line => _ = JumpToLineAsync(line);
        Search.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(Search.Query) or nameof(Search.UseRegex)
                or nameof(Search.CaseSensitive))
            {
                HighlightQuery = Search.Query;
                HighlightUseRegex = Search.UseRegex;
                HighlightCaseSensitive = Search.CaseSensitive;
            }
        };
    }

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        _editService = new DatasetEditService(settings);
        Validation.ApplySettings(settings);
        Search.ApplySettings(settings);
        Transform.ApplySettings(settings);
        Statistics.ApplySettings(settings);
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
            ResetEditSession(openedRows);
            SelectedRow = null;
            EditStatusText = string.Empty;
            RowEditError = null;
            if (string.IsNullOrWhiteSpace(TargetPath) || _targetPathIsSuggested)
            {
                SetSuggestedTargetPath(openedPath, SelectedTargetFormat.Format);
            }

            Validation.SetSource(openedPath, fileType);
            Search.SetSource(openedPath, fileType);
            Transform.SetSource(openedPath, fileType);
            Statistics.SetSource(openedPath, fileType);

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
        var previousAnchor = SelectedRow?.OriginalIndex;
        var previousInsertId = SelectedRow?.InsertId;
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
                AddInsertedRows(row.LineNumber);
                Rows.Add(CreateRowViewModel(row));
            }

            if (startRow + pageRows.Count >= TotalRows)
            {
                AddInsertedRows(TotalRows);
            }

            if (previousInsertId is { } insertId)
            {
                SelectedRow = Rows.FirstOrDefault(row => row.InsertId == insertId);
            }
            else if (previousAnchor is { } anchor)
            {
                SelectedRow = Rows.FirstOrDefault(row => row.OriginalIndex == anchor);
            }

            CurrentPage = page;
            Search.SetPageWindow(startRow, pageRows.Count);
            var lastRow = startRow + pageRows.Count;
            if (TotalRows == 0)
            {
                GoToRowText = "0";
            }
            else if (!long.TryParse(GoToRowText, out var requestedRow) ||
                     requestedRow < startRow + 1 || requestedRow > lastRow)
            {
                GoToRowText = (startRow + 1).ToString();
            }
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

    public Task JumpToLineAsync(long lineNumber)
    {
        if (_reader is null || TotalRows == 0)
        {
            return Task.CompletedTask;
        }

        var target = Math.Clamp(lineNumber, 1, TotalRows);
        GoToRowText = target.ToString();
        return LoadPageAsync((target - 1) / PageSize + 1);
    }

    #region Row editing

    [RelayCommand(CanExecute = nameof(CanInsertRow))]
    private Task InsertRowAboveAsync() => InsertRowAsync(above: true);

    [RelayCommand(CanExecute = nameof(CanInsertRow))]
    private Task InsertRowBelowAsync() => InsertRowAsync(above: false);

    [RelayCommand(CanExecute = nameof(CanEditSelectedRow))]
    private Task DuplicateRowAsync() => InsertJsonAsync(SelectedRow!.Json, above: false);

    [RelayCommand(CanExecute = nameof(CanDeleteSelectedRow))]
    private async Task DeleteRowAsync()
    {
        if (_editSession is null || SelectedRow is null) return;

        if (SelectedRow.InsertId is { } insertId)
        {
            _editSession.DeleteInsertedRow(insertId);
        }
        else if (SelectedRow.OriginalIndex is { } originalIndex)
        {
            _editSession.DeleteSourceRow(originalIndex);
        }

        EditStatusText = "Row marked for deletion; save to apply.";
        await ReloadPageAsync();
    }

    [RelayCommand(CanExecute = nameof(CanRestoreSelectedRow))]
    private async Task RestoreRowAsync()
    {
        if (_editSession is null || SelectedRow?.OriginalIndex is not { } originalIndex) return;

        _editSession.RestoreSourceRow(originalIndex);
        EditStatusText = "Row restored.";
        await ReloadPageAsync();
    }

    [RelayCommand(CanExecute = nameof(CanApplyRowEdit))]
    private async Task ApplyRowEditAsync()
    {
        if (_editSession is null || SelectedRow is null) return;

        var json = SelectedJsonText?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(json))
        {
            RowEditError = "Row JSON is empty.";
            return;
        }

        try
        {
            using var _ = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            RowEditError = $"Invalid JSON: {ex.Message}";
            return;
        }

        RowEditError = null;
        try
        {
            if (SelectedRow.InsertId is { } insertId)
            {
                _editSession.UpdateInsertedRow(insertId, json);
            }
            else if (SelectedRow.OriginalIndex is { } originalIndex)
            {
                _editSession.SetRowJson(originalIndex, json);
            }
        }
        catch (InvalidOperationException ex)
        {
            RowEditError = ex.Message;
            return;
        }

        EditStatusText = "Row edit applied; save to write it.";
        await ReloadPageAsync();
    }

    [RelayCommand(CanExecute = nameof(CanUndoEdit))]
    private async Task UndoEditAsync()
    {
        if (_editSession?.Undo() == true)
        {
            EditStatusText = "Undo applied.";
            await ReloadPageAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(CanRedoEdit))]
    private async Task RedoEditAsync()
    {
        if (_editSession?.Redo() == true)
        {
            EditStatusText = "Redo applied.";
            await ReloadPageAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(CanDiscardEdits))]
    private async Task DiscardEditsAsync()
    {
        _editSession?.Clear();
        EditStatusText = "Pending edits discarded.";
        await ReloadPageAsync();
    }

    /// <summary>Counts and before/after samples shown before saving edits.</summary>
    public async Task<DatasetEditSummary?> BuildEditSummaryAsync()
    {
        if (_reader is null || _editSession is null || !_editSession.HasChanges)
        {
            return null;
        }

        try
        {
            return await _editService.BuildSummaryAsync(_editSession.Snapshot(), _reader);
        }
        catch (Exception ex) when (ex is DatasetFormatException or ArgumentException or IOException)
        {
            EditStatusText = $"Cannot summarize edits: {ex.Message}";
            return null;
        }
    }

    /// <summary>
    /// Streams the source and writes pending edits to <paramref name="targetPath"/>.
    /// Overwriting the open source closes and reopens the reader around the atomic replace.
    /// </summary>
    public async Task<bool> SaveEditsAsync(string targetPath, CancellationToken cancellationToken = default)
    {
        if (_reader is null || _editSession is null)
        {
            StatusText = "Open a dataset first.";
            return false;
        }

        if (!_editSession.HasChanges)
        {
            EditStatusText = "There are no pending edits.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(targetPath))
        {
            EditStatusText = "Choose a target file.";
            return false;
        }

        if (IsSavingEdits)
        {
            return false;
        }

        var overwriteSource = SamePath(targetPath, SourcePath);
        var snapshot = _editSession.Snapshot();
        IsSavingEdits = true;
        EditProgress = 0;
        EditStatusText = "Saving edits...";

        try
        {
            if (overwriteSource)
            {
                await CloseReaderAsync();
            }

            var progress = new Progress<double>(value => EditProgress = value);
            var result = await _editService.SaveAsync(
                snapshot, SourcePath, _sourceFileType, targetPath,
                progress: progress, cancellationToken: cancellationToken);

            if (overwriteSource)
            {
                await ReopenAfterOverwriteAsync();
            }
            else
            {
                _editSession.Clear();
                await ReloadPageAsync();
            }

            EditStatusText =
                $"Saved {result.WrittenRows:N0} row(s) " +
                $"({result.ReplacedRows:N0} replaced, {result.DeletedRows:N0} deleted, " +
                $"{result.InsertedRows:N0} inserted) to '{result.TargetPath}' " +
                $"in {result.Elapsed.TotalSeconds:F1}s.";
            StatusText = overwriteSource
                ? "Edits saved over the source file."
                : $"Edited copy written to '{result.TargetPath}'.";
            return true;
        }
        catch (OperationCanceledException)
        {
            EditStatusText = "Save cancelled; the target was not replaced.";
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or DatasetFormatException or ArgumentException)
        {
            EditStatusText = $"Save failed: {ex.Message}";
            return false;
        }
        finally
        {
            IsSavingEdits = false;
            EditProgress = 0;
            if (overwriteSource && _reader is null)
            {
                await TryReopenSourceAsync();
            }
        }
    }

    private async Task InsertRowAsync(bool above)
    {
        if (_editSession is null) return;
        await InsertJsonAsync(NewRowTemplate, above);
    }

    private async Task InsertJsonAsync(string json, bool above)
    {
        if (_editSession is null) return;

        var anchor = ResolveInsertAnchor(above);
        var insertId = _editSession.InsertBefore(anchor, json);
        EditStatusText = "Row inserted; edit the JSON then save.";
        await ReloadPageAsync();
        SelectedRow = Rows.FirstOrDefault(row => row.InsertId == insertId);
    }

    private long ResolveInsertAnchor(bool above)
    {
        if (SelectedRow?.OriginalIndex is { } originalIndex)
        {
            return above ? originalIndex : originalIndex + 1;
        }

        if (SelectedRow?.InsertId is { } insertId && _editSession is not null)
        {
            return _editSession.FindInsertedRowAnchor(insertId) ?? 0;
        }

        var pageStart = (CurrentPage - 1) * PageSize;
        return above
            ? Math.Clamp(pageStart, 0, TotalRows)
            : Math.Clamp(pageStart + PageSize, 0, TotalRows);
    }

    private DatasetRowViewModel CreateRowViewModel(DatasetRow row)
    {
        var replacement = _editSession?.GetReplacement(row.LineNumber);
        var model = new DatasetRowViewModel(
            row.LineNumber + 1,
            replacement ?? row.Json,
            originalIndex: row.LineNumber,
            isDeleted: _editSession?.IsDeleted(row.LineNumber) == true,
            isModified: replacement is not null);
        ApplyHighlight(model);
        return model;
    }

    private void AddInsertedRows(long anchorRow)
    {
        if (_editSession is null) return;

        foreach (var inserted in _editSession.GetInsertions(anchorRow))
        {
            var model = new DatasetRowViewModel(
                anchorRow + 1,
                inserted.Json,
                insertId: inserted.Id,
                isInserted: true);
            ApplyHighlight(model);
            Rows.Add(model);
        }
    }

    private Task ReloadPageAsync() => LoadPageAsync(CurrentPage);

    private void ResetEditSession(long totalRows)
    {
        if (_editSession is not null)
        {
            _editSession.Changed -= OnEditSessionChanged;
        }

        _editSession = new DatasetEditSession(totalRows);
        _editSession.Changed += OnEditSessionChanged;
        HasPendingEdits = false;
        PendingEditText = string.Empty;
        NotifyEditCommands();
    }

    private void OnEditSessionChanged()
    {
        HasPendingEdits = _editSession?.HasChanges ?? false;
        PendingEditText = _editSession is { HasChanges: true }
            ? string.Format(
                LocalizationService.L("data.pending_edits_format"),
                _editSession.InsertedRows,
                _editSession.DeletedRows,
                _editSession.EffectiveRows)
            : string.Empty;
        NotifyEditCommands();
    }

    private async Task ReopenAfterOverwriteAsync()
    {
        var reader = DatasetIO.CreateReader(_sourceFileType, _settings);
        await reader.OpenAsync(SourcePath);
        _reader = reader;
        TotalRows = reader.TotalRows;
        ResetEditSession(TotalRows);
        SelectedRow = null;
        GoToRowText = TotalRows == 0 ? "0" : "1";
        await LoadPageAsync(1);
    }

    private async Task TryReopenSourceAsync()
    {
        try
        {
            await ReopenAfterOverwriteAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or DatasetFormatException or InvalidOperationException)
        {
            IsDatasetOpen = false;
            EditStatusText = $"The source could not be reopened: {ex.Message}";
        }
    }

    private bool CanInsertRow() => _editSession is not null && !IsBusy && !IsConverting && !IsSavingEdits;

    private bool CanEditSelectedRow() => CanInsertRow() && SelectedRow is { IsDeleted: false };

    private bool CanDeleteSelectedRow() => CanEditSelectedRow();

    private bool CanRestoreSelectedRow() =>
        CanInsertRow() && SelectedRow is { IsDeleted: true, OriginalIndex: not null };

    private bool CanApplyRowEdit() =>
        CanEditSelectedRow() &&
        SelectedRow is not null &&
        !string.Equals(SelectedJsonText, SelectedRow.Json, StringComparison.Ordinal);

    private bool CanUndoEdit() =>
        _editSession?.CanUndo == true && !IsBusy && !IsConverting && !IsSavingEdits;

    private bool CanRedoEdit() =>
        _editSession?.CanRedo == true && !IsBusy && !IsConverting && !IsSavingEdits;

    private bool CanDiscardEdits() => HasPendingEdits && !IsBusy && !IsConverting && !IsSavingEdits;

    private void NotifyEditCommands()
    {
        InsertRowAboveCommand.NotifyCanExecuteChanged();
        InsertRowBelowCommand.NotifyCanExecuteChanged();
        DuplicateRowCommand.NotifyCanExecuteChanged();
        DeleteRowCommand.NotifyCanExecuteChanged();
        RestoreRowCommand.NotifyCanExecuteChanged();
        ApplyRowEditCommand.NotifyCanExecuteChanged();
        UndoEditCommand.NotifyCanExecuteChanged();
        RedoEditCommand.NotifyCanExecuteChanged();
        DiscardEditsCommand.NotifyCanExecuteChanged();
    }

    private void ApplyHighlight(DatasetRowViewModel row)
    {
        row.HighlightQuery = HighlightQuery;
        row.HighlightUseRegex = HighlightUseRegex;
        row.HighlightCaseSensitive = HighlightCaseSensitive;
    }

    private void UpdateRowHighlight()
    {
        foreach (var row in Rows)
        {
            ApplyHighlight(row);
        }
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    #endregion

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

    partial void OnSelectedRowChanged(DatasetRowViewModel? value)
    {
        RowEditError = null;
        SelectedJsonText = value?.Json ?? string.Empty;
        NotifyEditCommands();
    }

    partial void OnSelectedJsonTextChanged(string value) => ApplyRowEditCommand.NotifyCanExecuteChanged();

    partial void OnIsBusyChanged(bool value) => NotifyEditCommands();

    partial void OnIsConvertingChanged(bool value) => NotifyEditCommands();

    partial void OnIsSavingEditsChanged(bool value) => NotifyEditCommands();

    partial void OnHasPendingEditsChanged(bool value) => NotifyEditCommands();

    partial void OnHighlightQueryChanged(string value) => UpdateRowHighlight();

    partial void OnHighlightUseRegexChanged(bool value) => UpdateRowHighlight();

    partial void OnHighlightCaseSensitiveChanged(bool value) => UpdateRowHighlight();

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
/// Inserted rows carry an <see cref="InsertId"/>; source rows carry an <see cref="OriginalIndex"/>.
/// </summary>
public sealed partial class DatasetRowViewModel : ObservableObject
{
    private const int MaxPreviewLength = 4000;

    [ObservableProperty]
    private string _highlightQuery = string.Empty;

    [ObservableProperty]
    private bool _highlightUseRegex;

    [ObservableProperty]
    private bool _highlightCaseSensitive;

    public DatasetRowViewModel(
        long number,
        string json,
        long? originalIndex = null,
        long? insertId = null,
        bool isDeleted = false,
        bool isInserted = false,
        bool isModified = false)
    {
        Number = number;
        Json = json;
        OriginalIndex = originalIndex;
        InsertId = insertId;
        IsDeleted = isDeleted;
        IsInserted = isInserted;
        IsModified = isModified;
        Preview = BuildPreview(json);
    }

    public long Number { get; }

    public long? OriginalIndex { get; }

    public long? InsertId { get; }

    public bool IsDeleted { get; }

    public bool IsInserted { get; }

    public bool IsModified { get; }

    public string NumberText => IsInserted ? "＋" : Number.ToString("N0");

    public string Json { get; }

    public string Preview { get; }

    public bool IsEditable => !IsDeleted;

    public bool IsSearchMatch => HighlightMatcher.HasMatch(
        Preview, HighlightQuery, HighlightUseRegex, HighlightCaseSensitive);

    partial void OnHighlightQueryChanged(string value) => OnPropertyChanged(nameof(IsSearchMatch));

    partial void OnHighlightUseRegexChanged(bool value) => OnPropertyChanged(nameof(IsSearchMatch));

    partial void OnHighlightCaseSensitiveChanged(bool value) => OnPropertyChanged(nameof(IsSearchMatch));

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
