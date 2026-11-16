namespace LMTrainingDataStudio2.Models;

/// <summary>
/// A single JSONL row identified by its zero-based line number.
/// </summary>
public sealed record DatasetRow(long LineNumber, string Json);