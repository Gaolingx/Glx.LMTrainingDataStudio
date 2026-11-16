using System.Runtime.CompilerServices;
using System.Text.Json;
using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Services.Executors;

public interface ILlmClient
{
    Task<string> CompleteAsync(string prompt, LlmBlockConfig config, CancellationToken cancellationToken = default);
}

/// <summary>
/// Calls an LLM per input row and emits OpenAI-style assistant messages.
/// </summary>
public sealed class LlmBlockExecutor : IBlockExecutor
{
    private readonly ILlmClient _client;

    public LlmBlockExecutor(ILlmClient client) => _client = client;

    public BlockType Type => BlockType.Llm;

    public Task<BlockExecutionResult> ExecuteAsync(BlockExecutionContext context, CancellationToken cancellationToken)
    {
        if (context.Block.Config is not LlmBlockConfig config)
        {
            throw new InvalidOperationException($"LLM block '{context.Block.Name}' has an invalid configuration.");
        }

        if (string.IsNullOrWhiteSpace(config.ModelAlias))
        {
            throw new InvalidOperationException($"LLM block '{context.Block.Name}' has no model alias configured.");
        }

        if (context.Input is null)
        {
            throw new InvalidOperationException($"LLM block '{context.Block.Name}' requires an input connection.");
        }

        return Task.FromResult(new BlockExecutionResult(GenerateAsync(context, config, cancellationToken)));
    }

    private async IAsyncEnumerable<DatasetRow> GenerateAsync(
        BlockExecutionContext context,
        LlmBlockConfig config,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var index = 0L;
        await foreach (var row in context.Input!.WithCancellation(ct))
        {
            string prompt;
            if (string.IsNullOrWhiteSpace(config.Prompt))
            {
                prompt = row.Json;
            }
            else
            {
                using var document = JsonRowReader.Parse(row, context.Block.Name);
                prompt = TemplateRenderer.Render(config.Prompt, document.RootElement, jsonEscape: false);
            }

            var completion = await _client.CompleteAsync(prompt, config, ct);
            var json = JsonSerializer.Serialize(new
            {
                messages = new[]
                {
                    new { role = "assistant", content = completion }
                }
            });

            yield return new DatasetRow(index++, json);
            context.RowsProcessed?.Report(index);
        }
    }
}