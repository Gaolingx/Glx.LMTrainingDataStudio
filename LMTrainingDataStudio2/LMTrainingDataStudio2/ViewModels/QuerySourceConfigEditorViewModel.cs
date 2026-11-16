using CommunityToolkit.Mvvm.ComponentModel;
using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.ViewModels;

public sealed record QuerySetModeOption(QuerySetMode Mode, string DisplayName);

/// <summary>
/// Edits a query source configuration and writes changes back to the owning block.
/// </summary>
public partial class QuerySourceConfigEditorViewModel : ObservableObject
{
    private readonly Action<QuerySourceBlockConfig> _apply;

    [ObservableProperty]
    private string _queriesText;

    [ObservableProperty]
    private QuerySetModeOption _selectedMode;

    [ObservableProperty]
    private int _repeat;

    [ObservableProperty]
    private string _systemPrompt;

    public QuerySourceConfigEditorViewModel(
        QuerySourceBlockConfig config,
        Action<QuerySourceBlockConfig> apply)
    {
        _apply = apply;
        _queriesText = string.Join(Environment.NewLine, config.Queries);
        _repeat = config.Repeat;
        _systemPrompt = config.SystemPrompt ?? string.Empty;
        _selectedMode = Modes.FirstOrDefault(option => option.Mode == config.Mode) ?? Modes[0];
    }

    public IReadOnlyList<QuerySetModeOption> Modes { get; } = new[]
    {
        new QuerySetModeOption(QuerySetMode.MultipleQueries, "Multiple queries"),
        new QuerySetModeOption(QuerySetMode.SameQuery, "Same query (first)")
    };

    partial void OnQueriesTextChanged(string value) => Push();

    partial void OnSelectedModeChanged(QuerySetModeOption value) => Push();

    partial void OnRepeatChanged(int value) => Push();

    partial void OnSystemPromptChanged(string value) => Push();

    private void Push()
    {
        var queries = (QueriesText ?? string.Empty)
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Select(query => query.Trim())
            .Where(query => query.Length > 0)
            .ToArray();

        _apply(new QuerySourceBlockConfig(
            queries,
            SelectedMode.Mode,
            Repeat,
            string.IsNullOrWhiteSpace(SystemPrompt) ? null : SystemPrompt));
    }
}
