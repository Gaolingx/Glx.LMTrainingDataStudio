using CommunityToolkit.Mvvm.ComponentModel;
using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.ViewModels;

/// <summary>
/// Edits cleaner toggles. Regex replacements remain in the Config JSON editor.
/// </summary>
public partial class CleanerConfigEditorViewModel : ObservableObject
{
    private readonly CleanerBlockConfig _base;
    private readonly Action<CleanerBlockConfig> _apply;

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

    public CleanerConfigEditorViewModel(
        CleanerBlockConfig config,
        Action<CleanerBlockConfig> apply)
    {
        _base = config;
        _apply = apply;
        _normalizeWhitespace = config.Rules.NormalizeWhitespace;
        _normalizeNewlines = config.Rules.NormalizeNewlines;
        _stripAnsi = config.Rules.StripAnsi;
        _removeReasoningContent = config.Rules.RemoveReasoningContent;
        _dropEmptyMessages = config.Rules.DropEmptyMessages;
    }

    partial void OnNormalizeWhitespaceChanged(bool value) => Push();

    partial void OnNormalizeNewlinesChanged(bool value) => Push();

    partial void OnStripAnsiChanged(bool value) => Push();

    partial void OnRemoveReasoningContentChanged(bool value) => Push();

    partial void OnDropEmptyMessagesChanged(bool value) => Push();

    private void Push()
    {
        _apply(new CleanerBlockConfig(_base.Rules with
        {
            NormalizeWhitespace = NormalizeWhitespace,
            NormalizeNewlines = NormalizeNewlines,
            StripAnsi = StripAnsi,
            RemoveReasoningContent = RemoveReasoningContent,
            DropEmptyMessages = DropEmptyMessages
        }));
    }
}
