using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Services.Executors;

/// <summary>
/// Streams rows to a JSONL file in OpenAI messages format, then passes them downstream.
/// Writes go through a temporary file and replace the target only after success.
/// </summary>
public sealed class SftExportBlockExecutor : IBlockExecutor
{
    private static readonly JsonSerializerOptions RowJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly DatasetFormatConverter FormatConverter = new();

    public BlockType Type => BlockType.SftExport;

    public Task<BlockExecutionResult> ExecuteAsync(
        BlockExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (context.Block.Config is not SftExportBlockConfig config)
        {
            throw new InvalidOperationException(
                $"SFT export block '{context.Block.Name}' has an invalid configuration.");
        }

        ValidateConfig(config, context.Block.Name);

        if (context.Input is null)
        {
            throw new InvalidOperationException(
                $"SFT export block '{context.Block.Name}' requires an input connection.");
        }

        return Task.FromResult(new BlockExecutionResult(
            ExportAsync(context, config, cancellationToken)));
    }

    private static void ValidateConfig(SftExportBlockConfig config, string blockName)
    {
        if (string.IsNullOrWhiteSpace(config.OutputPath))
        {
            throw new InvalidOperationException(
                $"SFT export block '{blockName}' has no output path configured.");
        }

        if (config.SplitMaxRows < 0)
        {
            throw new InvalidOperationException(
                $"SFT export block '{blockName}' split size cannot be negative.");
        }

        var ratioSplits = config.Splits?
            .Where(split => !string.IsNullOrWhiteSpace(split))
            .ToArray() ?? Array.Empty<string>();

        if (ratioSplits.Length > 0)
        {
            if (config.Append)
            {
                throw new InvalidOperationException(
                    $"SFT export block '{blockName}' cannot combine append mode with ratio splits.");
            }

            if (config.SplitMaxRows > 0)
            {
                throw new InvalidOperationException(
                    $"SFT export block '{blockName}' cannot combine row-count splitting with ratio splits.");
            }

            if (!SamplerSplitPlan.TryParse(ratioSplits, out var plan, out var error))
            {
                throw new InvalidOperationException(
                    $"SFT export block '{blockName}' has invalid ratio splits: {error}");
            }

            if (plan!.IsCountMode)
            {
                throw new InvalidOperationException(
                    $"SFT export block '{blockName}' ratio splits must use 'name:ratio' entries.");
            }
        }

        if (config.Append && config.SplitMaxRows > 0)
        {
            throw new InvalidOperationException(
                $"SFT export block '{blockName}' cannot combine append mode with file splitting.");
        }

        if (config.TargetFormat is DatasetFormat.Dpo or DatasetFormat.Pretrain)
        {
            throw new InvalidOperationException(
                $"SFT export block '{blockName}' cannot produce {config.TargetFormat} rows from chat messages.");
        }
    }

    private static async IAsyncEnumerable<DatasetRow> ExportAsync(
        BlockExecutionContext context,
        SftExportBlockConfig config,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await using var writer = SftExportWriter.Create(
            config.OutputPath,
            config.Append,
            config.SplitMaxRows,
            config.Splits,
            config.SplitSeed);

        var index = 0L;
        string? preview = null;
        await foreach (var row in context.Input!.WithCancellation(ct))
        {
            var line = NormalizeRow(row, config, context.Block.Name);
            await writer.WriteAsync(line, ct);
            preview ??= Preview(line);
            yield return new DatasetRow(index++, line);
            context.RowsProcessed?.Report(index);
        }

        await writer.CommitAsync(ct);

        var parts = writer.PartPaths;
        var destination = parts.Count == 1
            ? $"'{parts[0]}'"
            : $"{parts.Count} part file(s) starting at '{parts[0]}'";
        var fieldNote = config.Fields is { Length: > 0 }
            ? $" (fields: {string.Join(", ", config.Fields.Where(field => !string.IsNullOrWhiteSpace(field)))})"
            : string.Empty;
        var formatNote = config.TargetFormat == DatasetFormat.Auto
            ? string.Empty
            : $" (format: {config.TargetFormat})";
        var appendNote = config.Append ? " [append]" : string.Empty;
        var splitNote = config.Splits is { Length: > 0 }
            ? $" [ratio splits: {string.Join(", ", config.Splits.Where(split => !string.IsNullOrWhiteSpace(split)))}]"
            : string.Empty;
        context.Log($"Exported {index} row(s) to {destination}{fieldNote}{formatNote}{appendNote}{splitNote}.");
        context.Log(preview is null ? "Preview: no rows exported." : $"Preview: {preview}");
    }

    private static string NormalizeRow(DatasetRow row, SftExportBlockConfig config, string blockName)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(row.Json);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"SFT export block '{blockName}' could not parse line {row.LineNumber}: {ex.Message}", ex);
        }

        if (node is null)
        {
            throw new InvalidDataException(
                $"SFT export block '{blockName}' row {row.LineNumber} is empty JSON.");
        }

        if (config.TargetFormat != DatasetFormat.Auto)
        {
            if (node is not JsonObject sourceObject)
            {
                throw new InvalidDataException(
                    $"SFT export block '{blockName}' row {row.LineNumber} is not a JSON object; " +
                    "format conversion requires object rows.");
            }

            try
            {
                var sample = FormatConverter.Convert(sourceObject, DatasetFormat.Auto);
                node = FormatConverter.ConvertTo(sample, config.TargetFormat);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                throw new InvalidDataException(
                    $"SFT export block '{blockName}' could not convert row {row.LineNumber} " +
                    $"to {config.TargetFormat}: {ex.Message}", ex);
            }
        }

        var fields = config.Fields?
            .Where(field => !string.IsNullOrWhiteSpace(field))
            .Select(field => field.Trim())
            .ToArray();

        if (fields is not { Length: > 0 })
        {
            return node.ToJsonString(RowJsonOptions);
        }

        if (node is not JsonObject target)
        {
            throw new InvalidDataException(
                $"SFT export block '{blockName}' row {row.LineNumber} is not a JSON object; " +
                "field filtering requires object rows.");
        }

        var filtered = new JsonObject();
        foreach (var field in fields)
        {
            if (target.TryGetPropertyValue(field, out var value))
            {
                filtered[field] = value?.DeepClone();
            }
        }

        return filtered.ToJsonString(RowJsonOptions);
    }

    private static string Preview(string line) => line.Length <= 200 ? line : line[..200] + "…";
}
