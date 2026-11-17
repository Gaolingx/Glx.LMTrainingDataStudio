using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LMTrainingDataStudio2.Services.Datasets;

/// <summary>
/// One chat message as shown by the structured messages editor.
/// <see cref="RawJson"/> keeps every property the editor does not expose,
/// so applying role/content/tool_calls never drops provider-specific fields.
/// </summary>
public sealed record DatasetMessage(
    string Role,
    string ContentText,
    bool ContentIsJson,
    string ToolCallsText,
    string RawJson);

public static class DatasetMessageEditor
{
    public const string MessagesFieldName = "messages";

    public static readonly string[] CommonRoles = { "system", "user", "assistant", "tool", "developer" };

    private static readonly JsonSerializerOptions OutputOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static bool TryReadMessages(
        string? json,
        out IReadOnlyList<DatasetMessage> messages,
        out string? error)
    {
        messages = Array.Empty<DatasetMessage>();
        error = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "Messages value is empty.";
            return false;
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            error = $"Invalid messages JSON: {ex.Message}";
            return false;
        }

        if (node is not JsonArray array)
        {
            error = "Messages must be a JSON array.";
            return false;
        }

        var result = new List<DatasetMessage>(array.Count);
        for (var index = 0; index < array.Count; index++)
        {
            if (array[index] is not JsonObject obj)
            {
                error = $"Message {index + 1} is not an object.";
                return false;
            }

            var role = obj["role"] is JsonValue roleValue &&
                       roleValue.TryGetValue<string>(out var roleText)
                ? roleText
                : string.Empty;

            var contentNode = obj["content"];
            var (contentText, contentIsJson) = contentNode switch
            {
                null => (string.Empty, false),
                JsonValue value when value.GetValueKind() == JsonValueKind.String &&
                                     value.TryGetValue<string>(out var text) => (text, false),
                _ => (contentNode.ToJsonString(OutputOptions), true)
            };

            var toolCallsText = obj["tool_calls"] is JsonArray toolCalls
                ? toolCalls.ToJsonString(OutputOptions)
                : string.Empty;

            result.Add(new DatasetMessage(
                role,
                contentText,
                contentIsJson,
                toolCallsText,
                obj.ToJsonString(OutputOptions)));
        }

        messages = result;
        return true;
    }

    public static bool TryBuild(
        IEnumerable<DatasetMessage> messages,
        out string json,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(messages);
        json = string.Empty;
        error = null;

        var array = new JsonArray();
        var index = 0;
        foreach (var message in messages)
        {
            index++;
            if (string.IsNullOrWhiteSpace(message.Role))
            {
                error = $"Message {index}: role must not be empty.";
                return false;
            }

            JsonObject obj;
            try
            {
                obj = JsonNode.Parse(message.RawJson) as JsonObject ?? new JsonObject();
            }
            catch (JsonException)
            {
                obj = new JsonObject();
            }

            obj["role"] = message.Role.Trim();

            if (message.ContentIsJson)
            {
                try
                {
                    obj["content"] = JsonNode.Parse(
                        string.IsNullOrWhiteSpace(message.ContentText) ? "null" : message.ContentText);
                }
                catch (JsonException ex)
                {
                    error = $"Message {index}: content is not valid JSON: {ex.Message}";
                    return false;
                }
            }
            else
            {
                obj["content"] = message.ContentText;
            }

            if (string.IsNullOrWhiteSpace(message.ToolCallsText))
            {
                obj.Remove("tool_calls");
            }
            else
            {
                JsonNode? toolCalls;
                try
                {
                    toolCalls = JsonNode.Parse(message.ToolCallsText);
                }
                catch (JsonException ex)
                {
                    error = $"Message {index}: tool_calls is not valid JSON: {ex.Message}";
                    return false;
                }

                if (toolCalls is not JsonArray)
                {
                    error = $"Message {index}: tool_calls must be a JSON array.";
                    return false;
                }

                obj["tool_calls"] = toolCalls;
            }

            array.Add(obj);
        }

        json = array.ToJsonString(OutputOptions);
        return true;
    }
}
