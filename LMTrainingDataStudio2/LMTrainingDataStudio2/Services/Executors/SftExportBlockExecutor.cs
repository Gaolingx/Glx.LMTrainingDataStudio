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
        try
        {
            await using (var stream = new FileStream(
                tempPath, FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: 64 * 1024, useAsync: true))
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                await foreach (var row in context.Input!.WithCancellation(ct))
                {
                    var line = NormalizeRow(row, context.Block.Name);
                    await writer.WriteLineAsync(line.AsMemory(), ct);
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

        context.Log($"Exported {index} row(s) to '{fullPath}'.");
    }

    private static string NormalizeRow(DatasetRow row, string blockName)
    {
        try
        {
            return JsonNode.Parse(row.Json)?.ToJsonString(RowJsonOptions)
                   ?? throw new InvalidDataException(
                       $"SFT export block '{blockName}' row {row.LineNumber} is empty JSON.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"SFT export block '{blockName}' could not parse line {row.LineNumber}: {ex.Message}", ex);
        }
    }

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
