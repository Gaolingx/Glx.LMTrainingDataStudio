using CommunityToolkit.Mvvm.Input;
using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.ViewModels;

/// <summary>
/// One recipe validation issue shown in the property panel.
/// </summary>
public sealed class ValidationIssueViewModel
{
    public ValidationIssueViewModel(ValidationIssue issue, Action<ValidationIssueViewModel> locate)
    {
        Severity = issue.Severity;
        Message = issue.Message;
        BlockId = issue.BlockId;
        LocateCommand = new RelayCommand(() => locate(this), () => BlockId is not null);
    }

    public ValidationSeverity Severity { get; }
    public string Message { get; }
    public string? BlockId { get; }

    public string SeverityText => Severity.ToString();
    public bool IsError => Severity == ValidationSeverity.Error;
    public bool IsWarning => Severity == ValidationSeverity.Warning;
    public bool IsInfo => Severity == ValidationSeverity.Info;

    public IRelayCommand LocateCommand { get; }
}