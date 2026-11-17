using System.Globalization;
using System.Text.Json;
using Fluid;
using Fluid.Values;

namespace LMTrainingDataStudio2.Services.Executors;

public sealed record TemplateValidationResult(bool IsValid, string? Error);

/// <summary>
/// Fluid-powered template rendering for Expression and LLM prompts.
/// Supports variables, conditions, loops and filters (for example
/// <c>{{ messages | size }}</c> or <c>{{ content | upcase }}</c>).
/// </summary>
internal static class TemplateRenderer
{
    private static readonly FluidParser Parser = new();
    private static readonly TemplateOptions Options = CreateOptions();

    public static TemplateValidationResult Validate(string? template)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return new TemplateValidationResult(true, null);
        }

        return Parser.TryParse(template, out _, out var error)
            ? new TemplateValidationResult(true, null)
            : new TemplateValidationResult(false, error);
    }

    public static async Task<string> RenderAsync(
        string template,
        JsonElement root,
        IReadOnlyDictionary<string, string>? aliases = null)
    {
        if (!Parser.TryParse(template, out var parsed, out var error))
        {
            throw new InvalidOperationException($"Invalid template: {error}");
        }

        var model = BuildModel(root, aliases);
        var context = new TemplateContext(model, Options);
        return await parsed.RenderAsync(context);
    }

    public static string? ResolveString(JsonElement root, string path)
    {
        var value = ResolveValue(root, path);
        return value switch
        {
            null => null,
            string text => text,
            bool flag => flag ? "true" : "false",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => JsonSerializer.Serialize(value)
        };
    }

    private static object? ResolveValue(JsonElement root, string path)
    {
        var current = root;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == "length" && current.ValueKind == JsonValueKind.Array)
            {
                return current.GetArrayLength();
            }

            if (current.ValueKind == JsonValueKind.Array &&
                int.TryParse(segment, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) &&
                index >= 0 && index < current.GetArrayLength())
            {
                current = current[index];
                continue;
            }

            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return ToObject(current);
    }

    private static Dictionary<string, object?> BuildModel(
        JsonElement root,
        IReadOnlyDictionary<string, string>? aliases)
    {
        var model = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (ToObject(root) is Dictionary<string, object?> fields)
        {
            foreach (var pair in fields)
            {
                model[pair.Key] = pair.Value;
            }
        }
        else
        {
            model["value"] = ToObject(root);
        }

        if (aliases is not null)
        {
            foreach (var pair in aliases)
            {
                model[pair.Key] = ResolveValue(root, pair.Value);
            }
        }

        return model;
    }

    private static object? ToObject(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject()
            .ToDictionary(property => property.Name, property => ToObject(property.Value), StringComparer.Ordinal),
        JsonValueKind.Array => element.EnumerateArray().Select(ToObject).ToList(),
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var integer) ? integer : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => element.GetRawText()
    };

    private static TemplateOptions CreateOptions()
    {
        var options = new TemplateOptions();
        options.Filters.AddFilter("json", static (input, _, _) =>
        {
            var value = input.ToObjectValue();
            return new StringValue(JsonSerializer.Serialize(value));
        });
        return options;
    }
}
