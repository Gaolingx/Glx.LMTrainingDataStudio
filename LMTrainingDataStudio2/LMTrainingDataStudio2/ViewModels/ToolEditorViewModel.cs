using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.ViewModels;

/// <summary>
/// Edits one OpenAI tool definition. The parameter schema is editable as JSON
/// or through the nested parameter tree.
/// </summary>
public partial class ToolEditorViewModel : ObservableObject
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly Action _onChanged;
    private bool _syncing;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _description;

    private string _parametersJson;

    [ObservableProperty]
    private string? _parametersError;

    public string ParametersJson
    {
        get => _parametersJson;
        set
        {
            if (!SetProperty(ref _parametersJson, value) || _syncing) return;

            try
            {
                using var document = JsonDocument.Parse(value);
                RebuildTree(value);
                ParametersError = null;
                NotifyChanged();
            }
            catch (JsonException ex)
            {
                ParametersError = $"Parameters must be valid JSON Schema: {ex.Message}";
            }
        }
    }

    public ToolEditorViewModel(ToolDefinition tool, Action onChanged)
    {
        _onChanged = onChanged;
        _name = tool.Function.Name;
        _description = tool.Function.Description ?? string.Empty;
        _parametersJson = JsonSerializer.Serialize(
            tool.Function.Parameters ?? DefaultSchema(),
            JsonOptions);

        RebuildTree(_parametersJson);
    }

    public ObservableCollection<ToolParameterNodeViewModel> Parameters { get; } = new();

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "(unnamed tool)" : Name;

    [RelayCommand]
    private void AddParameter()
    {
        var node = new ToolParameterNodeViewModel(
            Parameters,
            null,
            $"field{Parameters.Count + 1}",
            "string",
            null,
            false,
            SyncFromTree);
        Parameters.Add(node);
        SyncFromTree();
    }

    partial void OnNameChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayName));
        NotifyChanged();
    }

    partial void OnDescriptionChanged(string value) => NotifyChanged();

    public ToolDefinition ToDefinition()
    {
        Dictionary<string, object>? parameters;
        try
        {
            parameters = JsonSerializer.Deserialize<Dictionary<string, object>>(ParametersJson);
        }
        catch (JsonException)
        {
            var fallback = TryBuildObjectSchema(out _) ??
                           DefaultSchema().ToDictionary(pair => pair.Key, pair => (object?)pair.Value);
            parameters = JsonSerializer.Deserialize<Dictionary<string, object>>(
                JsonSerializer.Serialize(fallback));
        }

        return new ToolDefinition
        {
            Type = "function",
            Function = new ToolFunction
            {
                Name = Name ?? string.Empty,
                Description = string.IsNullOrWhiteSpace(Description) ? null : Description,
                Parameters = parameters ?? DefaultSchema()
            }
        };
    }

    private void RebuildTree(string json)
    {
        using var document = JsonDocument.Parse(json);
        var schema = document.RootElement;

        Parameters.Clear();
        if (schema.ValueKind == JsonValueKind.Object &&
            schema.TryGetProperty("properties", out var properties) &&
            properties.ValueKind == JsonValueKind.Object)
        {
            var required = ReadRequiredNames(schema);
            foreach (var property in properties.EnumerateObject())
            {
                Parameters.Add(CreateNode(
                    property.Name,
                    property.Value,
                    required.Contains(property.Name),
                    parent: null));
            }
        }
        else if (schema.ValueKind == JsonValueKind.Object &&
                 schema.TryGetProperty("items", out var items))
        {
            Parameters.Add(CreateNode("item", items, isRequired: false, parent: null));
        }
    }

    private ToolParameterNodeViewModel CreateNode(
        string name,
        JsonElement schema,
        bool isRequired,
        ToolParameterNodeViewModel? parent)
    {
        var type = schema.ValueKind == JsonValueKind.Object &&
                   schema.TryGetProperty("type", out var typeElement) &&
                   typeElement.ValueKind == JsonValueKind.String
            ? typeElement.GetString() ?? "string"
            : "string";

        var description = schema.ValueKind == JsonValueKind.Object &&
                          schema.TryGetProperty("description", out var descriptionElement) &&
                          descriptionElement.ValueKind == JsonValueKind.String
            ? descriptionElement.GetString()
            : null;

        var node = new ToolParameterNodeViewModel(
            Parameters,
            parent,
            name,
            type,
            description,
            isRequired,
            SyncFromTree,
            ReadEnumText(schema),
            ReadDefaultText(schema));

        if (schema.ValueKind == JsonValueKind.Object &&
            schema.TryGetProperty("properties", out var properties) &&
            properties.ValueKind == JsonValueKind.Object)
        {
            var required = ReadRequiredNames(schema);
            foreach (var property in properties.EnumerateObject())
            {
                node.Children.Add(CreateNode(
                    property.Name,
                    property.Value,
                    required.Contains(property.Name),
                    node));
            }
        }
        else if (schema.ValueKind == JsonValueKind.Object &&
                 schema.TryGetProperty("items", out var items))
        {
            node.Children.Add(CreateNode("item", items, isRequired: false, node));
        }

        return node;
    }

    private void SyncFromTree()
    {
        if (_syncing) return;

        var schema = TryBuildObjectSchema(out var error);
        if (schema is null)
        {
            ParametersError = error;
            return;
        }

        _syncing = true;
        try
        {
            _parametersJson = JsonSerializer.Serialize(schema, JsonOptions);
            OnPropertyChanged(nameof(ParametersJson));
            ParametersError = null;
            NotifyChanged();
        }
        finally
        {
            _syncing = false;
        }
    }

    private Dictionary<string, object?>? TryBuildObjectSchema(out string? error)
    {
        error = null;
        var properties = BuildProperties(Parameters, ref error);
        if (properties is null) return null;

        return new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = Parameters
                .Where(node => node.IsRequired && !string.IsNullOrWhiteSpace(node.Name))
                .Select(node => node.Name)
                .ToList()
        };
    }

    private static Dictionary<string, object?>? BuildProperties(
        IEnumerable<ToolParameterNodeViewModel> nodes,
        ref string? error)
    {
        var properties = new Dictionary<string, object?>();
        foreach (var node in nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Name)) continue;

            var schema = BuildNodeSchema(node, ref error);
            if (schema is null) return null;
            properties[node.Name] = schema;
        }

        return properties;
    }

    private static Dictionary<string, object?>? BuildNodeSchema(
        ToolParameterNodeViewModel node,
        ref string? error)
    {
        var schema = new Dictionary<string, object?>
        {
            ["type"] = node.Type
        };

        if (!string.IsNullOrWhiteSpace(node.Description))
        {
            schema["description"] = node.Description;
        }

        if (node.TryGetEnumValues(out var enumValues, out var enumError))
        {
            if (enumValues is not null)
            {
                schema["enum"] = enumValues;
            }
        }
        else
        {
            error = $"{node.DisplayName}: {enumError}";
            return null;
        }

        if (node.TryGetDefaultValue(out var hasDefault, out var defaultValue, out var defaultError))
        {
            if (hasDefault)
            {
                schema["default"] = defaultValue;
            }
        }
        else
        {
            error = $"{node.DisplayName}: {defaultError}";
            return null;
        }

        if (node.Type == "object")
        {
            var properties = BuildProperties(node.Children, ref error);
            if (properties is null) return null;
            schema["properties"] = properties;

            var required = node.Children
                .Where(child => child.IsRequired && !string.IsNullOrWhiteSpace(child.Name))
                .Select(child => child.Name)
                .ToList();
            if (required.Count > 0)
            {
                schema["required"] = required;
            }
        }
        else if (node.Type == "array")
        {
            if (node.Children.Count > 0)
            {
                var items = BuildNodeSchema(node.Children[0], ref error);
                if (items is null) return null;
                schema["items"] = items;
            }
            else
            {
                schema["items"] = new Dictionary<string, object?> { ["type"] = "string" };
            }
        }

        return schema;
    }

    private static string ReadEnumText(JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object ||
            !schema.TryGetProperty("enum", out var enumElement) ||
            enumElement.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        if (enumElement.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String))
        {
            return string.Join(", ", enumElement.EnumerateArray()
                .Select(item => item.GetString()));
        }

        return enumElement.GetRawText();
    }

    private static string ReadDefaultText(JsonElement schema) =>
        schema.ValueKind == JsonValueKind.Object &&
        schema.TryGetProperty("default", out var defaultElement)
            ? defaultElement.GetRawText()
            : string.Empty;

    private static HashSet<string> ReadRequiredNames(JsonElement schema)
    {
        var required = new HashSet<string>(StringComparer.Ordinal);
        if (schema.TryGetProperty("required", out var requiredElement) &&
            requiredElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in requiredElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { } name)
                {
                    required.Add(name);
                }
            }
        }

        return required;
    }

    private static Dictionary<string, object> DefaultSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object>()
    };

    private void NotifyChanged() => _onChanged();
}
