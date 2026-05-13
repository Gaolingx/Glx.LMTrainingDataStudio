namespace LMTrainingDataStudio2.Models;

/// <summary>
/// Represents a validation issue found during recipe or dataset validation.
/// </summary>
public sealed class ValidationIssue
{
    public ValidationSeverity Severity { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? BlockId { get; set; }
    public long? LineNumber { get; set; }
    public string? FilePath { get; set; }
}

public enum ValidationSeverity
{
    Info,
    Warning,
    Error
}

/// <summary>
/// Result of a validation operation.
/// </summary>
public sealed class ValidationResult
{
    public bool IsValid => Issues.All(i => i.Severity != ValidationSeverity.Error);
    public List<ValidationIssue> Issues { get; set; } = new();
}
