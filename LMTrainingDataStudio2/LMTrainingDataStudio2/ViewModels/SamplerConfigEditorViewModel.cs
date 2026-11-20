using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services.Executors;

namespace LMTrainingDataStudio2.ViewModels;

/// <summary>
/// Edits sampler count/ratio or deterministic multi-file splits.
/// </summary>
public partial class SamplerConfigEditorViewModel : ObservableObject
{
    private readonly Action<SamplerBlockConfig> _apply;
    private long? _count;
    private double? _ratio;
    private string[] _splits;

    [ObservableProperty]
    private string _countText;

    [ObservableProperty]
    private string _ratioText;

    [ObservableProperty]
    private int _seed;

    [ObservableProperty]
    private string _splitsText;

    [ObservableProperty]
    private string _outputDirectory;

    [ObservableProperty]
    private string _stratifyField;

    [ObservableProperty]
    private string _previewRowsText;

    [ObservableProperty]
    private string? _statsPreview;

    [ObservableProperty]
    private string? _validationError;

    public SamplerConfigEditorViewModel(SamplerBlockConfig config, Action<SamplerBlockConfig> apply)
    {
        _apply = apply;
        _count = config.Count;
        _ratio = config.Ratio;
        _splits = [.. config.Splits];
        _countText = config.Count?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        _ratioText = config.Ratio?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        _seed = config.Seed;
        _splitsText = string.Join(Environment.NewLine, config.Splits);
        _outputDirectory = config.OutputDirectory ?? "out/splits";
        _stratifyField = config.StratifyField ?? string.Empty;
        _previewRowsText = "1000";
        UpdateStatsPreview();
    }

    partial void OnCountTextChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            _count = null;
            ValidationError = null;
            UpdateStatsPreview();
            Push();
            return;
        }

        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) || count < 0)
        {
            ValidationError = "Count must be a non-negative integer.";
            return;
        }

        _count = count;
        ValidationError = null;
        UpdateStatsPreview();
        Push();
    }

    partial void OnRatioTextChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            _ratio = null;
            ValidationError = null;
            UpdateStatsPreview();
            Push();
            return;
        }

        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var ratio) ||
            ratio <= 0 || ratio > 1)
        {
            ValidationError = "Ratio must be greater than 0 and at most 1.";
            return;
        }

        _ratio = ratio;
        ValidationError = null;
        UpdateStatsPreview();
        Push();
    }

    partial void OnSeedChanged(int value) => Push();

    partial void OnSplitsTextChanged(string value)
    {
        var splits = (value ?? string.Empty)
            .Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (splits.Length > 0 &&
            !SamplerSplitPlan.TryParse(splits, out _, out var error))
        {
            ValidationError = error;
            return;
        }

        _splits = splits;
        ValidationError = null;
        UpdateStatsPreview();
        Push();
    }

    partial void OnOutputDirectoryChanged(string value) => Push();

    partial void OnStratifyFieldChanged(string value)
    {
        UpdateStatsPreview();
        Push();
    }

    partial void OnPreviewRowsTextChanged(string value) => UpdateStatsPreview();

    private void UpdateStatsPreview()
    {
        if (!long.TryParse(
                PreviewRowsText,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var rows) ||
            rows <= 0)
        {
            rows = 1000;
        }

        string? error = null;
        SamplerSplitPlan? plan = null;
        var parsed = _splits.Length > 0 &&
                     SamplerSplitPlan.TryParse(_splits, out plan, out error);
        if (parsed)
        {
            var parts = plan!.Specs.Select(spec =>
            {
                if (spec.IsCount)
                {
                    return $"{spec.Name}: {spec.Count:N0}";
                }

                return $"{spec.Name}: {spec.Ratio:P1} (~{Math.Round(rows * spec.Ratio):N0})";
            });
            var stratified = string.IsNullOrWhiteSpace(StratifyField)
                ? string.Empty
                : $" · stratified by {StratifyField.Trim()}";
            StatsPreview = $"Splits: {string.Join(" | ", parts)}{stratified}";
            return;
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            StatsPreview = error;
            return;
        }

        var modeSuffix = string.IsNullOrWhiteSpace(StratifyField)
            ? string.Empty
            : $" · stratified by {StratifyField.Trim()}";
        StatsPreview = _count is { } count
            ? $"Sample {count:N0} row(s) (reservoir){modeSuffix}"
            : _ratio is { } ratio
                ? $"Sample {ratio:P1} (~{Math.Round(rows * ratio):N0} of {rows:N0}){modeSuffix}"
                : "No sampling configured.";
    }

    private void Push() => _apply(new SamplerBlockConfig(
        _count,
        _ratio,
        Seed,
        [.. _splits],
        string.IsNullOrWhiteSpace(OutputDirectory) ? null : OutputDirectory.Trim(),
        string.IsNullOrWhiteSpace(StratifyField) ? null : StratifyField.Trim()));
}
