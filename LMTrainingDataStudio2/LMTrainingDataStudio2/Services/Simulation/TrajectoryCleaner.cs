using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Services.Simulation;

/// <summary>
/// Applies declarative cleanup rules to OpenAI message samples.
/// Only the <c>messages</c> and <c>tools</c> payloads are touched; unknown fields are preserved.
/// </summary>
public static class TrajectoryCleaner
{
    private static readonly Regex AnsiEscape = new(
        @"\x1B\[[0-9;?]*[ -/]*[@-~]", RegexOptions.Compiled);

    private static readonly Regex TrailingSpaces = new(
        @"[ \t]+(?=\n|$)", RegexOptions.Compiled);

    private static readonly Regex ExcessBlankLines = new(
        @"\n{3,}", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions NodeJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string CleanText(string text, DatasetCleanupRules rules)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(rules);

        if (rules.StripAnsi)
        {
            text = AnsiEscape.Replace(text, string.Empty);
        }

        if (rules.NormalizeNewlines)
        {
            text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        }

        if (rules.RegexReplacements is { Length: > 0 })
        {
            foreach (var rule in rules.RegexReplacements)
            {
                if (string.IsNullOrEmpty(rule.Pattern))
                {
                    continue;
                }

                try
                {
                    var options = rule.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
                    text = Regex.Replace(text, rule.Pattern, rule.Replacement ?? string.Empty, options);
                }
                catch (ArgumentException ex)
                {
                    throw new InvalidOperationException(
                        $"Invalid cleanup regex '{rule.Pattern}': {ex.Message}", ex);
                }
            }
        }

        if (rules.NormalizeWhitespace)
        {
            text = TrailingSpaces.Replace(text, string.Empty);
            text = ExcessBlankLines.Replace(text, "\n\n");
            text = text.Trim();
        }

        return text;
    }

    /// <summary>
    /// Cleans an OAI sample in place. Returns true when anything changed.
    /// </summary>
    public static bool CleanSample(JsonObject sample, DatasetCleanupRules rules)
    {
        ArgumentNullException.ThrowIfNull(sample);
        ArgumentNullException.ThrowIfNull(rules);

        var changed = false;

        if (sample["messages"] is JsonArray messages)
        {
            for (var index = messages.Count - 1; index >= 0; index--)
            {
                if (messages[index] is not JsonObject message)
                {
                    continue;
                }

                changed |= CleanMessage(message, rules);

                if (rules.DropEmptyMessages && IsEmptyMessage(message))
                {
                    messages.RemoveAt(index);
                    changed = true;
                }
            }
        }

        if (sample["tools"] is JsonArray tools)
        {
            foreach (var tool in tools.OfType<JsonObject>())
            {
                if (tool["function"] is JsonObject function &&
                    function["description"] is JsonValue description &&
                    description.TryGetValue<string>(out var text))
                {
                    var cleaned = CleanText(text, rules);
                    if (cleaned != text)
                    {
                        function["description"] = cleaned;
                        changed = true;
                    }
                }
            }
        }

        return changed;
    }

    /// <summary>
    /// Cleans a JSON row string. Rows without a <c>messages</c> array are returned unchanged.
    /// </summary>
    public static string CleanJson(string json, DatasetCleanupRules rules)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(rules);

        if (JsonNode.Parse(json) is not JsonObject sample)
        {
            return json;
        }

        return CleanSample(sample, rules) ? sample.ToJsonString(NodeJsonOptions) : json;
    }

    private static bool CleanMessage(JsonObject message, DatasetCleanupRules rules)
    {
        var changed = false;

        if (message["content"] is JsonValue contentValue &&
            contentValue.TryGetValue<string>(out var content))
        {
            var cleaned = CleanText(content, rules);
            if (cleaned != content)
            {
                message["content"] = cleaned;
                changed = true;
            }
        }
        else if (message["content"] is JsonArray parts)
        {
            foreach (var part in parts.OfType<JsonObject>())
            {
                foreach (var key in new[] { "text", "value" })
                {
                    if (part[key] is JsonValue value && value.TryGetValue<string>(out var text))
                    {
                        var cleaned = CleanText(text, rules);
                        if (cleaned != text)
                        {
                            part[key] = cleaned;
                            changed = true;
                        }
                    }
                }
            }
        }

        if (message["reasoning_content"] is JsonValue reasoning &&
            reasoning.TryGetValue<string>(out var reasoningText))
        {
            if (rules.RemoveReasoningContent)
            {
                message.Remove("reasoning_content");
                changed = true;
            }
            else
            {
                var cleaned = CleanText(reasoningText, rules);
                if (cleaned != reasoningText)
                {
                    message["reasoning_content"] = cleaned;
                    changed = true;
                }
            }
        }

        return changed;
    }

    private static bool IsEmptyMessage(JsonObject message)
    {
        if (message["tool_calls"] is JsonArray { Count: > 0 })
        {
            return false;
        }

        return message["content"] switch
        {
            null => true,
            JsonValue value when value.TryGetValue<string>(out var text) => string.IsNullOrWhiteSpace(text),
            JsonArray { Count: 0 } => true,
            _ => false
        };
    }
}
