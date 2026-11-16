using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LMTrainingDataStudio2.Commands;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services;
using LMTrainingDataStudio2.Services.Executors;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

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
    private readonly RecipeStore _recipeStore;
    private readonly ExecutionStore _executionStore = new();
    private readonly HttpClient _llmHttpClient = new();
    private AppSettings _settings;
    private CancellationTokenSource? _runCancellation;
    private string _recipeId = Guid.NewGuid().ToString("N");
    private DateTime _recipeCreatedAt = DateTime.UtcNow;
    private DateTime _recipeModifiedAt = DateTime.UtcNow;
    private bool _suppressDocumentEvents;

    [ObservableProperty]
    private string _recipeName = "Untitled Recipe";

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private bool _hasValidationIssues;

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

    [ObservableProperty]
    private string _openAiApiKey = string.Empty;

    [ObservableProperty]
    private string _anthropicApiKey = string.Empty;

    public ObservableCollection<BlockNodeViewModel> Blocks { get; } = new();
    public ObservableCollection<EdgeViewModel> Edges { get; } = new();
    public ObservableCollection<BlockTemplateViewModel> BlockTemplates { get; } = new();
    public ObservableCollection<string> LogMessages { get; } = new();
    public ObservableCollection<string> RecentFiles { get; } = new();
    public ObservableCollection<ValidationIssueViewModel> ValidationIssues { get; } = new();
    public CommandHistory CommandHistory { get; } = new();
    public ExecutionsViewModel Executions { get; }

    /// <summary>Raised when the canvas must re-render without a collection change.</summary>
    public event Action? CanvasRenderRequested;

    public string DocumentTitle => $"{RecipeName}{(IsDirty ? " *" : string.Empty)}";

    public MainWindowViewModel() : this(new SettingsStore())
    {
    }

    public MainWindowViewModel(ISettingsStore settingsStore, RecipeStore? recipeStore = null)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _recipeStore = recipeStore ?? new RecipeStore();
        var loadResult = _settingsStore.Load();
        _settings = loadResult.Settings;
        Executions = new ExecutionsViewModel(_executionStore);

        LoadSettingsSnapshot();
        InitializeBlockTemplates();

        Blocks.CollectionChanged += OnBlocksCollectionChanged;
        Edges.CollectionChanged += OnEdgesCollectionChanged;
        CommandHistory.HistoryChanged += OnHistoryChanged;

        LoadDocument(CreateBlankRecipe(), path: null, markDirty: false);
        UpdateMemoryUsage();

        foreach (var warning in loadResult.Warnings)
        {
            LogMessages.Add($"[Warning] {warning}");
        }
    }

    private void LoadSettingsSnapshot()
    {
        MaxCacheSizeMb = _settings.MaxCacheSizeMb;
        CacheDirectory = _settings.CacheDirectory;
        EnableGridSnap = _settings.EnableGridSnap;
        GridSnapSize = _settings.GridSnapSize;
        IsDarkTheme = _settings.IsDarkTheme;
        OpenAiApiKey = _settings.OpenAiApiKey ?? string.Empty;
        AnthropicApiKey = _settings.AnthropicApiKey ?? string.Empty;

        RecentFiles.Clear();
        foreach (var path in _settings.RecentFiles)
        {
            if (File.Exists(path) &&
                !RecentFiles.Any(existing => string.Equals(existing, path, StringComparison.OrdinalIgnoreCase)))
            {
                RecentFiles.Add(path);
            }
        }

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
        BlockTemplates.Add(new BlockTemplateViewModel("Query Source", BlockType.QuerySource, "Simulated user queries (single or multiple groups)"));
        BlockTemplates.Add(new BlockTemplateViewModel("Simulation", BlockType.Simulation, "Mini harness: simulated model, tools, retries, timeouts"));
        BlockTemplates.Add(new BlockTemplateViewModel("Cleaner", BlockType.Cleaner, "Regex and formatting cleanup for OAI messages"));
        BlockTemplates.Add(new BlockTemplateViewModel("SFT Export", BlockType.SftExport, "Write OAI messages JSONL for SFT"));
    }

    #region Document lifecycle

    /// <summary>
    /// Starts a new document. With <paramref name="withSample"/>, the bundled sample pipeline is loaded and marked dirty.
    /// </summary>
    public async Task NewRecipeAsync(bool withSample = false)
    {
        LoadDocument(withSample ? CreateSampleRecipe() : CreateBlankRecipe(), path: null, markDirty: withSample);
        LogMessages.Clear();
        LogMessages.Add(withSample
            ? "[Info] Loaded the sample recipe. Use File > Save As to keep it."
            : "[Info] Started a new recipe.");
        await PersistDocumentStateAsync();
    }

    /// <summary>
    /// Replaces the current document with the recipe stored at <paramref name="path"/>.
    /// The current document is left untouched when loading fails.
    /// </summary>
    public async Task OpenRecipeAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        var recipe = await _recipeStore.LoadAsync(fullPath);

        LoadDocument(recipe, fullPath, markDirty: false);
        AddRecentFile(fullPath);
        LogMessages.Clear();
        LogMessages.Add($"[Info] Opened recipe '{fullPath}'.");
        await PersistDocumentStateAsync();
    }

    /// <summary>
    /// Saves the current document to <paramref name="path"/> and clears the dirty marker.
    /// </summary>
    public async Task SaveRecipeAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        _recipeModifiedAt = DateTime.UtcNow;

        await _recipeStore.SaveAsync(ToRecipe(), fullPath);

        CurrentFilePath = fullPath;
        IsDirty = false;
        AddRecentFile(fullPath);
        StatusText = $"Saved {fullPath}";
        LogMessages.Add($"[Info] Saved recipe to '{fullPath}'.");
        await PersistDocumentStateAsync();
    }

    /// <summary>
    /// Restores the most recently opened recipe. Returns false (leaving the blank document) when there is nothing to restore.
    /// </summary>
    public async Task<bool> RestoreLastRecipeAsync()
    {
        var path = _settings.LastRecipePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (!File.Exists(path))
        {
            LogMessages.Add($"[Warning] Last recipe '{path}' no longer exists; starting with a blank recipe.");
            return false;
        }

        try
        {
            await OpenRecipeAsync(path);
            StatusText = $"Restored {path}";
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogMessages.Add($"[Warning] Could not restore '{path}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Builds the current recipe model from the editor state.
    /// </summary>
    public Recipe ToRecipe()
    {
        var recipe = new Recipe
        {
            Id = _recipeId,
            Name = RecipeName,
            CreatedAt = _recipeCreatedAt,
            ModifiedAt = _recipeModifiedAt
        };

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
                Width = blockVm.Width,
                Height = blockVm.Height,
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

    private void LoadDocument(Recipe recipe, string? path, bool markDirty)
    {
        _suppressDocumentEvents = true;
        try
        {
            foreach (var block in Blocks)
            {
                block.PropertyChanged -= OnBlockPropertyChanged;
            }

            foreach (var edge in Edges)
            {
                edge.PropertyChanged -= OnEdgePropertyChanged;
            }

            Blocks.Clear();
            Edges.Clear();
            CommandHistory.Clear();
            ValidationIssues.Clear();
            HasValidationIssues = false;

            foreach (var block in recipe.Blocks)
            {
                var blockVm = new BlockNodeViewModel
                {
                    Id = block.Id,
                    Type = block.Type,
                    Name = block.Name,
                    DisplayName = block.DisplayName,
                    X = block.X,
                    Y = block.Y,
                    Width = block.Width,
                    Height = block.Height,
                    Config = block.Config.DeepClone()
                };

                foreach (var port in block.InputPorts)
                {
                    blockVm.InputPorts.Add(new PortViewModel
                    {
                        Id = port.Id,
                        Name = port.Name,
                        DataType = port.DataType
                    });
                }

                foreach (var port in block.OutputPorts)
                {
                    blockVm.OutputPorts.Add(new PortViewModel
                    {
                        Id = port.Id,
                        Name = port.Name,
                        DataType = port.DataType
                    });
                }

                Blocks.Add(blockVm);
            }

            foreach (var edge in recipe.Edges)
            {
                Edges.Add(new EdgeViewModel
                {
                    Id = edge.Id,
                    SourceBlockId = edge.SourceBlockId,
                    SourcePortId = edge.SourcePortId,
                    TargetBlockId = edge.TargetBlockId,
                    TargetPortId = edge.TargetPortId,
                    Label = edge.Label
                });
            }

            _recipeId = recipe.Id;
            _recipeCreatedAt = recipe.CreatedAt;
            _recipeModifiedAt = recipe.ModifiedAt;
            RecipeName = recipe.Name;
            SelectedBlock = null;
            CurrentFilePath = path ?? string.Empty;
            IsDirty = markDirty;
            StatusText = path is null
                ? (markDirty ? "New recipe loaded from sample" : "New recipe")
                : $"Opened {path}";
        }
        finally
        {
            _suppressDocumentEvents = false;
        }

        CanvasRenderRequested?.Invoke();
    }

    private static Recipe CreateBlankRecipe() => new() { Name = "Untitled Recipe" };

    private static Recipe CreateSampleRecipe()
    {
        var recipe = new Recipe { Name = "Sample SFT Trajectory" };

        var queries = CreateRecipeBlock(
            "queries", BlockType.QuerySource, "user_queries", "User Queries", 80, 160,
            new QuerySourceBlockConfig(
                new[] { "Explain the TCP three-way handshake with a small example." },
                QuerySetMode.MultipleQueries,
                Repeat: 1,
                SystemPrompt: "You are a concise network engineering tutor."));

        var simulation = CreateRecipeBlock(
            "simulation", BlockType.Simulation, "trajectory", "Tool-calling Simulation", 360, 120,
            new SimulationBlockConfig(
                "demo-model",
                MaxRounds: 6,
                SystemPrompt: "Use the python tool when a calculation helps.",
                Tools: SimulatedToolCatalog.Common(),
                FinalContent: "The TCP handshake establishes a reliable connection with SYN, SYN-ACK and ACK."));

        var cleaner = CreateRecipeBlock(
            "cleaner", BlockType.Cleaner, "clean", "Cleaner", 640, 160,
            new CleanerBlockConfig(new DatasetCleanupRules
            {
                NormalizeNewlines = true,
                NormalizeWhitespace = true
            }));

        var export = CreateRecipeBlock(
            "export", BlockType.SftExport, "sft_export", "SFT Export", 920, 160,
            new SftExportBlockConfig("out/sample-sft.jsonl"));

        recipe.Blocks.AddRange(new[] { queries, simulation, cleaner, export });
        AddSampleEdge(recipe, "edge_queries_simulation", queries, simulation);
        AddSampleEdge(recipe, "edge_simulation_cleaner", simulation, cleaner);
        AddSampleEdge(recipe, "edge_cleaner_export", cleaner, export);
        return recipe;
    }

    private static RecipeBlock CreateRecipeBlock(
        string id,
        BlockType type,
        string name,
        string displayName,
        double x,
        double y,
        BlockConfig config) => new()
    {
        Id = id,
        Type = type,
        Name = name,
        DisplayName = displayName,
        X = x,
        Y = y,
        Config = config,
        InputPorts = BlockPortSchema.CreateInputPorts(type),
        OutputPorts = BlockPortSchema.CreateOutputPorts(type)
    };

    private static void AddSampleEdge(Recipe recipe, string id, RecipeBlock source, RecipeBlock target)
    {
        var sourcePort = source.OutputPorts[0];
        recipe.Edges.Add(new RecipeEdge
        {
            Id = id,
            SourceBlockId = source.Id,
            SourcePortId = sourcePort.Id,
            TargetBlockId = target.Id,
            TargetPortId = target.InputPorts[0].Id,
            Label = sourcePort.DataType
        });
    }

    private void AddRecentFile(string path)
    {
        var existing = RecentFiles.FirstOrDefault(
            candidate => string.Equals(candidate, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            RecentFiles.Remove(existing);
        }

        RecentFiles.Insert(0, path);
        while (RecentFiles.Count > SettingsStore.MaxRecentFiles)
        {
            RecentFiles.RemoveAt(RecentFiles.Count - 1);
        }
    }

    private async Task PersistDocumentStateAsync()
    {
        _settings.RecentFiles = RecentFiles.ToList();
        _settings.LastRecipePath = string.IsNullOrWhiteSpace(CurrentFilePath) ? null : CurrentFilePath;

        try
        {
            await _settingsStore.SaveAsync(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            LogMessages.Add($"[Warning] Could not persist recent files: {ex.Message}");
        }
    }

    private void OnBlocksCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (BlockNodeViewModel block in e.OldItems)
            {
                block.PropertyChanged -= OnBlockPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (BlockNodeViewModel block in e.NewItems)
            {
                block.PropertyChanged += OnBlockPropertyChanged;
            }
        }

        if (_suppressDocumentEvents) return;

        MarkDirty();
        CanvasRenderRequested?.Invoke();
    }

    private void OnEdgesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (EdgeViewModel edge in e.OldItems)
            {
                edge.PropertyChanged -= OnEdgePropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (EdgeViewModel edge in e.NewItems)
            {
                edge.PropertyChanged += OnEdgePropertyChanged;
            }
        }

        if (_suppressDocumentEvents) return;

        MarkDirty();
        CanvasRenderRequested?.Invoke();
    }

    private void OnBlockPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BlockNodeViewModel.IsSelected)
            or nameof(BlockNodeViewModel.ConfigError)
            or nameof(BlockNodeViewModel.ConfigJsonError)
            or nameof(BlockNodeViewModel.ValidationError)
            or nameof(BlockNodeViewModel.ValidationErrorCount)
            or nameof(BlockNodeViewModel.ValidationWarningCount)
            or nameof(BlockNodeViewModel.ValidationIssueCount)
            or nameof(BlockNodeViewModel.HasValidationError))
        {
            return;
        }

        if (_suppressDocumentEvents) return;

        MarkDirty();
        if (e.PropertyName is nameof(BlockNodeViewModel.Name) or nameof(BlockNodeViewModel.DisplayName))
        {
            CanvasRenderRequested?.Invoke();
        }
    }

    private void OnEdgePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EdgeViewModel.IsSelected) or nameof(EdgeViewModel.IsHighlighted))
        {
            return;
        }

        MarkDirty();
    }

    private void OnHistoryChanged(object? sender, EventArgs e)
    {
        if (_suppressDocumentEvents) return;

        StatusText = CommandHistory.CanUndo || CommandHistory.CanRedo ? "Recipe history updated" : "Ready";
        MarkDirty();
        CanvasRenderRequested?.Invoke();
    }

    private void MarkDirty()
    {
        if (_suppressDocumentEvents) return;
        IsDirty = true;
    }

    partial void OnRecipeNameChanged(string value)
    {
        MarkDirty();
        OnPropertyChanged(nameof(DocumentTitle));
    }

    partial void OnIsDirtyChanged(bool value) => OnPropertyChanged(nameof(DocumentTitle));

    #endregion

    #region Validation and execution

    [RelayCommand]
    private void ValidateRecipe()
    {
        if (IsRunning) return;

        IsRunning = true;
        StatusText = "Validating recipe...";
        LogMessages.Clear();

        var result = _recipeValidation.Validate(ToRecipe());
        ApplyValidationResult(result);

        foreach (var issue in result.Issues)
        {
            LogMessages.Add($"[{issue.Severity}] {issue.Message}");
        }

        StatusText = result.IsValid
            ? "Validation passed ✓"
            : $"Validation failed: {result.Issues.Count(i => i.Severity == ValidationSeverity.Error)} error(s)";
        IsRunning = false;
    }

    private void ApplyValidationResult(ValidationResult result)
    {
        ValidationIssues.Clear();
        HasValidationIssues = result.Issues.Count > 0;

        foreach (var block in Blocks)
        {
            block.ValidationError = null;
            block.ValidationErrorCount = 0;
            block.ValidationWarningCount = 0;
        }

        foreach (var issue in result.Issues)
        {
            ValidationIssues.Add(new ValidationIssueViewModel(issue, LocateValidationIssue));

            if (issue.BlockId is null) continue;
            var block = Blocks.FirstOrDefault(candidate => candidate.Id == issue.BlockId);
            if (block is null) continue;

            if (issue.Severity == ValidationSeverity.Error)
            {
                block.ValidationErrorCount++;
                block.ValidationError ??= issue.Message;
            }
            else if (issue.Severity == ValidationSeverity.Warning)
            {
                block.ValidationWarningCount++;
            }
        }

        CanvasRenderRequested?.Invoke();
    }

    private void LocateValidationIssue(ValidationIssueViewModel issue)
    {
        if (issue.BlockId is null)
        {
            StatusText = issue.Message;
            return;
        }

        var block = Blocks.FirstOrDefault(candidate => candidate.Id == issue.BlockId);
        if (block is null)
        {
            StatusText = issue.Message;
            return;
        }

        foreach (var candidate in Blocks)
        {
            candidate.IsSelected = false;
        }

        block.IsSelected = true;
        SelectedBlock = block;
        IsPropertyPanelVisible = true;
        StatusText = $"Located block '{block.DisplayName}'";
        CanvasRenderRequested?.Invoke();
    }

    /// <summary>
    /// Adds an edge only when the connection is structurally valid.
    /// Returns false and explains the reason in <see cref="StatusText"/> otherwise.
    /// </summary>
    public bool TryConnect(
        BlockNodeViewModel sourceBlock,
        PortViewModel sourcePort,
        BlockNodeViewModel targetBlock,
        PortViewModel targetPort)
    {
        var recipe = ToRecipe();
        var error = ConnectionRules.Validate(
            recipe.Blocks,
            recipe.Edges,
            sourceBlock.Id,
            sourcePort.Id,
            targetBlock.Id,
            targetPort.Id);

        if (error is not null)
        {
            StatusText = error;
            LogMessages.Add($"[Error] {error}");
            return false;
        }

        CommandHistory.Execute(new AddEdgeCommand(this, new EdgeViewModel
        {
            SourceBlockId = sourceBlock.Id,
            SourcePortId = sourcePort.Id,
            TargetBlockId = targetBlock.Id,
            TargetPortId = targetPort.Id,
            Label = sourcePort.DataType
        }));
        StatusText = "Connection added";
        return true;
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
            var recipe = ToRecipe();
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

    private RecipeRunner CreateRecipeRunner() => new(
        _recipeValidation,
        new IBlockExecutor[]
        {
            new SeedBlockExecutor(_settings),
            new ExpressionBlockExecutor(),
            new ValidatorBlockExecutor(),
            new SamplerBlockExecutor(),
            new LlmBlockExecutor(new LlmApiClient(_llmHttpClient, new LlmCredentials(_settings))),
            new QuerySourceBlockExecutor(),
            new SimulationBlockExecutor(new LlmCredentials(_settings), _llmHttpClient),
            new CleanerBlockExecutor(),
            new SftExportBlockExecutor()
        });

    private void OnExecutionProgress(ExecutionProgress progress)
    {
        Progress = progress.TotalBlocks == 0 ? 0 : (double)progress.CompletedBlocks / progress.TotalBlocks;
        TotalLines = progress.ProcessedRows;
        StatusText = progress.CurrentBlockName is null
            ? "Running recipe..."
            : $"Running {progress.CurrentBlockName} ({progress.CompletedBlocks}/{progress.TotalBlocks})";
    }

    #endregion

    #region Editor commands

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
        block.ApplyDefaultPorts();
        CommandHistory.Execute(new AddBlockCommand(this, block));
        SelectedBlock = block;
    }

    [RelayCommand]
    private void DeleteSelectedBlock()
    {
        if (SelectedBlock is null) return;

        var connectedEdges = Edges
            .Where(edge => edge.SourceBlockId == SelectedBlock.Id || edge.TargetBlockId == SelectedBlock.Id)
            .ToList();

        CommandHistory.Execute(new DeleteSelectionCommand(this, new[] { SelectedBlock }, connectedEdges));
        SelectedBlock = null;
    }

    [RelayCommand]
    private void ApplyBlockConfigJson()
    {
        if (SelectedBlock == null) return;

        if (SelectedBlock.TryApplyConfigJson(SelectedBlock.ConfigJson))
        {
            StatusText = "Block configuration applied ✓";
            LogMessages.Add($"[Info] Applied JSON configuration to block '{SelectedBlock.DisplayName}'.");
            return;
        }

        StatusText = "Invalid block configuration";
        LogMessages.Add($"[Error] Block '{SelectedBlock.DisplayName}': {SelectedBlock.ConfigJsonError}");
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

    #endregion

    #region Settings

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
            IsDarkTheme = IsDarkTheme,
            OpenAiApiKey = NullIfBlank(OpenAiApiKey),
            AnthropicApiKey = NullIfBlank(AnthropicApiKey),
            RecentFiles = RecentFiles.ToList(),
            LastRecipePath = string.IsNullOrWhiteSpace(CurrentFilePath) ? null : CurrentFilePath
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

    private static string? NullIfBlank(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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

    #endregion
}