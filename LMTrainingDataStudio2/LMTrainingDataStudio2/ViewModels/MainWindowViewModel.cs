using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LMTrainingDataStudio2.Commands;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services;
using LMTrainingDataStudio2.Services.Executors;
using System.Collections.ObjectModel;

namespace LMTrainingDataStudio2.ViewModels;

/// <summary>
/// Main window view model managing the three-panel studio layout.
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    private readonly RecipeValidationService _recipeValidation = new();
    private readonly DatasetValidationService _datasetValidation = new();
    private readonly DatasetFormatConverter _formatConverter = new();
    private readonly ISettingsStore _settingsStore;
    private readonly ExecutionStore _executionStore = new();
    private readonly HttpClient _llmHttpClient = new();
    private AppSettings _settings;
    private CancellationTokenSource? _runCancellation;

    [ObservableProperty]
    private string _recipeName = "Untitled Recipe";

    [ObservableProperty]
    private bool _isEditorView = true;

    [ObservableProperty]
    private bool _isBlockSheetVisible = true;

    [ObservableProperty]
    private bool _isPropertyPanelVisible = true;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private long _totalLines;

    [ObservableProperty]
    private string _currentFilePath = string.Empty;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelRunCommand))]
    private bool _isRunning;

    [ObservableProperty]
    private string _memoryUsage = "0 MB";

    [ObservableProperty]
    private string _canvasZoomText = "Zoom: 100%";

    [ObservableProperty]
    private BlockNodeViewModel? _selectedBlock;

    [ObservableProperty]
    private bool _isSettingsVisible;

    [ObservableProperty]
    private long _maxCacheSizeMb;

    [ObservableProperty]
    private string _cacheDirectory = string.Empty;

    [ObservableProperty]
    private bool _enableGridSnap;

    [ObservableProperty]
    private int _gridSnapSize;

    [ObservableProperty]
    private bool _isDarkTheme;

    [ObservableProperty]
    private string _cacheSizeText = "0 MB";

    public ObservableCollection<BlockNodeViewModel> Blocks { get; } = new();
    public ObservableCollection<EdgeViewModel> Edges { get; } = new();
    public ObservableCollection<BlockTemplateViewModel> BlockTemplates { get; } = new();
    public ObservableCollection<string> LogMessages { get; } = new();
    public CommandHistory CommandHistory { get; } = new();
    public ExecutionsViewModel Executions { get; }

    public MainWindowViewModel() : this(new SettingsStore())
    {
    }

    public MainWindowViewModel(ISettingsStore settingsStore)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        var loadResult = _settingsStore.Load();
        _settings = loadResult.Settings;
        Executions = new ExecutionsViewModel(_executionStore);

        LoadSettingsSnapshot();
        InitializeBlockTemplates();
        LoadSampleRecipe();
        UpdateMemoryUsage();

        foreach (var warning in loadResult.Warnings)
        {
            LogMessages.Add($"[Warning] {warning}");
        }

        CommandHistory.HistoryChanged += (_, _) =>
        {
            StatusText = CommandHistory.CanUndo || CommandHistory.CanRedo ? "Recipe history updated" : "Ready";
        };
    }

    private void LoadSettingsSnapshot()
    {
        MaxCacheSizeMb = _settings.MaxCacheSizeMb;
        CacheDirectory = _settings.CacheDirectory;
        EnableGridSnap = _settings.EnableGridSnap;
        GridSnapSize = _settings.GridSnapSize;
        IsDarkTheme = _settings.IsDarkTheme;
        UpdateCacheSizeText();
    }

    private void InitializeBlockTemplates()
    {
        BlockTemplates.Add(new BlockTemplateViewModel("Seed", BlockType.Seed, "Data input source (JSONL / CSV / Parquet / HuggingFace)"));
        BlockTemplates.Add(new BlockTemplateViewModel("LLM / Model", BlockType.Llm, "Model call with structured output"));
        BlockTemplates.Add(new BlockTemplateViewModel("Expression", BlockType.Expression, "Jinja2 field transformation"));
        BlockTemplates.Add(new BlockTemplateViewModel("Validator", BlockType.Validator, "Code validation and filtering"));
        BlockTemplates.Add(new BlockTemplateViewModel("Sampler", BlockType.Sampler, "Deterministic sampling and splitting"));
        BlockTemplates.Add(new BlockTemplateViewModel("Tool Profile", BlockType.ToolProfile, "MCP tool configuration"));
    }

    private void LoadSampleRecipe()
    {
        var seedBlock = new BlockNodeViewModel
        {
            Id = "seed1",
            Type = BlockType.Seed,
            Name = "seed_data",
            DisplayName = "Training Data",
            X = 80,
            Y = 150,
            Config = new SeedBlockConfig("data/train.jsonl", DataFileType.Jsonl, Streaming: false, UseIndexCache: true)
        };
        seedBlock.OutputPorts.Add(new PortViewModel { Id = "seed1_out", Name = "rows", DataType = "jsonl" });

        var llmBlock = new BlockNodeViewModel
        {
            Id = "llm1",
            Type = BlockType.Llm,
            Name = "generator",
            DisplayName = "LLM Generator",
            X = 380,
            Y = 120,
            Config = new LlmBlockConfig(LlmProviderIds.OpenAiChat, "gpt-4o", Temperature: 0.7, MaxTokens: 2048, Prompt: null)
        };
        llmBlock.InputPorts.Add(new PortViewModel { Id = "llm1_in", Name = "input", DataType = "jsonl" });
        llmBlock.OutputPorts.Add(new PortViewModel { Id = "llm1_out", Name = "output", DataType = "messages[]" });

        var validatorBlock = new BlockNodeViewModel
        {
            Id = "val1",
            Type = BlockType.Validator,
            Name = "code_check",
            DisplayName = "Python Validator",
            X = 680,
            Y = 150,
            Config = new ValidatorBlockConfig("expression", "len(messages) > 0", RejectOnFailure: true)
        };
        validatorBlock.InputPorts.Add(new PortViewModel { Id = "val1_in", Name = "input", DataType = "messages[]" });
        validatorBlock.OutputPorts.Add(new PortViewModel { Id = "val1_out", Name = "valid", DataType = "messages[]" });

        Blocks.Add(seedBlock);
        Blocks.Add(llmBlock);
        Blocks.Add(validatorBlock);

        Edges.Add(new EdgeViewModel
        {
            Id = "edge1",
            SourceBlockId = "seed1",
            SourcePortId = "seed1_out",
            TargetBlockId = "llm1",
            TargetPortId = "llm1_in",
            Label = "jsonl"
        });

        Edges.Add(new EdgeViewModel
        {
            Id = "edge2",
            SourceBlockId = "llm1",
            SourcePortId = "llm1_out",
            TargetBlockId = "val1",
            TargetPortId = "val1_in",
            Label = "messages[]"
        });
    }

    [RelayCommand]
    private async Task ValidateRecipeAsync()
    {
        IsRunning = true;
        StatusText = "Validating recipe...";
        LogMessages.Clear();

        await Task.Run(() =>
        {
            var recipe = BuildRecipeFromViewModel();
            var result = _recipeValidation.Validate(recipe);

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                foreach (var issue in result.Issues)
                {
                    LogMessages.Add($"[{issue.Severity}] {issue.Message}");
                }

                foreach (var block in Blocks)
                {
                    block.ConfigError = result.Issues
                        .FirstOrDefault(i => i.BlockId == block.Id && i.Severity == ValidationSeverity.Error)
                        ?.Message;
                }

                StatusText = result.IsValid ? "Validation passed ✓" : $"Validation failed: {result.Issues.Count(i => i.Severity == ValidationSeverity.Error)} error(s)";
                IsRunning = false;
            });
        });
    }

    [RelayCommand]
    private async Task RunRecipeAsync()
    {
        if (IsRunning) return;

        IsRunning = true;
        Progress = 0;
        StatusText = "Running recipe...";
        LogMessages.Clear();
        LogMessages.Add("[Info] Recipe execution started.");

        _runCancellation = new CancellationTokenSource();
        var cancellationToken = _runCancellation.Token;

        try
        {
            var recipe = BuildRecipeFromViewModel();
            var runner = CreateRecipeRunner();
            var progress = new Progress<ExecutionProgress>(OnExecutionProgress);

            var run = await Task.Run(() => runner.RunAsync(recipe, progress, cancellationToken));

            try
            {
                await _executionStore.SaveAsync(run, CancellationToken.None);
                await Executions.RefreshAndSelectAsync(run.RunId);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LogMessages.Add($"[Error] Failed to save execution record: {ex.Message}");
            }

            Progress = 1.0;
            StatusText = run.Status switch
            {
                RunStatus.Succeeded => "Execution complete ✓",
                RunStatus.Cancelled => "Execution cancelled",
                RunStatus.Failed => "Execution failed — see Executions for details",
                _ => $"Execution {run.Status}"
            };
        }
        catch (Exception ex)
        {
            StatusText = $"Execution error: {ex.Message}";
            LogMessages.Add($"[Error] {ex.Message}");
        }
        finally
        {
            _runCancellation.Dispose();
            _runCancellation = null;
            IsRunning = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelRun))]
    private void CancelRun() => _runCancellation?.Cancel();

    private bool CanCancelRun() => IsRunning;

    [RelayCommand]
    private void AddBlock(BlockType type)
    {
        var block = new BlockNodeViewModel
        {
            Type = type,
            Name = $"block_{Blocks.Count + 1}",
            DisplayName = $"New {type}",
            X = 300,
            Y = 200,
            Config = BlockConfig.CreateDefault(type)
        };
        block.OutputPorts.Add(new PortViewModel { Name = "output", DataType = "any" });
        block.InputPorts.Add(new PortViewModel { Name = "input", DataType = "any" });
        Blocks.Add(block);
        SelectedBlock = block;
    }

    [RelayCommand]
    private void DeleteSelectedBlock()
    {
        if (SelectedBlock == null) return;

        // Remove connected edges
        var edgesToRemove = Edges
            .Where(e => e.SourceBlockId == SelectedBlock.Id || e.TargetBlockId == SelectedBlock.Id)
            .ToList();
        foreach (var edge in edgesToRemove)
            Edges.Remove(edge);

        Blocks.Remove(SelectedBlock);
        SelectedBlock = null;
    }

    [RelayCommand]
    private void ToggleBlockSheet()
    {
        IsBlockSheetVisible = !IsBlockSheetVisible;
    }

    [RelayCommand]
    private void TogglePropertyPanel()
    {
        IsPropertyPanelVisible = !IsPropertyPanelVisible;
    }

    [RelayCommand]
    private void SwitchToEditor()
    {
        IsEditorView = true;
    }

    [RelayCommand]
    private async Task SwitchToExecutionsAsync()
    {
        IsEditorView = false;
        await Executions.RefreshAsync();
    }

    [RelayCommand]
    private void OpenSettings()
    {
        LoadSettingsSnapshot();
        IsSettingsVisible = true;
    }

    [RelayCommand]
    private void CloseSettings()
    {
        IsSettingsVisible = false;
    }

    [RelayCommand]
    private async Task ApplySettingsAsync()
    {
        var defaults = new AppSettings();
        var settings = new AppSettings
        {
            MaxCacheSizeMb = Math.Max(SettingsStore.MinCacheSizeMb, MaxCacheSizeMb),
            CacheDirectory = string.IsNullOrWhiteSpace(CacheDirectory)
                ? defaults.CacheDirectory
                : CacheDirectory.Trim(),
            EnableGridSnap = EnableGridSnap,
            GridSnapSize = Math.Clamp(GridSnapSize, SettingsStore.MinGridSnapSize, SettingsStore.MaxGridSnapSize),
            IsDarkTheme = IsDarkTheme
        };

        try
        {
            await _settingsStore.SaveAsync(settings);
            _settings = settings;
            LoadSettingsSnapshot();
            StatusText = "Settings saved ✓";
            LogMessages.Add($"[Info] Settings saved to '{_settingsStore.FilePath}'.");
            IsSettingsVisible = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            StatusText = "Failed to save settings";
            LogMessages.Add($"[Error] Failed to save settings to '{_settingsStore.FilePath}': {ex.Message}");
        }
    }

    [RelayCommand]
    private void CleanIndexCache()
    {
        var service = new IndexCacheService(_settings);
        service.CleanCache();
        UpdateCacheSizeText();
        StatusText = "Index cache cleaned ✓";
        LogMessages.Add("[Info] Index cache cleaned.");
    }

    private Recipe BuildRecipeFromViewModel()
    {
        var recipe = new Recipe { Name = RecipeName };

        foreach (var blockVm in Blocks)
        {
            recipe.Blocks.Add(new RecipeBlock
            {
                Id = blockVm.Id,
                Type = blockVm.Type,
                Name = blockVm.Name,
                DisplayName = blockVm.DisplayName,
                X = blockVm.X,
                Y = blockVm.Y,
                Config = blockVm.Config,
                InputPorts = blockVm.InputPorts.Select(p => new BlockPort
                {
                    Id = p.Id,
                    Name = p.Name,
                    DataType = p.DataType,
                    Direction = PortDirection.Input
                }).ToList(),
                OutputPorts = blockVm.OutputPorts.Select(p => new BlockPort
                {
                    Id = p.Id,
                    Name = p.Name,
                    DataType = p.DataType,
                    Direction = PortDirection.Output
                }).ToList()
            });
        }

        foreach (var edgeVm in Edges)
        {
            recipe.Edges.Add(new RecipeEdge
            {
                Id = edgeVm.Id,
                SourceBlockId = edgeVm.SourceBlockId,
                SourcePortId = edgeVm.SourcePortId,
                TargetBlockId = edgeVm.TargetBlockId,
                TargetPortId = edgeVm.TargetPortId,
                Label = edgeVm.Label
            });
        }

        return recipe;
    }

    private RecipeRunner CreateRecipeRunner() => new(
        _recipeValidation,
        new IBlockExecutor[]
        {
            new SeedBlockExecutor(_settings),
            new ExpressionBlockExecutor(),
            new ValidatorBlockExecutor(),
            new SamplerBlockExecutor(),
            new LlmBlockExecutor(new LlmApiClient(_llmHttpClient, new EnvironmentLlmCredentials()))
        });

    private void OnExecutionProgress(ExecutionProgress progress)
    {
        Progress = progress.TotalBlocks == 0 ? 0 : (double)progress.CompletedBlocks / progress.TotalBlocks;
        TotalLines = progress.ProcessedRows;
        StatusText = progress.CurrentBlockName is null
            ? "Running recipe..."
            : $"Running {progress.CurrentBlockName} ({progress.CompletedBlocks}/{progress.TotalBlocks})";
    }

    private void UpdateMemoryUsage()
    {
        var process = System.Diagnostics.Process.GetCurrentProcess();
        var mb = process.WorkingSet64 / (1024.0 * 1024.0);
        MemoryUsage = $"{mb:F0} MB";
    }

    private void UpdateCacheSizeText()
    {
        try
        {
            using var service = new IndexCacheService(_settings);
            var mb = service.GetTotalCacheSize() / (1024.0 * 1024.0);
            CacheSizeText = $"{mb:F1} MB";
        }
        catch
        {
            CacheSizeText = "Unavailable";
        }
    }
}
