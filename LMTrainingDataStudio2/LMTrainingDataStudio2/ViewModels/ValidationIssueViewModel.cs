using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LMTrainingDataStudio2.Localization;
using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.ViewModels;

/// <summary>
/// One recipe validation issue shown in the property panel.
/// </summary>
public sealed class ValidationIssueViewModel : INotifyPropertyChanged
{
    public ValidationIssueViewModel(ValidationIssue issue, Action<ValidationIssueViewModel> locate)
    {
        Severity = issue.Severity;
        Code = issue.Code;
        Message = issue.Message;
        BlockId = issue.BlockId;
        LocateCommand = new RelayCommand(() => locate(this), () => BlockId is not null);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ValidationSeverity Severity { get; }
    public string? Code { get; }
    public string Message { get; }
    public string? BlockId { get; }

    public bool HasCode => !string.IsNullOrWhiteSpace(Code);

    public string SeverityText => Severity switch
    {
        ValidationSeverity.Error => LocalizationService.L("common.severity_error"),
        ValidationSeverity.Warning => LocalizationService.L("common.severity_warning"),
        _ => LocalizationService.L("common.severity_info")
    };

    public bool IsError => Severity == ValidationSeverity.Error;
    public bool IsWarning => Severity == ValidationSeverity.Warning;
    public bool IsInfo => Severity == ValidationSeverity.Info;

    public void RefreshLocalization() =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SeverityText)));

    public IRelayCommand LocateCommand { get; }
}
