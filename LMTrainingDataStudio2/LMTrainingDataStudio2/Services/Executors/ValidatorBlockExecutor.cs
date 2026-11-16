using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Json.Schema;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services.Simulation;

namespace LMTrainingDataStudio2.Services.Executors;

/// <summary>
/// Filters rows with an expression rule or a JSON Schema document.
/// Invalid rows are dropped when <see cref="ValidatorBlockConfig.RejectOnFailure"/> is true
/// and can be written to <see cref="ValidatorBlockConfig.RejectOutputPath"/>.
/// </summary>
public sealed class ValidatorBlockExecutor : IBlockExecutor
{
    public BlockType Type => BlockType.Validator;

    public Task<BlockExecutionResult> ExecuteAsync(BlockExecutionContext context, CancellationToken cancellationToken)
    {
        if (context.Block.Config is not ValidatorBlockConfig config)
        {
            throw new InvalidOperationException(
                $"Validator block '{context.Block.Name}' has an invalid configuration.");
        }

        if (context.Input is null)
        {
            throw new InvalidOperationException(
                $"Validator block '{context.Block.Name}' requires an input connection.");
        }

        if (string.IsNullOrWhiteSpace(config.Rule))
        {
            throw new InvalidOperationException($"Validator block '{context.Block.Name}' has no rule configured.");
        }

        JsonSchema? schema = null;
        if (IsSchemaRule(config.RuleKind))
        {
            try
            {
                schema = JsonSchemaRuleEvaluator.Parse(config.Rule);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                throw new InvalidOperationException(
                    $"Validator block '{context.Block.Name}' has an invalid JSON Schema: {ex.Message}",
                    ex);
            }
        }
        else if (!IsExpressionRule(config.RuleKind))
        {
            throw new NotSupportedException(
                $"Validator rule kind '{config.RuleKind}' is not supported; use 'expression' or 'schema'.");
        }

        return Task.FromResult(new BlockExecutionResult(FilterAsync(context, config, schema, cancellationToken)));
    }

    private static async IAsyncEnumerable<DatasetRow> FilterAsync(
        BlockExecutionContext context,
        ValidatorBlockConfig config,
        JsonSchema? schema,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var outputIndex = 0L;
        var rejected = 0L;
        var passed = 0L;

        await using var rejectSink = config.RejectOnFailure
            ? RejectSink.TryCreate(config.RejectOutputPath)
            : null;

        await foreach (var row in context.Input!.WithCancellation(ct))
        {
            var failure = ValidateRow(row, context.Block.Name, config, schema);
            var isValid = failure is null;

            if (!isValid && !config.RejectOnFailure)
            {
                isValid = true;
            }

            if (isValid)
            {
                var outputJson = row.Json;
                if (config.Cleanup is { HasAnyRule: true } cleanup)
                {
                    try
                    {
                        outputJson = TrajectoryCleaner.CleanJson(row.Json, cleanup);
                    }
                    catch (InvalidOperationException ex)
                    {
                        throw new InvalidOperationException(
                            $"Validator block '{context.Block.Name}' cleanup failed on line {row.LineNumber}: {ex.Message}",
                            ex);
                    }
                }

                passed++;
                yield return new DatasetRow(outputIndex++, outputJson);
            }
            else
            {
                rejected++;
                if (rejectSink is not null)
                {
                    await rejectSink.WriteAsync(row, failure ?? "Rule did not match.", ct);
                }
            }

            context.RowsProcessed?.Report(outputIndex + rejected);
        }

        if (rejectSink is not null)
        {
            await rejectSink.CommitAsync(ct);
        }

        var total = passed + rejected;
        var failureRate = total == 0 ? 0 : (double)rejected / total;
        context.Log($"Passed {passed}, rejected {rejected} row(s); failure rate {failureRate:P1}.");
    }

    private static string? ValidateRow(
        DatasetRow row,
        string blockName,
        ValidatorBlockConfig config,
        JsonSchema? schema)
    {
        if (schema is not null)
        {
            return JsonSchemaRuleEvaluator.Validate(schema, row.Json);
        }

        try
        {
            using var document = JsonRowReader.Parse(row, blockName);
            return PredicateRuleEvaluator.Evaluate(config.Rule, document.RootElement)
                ? null
                : "Expression rule did not match.";
        }
        catch (InvalidDataException ex)
        {
            return ex.Message;
        }
    }

    private static bool IsSchemaRule(string ruleKind) =>
        string.Equals(ruleKind, "schema", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(ruleKind, "json_schema", StringComparison.OrdinalIgnoreCase);

    private static bool IsExpressionRule(string ruleKind) =>
        string.Equals(ruleKind, "expression", StringComparison.OrdinalIgnoreCase);

    private sealed class RejectSink : IAsyncDisposable
    {
        private readonly string _tempPath;
        private readonly string _finalPath;
        private readonly StreamWriter _writer;
        private bool _committed;
        private bool _disposed;

        private RejectSink(string finalPath)
        {
            _finalPath = finalPath;
            _tempPath = finalPath + ".tmp";

            var directory = Path.GetDirectoryName(finalPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _writer = new StreamWriter(
                new FileStream(_tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        public static RejectSink? TryCreate(string? path) =>
            string.IsNullOrWhiteSpace(path) ? null : new RejectSink(Path.GetFullPath(path));

        public Task WriteAsync(DatasetRow row, string reason, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            object data;
            try
            {
                using var document = JsonDocument.Parse(row.Json);
                data = document.RootElement.Clone();
            }
            catch (JsonException)
            {
                data = row.Json;
            }

            var line = JsonSerializer.Serialize(new
            {
                lineNumber = row.LineNumber,
                reason,
                data
            });
            return _writer.WriteLineAsync(line);
        }

        public async Task CommitAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            await _writer.FlushAsync(ct);
            await DisposeWriterAsync();
            File.Move(_tempPath, _finalPath, overwrite: true);
            _committed = true;
        }

        public async ValueTask DisposeAsync()
        {
            await DisposeWriterAsync();
            if (_committed) return;
            TryDelete(_tempPath);
        }

        private async Task DisposeWriterAsync()
        {
            if (_disposed) return;
            _disposed = true;
            await _writer.DisposeAsync();
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
}