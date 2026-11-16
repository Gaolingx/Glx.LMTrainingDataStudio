using CommunityToolkit.Mvvm.ComponentModel;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services;
using System.Collections.ObjectModel;
using System.Text.Json;

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

    [ObservableProperty]
    private string? _configError;

    [ObservableProperty]
    private SeedConfigEditorViewModel? _seedEditor;

    [ObservableProperty]
    private LlmConfigEditorViewModel? _llmEditor;

    [ObservableProperty]
    private QuerySourceConfigEditorViewModel? _querySourceEditor;

    [ObservableProperty]
    private SimulationConfigEditorViewModel? _simulationEditor;

    [ObservableProperty]
    private CleanerConfigEditorViewModel? _cleanerEditor;

    [ObservableProperty]
    private SftExportConfigEditorViewModel? _sftExportEditor;

    [ObservableProperty]
    private string _configJson = string.Empty;

    [ObservableProperty]
    private string? _configJsonError;

    private BlockConfig _config = null!;

    /// <summary>
    /// Configuration whose block kind must match <see cref="Type"/>.
    /// </summary>
    public required BlockConfig Config
    {
        get => _config;
        set
        {
            if (!SetProperty(ref _config, value)) return;

            ConfigJson = JsonSerializer.Serialize(value, RecipeStore.SerializerOptions);
            ConfigJsonError = null;

            SeedEditor = value is SeedBlockConfig seed
                ? new SeedConfigEditorViewModel(seed, ApplySeedConfig)
                : null;

            LlmEditor = value is LlmBlockConfig llm
                ? new LlmConfigEditorViewModel(llm, ApplyLlmConfig)
                : null;

            QuerySourceEditor = value is QuerySourceBlockConfig query
                ? new QuerySourceConfigEditorViewModel(query, ApplyConfig)
                : null;

            SimulationEditor = value is SimulationBlockConfig simulation
                ? new SimulationConfigEditorViewModel(simulation, ApplyConfig)
                : null;

            CleanerEditor = value is CleanerBlockConfig cleaner
                ? new CleanerConfigEditorViewModel(cleaner, ApplyConfig)
                : null;

            SftExportEditor = value is SftExportBlockConfig export
                ? new SftExportConfigEditorViewModel(export, ApplyConfig)
                : null;
        }
    }

    /// <summary>
    /// Replaces the configuration from JSON. The config kind must match the block type.
    /// </summary>
    public bool TryApplyConfigJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            ConfigJsonError = "Config JSON is empty.";
            return false;
        }

        BlockConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<BlockConfig>(json, RecipeStore.SerializerOptions);
        }
        catch (JsonException ex)
        {
            ConfigJsonError = $"Invalid config JSON: {ex.Message}";
            return false;
        }
        catch (NotSupportedException ex)
        {
            ConfigJsonError = ex.Message;
            return false;
        }

        if (config is null)
        {
            ConfigJsonError = "Config JSON did not produce a block configuration.";
            return false;
        }

        if (config.GetBlockType() != Type)
        {
            ConfigJsonError = $"Config kind '{config.GetBlockType()}' does not match block type '{Type}'.";
            return false;
        }

        Config = config;
        return true;
    }

    private void ApplySeedConfig(SeedBlockConfig config) => ApplyConfig(config);

    private void ApplyLlmConfig(LlmBlockConfig config) => ApplyConfig(config);

    private void ApplyConfig(BlockConfig config)
    {
        if (_config.Equals(config)) return;

        _config = config;
        ConfigJson = JsonSerializer.Serialize(config, RecipeStore.SerializerOptions);
        OnPropertyChanged(nameof(Config));
        ConfigError = null;
    }

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
        BlockType.QuerySource => "QuerySourceBlockBrush",
        BlockType.Simulation => "SimulationBlockBrush",
        BlockType.Cleaner => "CleanerBlockBrush",
        BlockType.SftExport => "SftExportBlockBrush",
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
        BlockType.QuerySource => "⌕",
        BlockType.Simulation => "◎",
        BlockType.Cleaner => "✧",
        BlockType.SftExport => "⇩",
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
