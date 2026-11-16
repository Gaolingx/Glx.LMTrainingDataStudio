using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Services.Datasets;

/// <summary>
/// Converts dataset rows between chat (OpenAI/ShareGPT/Alpaca), DPO and pretraining shapes.
/// Unsupported combinations fail explicitly instead of silently degrading.
/// </summary>
public sealed class DatasetConversionService
{
    private readonly DatasetFormatConverter _chat = new();

    public static bool IsChatFormat(DatasetFormat format) =>
        format is DatasetFormat.OpenAI or DatasetFormat.ShareGPT or DatasetFormat.Alpaca;

    public DatasetFormat DetectFormat(JsonObject row) => _chat.DetectFormat(row);

    /// <summary>
    /// Converts one row. <paramref name="rowNumber"/> is 1-based and used in error messages.
    /// </summary>
    public JsonObject Convert(
        JsonObject row,
        DatasetFormat sourceFormat,
        DatasetFormat targetFormat,
        long rowNumber)
    {
        if (sourceFormat == DatasetFormat.Auto)
        {
            sourceFormat = DetectFormat(row);
        }

        if (sourceFormat == DatasetFormat.Auto)
        {
            throw new DatasetFormatException("Could not detect the source format.", rowNumber);
        }

        if (targetFormat == DatasetFormat.Auto)
        {
            throw new DatasetFormatException("A concrete target format is required.", rowNumber);
        }

        if (IsChatFormat(sourceFormat))
        {
            if (IsChatFormat(targetFormat))
            {
                return _chat.ConvertTo(_chat.Convert(row, sourceFormat), targetFormat);
            }

            if (targetFormat == DatasetFormat.Pretrain)
            {
                return new JsonObject { ["text"] = FlattenChat(row, sourceFormat) };
            }

            throw Unsupported(sourceFormat, targetFormat, rowNumber);
        }

        if (sourceFormat == DatasetFormat.Pretrain)
        {
            if (targetFormat == DatasetFormat.Pretrain)
            {
                return NormalizePretrain(row, rowNumber);
            }

            throw Unsupported(sourceFormat, targetFormat, rowNumber);
        }

        if (sourceFormat == DatasetFormat.Dpo)
        {
            if (targetFormat == DatasetFormat.Dpo)
            {
                return NormalizeDpo(row, rowNumber);
            }

            throw Unsupported(sourceFormat, targetFormat, rowNumber);
        }

        throw Unsupported(sourceFormat, targetFormat, rowNumber);
    }

    private string FlattenChat(JsonObject row, DatasetFormat sourceFormat)
    {
        var sample = _chat.Convert(row, sourceFormat);
        var builder = new StringBuilder();

        foreach (var message in sample.Messages)
        {
            var content = ContentText(message.Content);
            if (content.Length == 0 && message.ToolCalls is null)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append("\n\n");
            }

            builder.Append(message.Role).Append(": ").Append(content);
            if (message.ToolCalls is { Count: > 0 })
            {
                builder.Append(" [tool_calls: ")
                    .Append(JsonSerializer.Serialize(message.ToolCalls))
                    .Append(']');
            }
        }

        return builder.ToString();
    }

    private static string ContentText(object? content) => content switch
    {
        null => string.Empty,
        string text => text,
        _ => JsonSerializer.Serialize(content)
    };

    private static JsonObject NormalizePretrain(JsonObject row, long rowNumber)
    {
        if (!row.TryGetPropertyValue("text", out var text) || text is null)
        {
            throw new DatasetFormatException("Pretrain row is missing 'text'.", rowNumber);
        }

        var value = text is JsonValue scalar && scalar.TryGetValue<string>(out var plain)
            ? plain
            : text.ToJsonString();

        return new JsonObject { ["text"] = value };
    }

    private static JsonObject NormalizeDpo(JsonObject row, long rowNumber)
    {
        if (!row.TryGetPropertyValue("chosen", out var chosen) || chosen is null)
        {
            throw new DatasetFormatException("DPO row is missing 'chosen'.", rowNumber);
        }

        if (!row.TryGetPropertyValue("rejected", out var rejected) || rejected is null)
        {
            throw new DatasetFormatException("DPO row is missing 'rejected'.", rowNumber);
        }

        row.TryGetPropertyValue("prompt", out var prompt);

        return new JsonObject
        {
            ["prompt"] = prompt?.DeepClone() ?? JsonValue.Create(string.Empty),
            ["chosen"] = chosen.DeepClone(),
            ["rejected"] = rejected.DeepClone()
        };
    }

    private static DatasetFormatException Unsupported(
        DatasetFormat source, DatasetFormat target, long rowNumber) =>
        new($"Conversion from {source} to {target} is not supported.", rowNumber);
}
