using CommunityToolkit.Mvvm.ComponentModel;

namespace LMTrainingDataStudio2.ViewModels;

/// <summary>
/// ViewModel for an edge (connection) between two ports.
/// </summary>
public partial class EdgeViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string _sourceBlockId = string.Empty;

    [ObservableProperty]
    private string _sourcePortId = string.Empty;

    [ObservableProperty]
    private string _targetBlockId = string.Empty;

    [ObservableProperty]
    private string _targetPortId = string.Empty;

    [ObservableProperty]
    private string? _label;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isHighlighted;
}
