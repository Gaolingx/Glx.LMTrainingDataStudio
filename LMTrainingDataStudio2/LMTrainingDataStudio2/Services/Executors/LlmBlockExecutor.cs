using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Services.Executors;

public interface ILlmClient
{
    Task<string> CompleteAsync(string prompt, LlmBlockConfig config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Detailed completion that can send chat history, tool definitions and a response schema.
    /// The default bridge keeps simple clients working by collapsing the request to one prompt.
    /// </summary>
    async Task<LlmCompletionResult> CompleteAsync(
        LlmCompletionRequest request,
        LlmBlockConfig config,
        CancellationToken cancellationToken = default)
    {
        var prompt = request.Messages.LastOrDefault(message => message.Role == "user")?.Content
                     ?? request.Messages.LastOrDefault()?.Content
                     ?? string.Empty;
        return new LlmCompletionResult(
            await CompleteAsync(prompt, config, cancellationToken),
            ToolCalls: null,
            Usage: null);
    }
}

/// <summary>
/// Calls an LLM per input row. Supports request-side tools, structured output, row chat
/// history, bounded concurrency, token/cost accounting and failure samples.
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

        if (!string.IsNullOrWhiteSpace(config.ToolProfileName) && context.ToolProfile is null)
        {
            throw new InvalidOperationException(
                $"LLM block '{context.Block.Name}' references unknown tool profile '{config.ToolProfileName}'.");
        }

        if (config.MaxConcurrency < 1)
        {
            throw new InvalidOperationException(
                $"LLM block '{context.Block.Name}' max concurrency must be at least 1.");
        }

        if (config.MaxFailureSamples < 0)
        {
            throw new InvalidOperationException(
                $"LLM block '{context.Block.Name}' max failure samples must not be negative.");
        }

        if (!string.IsNullOrWhiteSpace(config.ResponseSchema))
        {
            try
            {
                JsonSchemaRuleEvaluator.Parse(config.ResponseSchema!);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                throw new InvalidOperationException(
                    $"LLM block '{context.Block.Name}' has an invalid response schema: {ex.Message}", ex);
            }
        }

        var tools = context.ToolProfile?.Tools is { Count: > 0 } profileTools
            ? profileTools
            : null;

