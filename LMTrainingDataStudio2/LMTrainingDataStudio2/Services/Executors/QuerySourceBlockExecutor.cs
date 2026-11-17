using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services.Datasets;

namespace LMTrainingDataStudio2.Services.Executors;

/// <summary>
/// Emits user queries from configuration or an external JSONL/CSV/Parquet file.
/// Query strings support the same Fluid template variables as Expression blocks
/// (<c>{{ name }}</c>, conditions, loops and filters).
/// </summary>
public sealed class QuerySourceBlockExecutor : IBlockExecutor
{
    private const int PageSize = 500;

    private readonly AppSettings _settings;

    public QuerySourceBlockExecutor(AppSettings? settings = null) =>
        _settings = settings ?? new AppSettings();

    public BlockType Type => BlockType.QuerySource;

    public Task<BlockExecutionResult> ExecuteAsync(
        BlockExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (context.Block.Config is not QuerySourceBlockConfig config)
        {
            throw new InvalidOperationException(
                $"Query source block '{context.Block.Name}' has an invalid configuration.");
        }

        if (string.IsNullOrWhiteSpace(config.SourcePath))
        {
            var queries = config.Queries.Where(query => !string.IsNullOrWhiteSpace(query)).ToArray();
            if (queries.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Query source block '{context.Block.Name}' has no queries configured.");
            }
        }
        else if (!File.Exists(config.SourcePath))
        {
            throw new InvalidOperationException(
                $"Query source block '{context.Block.Name}' source file does not exist: '{config.SourcePath}'.");
        }

        if (config.Repeat < 1)
        {
            throw new InvalidOperationException(
                $"Query source block '{context.Block.Name}' repeat must be at least 1; got {config.Repeat}.");
        }

        var variables = ValidateVariables(context.Block.Name, config.TemplateVariablesJson);

        return Task.FromResult(new BlockExecutionResult(
            !string.IsNullOrWhiteSpace(config.SourcePath)
                ? GenerateFromSourceAsync(context, config, variables, cancellationToken)
                : GenerateFromLiteralAsync(context, config, variables, cancellationToken)));
    }

    private static string? ValidateVariables(string blockName, string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException(
                    $"Query source block '{blockName}' template variables must be a JSON object.");
            }

            return json;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Query source block '{blockName}' template variables are not valid JSON: {ex.Message}", ex);
        }
    }

    private static async IAsyncEnumerable<DatasetRow> GenerateFromLiteralAsync(
        BlockExecutionContext context,
        QuerySourceBlockConfig config,
        string? variables,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var queries = config.Queries.Where(query => !string.IsNullOrWhiteSpace(query)).ToArray();
        var sequence = config.Mode == QuerySetMode.SameQuery
            ? Enumerable.Repeat(queries[0], config.Repeat)
            : queries.SelectMany(query => Enumerable.Repeat(query, config.Repeat));

        var index = 0L;
        foreach (var template in sequence)
        {
            ct.ThrowIfCancellationRequested();
            var model = BuildModel(variables, index);
            var query = await RenderAsync(template, model);
            var row = new JsonObject { ["query"] = query };
            if (!string.IsNullOrWhiteSpace(config.SystemPrompt))
            {
                row["system"] = config.SystemPrompt;
            }

            yield return new DatasetRow(index++, row.ToJsonString());
            context.RowsProcessed?.Report(index);
        }

        context.Log($"Emitted {index} query row(s) using mode '{config.Mode}'.");
    }

    private async IAsyncEnumerable<DatasetRow> GenerateFromSourceAsync(
        BlockExecutionContext context,
        QuerySourceBlockConfig config,
        string? variables,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var fileType = DatasetIO.DetectFileType(config.SourcePath!);
        await using var reader = DatasetIO.CreateReader(fileType, _settings);
        await reader.OpenAsync(config.SourcePath!, ct);
        context.Log($"Loaded {reader.TotalRows} query row(s) from '{reader.SourcePath}'.");

        var index = 0L;
        var skipped = 0L;
        for (var start = 0L; start < reader.TotalRows; start += PageSize)
        {
            var page = await reader.ReadPageAsync(start, PageSize, ct);
            foreach (var sourceRow in page)
            {
                ct.ThrowIfCancellationRequested();
                using var document = ParseRow(context.Block.Name, sourceRow);
                if (document is null)
                {
                    skipped++;
                    continue;
                }

                for (var repeat = 0; repeat < config.Repeat; repeat++)
                {
                    var query = await BuildQueryFromSourceAsync(
                        context.Block.Name, config, document.RootElement, variables, index);
                    if (string.IsNullOrWhiteSpace(query))
                    {
                        skipped++;
                        continue;
                    }

                    var row = document.RootElement.ValueKind == JsonValueKind.Object
                        ? JsonNode.Parse(document.RootElement.GetRawText()) as JsonObject ?? new JsonObject()
                        : new JsonObject();
                    row["query"] = query;
                    if (config.SystemField is { Length: > 0 } systemField &&
                        document.RootElement.ValueKind == JsonValueKind.Object &&
                        document.RootElement.TryGetProperty(systemField, out var system) &&
                        system.ValueKind == JsonValueKind.String)
                    {
                        row["system"] = system.GetString();
                    }
                    else if (!string.IsNullOrWhiteSpace(config.SystemPrompt))
                    {
                        row["system"] = config.SystemPrompt;
                    }

                    yield return new DatasetRow(index++, row.ToJsonString());
                    context.RowsProcessed?.Report(index);
                }
            }
        }

        context.Log($"Emitted {index} query row(s) from '{config.SourcePath}', skipped {skipped}.");
    }

    private static JsonDocument? ParseRow(string blockName, DatasetRow row)
    {
        try
        {
            return JsonDocument.Parse(row.Json);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"Query source block '{blockName}' row {row.LineNumber} is not valid JSON: {ex.Message}", ex);
        }
    }

    private static async Task<string> BuildQueryFromSourceAsync(
        string blockName,
        QuerySourceBlockConfig config,
        JsonElement root,
        string? variables,
        long index)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return root.ValueKind == JsonValueKind.String
                ? root.GetString() ?? string.Empty
                : root.GetRawText();
        }

        if (!root.TryGetProperty(config.QueryField, out var queryValue) ||
            queryValue.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException(
                $"Query source block '{blockName}' row is missing the '{config.QueryField}' string field.");
        }

        var model = MergeModel(root, variables, index);
        return await RenderAsync(queryValue.GetString() ?? string.Empty, model);
    }

    private static string MergeModel(JsonElement row, string? variables, long index)
    {
        var model = new JsonObject();
        if (row.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in row.EnumerateObject())
            {
                model[property.Name] = JsonNode.Parse(property.Value.GetRawText());
            }
        }

        if (!string.IsNullOrWhiteSpace(variables))
        {
            using var document = JsonDocument.Parse(variables);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                model[property.Name] = JsonNode.Parse(property.Value.GetRawText());
            }
        }

        model["index"] = index;
        return model.ToJsonString();
    }

    private static string BuildModel(string? variables, long index)
    {
        var model = !string.IsNullOrWhiteSpace(variables)
            ? JsonNode.Parse(variables) as JsonObject ?? new JsonObject()
            : new JsonObject();
        model["index"] = index;
        return model.ToJsonString();
    }

    private static async Task<string> RenderAsync(string template, string modelJson)
    {
        using var document = JsonDocument.Parse(modelJson);
        return await TemplateRenderer.RenderAsync(template, document.RootElement);
    }
}
