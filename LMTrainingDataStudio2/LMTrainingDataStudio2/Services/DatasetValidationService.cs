using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services.Datasets;

namespace LMTrainingDataStudio2.Services;

/// <summary>
/// Validates dataset files. JSONL is scanned byte-by-byte for invalid UTF-8,
/// oversized lines and JSON syntax; CSV/Parquet rows are validated as JSON objects.
/// </summary>
public sealed class DatasetValidationService
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly HashSet<string> KnownRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "user", "assistant", "tool", "function", "developer"
    };

    private static readonly DatasetFormatConverter FormatDetector = new();

    private readonly AppSettings _settings;

    public DatasetValidationService(AppSettings? settings = null)
    {
        _settings = settings ?? new AppSettings();
    }

    /// <summary>
    /// Validates a JSONL file line by line, collecting syntax errors.
    /// </summary>
    public async Task<ValidationResult> ValidateJsonlAsync(
        string filePath,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var result = new ValidationResult();
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
        {
            result.Issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Error,
                Message = $"File not found: {filePath}",
                FilePath = filePath
            });
            return result;
        }

        var totalBytes = fileInfo.Length;
        long bytesRead = 0;
        long lineNumber = 0;

        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan | FileOptions.Asynchronous);
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 65536);

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            lineNumber++;

            var line = await reader.ReadLineAsync(ct);
            if (line == null) break;

            bytesRead += System.Text.Encoding.UTF8.GetByteCount(line) + 1;

            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                JsonNode.Parse(line);
            }
            catch (JsonException ex)
            {
                result.Issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Error,
                    Message = $"JSON syntax error: {ex.Message}",
                    LineNumber = lineNumber,
                    FilePath = filePath
                });
            }

            if (lineNumber % 5000 == 0)
            {
                progress?.Report((double)bytesRead / totalBytes);
            }
        }

        progress?.Report(1.0);
        return result;
    }

    /// <summary>
    /// Quality validation with codes, JSON paths and a bounded issue list.
    /// </summary>
    public async Task<DatasetValidationReport> ValidateAsync(
        string filePath,
        DataFileType fileType,
        DatasetValidationOptions? options = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new DatasetValidationOptions();
        var report = new DatasetValidationReport();
        var stopwatch = Stopwatch.StartNew();
        var fullPath = SafeFullPath(filePath);

        try
        {
            if (!File.Exists(fullPath))
            {
                AddIssue(report, options, new ValidationIssue
                {
                    Severity = ValidationSeverity.Error,
                    Code = "file-not-found",
                    Message = $"File not found: {fullPath}",
                    FilePath = fullPath
                });
                return report;
            }

            if (fileType == DataFileType.Jsonl)
            {
                await ValidateJsonlRowsAsync(fullPath, report, options, progress, cancellationToken);
            }
            else
            {
                await ValidateReaderRowsAsync(fullPath, fileType, report, options, progress, cancellationToken);
            }

            return report;
        }
        finally
        {
            report.Elapsed = stopwatch.Elapsed;
        }
    }

    /// <summary>
    /// Previews JSON syntax fixes without touching the source file.
    /// </summary>
    public async Task<DatasetAutoFixPreview> PreviewAutoFixAsync(
        string filePath,
        DatasetValidationOptions? options = null,
        int maxSamples = 100,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new DatasetValidationOptions();
        var stopwatch = Stopwatch.StartNew();
        var fullPath = SafeFullPath(filePath);
        var preview = new DatasetAutoFixPreview();

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Dataset file not found: '{fullPath}'.", fullPath);
        }

        await using var reader = new JsonlRawLineReader(fullPath, options.EffectiveMaxLineBytes);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null) break;

            preview.ScannedRows++;
            if (line.TooLong)
            {
                preview.InvalidRows++;
                preview.UnfixableRows++;
            }
            else if (!TryDecode(line.Bytes!, out var text))
            {
                preview.InvalidRows++;
                preview.UnfixableRows++;
            }
            else if (!string.IsNullOrWhiteSpace(text) && !IsValidJson(text))
            {
                preview.InvalidRows++;
                var fixedText = TryAutoFix(text);
                if (fixedText is null)
                {
                    preview.UnfixableRows++;
                }
                else
                {
                    preview.FixableRows++;
                    if (preview.Samples.Count < maxSamples)
                    {
                        preview.Samples.Add(new DatasetFixSample(
                            line.LineNumber, Truncate(text, 2000), Truncate(fixedText, 2000)));
                    }
                    else
                    {
                        preview.SamplesTruncated = true;
                    }
                }
            }

            if (reader.Length > 0 && preview.ScannedRows % 2000 == 0)
            {
                progress?.Report((double)reader.BytesConsumed / reader.Length);
            }
        }

        progress?.Report(1.0);
        preview.Elapsed = stopwatch.Elapsed;
        return preview;
    }

    /// <summary>
    /// Writes a fixed copy to <paramref name="targetPath"/>; the source file is never modified.
    /// Lines that cannot be fixed are copied byte-for-byte.
    /// </summary>
    public async Task<DatasetAutoFixResult> WriteAutoFixAsync(
        string sourcePath,
        string targetPath,
        DatasetValidationOptions? options = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new DatasetValidationOptions();
        var source = SafeFullPath(sourcePath);
        var target = SafeFullPath(targetPath);

        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Source and target paths must differ.", nameof(targetPath));
        }

        if (!File.Exists(source))
        {
            throw new FileNotFoundException($"Dataset file not found: '{source}'.", source);
        }

        var directory = Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var stopwatch = Stopwatch.StartNew();
        var tempPath = target + ".tmp-" + Guid.NewGuid().ToString("N");
        var completed = false;
        FileStream? input = null;
        FileStream? output = null;

        try
        {
            input = new FileStream(
                source, FileMode.Open, FileAccess.Read, FileShare.Read, 65536,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            output = new FileStream(
                tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536,
                FileOptions.Asynchronous);

            var writer = new AutoFixWriter(output, options.EffectiveMaxLineBytes, TryAutoFix);
            var buffer = new byte[65536];
            long totalRead = 0;
            int read;

            while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                totalRead += read;
                await writer.AppendAsync(buffer.AsMemory(0, read), cancellationToken);
                if (input.Length > 0)
                {
                    progress?.Report((double)totalRead / input.Length);
                }
            }

            await writer.FinishAsync(cancellationToken);
            await output.FlushAsync(cancellationToken);
            await output.DisposeAsync();
            output = null;

            File.Move(tempPath, target, overwrite: true);
            completed = true;
            progress?.Report(1.0);

            return new DatasetAutoFixResult(
                writer.ScannedRows, writer.FixedRows, writer.UnfixableRows,
                target, stopwatch.Elapsed);
        }
        finally
        {
            if (output is not null)
            {
                await output.DisposeAsync();
            }

            if (input is not null)
            {
                await input.DisposeAsync();
            }

            if (!completed)
            {
                TryDelete(tempPath);
            }
        }
    }

    /// <summary>
    /// Attempts to auto-fix simple JSON syntax errors in a line.
    /// Returns the fixed line or null if unfixable.
    /// </summary>
    public string? TryAutoFix(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        var trimmed = line.Trim();

        // Try adding missing closing brace
        if (trimmed.StartsWith('{') && !trimmed.EndsWith('}'))
        {
            var attempt = trimmed + "}";
            if (IsValidJson(attempt)) return attempt;
        }

        // Try adding missing closing bracket
        if (trimmed.StartsWith('[') && !trimmed.EndsWith(']'))
        {
            var attempt = trimmed + "]";
            if (IsValidJson(attempt)) return attempt;
        }

        // Try fixing trailing comma before closing brace/bracket
        var trailingComma = trimmed.TrimEnd('}', ']');
        if (trailingComma.EndsWith(','))
        {
            var suffix = trimmed[trailingComma.Length..];
            var attempt = trailingComma[..^1] + suffix;
            if (IsValidJson(attempt)) return attempt;
        }

        // Try wrapping in braces if it looks like key-value pairs
        if (!trimmed.StartsWith('{') && trimmed.Contains(':'))
        {
            var attempt = "{" + trimmed + "}";
            if (IsValidJson(attempt)) return attempt;
        }

        return null;
    }

    private async Task ValidateJsonlRowsAsync(
        string fullPath,
        DatasetValidationReport report,
        DatasetValidationOptions options,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        await using var reader = new JsonlRawLineReader(fullPath, options.EffectiveMaxLineBytes);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null) break;

            report.ScannedRows++;
            if (line.TooLong)
            {
                AddIssue(report, options, Error(
                    "line-too-long",
                    $"Line exceeds {options.EffectiveMaxLineBytes:N0} bytes.",
                    fullPath, line.LineNumber, path: null));
            }
            else if (!TryDecode(line.Bytes!, out var text))
            {
                AddIssue(report, options, Error(
                    "invalid-utf8", "Line is not valid UTF-8.",
                    fullPath, line.LineNumber, path: null));
            }
            else if (string.IsNullOrWhiteSpace(text))
            {
                AddIssue(report, options, Warning(
                    "empty-line", "Line is empty.",
                    fullPath, line.LineNumber, path: null));
            }
            else
            {
                ValidateJsonText(text, fullPath, line.LineNumber, issue =>
                    AddIssue(report, options, issue));
            }

            if (reader.Length > 0 && report.ScannedRows % 2000 == 0)
            {
                progress?.Report((double)reader.BytesConsumed / reader.Length);
            }
        }

        progress?.Report(1.0);
    }

    private async Task ValidateReaderRowsAsync(
        string fullPath,
        DataFileType fileType,
        DatasetValidationReport report,
        DatasetValidationOptions options,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        await using var reader = DatasetIO.CreateReader(fileType, _settings);
        await reader.OpenAsync(fullPath, cancellationToken);

        const int pageSize = 500;
        for (var start = 0L; start < reader.TotalRows; start += pageSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await reader.ReadPageAsync(start, pageSize, cancellationToken);
            foreach (var row in page)
            {
                report.ScannedRows++;
                ValidateJsonText(row.Json, fullPath, row.LineNumber + 1, issue =>
                    AddIssue(report, options, issue));
            }

            progress?.Report(reader.TotalRows == 0 ? 1.0 : (double)(start + page.Count) / reader.TotalRows);
        }
    }

    private void ValidateJsonText(
        string text, string filePath, long lineNumber, Action<ValidationIssue> add)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(text);
        }
        catch (JsonException ex)
        {
            var fixable = TryAutoFix(text) is not null
                ? " The line can be repaired with the auto-fix preview."
                : string.Empty;
            add(Error("json-syntax", $"JSON syntax error: {ex.Message}.{fixable}",
                filePath, lineNumber, path: null));
            return;
        }

        if (node is not JsonObject obj)
        {
            add(Error("not-object", "Row must be a JSON object.",
                filePath, lineNumber, path: "$"));
            return;
        }

        var format = FormatDetector.DetectFormat(obj);
        switch (format)
        {
            case DatasetFormat.OpenAI:
                ValidateOpenAi(obj, filePath, lineNumber, add);
                break;
            case DatasetFormat.ShareGPT:
                ValidateShareGpt(obj, filePath, lineNumber, add);
                break;
            case DatasetFormat.Alpaca:
                ValidateAlpaca(obj, filePath, lineNumber, add);
                break;
            case DatasetFormat.Dpo:
                ValidateDpo(obj, filePath, lineNumber, add);
                break;
            case DatasetFormat.Pretrain:
                ValidatePretrain(obj, filePath, lineNumber, add);
                break;
            default:
                add(Warning("unrecognized-format",
                    "Row does not match a known dataset format.",
                    filePath, lineNumber, path: "$"));
                break;
        }
    }

    private static void ValidateOpenAi(
        JsonObject row, string filePath, long lineNumber, Action<ValidationIssue> add)
    {
        if (!row.TryGetPropertyValue("messages", out var messagesNode) || messagesNode is null)
        {
            add(Warning("missing-messages", "Row has no 'messages' array.",
                filePath, lineNumber, "$.messages"));
            return;
        }

        if (messagesNode is not JsonArray messages)
        {
            add(Error("invalid-messages", "'messages' must be an array.",
                filePath, lineNumber, "$.messages"));
            return;
        }

        if (messages.Count == 0)
        {
            add(Warning("empty-messages", "'messages' is empty.",
                filePath, lineNumber, "$.messages"));
            return;
        }

        for (var i = 0; i < messages.Count; i++)
        {
            var path = $"$.messages[{i}]";
            if (messages[i] is not JsonObject message)
            {
                add(Error("invalid-message", "Message must be an object.",
                    filePath, lineNumber, path));
                continue;
            }

            ValidateRole(message, "role", filePath, lineNumber, path, add);
            var hasToolCalls = message.TryGetPropertyValue("tool_calls", out var calls)
                               && calls is JsonArray { Count: > 0 };
            if (!HasContent(message, "content") && !hasToolCalls)
            {
                add(Warning("empty-content", "Message has no content.",
                    filePath, lineNumber, path + ".content"));
            }
        }

        ValidateTools(row, filePath, lineNumber, add);
    }

    private static void ValidateShareGpt(
        JsonObject row, string filePath, long lineNumber, Action<ValidationIssue> add)
    {
        if (!row.TryGetPropertyValue("conversations", out var conversationsNode) ||
            conversationsNode is null)
        {
            add(Warning("missing-conversations", "Row has no 'conversations' array.",
                filePath, lineNumber, "$.conversations"));
            return;
        }

        if (conversationsNode is not JsonArray conversations)
        {
            add(Error("invalid-conversations", "'conversations' must be an array.",
                filePath, lineNumber, "$.conversations"));
            return;
        }

        if (conversations.Count == 0)
        {
            add(Warning("empty-conversations", "'conversations' is empty.",
                filePath, lineNumber, "$.conversations"));
            return;
        }

        for (var i = 0; i < conversations.Count; i++)
        {
            var path = $"$.conversations[{i}]";
            if (conversations[i] is not JsonObject message)
            {
                add(Error("invalid-message", "Conversation turn must be an object.",
                    filePath, lineNumber, path));
                continue;
            }

            var roleKey = message.ContainsKey("from") ? "from" : "role";
            ValidateRole(message, roleKey, filePath, lineNumber, path, add);

            var contentKey = message.ContainsKey("content") ? "content" : "value";
            if (!HasContent(message, contentKey))
            {
                add(Warning("empty-content", "Conversation turn has no content.",
                    filePath, lineNumber, path + "." + contentKey));
            }
        }
    }

    private static void ValidateAlpaca(
        JsonObject row, string filePath, long lineNumber, Action<ValidationIssue> add)
    {
        if (!HasContent(row, "instruction") && !HasContent(row, "input"))
        {
            add(Warning("missing-instruction",
                "Alpaca row has neither 'instruction' nor 'input' content.",
                filePath, lineNumber, "$.instruction"));
        }

        if (!HasContent(row, "output"))
        {
            add(Warning("empty-output", "Alpaca row has no 'output' content.",
                filePath, lineNumber, "$.output"));
        }
    }

    private static void ValidateDpo(
        JsonObject row, string filePath, long lineNumber, Action<ValidationIssue> add)
    {
        if (!HasContent(row, "chosen"))
        {
            add(Warning("empty-chosen", "DPO row has no 'chosen' content.",
                filePath, lineNumber, "$.chosen"));
        }

        if (!HasContent(row, "rejected"))
        {
            add(Warning("empty-rejected", "DPO row has no 'rejected' content.",
                filePath, lineNumber, "$.rejected"));
        }
    }

    private static void ValidatePretrain(
        JsonObject row, string filePath, long lineNumber, Action<ValidationIssue> add)
    {
        if (!HasContent(row, "text"))
        {
            add(Warning("empty-text", "Pretrain row has no 'text' content.",
                filePath, lineNumber, "$.text"));
        }
    }

    private static void ValidateRole(
        JsonObject message, string key, string filePath, long lineNumber,
        string parentPath, Action<ValidationIssue> add)
    {
        var path = parentPath + "." + key;
        if (!message.TryGetPropertyValue(key, out var roleNode) || roleNode is null)
        {
            add(Error("missing-role", "Message has no role.",
                filePath, lineNumber, path));
            return;
        }

        if (roleNode is not JsonValue value ||
            !value.TryGetValue<string>(out var role) ||
            string.IsNullOrWhiteSpace(role))
        {
            add(Error("invalid-role", "Message role must be a non-empty string.",
                filePath, lineNumber, path));
            return;
        }

        if (!KnownRoles.Contains(role))
        {
            add(Warning("unknown-role", $"Unknown role '{role}'.",
                filePath, lineNumber, path));
        }
    }

    private static void ValidateTools(
        JsonObject row, string filePath, long lineNumber, Action<ValidationIssue> add)
    {
        if (!row.TryGetPropertyValue("tools", out var toolsNode) || toolsNode is null)
        {
            return;
        }

        if (toolsNode is not JsonArray tools)
        {
            add(Warning("invalid-tools", "'tools' must be an array.",
                filePath, lineNumber, "$.tools"));
            return;
        }

        for (var i = 0; i < tools.Count; i++)
        {
            if (tools[i] is not JsonObject)
            {
                add(Warning("invalid-tool", "Tool definition must be an object.",
                    filePath, lineNumber, $"$.tools[{i}]"));
            }
        }
    }

    private static bool HasContent(JsonObject obj, string key)
    {
        if (!obj.TryGetPropertyValue(key, out var node) || node is null)
        {
            return false;
        }

        return node switch
        {
            JsonValue value => value.TryGetValue<string>(out var text)
                ? !string.IsNullOrWhiteSpace(text)
                : true,
            JsonArray array => array.Count > 0,
            JsonObject child => child.Count > 0,
            _ => false
        };
    }

    private static bool TryDecode(byte[] bytes, out string text)
    {
        try
        {
            text = StrictUtf8.GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            text = string.Empty;
            return false;
        }
    }

    private static bool IsValidJson(string text)
    {
        try
        {
            JsonNode.Parse(text);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static ValidationIssue Error(
        string code, string message, string filePath, long lineNumber, string? path) =>
        new()
        {
            Severity = ValidationSeverity.Error,
            Code = code,
            Message = message,
            FilePath = filePath,
            LineNumber = lineNumber,
            Path = path
        };

    private static ValidationIssue Warning(
        string code, string message, string filePath, long lineNumber, string? path) =>
        new()
        {
            Severity = ValidationSeverity.Warning,
            Code = code,
            Message = message,
            FilePath = filePath,
            LineNumber = lineNumber,
            Path = path
        };

    private static void AddIssue(
        DatasetValidationReport report, DatasetValidationOptions options, ValidationIssue issue)
    {
        switch (issue.Severity)
        {
            case ValidationSeverity.Error:
                report.ErrorCount++;
                break;
            case ValidationSeverity.Warning:
                report.WarningCount++;
                break;
            default:
                report.InfoCount++;
                break;
        }

        if (report.Issues.Count < options.EffectiveMaxIssues)
        {
            report.Issues.Add(issue);
        }
        else
        {
            report.IssuesTruncated = true;
        }
    }

    private static string SafeFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "…";

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

    /// <summary>
    /// Streaming auto-fix writer. Untouched lines keep their original bytes;
    /// only syntax-repaired lines are re-encoded.
    /// </summary>
    private sealed class AutoFixWriter
    {
        private static readonly byte[] NewLine = { (byte)'\n' };

        private readonly Stream _output;
        private readonly int _maxLineBytes;
        private readonly Func<string, string?> _tryAutoFix;
        private readonly MemoryStream _line = new();
        private bool _overlong;
        private bool _lineStarted;
        private bool _firstLine = true;

        public AutoFixWriter(Stream output, int maxLineBytes, Func<string, string?> tryAutoFix)
        {
            _output = output;
            _maxLineBytes = maxLineBytes;
            _tryAutoFix = tryAutoFix;
        }

        public long ScannedRows { get; private set; }
        public long FixedRows { get; private set; }
        public long UnfixableRows { get; private set; }

        public async Task AppendAsync(ReadOnlyMemory<byte> chunk, CancellationToken cancellationToken)
        {
            while (chunk.Length > 0)
            {
                var newline = chunk.Span.IndexOf((byte)'\n');
                if (newline < 0)
                {
                    await AppendSegmentAsync(chunk, cancellationToken);
                    return;
                }

                await AppendSegmentAsync(chunk.Slice(0, newline), cancellationToken);
                chunk = chunk.Slice(newline + 1);
                await CompleteLineAsync(cancellationToken);
            }
        }

        public Task FinishAsync(CancellationToken cancellationToken)
        {
            return _lineStarted || _line.Length > 0
                ? CompleteLineAsync(cancellationToken)
                : Task.CompletedTask;
        }

        private async Task AppendSegmentAsync(
            ReadOnlyMemory<byte> segment, CancellationToken cancellationToken)
        {
            if (segment.Length == 0)
            {
                return;
            }

            _lineStarted = true;

            if (_overlong)
            {
                await _output.WriteAsync(segment, cancellationToken);
                return;
            }

            if (_line.Length + segment.Length > _maxLineBytes)
            {
                _overlong = true;
                var buffered = _line.ToArray();
                _line.SetLength(0);
                if (buffered.Length > 0)
                {
                    await _output.WriteAsync(buffered, cancellationToken);
                }

                await _output.WriteAsync(segment, cancellationToken);
                return;
            }

            _line.Write(segment.Span);
        }

        private async Task CompleteLineAsync(CancellationToken cancellationToken)
        {
            ScannedRows++;

            if (_overlong)
            {
                UnfixableRows++;
                await WriteNewlineAsync(cancellationToken);
                ResetLine();
                return;
            }

            var bytes = _line.ToArray();
            if (bytes.Length > 0 && bytes[^1] == (byte)'\r')
            {
                Array.Resize(ref bytes, bytes.Length - 1);
            }

            if (_firstLine && bytes.Length >= 3 &&
                bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                bytes = bytes[3..];
            }

            _firstLine = false;

            if (!TryDecode(bytes, out var text))
            {
                UnfixableRows++;
                await WriteLineAsync(bytes, cancellationToken);
                ResetLine();
                return;
            }

            if (string.IsNullOrWhiteSpace(text) || IsValidJson(text))
            {
                await WriteLineAsync(bytes, cancellationToken);
                ResetLine();
                return;
            }

            var fixedText = _tryAutoFix(text);
            if (fixedText is null)
            {
                UnfixableRows++;
                await WriteLineAsync(bytes, cancellationToken);
            }
            else
            {
                FixedRows++;
                await WriteLineAsync(StrictUtf8.GetBytes(fixedText), cancellationToken);
            }

            ResetLine();
        }

        private async Task WriteLineAsync(byte[] bytes, CancellationToken cancellationToken)
        {
            if (bytes.Length > 0)
            {
                await _output.WriteAsync(bytes, cancellationToken);
            }

            await WriteNewlineAsync(cancellationToken);
        }

        private Task WriteNewlineAsync(CancellationToken cancellationToken) =>
            _output.WriteAsync(NewLine, cancellationToken).AsTask();

        private void ResetLine()
        {
            _line.SetLength(0);
            _overlong = false;
            _lineStarted = false;
        }
    }
}
