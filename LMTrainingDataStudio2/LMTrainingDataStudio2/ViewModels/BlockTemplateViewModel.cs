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
}
