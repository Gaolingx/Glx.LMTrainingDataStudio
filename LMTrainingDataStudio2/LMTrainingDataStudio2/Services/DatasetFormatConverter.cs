using System.Text.Json;
using System.Text.Json.Nodes;
using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Services;

/// <summary>
/// Converts between OpenAI, ShareGPT, and Alpaca dataset formats.
/// Port of the Python reference implementation to C#.
/// </summary>
public sealed class DatasetFormatConverter
{
    private static readonly Dictionary<string, string> RoleAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["human"] = "user",
        ["user"] = "user",
        ["prompter"] = "user",
        ["instruction"] = "user",
        ["gpt"] = "assistant",
        ["assistant"] = "assistant",
        ["bot"] = "assistant",
        ["model"] = "assistant",
        ["system"] = "system",
        ["function_call"] = "assistant",
        ["function"] = "tool",
        ["observation"] = "tool",
        ["tool"] = "tool",
    };

    /// <summary>
    /// Detects the dataset format from a raw JSON row.
    /// </summary>
    public DatasetFormat DetectFormat(JsonObject row)
    {
        if (row.ContainsKey("messages")) return DatasetFormat.OpenAI;
        if (row.ContainsKey("conversations")) return DatasetFormat.ShareGPT;
        if (row.ContainsKey("instruction") || row.ContainsKey("output")) return DatasetFormat.Alpaca;
        return DatasetFormat.Auto;
    }

    /// <summary>
    /// Converts a raw JSON row to canonical SFT sample.
    /// </summary>
    public SftSample Convert(JsonObject row, DatasetFormat format = DatasetFormat.Auto)
    {
        if (format == DatasetFormat.Auto)
            format = DetectFormat(row);

        return format switch
        {
            DatasetFormat.OpenAI => ConvertOpenAI(row),
            DatasetFormat.ShareGPT => ConvertShareGPT(row),
            DatasetFormat.Alpaca => ConvertAlpaca(row),
            _ => ConvertFallback(row)
        };
    }

    /// <summary>
    /// Converts canonical SFT sample back to a specific format.
    /// </summary>
    public JsonObject ConvertTo(SftSample sample, DatasetFormat targetFormat)
    {
        return targetFormat switch
        {
            DatasetFormat.OpenAI => ToOpenAI(sample),
            DatasetFormat.ShareGPT => ToShareGPT(sample),
            DatasetFormat.Alpaca => ToAlpaca(sample),
            _ => ToOpenAI(sample)
        };
    }

    #region From-Format Converters

    private SftSample ConvertOpenAI(JsonObject row)
    {
        var messages = new List<ChatMessage>();
        var rawMessages = row["messages"]?.AsArray();
        if (rawMessages == null) return new SftSample { Messages = messages };

        foreach (var msgNode in rawMessages)
        {
            if (msgNode is JsonObject msgObj)
            {
                messages.Add(NormalizeMessage(msgObj));
            }
        }

        var tools = NormalizeTools(row["tools"]);
        if (tools != null)
            messages = AttachTools(messages, tools);

        return new SftSample { Messages = messages, Tools = tools };
    }

    private SftSample ConvertShareGPT(JsonObject row)
    {
        var messages = new List<ChatMessage>();
        var rawConversations = row["conversations"]?.AsArray();
        if (rawConversations == null) return new SftSample { Messages = messages };

        foreach (var msgNode in rawConversations)
        {
            if (msgNode is not JsonObject msgObj) continue;

            var fromRole = msgObj["from"]?.GetValue<string>() ?? msgObj["role"]?.GetValue<string>() ?? "";
            var value = msgObj["value"]?.DeepClone() ?? msgObj["content"]?.DeepClone();

            if (fromRole.Equals("function_call", StringComparison.OrdinalIgnoreCase))
            {
                var toolCallMsg = ConvertShareGPTToolCall(value);
                if (toolCallMsg != null) messages.Add(toolCallMsg);
                continue;
            }

            var normalized = new JsonObject
            {
                ["role"] = fromRole,
                ["content"] = value?.DeepClone()
            };

            // Copy extra fields
            foreach (var prop in msgObj)
            {
                if (prop.Key is "from" or "value" or "role" or "content") continue;
                normalized[prop.Key] = prop.Value?.DeepClone();
            }

            messages.Add(NormalizeMessage(normalized));
        }

        var tools = NormalizeTools(row["tools"]);
        if (tools != null)
            messages = AttachTools(messages, tools);

        return new SftSample { Messages = messages, Tools = tools };
    }

    private SftSample ConvertAlpaca(JsonObject row)
    {
        var messages = new List<ChatMessage>();

        var system = row["system"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(system))
        {
            messages.Add(new ChatMessage { Role = "system", Content = system });
        }

        var instruction = row["instruction"]?.GetValue<string>() ?? "";
        var input = row["input"]?.GetValue<string>() ?? "";
        var prompt = string.IsNullOrEmpty(input) ? instruction
            : string.IsNullOrEmpty(instruction) ? input
            : $"{instruction}\n{input}";

        if (!string.IsNullOrEmpty(prompt))
        {
            messages.Add(new ChatMessage { Role = "user", Content = prompt });
        }

        var output = row["output"]?.GetValue<string>() ?? row["response"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(output))
        {
            messages.Add(new ChatMessage { Role = "assistant", Content = output });
        }

        var tools = NormalizeTools(row["tools"]);
        if (tools != null)
            messages = AttachTools(messages, tools);

        return new SftSample { Messages = messages, Tools = tools };
    }

    private SftSample ConvertFallback(JsonObject row)
    {
        var messages = new List<ChatMessage>();

        var system = row["system"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(system))
            messages.Add(new ChatMessage { Role = "system", Content = system });

        var prompt = row["prompt"]?.GetValue<string>()
            ?? row["question"]?.GetValue<string>()
            ?? row["problem"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(prompt))
            messages.Add(new ChatMessage { Role = "user", Content = prompt });

        var response = row["response"]?.GetValue<string>()
            ?? row["answer"]?.GetValue<string>()
            ?? row["solution"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(response))
            messages.Add(new ChatMessage { Role = "assistant", Content = response });

        return new SftSample { Messages = messages };
    }

    #endregion

    #region To-Format Converters

    private JsonObject ToOpenAI(SftSample sample)
    {
        var messagesArray = new JsonArray();
        foreach (var msg in sample.Messages)
        {
            var msgObj = new JsonObject
            {
                ["role"] = msg.Role,
                ["content"] = JsonSerializer.SerializeToNode(msg.Content)
            };
            if (msg.ToolCalls is { Count: > 0 })
                msgObj["tool_calls"] = JsonSerializer.SerializeToNode(msg.ToolCalls);
            if (!string.IsNullOrEmpty(msg.ToolCallId))
                msgObj["tool_call_id"] = msg.ToolCallId;
            if (!string.IsNullOrEmpty(msg.Name))
                msgObj["name"] = msg.Name;
            messagesArray.Add(msgObj);
        }

        var result = new JsonObject { ["messages"] = messagesArray };
        if (sample.Tools is { Count: > 0 })
            result["tools"] = JsonSerializer.SerializeToNode(sample.Tools);
        return result;
    }

    private JsonObject ToShareGPT(SftSample sample)
    {
        var conversations = new JsonArray();
        foreach (var msg in sample.Messages)
        {
            var from = msg.Role switch
            {
                "user" => "human",
                "assistant" => "gpt",
                _ => msg.Role
            };

            if (msg.ToolCalls is { Count: > 0 })
            {
                conversations.Add(new JsonObject
                {
                    ["from"] = "function_call",
                    ["value"] = JsonSerializer.SerializeToNode(msg.ToolCalls)?.ToJsonString()
                });
                continue;
            }

            conversations.Add(new JsonObject
            {
                ["from"] = from,
                ["value"] = msg.Content?.ToString() ?? ""
            });
        }

        var result = new JsonObject { ["conversations"] = conversations };
        if (sample.Tools is { Count: > 0 })
            result["tools"] = JsonSerializer.SerializeToNode(sample.Tools);
        return result;
    }

    private JsonObject ToAlpaca(SftSample sample)
    {
        var result = new JsonObject();

        foreach (var msg in sample.Messages)
        {
            switch (msg.Role)
            {
                case "system":
                    result["system"] = msg.Content?.ToString() ?? "";
                    break;
                case "user":
                    result["instruction"] = msg.Content?.ToString() ?? "";
                    break;
                case "assistant":
                    result["output"] = msg.Content?.ToString() ?? "";
                    break;
            }
        }

        return result;
    }

    #endregion

    #region Helpers

    private static string NormalizeRole(string? role)
    {
        if (string.IsNullOrEmpty(role)) return "user";
        return RoleAliases.TryGetValue(role, out var normalized) ? normalized : role;
    }

    private ChatMessage NormalizeMessage(JsonObject raw)
    {
        var role = NormalizeRole(raw["role"]?.GetValue<string>() ?? raw["from"]?.GetValue<string>());
        var content = raw["content"]?.DeepClone() ?? raw["value"]?.DeepClone();

        var msg = new ChatMessage
        {
            Role = role,
            Content = MaybeTextContent(content)
        };

        if (raw["reasoning_content"] is JsonValue rc)
            msg.ReasoningContent = rc.GetValue<string>();
        if (raw["name"] is JsonValue nameVal)
            msg.Name = nameVal.GetValue<string>();
        if (raw["tool_call_id"] is JsonValue tcId)
            msg.ToolCallId = tcId.GetValue<string>();

        if (raw["tools"] is JsonNode toolsNode)
            msg.Tools = NormalizeTools(toolsNode);

        if (raw["tool_calls"] is JsonArray toolCallsArray)
        {
            msg.ToolCalls = new List<ToolCall>();
            foreach (var tc in toolCallsArray)
            {
                if (tc is JsonObject tcObj)
                    msg.ToolCalls.Add(NormalizeToolCall(tcObj));
            }
        }

        return msg;
    }

    private static object? MaybeTextContent(JsonNode? value)
    {
        if (value == null) return "";
        if (value is JsonValue jv) return jv.ToString();
        if (value is JsonArray arr)
        {
            // Check if all items are text-type content blocks
            var textParts = new List<string>();
            foreach (var item in arr)
            {
                if (item is not JsonObject obj) return value.ToJsonString();
                var type = obj["type"]?.GetValue<string>();
                if (type != null && type != "text") return value.ToJsonString();
                var text = obj["text"]?.GetValue<string>() ?? obj["value"]?.GetValue<string>();
                if (text != null) textParts.Add(text);
                else return value.ToJsonString();
            }
            return string.Join("", textParts);
        }
        return value.ToJsonString();
    }

    private static List<ToolDefinition>? NormalizeTools(JsonNode? tools)
    {
        if (tools == null) return null;
        if (tools is not JsonArray arr || arr.Count == 0) return null;

        var result = new List<ToolDefinition>();
        foreach (var tool in arr)
        {
            if (tool is JsonObject toolObj)
            {
                var def = new ToolDefinition();
                if (toolObj["function"] is JsonObject funcObj)
                {
                    def.Function = new ToolFunction
                    {
                        Name = funcObj["name"]?.GetValue<string>() ?? "",
                        Description = funcObj["description"]?.GetValue<string>(),
                        Parameters = funcObj["parameters"] is JsonObject p
                            ? JsonSerializer.Deserialize<Dictionary<string, object>>(p.ToJsonString())
                            : null
                    };
                }
                else if (toolObj["name"] is not null)
                {
                    // Flat tool definition without "function" wrapper
                    def.Function = new ToolFunction
                    {
                        Name = toolObj["name"]?.GetValue<string>() ?? "",
                        Description = toolObj["description"]?.GetValue<string>(),
                        Parameters = toolObj["parameters"] is JsonObject p
                            ? JsonSerializer.Deserialize<Dictionary<string, object>>(p.ToJsonString())
                            : null
                    };
                }
                result.Add(def);
            }
        }
        return result.Count > 0 ? result : null;
    }

    private static ToolCall NormalizeToolCall(JsonObject raw)
    {
        var tc = new ToolCall
        {
            Id = raw["id"]?.GetValue<string>(),
            Type = raw["type"]?.GetValue<string>() ?? "function"
        };

        if (raw["function"] is JsonObject funcObj)
        {
            tc.Function = new ToolCallFunction
            {
                Name = funcObj["name"]?.GetValue<string>() ?? "",
                Arguments = funcObj["arguments"]?.DeepClone()
            };
        }
        else if (raw["name"] is not null)
        {
            tc.Function = new ToolCallFunction
            {
                Name = raw["name"]?.GetValue<string>() ?? "",
                Arguments = raw["arguments"]?.DeepClone()
            };
        }

        return tc;
    }

    private ChatMessage? ConvertShareGPTToolCall(JsonNode? value)
    {
        if (value == null) return null;

        var toolCalls = new List<ToolCall>();

        if (value is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item is JsonObject obj)
                    toolCalls.Add(NormalizeToolCall(obj));
            }
        }
        else if (value is JsonObject obj)
        {
            toolCalls.Add(NormalizeToolCall(obj));
        }
        else if (value is JsonValue jv)
        {
            // Try parse string as JSON
            try
            {
                var parsed = JsonNode.Parse(jv.GetValue<string>());
                return ConvertShareGPTToolCall(parsed);
            }
            catch { return null; }
        }

        if (toolCalls.Count == 0) return null;

        return new ChatMessage
        {
            Role = "assistant",
            Content = "",
            ToolCalls = toolCalls
        };
    }

    private static List<ChatMessage> AttachTools(List<ChatMessage> messages, List<ToolDefinition> tools)
    {
        var result = new List<ChatMessage>(messages);
        var systemMsg = result.FirstOrDefault(m => m.Role == "system");
        if (systemMsg != null)
        {
            systemMsg.Tools ??= tools;
        }
        else
        {
            result.Insert(0, new ChatMessage { Role = "system", Content = "", Tools = tools });
        }
        return result;
    }

    #endregion
}
