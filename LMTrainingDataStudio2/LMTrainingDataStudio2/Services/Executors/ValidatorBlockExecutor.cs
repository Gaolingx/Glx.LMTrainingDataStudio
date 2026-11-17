using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Json.Schema;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services.Simulation;

namespace LMTrainingDataStudio2.Services.Executors;

/// <summary>
/// Evaluates one or more expression/schema/regex/script rules.
/// Passing rows go to the <c>pass</c> output, rejected rows can go to the <c>reject</c>
/// output and/or <see cref="ValidatorBlockConfig.RejectOutputPath"/>.
/// </summary>
public sealed class ValidatorBlockExecutor : IBlockExecutor
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    private readonly Func<string, JsonElement, CancellationToken, Task<bool>>? _scriptEvaluator;

    /// <param name="scriptEvaluator">
    /// Optional override for script rules. The default uses the Fluid template engine and
    /// expects the rendered result to be "true" or "false".
    /// </param>
    public ValidatorBlockExecutor(
        Func<string, JsonElement, CancellationToken, Task<bool>>? scriptEvaluator = null) =>
        _scriptEvaluator = scriptEvaluator;

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

        var rules = CompileRules(context.Block.Name, config);
        if (rules.Count == 0)
        {
            throw new InvalidOperationException($"Validator block '{context.Block.Name}' has no rule configured.");
        }

        if (config.TimeoutMs < 0)
        {
            throw new InvalidOperationException(
                $"Validator block '{context.Block.Name}' timeout must not be negative.");
        }

        var emitRejects = context.ConnectedOutputPorts is null ||
                          context.ConnectedOutputPorts.Contains(
                              BlockPortSchema.ValidatorRejectPort, StringComparer.OrdinalIgnoreCase);
        var rejectChannel = emitRejects
            ? Channel.CreateUnbounded<DatasetRow>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true
            })
            : null;

        var result = FilterAsync(context, config, rules, rejectChannel, cancellationToken);
        var additional = rejectChannel is null
            ? null
            : new Dictionary<string, IAsyncEnumerable<DatasetRow>>(StringComparer.OrdinalIgnoreCase)
            {
                [BlockPortSchema.ValidatorRejectPort] = rejectChannel.Reader.ReadAllAsync(cancellationToken)
            };

        return Task.FromResult(new BlockExecutionResult(result, additional));
    }

    private async IAsyncEnumerable<DatasetRow> FilterAsync(
        BlockExecutionContext context,
        ValidatorBlockConfig config,
        IReadOnlyList<CompiledRule> rules,
        Channel<DatasetRow>? rejectChannel,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var outputIndex = 0L;
        var rejected = 0L;
        var passed = 0L;

        await using var rejectSink = config.RejectOnFailure
            ? RejectSink.TryCreate(config.RejectOutputPath)
            : null;

        try
        {
            await foreach (var row in context.Input!.WithCancellation(ct))
            {
                var failure = await ValidateRowAsync(row, context.Block.Name, config, rules, ct);
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
                    continue;
                }

                rejected++;
                var reason = failure ?? "Rule did not match.";
                if (rejectSink is not null)
                {
                    await rejectSink.WriteAsync(row, reason, ct);
                }

                if (rejectChannel is not null)
                {
                    await rejectChannel.Writer.WriteAsync(
                        new DatasetRow(outputIndex++, row.Json), ct);
                }

                context.RowsProcessed?.Report(outputIndex + rejected);
            }
        }
        finally
        {
            rejectChannel?.Writer.TryComplete();
        }

        if (rejectSink is not null)
        {
            await rejectSink.CommitAsync(ct);
        }

        var total = passed + rejected;
        var failureRate = total == 0 ? 0 : (double)rejected / total;
        context.Log($"Passed {passed}, rejected {rejected} row(s); failure rate {failureRate:P1}.");
    }

    private async Task<string?> ValidateRowAsync(
        DatasetRow row,
        string blockName,
        ValidatorBlockConfig config,
        IReadOnlyList<CompiledRule> rules,
        CancellationToken ct)
    {
        var evaluation = EvaluateAsync(row, blockName, config, rules, ct);
        if (config.TimeoutMs > 0)
        {
            try
            {
                return await evaluation.WaitAsync(TimeSpan.FromMilliseconds(config.TimeoutMs), ct);
            }
            catch (TimeoutException)
            {
                return $"Validator rule timed out after {config.TimeoutMs} ms.";
            }
        }

        return await evaluation;
    }

    private async Task<string?> EvaluateAsync(
        DatasetRow row,
        string blockName,
        ValidatorBlockConfig config,
        IReadOnlyList<CompiledRule> rules,
        CancellationToken ct)
    {
        JsonDocument? document = null;
        try
        {
            var needsDocument = rules.Any(rule => rule.Kind is RuleKind.Expression or RuleKind.Script);
            if (needsDocument)
            {
                try
                {
                    document = JsonRowReader.Parse(row, blockName);
                }
                catch (InvalidDataException ex)
                {
                    return ex.Message;
                }
            }

            var failures = new List<string>();
            foreach (var rule in rules)
            {
                ct.ThrowIfCancellationRequested();
                var (matched, reason) = await EvaluateRuleAsync(rule, row, document?.RootElement, ct);
                if (rule.Source.Negate)
                {
                    matched = !matched;
                }

                if (!matched)
                {
                    failures.Add(reason ?? $"Rule '{rule.Source.Kind}' did not match.");
                    if (config.MatchAllRules)
                    {
                        return failures[0];
                    }
                }
                else if (!config.MatchAllRules)
                {
                    return null;
                }
            }

            if (config.MatchAllRules)
            {
                return failures.Count == 0 ? null : failures[0];
            }

            return failures.Count == 0
                ? "No validator rule matched."
                : null;
        }
        finally
        {
            document?.Dispose();
        }
    }

    private async Task<(bool Matched, string? Reason)> EvaluateRuleAsync(
        CompiledRule rule,
        DatasetRow row,
        JsonElement? root,
        CancellationToken ct)
    {
        switch (rule.Kind)
        {
            case RuleKind.Schema:
                var schemaFailure = JsonSchemaRuleEvaluator.Validate(rule.Schema!, row.Json);
                return (schemaFailure is null, schemaFailure);

            case RuleKind.Regex:
                {
                    try
                    {
                        var matched = rule.Regex!.IsMatch(row.Json);
                        return (matched, matched ? null : $"Row did not match /{rule.Source.Rule}/.");
                    }
                    catch (RegexMatchTimeoutException)
                    {
                        return (false, "Regular expression rule timed out.");
                    }
                }

            case RuleKind.Expression:
                {
                    var matched = PredicateRuleEvaluator.Evaluate(rule.Source.Rule, root!.Value);
                    return (matched, matched ? null : "Expression rule did not match.");
                }

            case RuleKind.Script:
                {
                    try
                    {
                        var matched = _scriptEvaluator is not null
                            ? await _scriptEvaluator(rule.Source.Rule, root!.Value, ct)
                            : await RenderScriptAsync(rule.Source.Rule, root!.Value);
                        return (matched, matched ? null : "Script rule rendered false.");
                    }
                    catch (InvalidOperationException ex)
                    {
                        return (false, $"Script rule failed: {ex.Message}");
                    }
                }

            default:
                return (false, $"Unsupported validator rule kind '{rule.Source.Kind}'.");
        }
    }

    private static async Task<bool> RenderScriptAsync(string script, JsonElement root)
    {
        var rendered = (await TemplateRenderer.RenderAsync(script, root)).Trim();
        if (bool.TryParse(rendered, out var result))
        {
            return result;
        }

        throw new InvalidOperationException(
            $"Script rule must render 'true' or 'false'; got '{rendered}'.");
    }

    private static string CreateRejectJson(DatasetRow row, string reason)
    {
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

        return JsonSerializer.Serialize(new
        {
            lineNumber = row.LineNumber,
            reason,
            data
        });
    }

    private static IReadOnlyList<CompiledRule> CompileRules(string blockName, ValidatorBlockConfig config)
    {
        var sources = GetRuleSources(config);
        var compiled = new List<CompiledRule>(sources.Count);
        foreach (var source in sources)
        {
            var kind = source.Kind.Trim().ToLowerInvariant();
            switch (kind)
            {
                case "schema" or "json_schema":
                    try
                    {
                        compiled.Add(new CompiledRule(
                            RuleKind.Schema, source, JsonSchemaRuleEvaluator.Parse(source.Rule), null));
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        throw new InvalidOperationException(
                            $"Validator block '{blockName}' has an invalid JSON Schema: {ex.Message}", ex);
                    }

                    break;

                case "expression":
                    compiled.Add(new CompiledRule(RuleKind.Expression, source, null, null));
                    break;

                case "regex":
                    try
                    {
                        compiled.Add(new CompiledRule(
                            RuleKind.Regex, source, null, new Regex(source.Rule, RegexOptions.None, RegexTimeout)));
                    }
                    catch (ArgumentException ex)
                    {
                        throw new InvalidOperationException(
                            $"Validator block '{blockName}' has an invalid regular expression: {ex.Message}", ex);
                    }

                    break;

                case "script" or "code":
                    compiled.Add(new CompiledRule(RuleKind.Script, source, null, null));
                    break;

                default:
                    throw new NotSupportedException(
                        $"Validator rule kind '{source.Kind}' is not supported; use expression, schema, regex or script.");
            }
        }

        return compiled;
    }

    internal static IReadOnlyList<ValidatorRuleSpec> GetRuleSources(ValidatorBlockConfig config)
    {
        if (config.Rules is { Length: > 0 })
        {
            return config.Rules
                .Where(rule => !string.IsNullOrWhiteSpace(rule.Rule))
                .ToArray();
        }

        return string.IsNullOrWhiteSpace(config.Rule)
            ? Array.Empty<ValidatorRuleSpec>()
            : [new ValidatorRuleSpec(config.RuleKind, config.Rule)];
    }

    private enum RuleKind
    {
        Expression,
        Schema,
        Regex,
        Script
    }

    private sealed record CompiledRule(
        RuleKind Kind,
        ValidatorRuleSpec Source,
        JsonSchema? Schema,
        Regex? Regex);

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
