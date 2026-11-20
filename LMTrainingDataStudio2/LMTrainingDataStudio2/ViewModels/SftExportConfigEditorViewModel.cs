using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LMTrainingDataStudio2.Localization;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services.Datasets;
using LMTrainingDataStudio2.Services.Executors;

namespace LMTrainingDataStudio2.ViewModels;

/// <summary>
/// Edits the SFT export output path, target format, append/split options and
/// optional top-level field whitelist.
/// </summary>
public partial class SftExportConfigEditorViewModel : ObservableObject
{
    private readonly Action<SftExportBlockConfig> _apply;
    private bool _syncing;

    [ObservableProperty]
    private string _outputPath;

    [ObservableProperty]
    private string _fieldsText;

    [ObservableProperty]
    private string? _fieldsError;

    [ObservableProperty]
    private string? _fieldsSourceInfo;

    [ObservableProperty]
    private string? _fieldsSourceError;

    [ObservableProperty]
    private bool _append;

    [ObservableProperty]
    private SftTargetFormatOption _selectedTargetFormat;

    [ObservableProperty]
    private int _splitMaxRows;

    [ObservableProperty]
    private string _splitsText;

    [ObservableProperty]
    private string? _splitsError;

    [ObservableProperty]
    private int _splitSeed;

    public SftExportConfigEditorViewModel(
        SftExportBlockConfig config,
        Action<SftExportBlockConfig> apply)
    {
        _apply = apply;
        _outputPath = config.OutputPath;
        _fieldsText = config.Fields is { Length: > 0 }
            ? string.Join(", ", config.Fields)
            : string.Empty;
        _append = config.Append;
        _splitMaxRows = Math.Max(0, config.SplitMaxRows);
        _splitsText = config.Splits is { Length: > 0 }
            ? string.Join("\n", config.Splits)
            : string.Empty;
        _splitSeed = config.SplitSeed;
        ValidateSplits(_splitsText);

        TargetFormats = new[]
        {
            new SftTargetFormatOption(DatasetFormat.Auto, "Auto"),
            new SftTargetFormatOption(DatasetFormat.OpenAI, "OpenAI"),
            new SftTargetFormatOption(DatasetFormat.ShareGPT, "ShareGPT"),
            new SftTargetFormatOption(DatasetFormat.Alpaca, "Alpaca")
        };
        _selectedTargetFormat = TargetFormats.FirstOrDefault(option => option.Format == config.TargetFormat)
                               ?? TargetFormats[0];
    }

    public IReadOnlyList<SftTargetFormatOption> TargetFormats { get; }

    public ObservableCollection<SftFieldOptionViewModel> AvailableFields { get; } = new();

    partial void OnOutputPathChanged(string value) => Push();

    partial void OnAppendChanged(bool value) => Push();

    partial void OnSelectedTargetFormatChanged(SftTargetFormatOption value) => Push();

    partial void OnSplitMaxRowsChanged(int value) => Push();

    partial void OnSplitSeedChanged(int value) => Push();

    partial void OnSplitsTextChanged(string value)
    {
        ValidateSplits(value);
        Push();
    }

    partial void OnFieldsTextChanged(string value)
    {
        ValidateFields(value);
        if (!_syncing)
        {
            SyncFieldSelection(value);
        }

        Push();
    }

    /// <summary>
    /// Loads checkbox options for discovered top-level fields, preselecting names
    /// already present in the whitelist.
    /// </summary>
    public void LoadFields(IEnumerable<string> fields)
    {
        var selected = ParseFields(FieldsText).ToHashSet(StringComparer.Ordinal);
        var names = fields
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        _syncing = true;
        AvailableFields.Clear();
        foreach (var name in names)
        {
            AvailableFields.Add(new SftFieldOptionViewModel(
                name, selected.Contains(name), OnFieldOptionChanged));
        }

        _syncing = false;
        FieldsSourceInfo = string.Empty;
        FieldsSourceError = null;
    }

    /// <summary>
    /// Reads top-level fields from a JSONL, CSV or Parquet sample and fills the selector.
    /// </summary>
    public async Task LoadFieldsFromFileAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            var fields = await DatasetFieldDiscovery.ReadTopLevelFieldsAsync(path, cancellationToken);
            if (fields.Count == 0)
            {
                AvailableFields.Clear();
                FieldsSourceInfo = null;
                FieldsSourceError = LocalizationService.L("property.sft_export_fields_empty");
                return;
            }

