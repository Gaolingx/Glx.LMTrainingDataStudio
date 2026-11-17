using System.Text.RegularExpressions;

namespace LMTrainingDataStudio2.Services;

/// <summary>
/// Removes credentials from diagnostic text before it is exported off the machine.
/// </summary>
public static class DiagnosticRedactor
{
    public const string Placeholder = "[REDACTED]";

    private static readonly Regex AuthorizationHeader = new(
        @"(?<prefix>\b(?:proxy-)?authorization\s*[:=]\s*)(?:bearer\s+)?[^\s""',;]{6,}",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BearerToken = new(
        @"(?<prefix>\bbearer\s+)[A-Za-z0-9._~+/-]{8,}=*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex JsonSecretField = new(
        "(?<name>\"(?:api[_-]?key|openai[_-]?api[_-]?key|anthropic[_-]?api[_-]?key|" +
        "access[_-]?token|refresh[_-]?token|id[_-]?token|client[_-]?secret|secret|password|token)\"\\s*:\\s*\")" +
        "(?:[^\"\\\\]|\\\\.)*(?<suffix>\")",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex QuerySecretParameter = new(
        @"(?<prefix>[?&](?:api[_-]?key|apikey|key|token|access[_-]?token|secret)=)[^&\s""']+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex WellKnownToken = new(
        @"\b(?:sk-(?:ant-|proj-)?[A-Za-z0-9_-]{8,}|gh[pousr]_[A-Za-z0-9]{20,}|AKIA[0-9A-Z]{16})\b",
        RegexOptions.Compiled);

    /// <summary>
    /// Replaces credentials found by pattern and exact values supplied by the caller.
    /// </summary>
    public static string Redact(string? text, IEnumerable<string?>? knownSecrets = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        var result = text;
        if (knownSecrets is not null)
        {
            foreach (var secret in knownSecrets)
            {
                if (string.IsNullOrWhiteSpace(secret) || secret.Length < 8)
                {
                    continue;
                }

                result = result.Replace(secret.Trim(), Placeholder, StringComparison.Ordinal);
            }
        }

        result = AuthorizationHeader.Replace(
            result, match => match.Groups["prefix"].Value + Placeholder);
        result = BearerToken.Replace(
            result, match => match.Groups["prefix"].Value + Placeholder);
        result = QuerySecretParameter.Replace(
            result, match => match.Groups["prefix"].Value + Placeholder);
        result = JsonSecretField.Replace(
            result,
            match => match.Groups["name"].Value + Placeholder + match.Groups["suffix"].Value);
        result = WellKnownToken.Replace(result, Placeholder);
        return result;
    }
}
