using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LMTrainingDataStudio2.Services.Executors;

/// <summary>
/// Minimal placeholder engine supporting <c>{{ field.path }}</c> substitution.
/// The full Jinja engine arrives with the editor work.
/// </summary>
internal static class TemplateRenderer
{
    private static readonly Regex TokenPattern = new(@"\{\{\s*([\w.]+)\s*\}\}", RegexOptions.Compiled);

    public static string Render(
        string template,
        JsonElement root,
        IReadOnlyDictionary<string, string>? aliases = null,
        bool jsonEscape = true)
    {
        return TokenPattern.Replace(template, match =>
        {
            var name = match.Groups[1].Value;
            var path = aliases is not null && aliases.TryGetValue(name, out var mapped) ? mapped : name;
            var value = ResolveString(root, path) ?? string.Empty;
            return jsonEscape ? JsonEncodedText.Encode(value).ToString() : value;
        });
    }

    public static string? ResolveString(JsonElement root, string path)
    {
        var current = root;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == "length" && current.ValueKind == JsonValueKind.Array)
            {
                return current.GetArrayLength().ToString(CultureInfo.InvariantCulture);
            }

            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => current.GetRawText()
        };
    }
}