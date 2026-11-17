using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LMTrainingDataStudio2.Services.Datasets;

/// <summary>One editable top-level field of a dataset row.</summary>
public sealed record DatasetFieldValue(string Name, string ValueText, string Kind);

/// <summary>
/// Reads a JSON object row into editable fields and rebuilds it while preserving
/// every field, including nested objects and arrays edited as JSON text.
/// </summary>
public static class DatasetFieldEditor
{
    private static readonly JsonSerializerOptions OutputOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static bool TryReadObject(
        string? json,
        out IReadOnlyList<DatasetFieldValue> fields,
        out string? error)
    {
        fields = Array.Empty<DatasetFieldValue>();
        error = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "Row JSON is empty.";
            return false;
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            error = $"Invalid JSON: {ex.Message}";
            return false;
        }

        if (node is not JsonObject obj)
        {
            error = "Row JSON is not an object; use the raw JSON editor.";
            return false;
        }

        fields = obj
            .Select(pair => new DatasetFieldValue(
                pair.Key,
                pair.Value?.ToJsonString(OutputOptions) ?? "null",
                DescribeKind(pair.Value)))
            .ToArray();
        return true;
    }

    public static bool TryBuild(
        IEnumerable<DatasetFieldValue> fields,
        out string json,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(fields);
        json = string.Empty;
        error = null;

        var obj = new JsonObject();
        foreach (var field in fields)
        {
            var name = field.Name ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                error = "Field name must not be empty.";
                return false;
            }

            if (obj.ContainsKey(name))
            {
                error = $"Field '{name}' appears more than once.";
                return false;
            }

            try
            {
                obj[name] = JsonNode.Parse(field.ValueText ?? "null");
            }
            catch (JsonException ex)
            {
                error = $"Field '{name}' is not valid JSON: {ex.Message}";
                return false;
            }
        }

        json = obj.ToJsonString(OutputOptions);
        return true;
    }

    public static string DescribeKind(JsonNode? node) => node switch
    {
        null => "null",
        JsonObject => "object",
        JsonArray => "array",
        JsonValue value => value.GetValueKind() switch
        {
            JsonValueKind.String => "string",
            JsonValueKind.Number => "number",
            JsonValueKind.True or JsonValueKind.False => "bool",
            JsonValueKind.Null => "null",
            _ => "value"
        },
        _ => "value"
    };
}
