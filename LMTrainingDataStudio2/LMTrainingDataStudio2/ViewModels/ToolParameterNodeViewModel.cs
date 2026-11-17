using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LMTrainingDataStudio2.ViewModels;

/// <summary>
/// Editable node in a tool parameter schema tree.
/// </summary>
public partial class ToolParameterNodeViewModel : ObservableObject
{
    private readonly ObservableCollection<ToolParameterNodeViewModel> _owner;
    private readonly Action _onChanged;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _type;

    [ObservableProperty]
    private string _description;

    [ObservableProperty]
    private bool _isRequired;

    [ObservableProperty]
    private string _enumText;

    [ObservableProperty]
    private string _defaultText;

    public ToolParameterNodeViewModel(
        ObservableCollection<ToolParameterNodeViewModel> owner,
        ToolParameterNodeViewModel? parent,
        string name,
        string type,
        string? description,
        bool isRequired,
        Action onChanged,
        string? enumText = null,
        string? defaultText = null)
    {
        _owner = owner;
        _onChanged = onChanged;
        Parent = parent;

        _name = name;
        _type = type;
        _description = description ?? string.Empty;
        _isRequired = isRequired;
        _enumText = enumText ?? string.Empty;
        _defaultText = defaultText ?? string.Empty;

        AddChildCommand = new RelayCommand(AddChild);
        RemoveCommand = new RelayCommand(Remove);
    }

    public ToolParameterNodeViewModel? Parent { get; }

    public ObservableCollection<ToolParameterNodeViewModel> Children { get; } = new();

    public IReadOnlyList<string> Types { get; } = new[]
    {
        "string", "number", "integer", "boolean", "object", "array"
    };

    public IRelayCommand AddChildCommand { get; }

    public IRelayCommand RemoveCommand { get; }

    partial void OnNameChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayName));
        _onChanged();
    }

    partial void OnTypeChanged(string value) => _onChanged();

    partial void OnDescriptionChanged(string value) => _onChanged();

    partial void OnIsRequiredChanged(bool value) => _onChanged();

    partial void OnEnumTextChanged(string value) => _onChanged();

    partial void OnDefaultTextChanged(string value) => _onChanged();

    /// <summary>
    /// Comma-separated values are treated as strings; a JSON array is parsed as-is
    /// so numbers, booleans and null stay typed.
    /// </summary>
    public bool TryGetEnumValues(out JsonArray? values, out string? error)
    {
        values = null;
        error = null;

        var text = EnumText?.Trim() ?? string.Empty;
        if (text.Length == 0) return true;

        if (!text.StartsWith('['))
        {
            values = new JsonArray();
            foreach (var part in text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                values.Add(part);
            }

            return true;
        }

        try
        {
            if (JsonNode.Parse(text) is not JsonArray array)
            {
                error = "enum must be a JSON array.";
                return false;
            }

            values = array;
            return true;
        }
        catch (JsonException ex)
        {
            error = $"enum is not valid JSON: {ex.Message}";
            return false;
        }
    }

    public bool TryGetDefaultValue(out bool hasDefault, out JsonNode? value, out string? error)
    {
        hasDefault = false;
        value = null;
        error = null;

        var text = DefaultText?.Trim() ?? string.Empty;
        if (text.Length == 0) return true;

        try
        {
            value = JsonNode.Parse(text);
            hasDefault = true;
            return true;
        }
        catch (JsonException ex)
        {
            error = $"default is not valid JSON: {ex.Message}";
            return false;
        }
    }

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "(unnamed)" : Name;

    private void AddChild()
    {
        var child = new ToolParameterNodeViewModel(
            _owner,
            this,
            $"field{Children.Count + 1}",
            "string",
            null,
            false,
            _onChanged);
        Children.Add(child);
        _onChanged();
    }

    private void Remove()
    {
        if (Parent is not null)
        {
            Parent.Children.Remove(this);
        }
        else
        {
            _owner.Remove(this);
        }

        _onChanged();
    }
}
