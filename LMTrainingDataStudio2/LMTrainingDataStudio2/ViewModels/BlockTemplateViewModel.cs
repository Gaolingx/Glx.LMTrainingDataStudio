using CommunityToolkit.Mvvm.ComponentModel;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services;

namespace LMTrainingDataStudio2.ViewModels;

/// <summary>
/// ViewModel for a block template in the Block Sheet panel.
/// Text is pulled from <see cref="BlockCatalog"/> and refreshed on language changes.
/// </summary>
public partial class BlockTemplateViewModel : ObservableObject
{
    public BlockTemplateViewModel(BlockType type) => Type = type;

    public BlockType Type { get; }

    public string Name => BlockCatalog.GetName(Type);

    public string Description => BlockCatalog.GetSummary(Type);

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Description));
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
        BlockType.QuerySource => "⌕",
        BlockType.Simulation => "◎",
        BlockType.Cleaner => "✧",
        BlockType.SftExport => "⇩",
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
        BlockType.QuerySource => "#E67E22",
        BlockType.Simulation => "#00A3A3",
        BlockType.Cleaner => "#8E44AD",
        BlockType.SftExport => "#2E8B57",
        _ => "#FFFFFF"
    };
}
