using CommunityToolkit.Mvvm.ComponentModel;
using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.ViewModels;

/// <summary>
/// ViewModel for a block template in the Block Sheet panel.
/// </summary>
public partial class BlockTemplateViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private BlockType _type;

    [ObservableProperty]
    private string _description;

    public BlockTemplateViewModel(string name, BlockType type, string description)
    {
        _name = name;
        _type = type;
        _description = description;
    }

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

    /// <summary>
    /// Gets the color key for the block type.
    /// </summary>
    public string ColorKey => Type switch
    {
        BlockType.Seed => "#4A90D9",
        BlockType.Llm => "#7B61FF",
        BlockType.Expression => "#F5A623",
        BlockType.Validator => "#D0021B",
        BlockType.Sampler => "#417505",
        BlockType.ToolProfile => "#9B9B9B",
        _ => "#FFFFFF"
    };
}
