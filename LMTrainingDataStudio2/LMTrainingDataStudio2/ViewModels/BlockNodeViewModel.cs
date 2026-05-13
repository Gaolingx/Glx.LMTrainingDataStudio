using CommunityToolkit.Mvvm.ComponentModel;
using LMTrainingDataStudio2.Models;
using System.Collections.ObjectModel;

namespace LMTrainingDataStudio2.ViewModels;

/// <summary>
/// ViewModel for a single block node on the canvas.
/// </summary>
public partial class BlockNodeViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private BlockType _type;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private double _x;

    [ObservableProperty]
    private double _y;

    [ObservableProperty]
    private double _width = 200;

    [ObservableProperty]
    private double _height = 80;

    [ObservableProperty]
    private bool _isSelected;

    public ObservableCollection<PortViewModel> InputPorts { get; } = new();
    public ObservableCollection<PortViewModel> OutputPorts { get; } = new();

    /// <summary>
    /// Gets the header color brush key based on block type.
    /// </summary>
    public string HeaderColorKey => Type switch
    {
        BlockType.Seed => "SeedBlockBrush",
        BlockType.Llm => "LlmBlockBrush",
        BlockType.Expression => "ExpressionBlockBrush",
        BlockType.Validator => "ValidatorBlockBrush",
        BlockType.Sampler => "SamplerBlockBrush",
        BlockType.ToolProfile => "ToolProfileBlockBrush",
        _ => "SeedBlockBrush"
    };

    /// <summary>
    /// Gets the icon text for the block type.
    /// </summary>
    public string IconText => Type switch
    {
        BlockType.Seed => "📂",
        BlockType.Llm => "🤖",
        BlockType.Expression => "⚡",
        BlockType.Validator => "✓",
        BlockType.Sampler => "🎲",
        BlockType.ToolProfile => "🔧",
        _ => "?"
    };
}

/// <summary>
/// ViewModel for a connection port on a block.
/// </summary>
public partial class PortViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _dataType = "any";

    [ObservableProperty]
    private bool _isHighlighted;
}