            LoadFields(fields);
            FieldsSourceError = null;
            FieldsSourceInfo = string.Format(
                LocalizationService.L("property.sft_export_fields_loaded"),
                fields.Count,
                Path.GetFileName(path));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            FieldsSourceInfo = null;
            FieldsSourceError = string.Format(
                LocalizationService.L("property.sft_export_fields_load_failed"),
                ex.Message);
        }
    }

    [RelayCommand]
    private void SelectAllFields() => SetAllFields(selected: true);

    [RelayCommand]
    private void ClearFields() => SetAllFields(selected: false);

    private void SetAllFields(bool selected)
    {
        if (AvailableFields.Count == 0)
        {
            return;
        }

        _syncing = true;
        foreach (var option in AvailableFields)
        {
            option.IsSelected = selected;
        }

        _syncing = false;
        RebuildFieldText();
    }

    private void OnFieldOptionChanged(SftFieldOptionViewModel option)
    {
        if (_syncing)
        {
            return;
        }

        RebuildFieldText();
    }

    private void RebuildFieldText()
    {
        var known = AvailableFields
            .Select(option => option.Name)
            .ToHashSet(StringComparer.Ordinal);
        var extras = ParseFields(FieldsText).Where(field => !known.Contains(field));
        var selected = AvailableFields
            .Where(option => option.IsSelected)
            .Select(option => option.Name);

        _syncing = true;
        FieldsText = string.Join(", ", selected.Concat(extras));
        _syncing = false;
    }

    private void SyncFieldSelection(string? text)
    {
        if (AvailableFields.Count == 0)
        {
            return;
        }

        var selected = ParseFields(text).ToHashSet(StringComparer.Ordinal);
        _syncing = true;
        foreach (var option in AvailableFields)
        {
            option.IsSelected = selected.Contains(option.Name);
        }

        _syncing = false;
    }

    private void ValidateFields(string? text)
    {
        var fields = ParseFields(text);
        var hasInvalidName = fields.Any(field => field.Any(char.IsWhiteSpace));
        var hasDuplicate = fields
            .GroupBy(field => field, StringComparer.Ordinal)
            .Any(group => group.Count() > 1);

        FieldsError = hasInvalidName || hasDuplicate
            ? LocalizationService.L("property.sft_fields_invalid")
            : null;
    }

    private void Push()
    {
        var fields = ParseFields(FieldsText);
        _apply(new SftExportBlockConfig(
            OutputPath ?? string.Empty,
            fields.Length > 0 ? fields : null,
            Append,
            SelectedTargetFormat?.Format ?? DatasetFormat.Auto,
            Math.Max(0, SplitMaxRows),
            ParseSplits(SplitsText),
            SplitSeed));
    }

    private void ValidateSplits(string? text)
    {
        var splits = ParseSplits(text);
        if (splits is null)
        {
            SplitsError = null;
            return;
        }

        if (!SamplerSplitPlan.TryParse(splits, out var plan, out var error))
        {
            SplitsError = string.Format(LocalizationService.L("property.sft_splits_invalid"), error);
            return;
        }

        SplitsError = plan!.IsCountMode
            ? string.Format(
                LocalizationService.L("property.sft_splits_invalid"),
                LocalizationService.L("property.sft_splits_count_not_supported"))
            : null;
    }

    private static string[] ParseFields(string? text) =>
        (text ?? string.Empty).Split(
            ',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static string[]? ParseSplits(string? text)
    {
        var splits = (text ?? string.Empty).Split(
            ['\r', '\n', ','],
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return splits.Length > 0 ? splits : null;
    }
}

/// <summary>Target output format option shown in the SFT export editor.</summary>
public sealed record SftTargetFormatOption(DatasetFormat Format, string DisplayName);

/// <summary>One selectable top-level field discovered from a sample dataset.</summary>
public partial class SftFieldOptionViewModel : ObservableObject
{
    private readonly Action<SftFieldOptionViewModel> _changed;

    [ObservableProperty]
    private bool _isSelected;

    public SftFieldOptionViewModel(
        string name,
        bool isSelected,
        Action<SftFieldOptionViewModel> changed)
    {
        Name = name;
        _isSelected = isSelected;
        _changed = changed;
    }

    public string Name { get; }

    partial void OnIsSelectedChanged(bool value) => _changed(this);
}
