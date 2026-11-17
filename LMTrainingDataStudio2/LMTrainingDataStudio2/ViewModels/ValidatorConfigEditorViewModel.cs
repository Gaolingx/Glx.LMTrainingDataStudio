using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services.Executors;

namespace LMTrainingDataStudio2.ViewModels;

public sealed record ValidatorRuleKindOption(string Id, string DisplayName);

/// <summary>
/// One editable validator rule in the additional-rules list.
/// </summary>
public partial class ValidatorRuleEditorViewModel : ObservableObject
{
    private readonly Action _changed;

    [ObservableProperty]
    private ValidatorRuleKindOption _selectedKind;

    [ObservableProperty]
    private string _rule;

    [ObservableProperty]
    private bool _negate;

    public ValidatorRuleEditorViewModel(
        ValidatorRuleSpec spec,
        IReadOnlyList<ValidatorRuleKindOption> kinds,
        Action changed)
    {
        _changed = changed;
        _rule = spec.Rule;
        _negate = spec.Negate;
        _selectedKind = kinds.FirstOrDefault(option =>
            string.Equals(option.Id, spec.Kind, StringComparison.OrdinalIgnoreCase)) ?? kinds[0];
    }

    public ValidatorRuleSpec ToSpec() => new(SelectedKind.Id, Rule ?? string.Empty, Negate);

    partial void OnSelectedKindChanged(ValidatorRuleKindOption value) => _changed();

    partial void OnRuleChanged(string value) => _changed();

    partial void OnNegateChanged(bool value) => _changed();
}

/// <summary>
/// Edits validator rules, match mode, timeout, reject behavior and JSON Schema documents.
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
    private bool _matchAllRules;

    [ObservableProperty]
    private int _timeoutMs;

    [ObservableProperty]
    private string? _ruleError;

    public ValidatorConfigEditorViewModel(ValidatorBlockConfig config, Action<ValidatorBlockConfig> apply)
    {
        _apply = apply;
        _cleanup = config.Cleanup;
        _rejectOnFailure = config.RejectOnFailure;
        _rejectOutputPath = config.RejectOutputPath ?? string.Empty;
        _matchAllRules = config.MatchAllRules;
        _timeoutMs = config.TimeoutMs;

        var sources = config.Rules is { Length: > 0 }
            ? config.Rules
            : [new ValidatorRuleSpec(config.RuleKind, config.Rule)];
        var first = sources[0];
        _rule = first.Rule;
        _selectedRuleKind = RuleKinds.FirstOrDefault(option =>
            string.Equals(option.Id, first.Kind, StringComparison.OrdinalIgnoreCase)) ?? RuleKinds[0];

        foreach (var spec in sources.Skip(1))
        {
            AdditionalRules.Add(new ValidatorRuleEditorViewModel(spec, RuleKinds, Push));
        }
    }

    public IReadOnlyList<ValidatorRuleKindOption> RuleKinds { get; } = new[]
    {
        new ValidatorRuleKindOption("expression", "Expression rule"),
        new ValidatorRuleKindOption("schema", "JSON Schema"),
        new ValidatorRuleKindOption("regex", "Regular expression"),
        new ValidatorRuleKindOption("script", "Script (renders true/false)")
    };

    public ObservableCollection<ValidatorRuleEditorViewModel> AdditionalRules { get; } = new();

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

    partial void OnMatchAllRulesChanged(bool value) => Push();

    partial void OnTimeoutMsChanged(int value) => Push();

    [RelayCommand]
    private void AddRule()
    {
        AdditionalRules.Add(new ValidatorRuleEditorViewModel(
            new ValidatorRuleSpec("expression", string.Empty), RuleKinds, Push));
        Push();
    }

    [RelayCommand]
    private void RemoveRule(ValidatorRuleEditorViewModel? rule)
    {
        if (rule is null || !AdditionalRules.Remove(rule)) return;
        Push();
    }

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

    private void Push()
    {
        var rules = new List<ValidatorRuleSpec>
        {
            new(SelectedRuleKind.Id, Rule ?? string.Empty)
        };
        rules.AddRange(AdditionalRules.Select(rule => rule.ToSpec()));

        _apply(new ValidatorBlockConfig(
            SelectedRuleKind.Id,
            Rule ?? string.Empty,
            RejectOnFailure,
            _cleanup,
            string.IsNullOrWhiteSpace(RejectOutputPath) ? null : RejectOutputPath.Trim(),
            rules.ToArray(),
            MatchAllRules,
            TimeoutMs));
    }
}
