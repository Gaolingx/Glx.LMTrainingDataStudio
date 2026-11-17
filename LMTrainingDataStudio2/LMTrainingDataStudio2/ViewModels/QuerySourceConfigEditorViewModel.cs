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

    [ObservableProperty]
    private string _sourcePath;

    [ObservableProperty]
    private string _queryField;

    [ObservableProperty]
    private string _systemField;

    [ObservableProperty]
    private string _templateVariablesJson;

    [ObservableProperty]
    private string? _templateVariablesError;

    public QuerySourceConfigEditorViewModel(
        QuerySourceBlockConfig config,
        Action<QuerySourceBlockConfig> apply)
    {
        _apply = apply;
        _queriesText = string.Join(Environment.NewLine, config.Queries);
        _repeat = config.Repeat;
        _systemPrompt = config.SystemPrompt ?? string.Empty;
        _sourcePath = config.SourcePath ?? string.Empty;
        _queryField = config.QueryField;
        _systemField = config.SystemField ?? string.Empty;
        _templateVariablesJson = config.TemplateVariablesJson ?? string.Empty;
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

    partial void OnSourcePathChanged(string value) => Push();

    partial void OnQueryFieldChanged(string value) => Push();

    partial void OnSystemFieldChanged(string value) => Push();

    partial void OnTemplateVariablesJsonChanged(string value)
    {
        TemplateVariablesError = null;
        if (!string.IsNullOrWhiteSpace(value))
        {
            try
            {
                using var document = System.Text.Json.JsonDocument.Parse(value);
                if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
                {
                    TemplateVariablesError = "Template variables must be a JSON object.";
                }
            }
            catch (System.Text.Json.JsonException ex)
            {
                TemplateVariablesError = $"Invalid JSON: {ex.Message}";
            }
        }

        Push();
    }

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
            string.IsNullOrWhiteSpace(SystemPrompt) ? null : SystemPrompt,
            string.IsNullOrWhiteSpace(SourcePath) ? null : SourcePath.Trim(),
            string.IsNullOrWhiteSpace(QueryField) ? "query" : QueryField.Trim(),
            string.IsNullOrWhiteSpace(SystemField) ? null : SystemField.Trim(),
            string.IsNullOrWhiteSpace(TemplateVariablesJson) ? null : TemplateVariablesJson));
    }
}
