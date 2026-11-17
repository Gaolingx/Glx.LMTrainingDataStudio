using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.ViewModels;

/// <summary>
/// Edits one regex replacement rule used by Cleaner and Validator cleanup.
/// </summary>
public partial class RegexReplacementEditorViewModel : ObservableObject
{
    private readonly Action _changed;

    [ObservableProperty]
    private string _pattern;

    [ObservableProperty]
    private string _replacement;

    [ObservableProperty]
    private bool _ignoreCase;

    [ObservableProperty]
    private string? _patternError;

    public RegexReplacementEditorViewModel(RegexReplacementRule rule, Action changed)
    {
        _changed = changed;
        _pattern = rule.Pattern;
        _replacement = rule.Replacement;
        _ignoreCase = rule.IgnoreCase;
        ValidatePattern();
    }

    public RegexReplacementRule ToRule() => new(Pattern ?? string.Empty, Replacement ?? string.Empty, IgnoreCase);

    partial void OnPatternChanged(string value)
    {
        ValidatePattern();
        _changed();
    }

    partial void OnReplacementChanged(string value) => _changed();

    partial void OnIgnoreCaseChanged(bool value) => _changed();

    private void ValidatePattern()
    {
        if (string.IsNullOrWhiteSpace(Pattern))
        {
            PatternError = null;
            return;
        }

        try
        {
            _ = new Regex(Pattern);
            PatternError = null;
        }
        catch (ArgumentException ex)
        {
            PatternError = ex.Message;
        }
    }
}

/// <summary>
/// Shared cleanup-rule editor for Cleaner and Validator nodes.
/// </summary>
public partial class CleanupRulesEditorViewModel : ObservableObject
{
    private readonly Action _changed;

    [ObservableProperty]
    private bool _normalizeWhitespace;

    [ObservableProperty]
    private bool _normalizeNewlines;

    [ObservableProperty]
    private bool _stripAnsi;

    [ObservableProperty]
    private bool _removeReasoningContent;

    [ObservableProperty]
    private bool _dropEmptyMessages;

    public CleanupRulesEditorViewModel(DatasetCleanupRules? rules, Action changed)
    {
        _changed = changed;
        rules ??= new DatasetCleanupRules();

        _normalizeWhitespace = rules.NormalizeWhitespace;
        _normalizeNewlines = rules.NormalizeNewlines;
        _stripAnsi = rules.StripAnsi;
        _removeReasoningContent = rules.RemoveReasoningContent;
        _dropEmptyMessages = rules.DropEmptyMessages;

        foreach (var rule in rules.RegexReplacements ?? [])
        {
            RegexReplacements.Add(new RegexReplacementEditorViewModel(rule, _changed));
        }
    }

    public ObservableCollection<RegexReplacementEditorViewModel> RegexReplacements { get; } = new();

    public bool HasAnyRule =>
        RegexReplacements.Any(rule => !string.IsNullOrWhiteSpace(rule.Pattern)) ||
        NormalizeWhitespace ||
        NormalizeNewlines ||
        StripAnsi ||
        RemoveReasoningContent ||
        DropEmptyMessages;

    partial void OnNormalizeWhitespaceChanged(bool value) => _changed();

    partial void OnNormalizeNewlinesChanged(bool value) => _changed();

    partial void OnStripAnsiChanged(bool value) => _changed();

    partial void OnRemoveReasoningContentChanged(bool value) => _changed();

    partial void OnDropEmptyMessagesChanged(bool value) => _changed();

    [RelayCommand]
    private void AddRegex()
    {
        RegexReplacements.Add(new RegexReplacementEditorViewModel(
            new RegexReplacementRule(string.Empty, string.Empty), _changed));
        _changed();
    }

    [RelayCommand]
    private void RemoveRegex(RegexReplacementEditorViewModel? rule)
    {
        if (rule is null || !RegexReplacements.Remove(rule)) return;
        _changed();
    }

    [RelayCommand]
    private void MoveRegexUp(RegexReplacementEditorViewModel? rule)
    {
        if (rule is null) return;
        var index = RegexReplacements.IndexOf(rule);
        if (index <= 0) return;

        RegexReplacements.Move(index, index - 1);
        _changed();
    }

    [RelayCommand]
    private void MoveRegexDown(RegexReplacementEditorViewModel? rule)
    {
        if (rule is null) return;
        var index = RegexReplacements.IndexOf(rule);
        if (index < 0 || index >= RegexReplacements.Count - 1) return;

        RegexReplacements.Move(index, index + 1);
        _changed();
    }

    public DatasetCleanupRules Build()
    {
        var replacements = RegexReplacements
            .Where(rule => !string.IsNullOrWhiteSpace(rule.Pattern))
            .Select(rule => rule.ToRule())
            .ToArray();

        return new DatasetCleanupRules
        {
            RegexReplacements = replacements.Length > 0 ? replacements : null,
            NormalizeWhitespace = NormalizeWhitespace,
            NormalizeNewlines = NormalizeNewlines,
            StripAnsi = StripAnsi,
            RemoveReasoningContent = RemoveReasoningContent,
            DropEmptyMessages = DropEmptyMessages
        };
    }
}
