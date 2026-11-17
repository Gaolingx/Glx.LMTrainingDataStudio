using CommunityToolkit.Mvvm.ComponentModel;
using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.ViewModels;

/// <summary>
/// Edits cleaner rules through the shared cleanup-rule editor.
/// </summary>
public partial class CleanerConfigEditorViewModel : ObservableObject
{
    private readonly Action<CleanerBlockConfig> _apply;

    public CleanerConfigEditorViewModel(
        CleanerBlockConfig config,
        Action<CleanerBlockConfig> apply)
    {
        _apply = apply;
        Cleanup = new CleanupRulesEditorViewModel(config.Rules, Push);
    }

    public CleanupRulesEditorViewModel Cleanup { get; }

    public bool NormalizeWhitespace
    {
        get => Cleanup.NormalizeWhitespace;
        set => Cleanup.NormalizeWhitespace = value;
    }

    public bool NormalizeNewlines
    {
        get => Cleanup.NormalizeNewlines;
        set => Cleanup.NormalizeNewlines = value;
    }

    public bool StripAnsi
    {
        get => Cleanup.StripAnsi;
        set => Cleanup.StripAnsi = value;
    }

    public bool RemoveReasoningContent
    {
        get => Cleanup.RemoveReasoningContent;
        set => Cleanup.RemoveReasoningContent = value;
    }

    public bool DropEmptyMessages
    {
        get => Cleanup.DropEmptyMessages;
        set => Cleanup.DropEmptyMessages = value;
    }

    private void Push() => _apply(new CleanerBlockConfig(Cleanup.Build()));
}
