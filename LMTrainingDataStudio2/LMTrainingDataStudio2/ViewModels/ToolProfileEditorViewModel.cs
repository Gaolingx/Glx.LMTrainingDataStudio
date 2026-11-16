using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.ViewModels;

/// <summary>
/// Edits a Tool Profile: tool list, parameter tree and JSON import/export.
/// </summary>
public partial class ToolProfileEditorViewModel : ObservableObject
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly Action<ToolProfileBlockConfig> _apply;
    private string _profileId;
    private bool _syncing;

    [ObservableProperty]
    private string _name;

    private string _profileJson;

    public string ProfileJson
    {
        get => _profileJson;
        set => SetProperty(ref _profileJson, value);
    }

    [ObservableProperty]
    private string? _profileJsonError;

    [ObservableProperty]
    private ToolEditorViewModel? _selectedTool;

    public ToolProfileEditorViewModel(ToolProfile profile, Action<ToolProfileBlockConfig> apply)
    {
        _apply = apply;
        _profileId = profile.Id;
        _name = profile.Name;

        foreach (var tool in profile.Tools ?? new List<ToolDefinition>())
        {
            Tools.Add(new ToolEditorViewModel(tool, Push));
        }

        _selectedTool = Tools.FirstOrDefault();
        _profileJson = JsonSerializer.Serialize(BuildProfile(), JsonOptions);
    }

    public ObservableCollection<ToolEditorViewModel> Tools { get; } = new();

    partial void OnNameChanged(string value) => Push();

    [RelayCommand]
    private void AddTool()
    {
        var definition = new ToolDefinition
        {
            Function = new ToolFunction
            {
                Name = $"tool_{Tools.Count + 1}",
                Description = string.Empty,
                Parameters = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>()
                }
            }
        };

        var editor = new ToolEditorViewModel(definition, Push);
        Tools.Add(editor);
        SelectedTool = editor;
        Push();
    }

    [RelayCommand]
    private void RemoveSelectedTool()
    {
        if (SelectedTool is null) return;

        var index = Tools.IndexOf(SelectedTool);
        Tools.Remove(SelectedTool);
        SelectedTool = Tools.Count == 0 ? null : Tools[Math.Min(index, Tools.Count - 1)];
        Push();
    }

    [RelayCommand]
    private void ImportJson()
    {
        if (string.IsNullOrWhiteSpace(ProfileJson))
        {
            ProfileJsonError = "Profile JSON is empty.";
            return;
        }

        try
        {
            var tools = ParseTools(ProfileJson, out var profileName, out var profileId);

            _syncing = true;
            try
            {
                _profileId = profileId ?? _profileId;
                Name = profileName ?? Name;

                Tools.Clear();
                foreach (var tool in tools)
                {
                    Tools.Add(new ToolEditorViewModel(tool, Push));
                }

                SelectedTool = Tools.FirstOrDefault();
            }
            finally
            {
                _syncing = false;
            }

            ProfileJsonError = null;
            Push();
        }
        catch (JsonException ex)
        {
            ProfileJsonError = $"Invalid tool profile JSON: {ex.Message}";
        }
        catch (NotSupportedException ex)
        {
            ProfileJsonError = ex.Message;
        }
    }

    [RelayCommand]
    private void ExportJson()
    {
        ProfileJson = JsonSerializer.Serialize(BuildProfile(), JsonOptions);
        ProfileJsonError = null;
    }

    private void Push()
    {
        if (_syncing) return;

        var profile = BuildProfile();
        _profileJson = JsonSerializer.Serialize(profile, JsonOptions);
        OnPropertyChanged(nameof(ProfileJson));
        _apply(new ToolProfileBlockConfig(profile));
    }

    private ToolProfile BuildProfile() => new()
    {
        Id = _profileId,
        Name = Name ?? string.Empty,
        Tools = Tools.Select(tool => tool.ToDefinition()).ToList()
    };

    private static List<ToolDefinition> ParseTools(
        string json,
        out string? profileName,
        out string? profileId)
    {
        using var document = JsonDocument.Parse(json);
        profileName = null;
        profileId = null;

        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize<List<ToolDefinition>>(json, JsonOptions)
                ?? new List<ToolDefinition>();
        }

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new NotSupportedException(
                "Tool profile JSON must be an object with a tools array, a tool array, or a single tool object.");
        }

        var profile = JsonSerializer.Deserialize<ToolProfile>(json, JsonOptions);
        if (profile is not null && document.RootElement.TryGetProperty("tools", out _))
        {
            profileName = profile.Name;
            profileId = profile.Id;
            return profile.Tools ?? new List<ToolDefinition>();
        }

        var tool = JsonSerializer.Deserialize<ToolDefinition>(json, JsonOptions);
        return tool is null ? new List<ToolDefinition>() : new List<ToolDefinition> { tool };
    }
}