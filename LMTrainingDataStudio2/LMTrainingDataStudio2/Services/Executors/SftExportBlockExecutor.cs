using System.Runtime.CompilerServices;
using System.Text;
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

        if (string.IsNullOrWhiteSpace(config.OutputPath))
        {
            throw new InvalidOperationException(
                $"SFT export block '{context.Block.Name}' has no output path configured.");
        }

        if (context.Input is null)
        {
            throw new InvalidOperationException(
                $"SFT export block '{context.Block.Name}' requires an input connection.");
        }

        return Task.FromResult(new BlockExecutionResult(
            ExportAsync(context, config, cancellationToken)));
    }

    private static async IAsyncEnumerable<DatasetRow> ExportAsync(
        BlockExecutionContext context,
        SftExportBlockConfig config,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var fullPath = Path.GetFullPath(config.OutputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var tempPath = fullPath + ".tmp";

        var index = 0L;
        var completed = false;
        string? preview = null;
        try
        {
            await using (var stream = new FileStream(
                tempPath, FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: 64 * 1024, useAsync: true))
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                await foreach (var row in context.Input!.WithCancellation(ct))
                {
                    var line = NormalizeRow(row, config, context.Block.Name);
                    await writer.WriteLineAsync(line.AsMemory(), ct);
                    preview ??= Preview(line);
                    yield return new DatasetRow(index++, line);
                    context.RowsProcessed?.Report(index);
                }
            }

            File.Move(tempPath, fullPath, overwrite: true);
            completed = true;
        }
        finally
        {
            if (!completed)
            {
                TryDelete(tempPath);
            }
        }

        var fieldNote = config.Fields is { Length: > 0 }
            ? $" (fields: {string.Join(", ", config.Fields.Where(field => !string.IsNullOrWhiteSpace(field)))})"
            : string.Empty;
        context.Log($"Exported {index} row(s) to '{fullPath}'{fieldNote}.");
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

        var fields = config.Fields?
            .Where(field => !string.IsNullOrWhiteSpace(field))
            .Select(field => field.Trim())
            .ToArray();

        if (fields is not { Length: > 0 })
        {
            return node.ToJsonString(RowJsonOptions);
        }

        if (node is not JsonObject source)
        {
            throw new InvalidDataException(
                $"SFT export block '{blockName}' row {row.LineNumber} is not a JSON object; " +
                "field filtering requires object rows.");
        }

        var filtered = new JsonObject();
        foreach (var field in fields)
        {
            if (source.TryGetPropertyValue(field, out var value))
            {
                filtered[field] = value?.DeepClone();
            }
        }

        return filtered.ToJsonString(RowJsonOptions);
    }

    private static string Preview(string line) => line.Length <= 200 ? line : line[..200] + "…";

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
