using System.Text.Json;
using System.Text.Json.Nodes;
using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Services;

/// <summary>
/// Validates JSONL dataset files line by line.
/// Supports async streaming validation with progress reporting.
/// </summary>
public sealed class DatasetValidationService
{
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
}
