using System.Runtime.CompilerServices;
using System.Text.Json;
using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Services.Executors;

/// <summary>
/// Renders a JSON template or a field mapping from each input row.
/// </summary>
public sealed class ExpressionBlockExecutor : IBlockExecutor
{
    public BlockType Type => BlockType.Expression;

    public Task<BlockExecutionResult> ExecuteAsync(BlockExecutionContext context, CancellationToken cancellationToken)
    {
        if (context.Block.Config is not ExpressionBlockConfig config)
        {
            throw new InvalidOperationException(
                $"Expression block '{context.Block.Name}' has an invalid configuration.");
        }

        if (context.Input is null)
        {
            throw new InvalidOperationException(
                $"Expression block '{context.Block.Name}' requires an input connection.");
        }

        if (string.IsNullOrWhiteSpace(config.Template) && config.Fields.Count == 0)
        {
            throw new InvalidOperationException(
                $"Expression block '{context.Block.Name}' has no template or fields configured.");
        }

        var templateValidation = TemplateRenderer.Validate(config.Template);
        if (!templateValidation.IsValid)
        {
            throw new InvalidOperationException(
                $"Expression block '{context.Block.Name}' has an invalid template: {templateValidation.Error}");
        }

        return Task.FromResult(new BlockExecutionResult(TransformAsync(context, config, cancellationToken)));
    }

    private static async IAsyncEnumerable<DatasetRow> TransformAsync(
        BlockExecutionContext context,
        ExpressionBlockConfig config,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var outputIndex = 0L;
        await foreach (var row in context.Input!.WithCancellation(ct))
        {
            using var document = JsonRowReader.Parse(row, context.Block.Name);
            var root = document.RootElement;

            string json;
            if (!string.IsNullOrWhiteSpace(config.Template))
            {
                json = await TemplateRenderer.RenderAsync(config.Template, root, config.Fields);
                try
                {
                    using var _ = JsonDocument.Parse(json);
                }
                catch (JsonException ex)
                {
                    throw new InvalidOperationException(
                        $"Expression block '{context.Block.Name}' produced invalid JSON for line {row.LineNumber}: {ex.Message}",
                        ex);
                }
            }
            else
            {
                var mapped = config.Fields.ToDictionary(
                    pair => pair.Key,
                    pair => TemplateRenderer.ResolveString(root, pair.Value));
                json = JsonSerializer.Serialize(mapped);
            }

            yield return new DatasetRow(outputIndex++, json);
            context.RowsProcessed?.Report(outputIndex);
        }
    }
}