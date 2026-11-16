using System.Text.Json.Serialization;

namespace LMTrainingDataStudio2.Models;

/// <summary>
/// A single regex replacement applied to message text during cleanup.
/// </summary>
public sealed record RegexReplacementRule(
    string Pattern,
    string Replacement,
    bool IgnoreCase = false);

/// <summary>
/// Declarative cleanup rules for OAI message samples.
/// All options are off by default; enable only what a dataset needs.
/// </summary>
public sealed record DatasetCleanupRules
{
    public RegexReplacementRule[]? RegexReplacements { get; init; }
    public bool NormalizeWhitespace { get; init; }
    public bool NormalizeNewlines { get; init; }
    public bool StripAnsi { get; init; }
    public bool RemoveReasoningContent { get; init; }
    public bool DropEmptyMessages { get; init; }

    [JsonIgnore]
    public bool HasAnyRule =>
        RegexReplacements is { Length: > 0 } ||
        NormalizeWhitespace ||
        NormalizeNewlines ||
        StripAnsi ||
        RemoveReasoningContent ||
        DropEmptyMessages;
}
