using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services.Executors;

namespace LMTrainingDataStudio2.ViewModels;

public sealed record ValidatorRuleKindOption(string Id, string DisplayName);

/// <summary>
/// Edits a Validator rule, reject behavior and JSON Schema documents.
/// </summary>
public partial class ValidatorConfigEditorViewModel : ObservableObject
{
    private readonly Action<ValidatorBlockConfig> _apply;
    private readonly DatasetCleanupRules? _cleanup;

    [ObservableProperty]
    private ValidatorRuleKindOption _selectedRuleKind;

    [ObservableProperty]
    private string _rule;

    [ObservableProperty]
    private bool _rejectOnFailure;

    [ObservableProperty]
    private string _rejectOutputPath;

    [ObservableProperty]
    private string? _ruleError;

    public ValidatorConfigEditorViewModel(ValidatorBlockConfig config, Action<ValidatorBlockConfig> apply)
    {
        _apply = apply;
        _cleanup = config.Cleanup;
        _rule = config.Rule;
        _rejectOnFailure = config.RejectOnFailure;
        _rejectOutputPath = config.RejectOutputPath ?? string.Empty;
        _selectedRuleKind = RuleKinds.FirstOrDefault(option =>
            string.Equals(option.Id, config.RuleKind, StringComparison.OrdinalIgnoreCase)) ?? RuleKinds[0];
    }

    public IReadOnlyList<ValidatorRuleKindOption> RuleKinds { get; } = new[]
    {
        new ValidatorRuleKindOption("expression", "Expression rule"),
        new ValidatorRuleKindOption("schema", "JSON Schema")
    };

    partial void OnSelectedRuleKindChanged(ValidatorRuleKindOption value)
    {
        RuleError = null;
        Push();
    }

    partial void OnRuleChanged(string value)
    {
        RuleError = null;
        Push();
    }

    partial void OnRejectOnFailureChanged(bool value) => Push();

    partial void OnRejectOutputPathChanged(string value) => Push();

    [RelayCommand]
    private void ValidateRule()
    {
        RuleError = null;
        if (!string.Equals(SelectedRuleKind.Id, "schema", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(Rule))
        {
            return;
        }

        try
        {
            JsonSchemaRuleEvaluator.Parse(Rule);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            RuleError = $"Invalid JSON Schema: {ex.Message}";
        }
    }

    private void Push() => _apply(new ValidatorBlockConfig(
        SelectedRuleKind.Id,
        Rule ?? string.Empty,
        RejectOnFailure,
        _cleanup,
        string.IsNullOrWhiteSpace(RejectOutputPath) ? null : RejectOutputPath.Trim()));
}