        return Task.FromResult(new BlockExecutionResult(
            GenerateAsync(context, config, tools, cancellationToken)));
    }

    private async IAsyncEnumerable<DatasetRow> GenerateAsync(
        BlockExecutionContext context,
        LlmBlockConfig config,
        IReadOnlyList<ToolDefinition>? tools,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var maxConcurrency = Math.Clamp(config.MaxConcurrency, 1, 64);
        var pending = new Queue<Task<RowOutcome>>();
        var failures = new List<LlmFailure>();
        var outputIndex = 0L;
        var consumed = 0L;
        var emitted = 0L;
        var skipped = 0L;
        var promptTokens = 0L;
        var completionTokens = 0L;

        using var pipelineCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            await foreach (var row in context.Input!.WithCancellation(ct))
            {
                ct.ThrowIfCancellationRequested();
                pending.Enqueue(ProcessRowAsync(
                    row, context.Block.Name, config, tools, pipelineCts.Token));

                while (pending.Count >= maxConcurrency)
                {
                    var outcome = await pending.Dequeue();
                    consumed++;
                    Accumulate(outcome, ref promptTokens, ref completionTokens);
                    if (outcome.Failure is not null)
                    {
                        skipped++;
                        TrackFailure(outcome.Failure, config, failures);
                        if (!config.ContinueOnError)
                        {
                            throw new InvalidOperationException(outcome.Failure.Error);
                        }
                    }
                    else
                    {
                        emitted++;
                        yield return new DatasetRow(outputIndex++, outcome.Json!);
                    }

                    context.RowsProcessed?.Report(consumed);
                }
            }

            while (pending.Count > 0)
            {
                var outcome = await pending.Dequeue();
                consumed++;
                Accumulate(outcome, ref promptTokens, ref completionTokens);
                if (outcome.Failure is not null)
                {
                    skipped++;
                    TrackFailure(outcome.Failure, config, failures);
                    if (!config.ContinueOnError)
                    {
                        throw new InvalidOperationException(outcome.Failure.Error);
                    }
                }
                else
                {
                    emitted++;
                    yield return new DatasetRow(outputIndex++, outcome.Json!);
                }

                context.RowsProcessed?.Report(consumed);
            }
        }
        finally
        {
            pipelineCts.Cancel();
            while (pending.Count > 0)
            {
                try
                {
                    await pending.Dequeue();
                }
                catch
                {
                    // Cancelled rows are not part of the output.
                }
            }
        }

        if (failures.Count > 0 && !string.IsNullOrWhiteSpace(config.FailureOutputPath))
        {
            await WriteFailuresAsync(config.FailureOutputPath!, failures, ct);
            context.Log($"Wrote {failures.Count} failure sample(s) to '{config.FailureOutputPath}'.");
        }

        var cost = promptTokens / 1_000_000d * config.InputCostPerMillionTokens +
                   completionTokens / 1_000_000d * config.OutputCostPerMillionTokens;
        context.Log(
            $"Completed {consumed} row(s): emitted {emitted}, skipped {skipped}. " +
            $"Tokens: {promptTokens}+{completionTokens}, estimated cost: {cost:F6}.");
    }

    private async Task<RowOutcome> ProcessRowAsync(
        DatasetRow row,
        string blockName,
        LlmBlockConfig config,
        IReadOnlyList<ToolDefinition>? tools,
        CancellationToken ct)
    {
        try
        {
            var request = BuildRequest(row, blockName, config, tools);
            var completion = await _client.CompleteAsync(request, config, ct);

            if (!string.IsNullOrWhiteSpace(config.ResponseSchema) &&
                !IsValidJson(completion.Content))
            {
                throw new InvalidOperationException(
                    $"Row {row.LineNumber}: structured output is not valid JSON: {Truncate(completion.Content)}");
            }

            var json = BuildOutputJson(row, config, tools, completion);
            return new RowOutcome(json, null, completion.Usage);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new RowOutcome(null, new LlmFailure(row.LineNumber, row.Json, $"Row {row.LineNumber}: {ex.Message}"), null);
        }
    }

    private static LlmCompletionRequest BuildRequest(
        DatasetRow row,
        string blockName,
        LlmBlockConfig config,
        IReadOnlyList<ToolDefinition>? tools)
    {
        var messages = new List<LlmChatTurn>();
        if (!string.IsNullOrWhiteSpace(config.SystemPrompt))
        {
            messages.Add(new LlmChatTurn("system", config.SystemPrompt));
        }

        using var document = JsonRowReader.Parse(row, blockName);
        var root = document.RootElement;
        var hasRowMessages = config.UseRowMessages &&
                             root.ValueKind == JsonValueKind.Object &&
                             root.TryGetProperty("messages", out var rowMessages) &&
                             rowMessages.ValueKind == JsonValueKind.Array;

        if (hasRowMessages)
        {
            foreach (var message in root.GetProperty("messages").EnumerateArray())
            {
                if (message.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var role = message.TryGetProperty("role", out var roleValue) &&
                           roleValue.ValueKind == JsonValueKind.String
                    ? roleValue.GetString() ?? "user"
                    : "user";
                var content = message.TryGetProperty("content", out var contentValue)
                    ? ReadContent(contentValue)
                    : null;
                messages.Add(new LlmChatTurn(role, content));
            }
        }

        var prompt = string.IsNullOrWhiteSpace(config.Prompt)
            ? (hasRowMessages ? null : row.Json)
            : TemplateRenderer.RenderAsync(config.Prompt!, root).GetAwaiter().GetResult();

        if (!string.IsNullOrWhiteSpace(prompt))
        {
            messages.Add(new LlmChatTurn("user", prompt));
        }

        if (messages.Count == 0)
        {
            messages.Add(new LlmChatTurn("user", row.Json));
        }

        return new LlmCompletionRequest(messages, tools, config.ResponseSchema);
    }

    private static string BuildOutputJson(
        DatasetRow row,
        LlmBlockConfig config,
        IReadOnlyList<ToolDefinition>? tools,
        LlmCompletionResult completion)
    {
        JsonArray messages;
        if (config.UseRowMessages)
        {
            using var document = JsonRowReader.Parse(row, "LLM");
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("messages", out var rowMessages) &&
                rowMessages.ValueKind == JsonValueKind.Array)
            {
                messages = JsonNode.Parse(rowMessages.GetRawText()) as JsonArray ?? new JsonArray();
            }
            else
            {
                messages = new JsonArray();
            }
        }
        else
        {
            messages = new JsonArray();
        }

        var assistant = new JsonObject
        {
            ["role"] = "assistant",
            ["content"] = completion.Content
        };

        if (completion.ToolCalls is { Count: > 0 } toolCalls)
        {
            assistant["tool_calls"] = new JsonArray(toolCalls.Select(call => (JsonNode)new JsonObject
            {
                ["id"] = call.Id,
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = call.Name,
                    ["arguments"] = call.ArgumentsJson
                }
            }).ToArray());
        }

        messages.Add(assistant);

        if (!string.IsNullOrWhiteSpace(config.SystemPrompt) &&
            !messages.Any(node =>
                node is JsonObject obj &&
                obj["role"]?.GetValue<string>() == "system"))
        {
            messages.Insert(0, new JsonObject
            {
                ["role"] = "system",
                ["content"] = config.SystemPrompt
            });
        }

        var output = new JsonObject { ["messages"] = messages };
        if (tools is { Count: > 0 })
        {
            output["tools"] = JsonSerializer.SerializeToNode(tools);
        }

        return output.ToJsonString();
    }

    private static string? ReadContent(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String)
        {
            return content.GetString();
        }

        if (content.ValueKind == JsonValueKind.Array)
        {
            var parts = content.EnumerateArray()
                .Where(part => part.ValueKind == JsonValueKind.Object &&
                               part.TryGetProperty("text", out var text) &&
                               text.ValueKind == JsonValueKind.String)
                .Select(part => part.GetProperty("text").GetString())
                .Where(text => !string.IsNullOrEmpty(text));
            return string.Concat(parts);
        }

        return content.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            ? null
            : content.GetRawText();
    }

    private static bool IsValidJson(string content)
    {
        try
        {
            using var _ = JsonDocument.Parse(content);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void Accumulate(
        RowOutcome outcome,
        ref long promptTokens,
        ref long completionTokens)
    {
        if (outcome.Usage is null)
        {
            return;
        }

        promptTokens += outcome.Usage.PromptTokens;
        completionTokens += outcome.Usage.CompletionTokens;
    }

    private static void TrackFailure(LlmFailure failure, LlmBlockConfig config, List<LlmFailure> failures)
    {
        if (config.MaxFailureSamples == 0 || failures.Count < config.MaxFailureSamples)
        {
            failures.Add(failure);
        }
    }

    private static async Task WriteFailuresAsync(
        string path,
        IReadOnlyList<LlmFailure> failures,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = fullPath + ".tmp";
        var completed = false;
        try
        {
            await using (var stream = new FileStream(
                tempPath, FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: 64 * 1024, useAsync: true))
            await using (var writer = new StreamWriter(
                stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                foreach (var failure in failures)
                {
                    object data;
                    try
                    {
                        using var document = JsonDocument.Parse(failure.RowJson);
                        data = document.RootElement.Clone();
                    }
                    catch (JsonException)
                    {
                        data = failure.RowJson;
                    }

                    await writer.WriteLineAsync(
                        JsonSerializer.Serialize(new
                        {
                            lineNumber = failure.LineNumber,
                            error = failure.Error,
                            data
                        }).AsMemory(), ct);
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

    private static string Truncate(string value) =>
        value.Length <= 200 ? value : value[..197] + "...";

    private sealed record RowOutcome(string? Json, LlmFailure? Failure, LlmTokenUsage? Usage);

    private sealed record LlmFailure(long LineNumber, string RowJson, string Error);
}
