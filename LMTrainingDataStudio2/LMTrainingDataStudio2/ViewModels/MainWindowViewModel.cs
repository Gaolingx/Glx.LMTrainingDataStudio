using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LMTrainingDataStudio2.Commands;
using LMTrainingDataStudio2.Localization;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services;
using LMTrainingDataStudio2.Services.Executors;

namespace LMTrainingDataStudio2.ViewModels;

/// <summary>
/// Main window view model managing the three-panel studio layout.
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    private readonly RecipeValidationService _recipeValidation = new();
    private readonly ISettingsStore _settingsStore;
    private readonly AppSettingsService _settingsService;
    private readonly RecipeStore _recipeStore;
    private readonly RecipePresetStore _presetStore;
    private readonly ExecutionStore _executionStore;
    private readonly HttpClient _llmHttpClient = new();
    private readonly List<string> _presetLoadWarnings = new();
    private CancellationTokenSource? _runCancellation;
    private ExecutionPauseController? _runPause;
    private string _recipeId = Guid.NewGuid().ToString("N");
    private DateTime _recipeCreatedAt = DateTime.UtcNow;
    private DateTime _recipeModifiedAt = DateTime.UtcNow;
    private bool _suppressDocumentEvents;

    private AppSettings Settings => _settingsService.Current;

    /// <summary>
    /// Settings live in effect; long-lived services can subscribe to <see cref="IAppSettingsService.Changed"/>.
    /// </summary>
    public IAppSettingsService SettingsService => _settingsService;

    [ObservableProperty]
    private string _recipeName = "Untitled Recipe";

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private bool _hasValidationIssues;

    [ObservableProperty]
    private bool _isEditorView = true;

    [ObservableProperty]
    private bool _isExecutionsView;

    [ObservableProperty]
    private bool _isDataView;

    [ObservableProperty]
    private bool _isBlockSheetVisible = true;

    [ObservableProperty]
    private bool _isPropertyPanelVisible = true;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalLinesText))]
    private long _totalLines;
    [ObservableProperty]
    private string _currentFilePath = string.Empty;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelRunCommand))]
    [NotifyCanExecuteChangedFor(nameof(PauseRunCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResumeRunCommand))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PauseRunCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResumeRunCommand))]
    private bool _isRunPaused;

    [ObservableProperty]
    private string _memoryUsage = "0 MB";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
    private bool _canUndo;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RedoCommand))]
    private bool _canRedo;

    public string UndoDescription => CommandHistory.UndoDescription ?? string.Empty;

    public string RedoDescription => CommandHistory.RedoDescription ?? string.Empty;

    private double _canvasZoomScale = 1;

    public string CanvasZoomText => string.Format(LocalizationService.L("shell.zoom_format"), _canvasZoomScale);

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
    private int _maxParallelism = 4;

    [ObservableProperty]
    private int _nodeRetryCount;

    [ObservableProperty]
    private string _runOutputDirectory = string.Empty;

    [ObservableProperty]
    private long _spillThresholdRows = 10_000;

    [ObservableProperty]
    private long _spillThresholdMb = 64;

    [ObservableProperty]
    private string _runTempDirectory = string.Empty;

    [ObservableProperty]
    private long _checkpointQuotaMb = 512;

    [ObservableProperty]
    private int _executionHistoryLimit = 200;

    [ObservableProperty]
    private int _executionHistoryRetentionDays = 30;

    [ObservableProperty]
    private bool _isDarkTheme;

    [ObservableProperty]
    private string _cacheSizeText = "0 MB";

    [ObservableProperty]
    private string _openAiApiKey = string.Empty;

    [ObservableProperty]
    private string _anthropicApiKey = string.Empty;

    [ObservableProperty]
    private LanguageOption _selectedLanguageOption = LanguageCatalog.All[0];

    [ObservableProperty]
    private string _settingsValidationMessage = string.Empty;

    public bool HasSettingsValidationMessage => !string.IsNullOrEmpty(SettingsValidationMessage);

    partial void OnSettingsValidationMessageChanged(string value) =>
        OnPropertyChanged(nameof(HasSettingsValidationMessage));

    public ObservableCollection<BlockNodeViewModel> Blocks { get; } = new();
    public ObservableCollection<EdgeViewModel> Edges { get; } = new();
    public ObservableCollection<BlockTemplateViewModel> BlockTemplates { get; } = new();
    public ObservableCollection<RecipePresetViewModel> Presets { get; } = new();
    public ObservableCollection<string> LogMessages { get; } = new();
    public ObservableCollection<string> RecentFiles { get; } = new();
    public ObservableCollection<ValidationIssueViewModel> ValidationIssues { get; } = new();
    public CommandHistory CommandHistory { get; } = new();
    public ExecutionsViewModel Executions { get; }
    public DataWorkbenchViewModel DataWorkbench { get; }
    public IReadOnlyList<LanguageOption> LanguageOptions => LanguageCatalog.All;

    public string TotalLinesText => string.Format(LocalizationService.L("shell.lines_format"), TotalLines);

    public string SelectedBlockHelp => SelectedBlock is null
        ? string.Empty
        : BlockCatalog.GetHelp(SelectedBlock.Type);

    public string SelectedBlockPorts => SelectedBlock is null
        ? string.Empty
        : BlockCatalog.DescribePorts(SelectedBlock.Type, SelectedBlock.Config);

    /// <summary>Raised when the canvas must re-render without a collection change.</summary>
    public event Action? CanvasRenderRequested;

    public string DocumentTitle => $"{RecipeName}{(IsDirty ? " *" : string.Empty)}";

    public MainWindowViewModel() : this(new SettingsStore())
    {
    }

    public MainWindowViewModel(
        ISettingsStore settingsStore,
        RecipeStore? recipeStore = null,
        RecipePresetStore? presetStore = null,
        ExecutionStore? executionStore = null)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _recipeStore = recipeStore ?? new RecipeStore();
        _presetStore = presetStore ?? new RecipePresetStore();
        _executionStore = executionStore ?? new ExecutionStore();
        var loadResult = _settingsStore.Load();
        _settingsService = new AppSettingsService(loadResult.Settings);
        LocalizationService.Instance.SetLanguage(Settings.Language);
        ThemeManager.Apply(Settings.IsDarkTheme);
        Executions = new ExecutionsViewModel(_executionStore);
        DataWorkbench = new DataWorkbenchViewModel(_settingsService);

        LoadSettingsSnapshot();
        InitializeBlockTemplates();
        InitializePresets();

        Blocks.CollectionChanged += OnBlocksCollectionChanged;
        Edges.CollectionChanged += OnEdgesCollectionChanged;
        CommandHistory.HistoryChanged += OnHistoryChanged;
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;

        LoadDocument(CreateBlankRecipe(), path: null, markDirty: false);
        UpdateMemoryUsage();

        foreach (var warning in loadResult.Warnings)
        {
            LogMessages.Add($"[Warning] {warning}");
        }

        foreach (var warning in _presetLoadWarnings)
        {
            LogMessages.Add($"[Warning] {warning}");
        }
    }

    private void LoadSettingsSnapshot()
    {
        MaxCacheSizeMb = Settings.MaxCacheSizeMb;
        CacheDirectory = Settings.CacheDirectory;
        EnableGridSnap = Settings.EnableGridSnap;
        GridSnapSize = Settings.GridSnapSize;
        MaxParallelism = Settings.MaxParallelism;
        NodeRetryCount = Settings.NodeRetryCount;
        RunOutputDirectory = Settings.RunOutputDirectory ?? string.Empty;
        SpillThresholdRows = Settings.SpillThresholdRows;
        SpillThresholdMb = Settings.SpillThresholdBytes / (1024 * 1024);
        RunTempDirectory = Settings.RunTempDirectory;
        CheckpointQuotaMb = Settings.CheckpointQuotaMb;
        ExecutionHistoryLimit = Settings.ExecutionHistoryLimit;
        ExecutionHistoryRetentionDays = Settings.ExecutionHistoryRetentionDays;
        IsDarkTheme = Settings.IsDarkTheme;
        OpenAiApiKey = Settings.OpenAiApiKey ?? string.Empty;
        AnthropicApiKey = Settings.AnthropicApiKey ?? string.Empty;
        SelectedLanguageOption = LanguageCatalog.Resolve(Settings.Language);

        RecentFiles.Clear();
        foreach (var path in Settings.RecentFiles)
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
        foreach (var type in BlockCatalog.Types)
        {
            BlockTemplates.Add(new BlockTemplateViewModel(type));
        }
    }

    private void InitializePresets()
    {
        foreach (var preset in RecipePresetCatalog.All)
        {
            Presets.Add(new RecipePresetViewModel(preset));
        }

        var loaded = _presetStore.LoadAll();
        foreach (var document in loaded.Presets)
        {
            Presets.Add(new RecipePresetViewModel(RecipePresetCatalog.FromDocument(document)));
        }

        _presetLoadWarnings.AddRange(loaded.Warnings);
    }

    private RecipePresetViewModel AddCustomPreset(RecipePresetDocument document)
    {
        var viewModel = new RecipePresetViewModel(RecipePresetCatalog.FromDocument(document));
        Presets.Add(viewModel);
        return viewModel;
    }

    private RecipePresetViewModel? FindPreset(string id) =>
        Presets.FirstOrDefault(preset => string.Equals(preset.Id, id, StringComparison.OrdinalIgnoreCase));

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(TotalLinesText));
        OnPropertyChanged(nameof(CanvasZoomText));
        OnPropertyChanged(nameof(SelectedBlockHelp));
        OnPropertyChanged(nameof(SelectedBlockPorts));

        foreach (var template in BlockTemplates)
        {
            template.RefreshLocalization();
        }

        foreach (var preset in Presets)
        {
            preset.RefreshLocalization();
        }

        foreach (var block in Blocks)
        {
            block.RefreshLocalization();
        }

        foreach (var issue in ValidationIssues)
        {
            issue.RefreshLocalization();
        }

        DataWorkbench.RefreshLocalization();
    }

    partial void OnSelectedBlockChanged(BlockNodeViewModel? value)
    {
        OnPropertyChanged(nameof(SelectedBlockHelp));
        OnPropertyChanged(nameof(SelectedBlockPorts));
    }

    public void SetCanvasZoomScale(double scale)
    {
        if (Math.Abs(_canvasZoomScale - scale) < 0.0001)
        {
            return;
        }

        _canvasZoomScale = scale;
        OnPropertyChanged(nameof(CanvasZoomText));
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
    /// Replaces the current document with a bundled or user workflow preset and marks it dirty.
    /// </summary>
    public async Task NewRecipeFromPresetAsync(string presetId)
    {
        var preset = FindPreset(presetId)?.Preset
                     ?? throw new ArgumentException($"Unknown recipe preset '{presetId}'.", nameof(presetId));

        LoadDocument(preset.Create(), path: null, markDirty: true);
        LogMessages.Clear();
        LogMessages.Add($"[Info] Loaded workflow preset '{preset.DisplayName}'.");
        await PersistDocumentStateAsync();
    }

    /// <summary>
    /// Stores the current document as a user preset in the local preset library.
    /// </summary>
    public async Task<RecipePresetViewModel> SaveAsPresetAsync(string name, string? description)
    {
        var document = await _presetStore.SaveAsync(name, description, ToRecipe());
        var viewModel = AddCustomPreset(document);
        StatusText = string.Format(LocalizationService.L("preset.saved_format"), document.Name);
        LogMessages.Add($"[Info] Saved user preset '{document.Name}'.");
        return viewModel;
    }

    /// <summary>
    /// Imports a preset file into the local library without changing the current document.
    /// </summary>
    public async Task<RecipePresetViewModel> ImportPresetAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var document = await _presetStore.ImportAsync(path);
        var viewModel = AddCustomPreset(document);
        StatusText = string.Format(LocalizationService.L("preset.imported_format"), document.Name);
        LogMessages.Add($"[Info] Imported preset '{document.Name}' from '{path}'.");
        return viewModel;
    }

    /// <summary>
    /// Deletes a user preset. Built-in presets cannot be deleted.
    /// </summary>
    public async Task<bool> DeleteCustomPresetAsync(string presetId)
    {
        var viewModel = FindPreset(presetId);
        if (viewModel is null || !viewModel.IsCustom || viewModel.Preset.CustomDocumentId is null)
        {
            return false;
        }

        await _presetStore.DeleteAsync(viewModel.Preset.CustomDocumentId);
        Presets.Remove(viewModel);
        StatusText = string.Format(LocalizationService.L("preset.deleted_format"), viewModel.Name);
        LogMessages.Add($"[Info] Deleted user preset '{viewModel.Name}'.");
        return true;
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
        var path = Settings.LastRecipePath;
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
            OutputPorts = BlockPortSchema.CreateOutputPorts(type, config)
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

    public void PruneRecentFiles()
    {
        for (var index = RecentFiles.Count - 1; index >= 0; index--)
        {
            if (!File.Exists(RecentFiles[index]))
            {
                RecentFiles.RemoveAt(index);
            }
        }
    }

    [RelayCommand]
    private async Task ClearRecentFilesAsync()
    {
        RecentFiles.Clear();
        Settings.RecentFiles = new List<string>();

        try
        {
            await _settingsStore.SaveAsync(Settings);
            StatusText = "Recent files cleared";
            LogMessages.Add("[Info] Recent files cleared.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            StatusText = "Failed to save settings";
            LogMessages.Add($"[Error] Failed to clear recent files: {ex.Message}");
        }
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
        Settings.RecentFiles = RecentFiles.ToList();
        Settings.LastRecipePath = string.IsNullOrWhiteSpace(CurrentFilePath) ? null : CurrentFilePath;

        try
        {
            await _settingsStore.SaveAsync(Settings);
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
                block.ConfigCommandSink = null;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (BlockNodeViewModel block in e.NewItems)
            {
                block.PropertyChanged += OnBlockPropertyChanged;
                block.ConfigCommandSink = command =>
                    CommandHistory.ExecuteMerged(command, MergeConfigCommands);
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
        else if (e.PropertyName == nameof(BlockNodeViewModel.Config) &&
                 sender is BlockNodeViewModel changedBlock &&
                 changedBlock.RefreshOutputPorts())
        {
            OnPropertyChanged(nameof(SelectedBlockPorts));
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
        CanUndo = CommandHistory.CanUndo;
        CanRedo = CommandHistory.CanRedo;
        OnPropertyChanged(nameof(UndoDescription));
        OnPropertyChanged(nameof(RedoDescription));

        if (_suppressDocumentEvents) return;

        StatusText = "Recipe history updated";
        MarkDirty();
        CanvasRenderRequested?.Invoke();
    }

    private static IUndoableCommand? MergeConfigCommands(
        IUndoableCommand previous,
        IUndoableCommand current)
    {
        if (previous is not SetBlockConfigCommand first ||
            current is not SetBlockConfigCommand second ||
            !ReferenceEquals(first.Block, second.Block) ||
            (second.TimestampUtc - first.TimestampUtc).TotalMilliseconds > 1500)
        {
            return null;
        }

        return first.MergeWith(second);
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo() => CommandHistory.Undo();

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo() => CommandHistory.Redo();

    /// <summary>
    /// Re-samples the process working set for the status bar.
    /// </summary>
    public void RefreshMemoryUsage() => UpdateMemoryUsage();

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
    private async Task RunRecipeAsync() => await RunRecipeCoreAsync(resumeFrom: null);

    /// <summary>
    /// Confirms replacing existing SFT export targets before a run starts.
    /// The window supplies the handler; headless callers proceed without prompting.
    /// </summary>
    public Func<IReadOnlyList<string>, Task<bool>>? ConfirmOverwriteAsync { get; set; }

    [RelayCommand]
    private async Task ResumeExecutionAsync(ExecutionRun? run)
    {
        if (run is null) return;

        if (run.Status is not (RunStatus.Failed or RunStatus.Cancelled) ||
            string.IsNullOrWhiteSpace(run.OutputDirectory))
        {
            StatusText = "Only failed or cancelled runs with an output directory can be resumed.";
            return;
        }

        var recipe = ToRecipe();
        if (!string.Equals(run.RecipeId, recipe.Id, StringComparison.Ordinal))
        {
            StatusText = "Open the recipe that produced this run before resuming it.";
            return;
        }

        await RunRecipeCoreAsync(run);
    }

    private async Task RunRecipeCoreAsync(ExecutionRun? resumeFrom)
    {
        if (IsRunning) return;

        var recipe = ToRecipe();
        if (!await ConfirmExportOverwriteAsync(recipe))
        {
            StatusText = "Execution cancelled — existing export target was not replaced.";
            return;
        }

        IsRunning = true;
        Progress = 0;
        StatusText = resumeFrom is null ? "Running recipe..." : "Resuming execution...";
        LogMessages.Clear();
        LogMessages.Add(resumeFrom is null
            ? "[Info] Recipe execution started."
            : $"[Info] Resuming run {resumeFrom.RunId}.");

        _runCancellation = new CancellationTokenSource();
        _runPause = new ExecutionPauseController();
        IsRunPaused = false;
        var cancellationToken = _runCancellation.Token;

        try
        {
            var runner = CreateRecipeRunner();
            var progress = new Progress<ExecutionProgress>(OnExecutionProgress);
            var pause = _runPause;

            var run = await Task.Run(() =>
                runner.RunAsync(recipe, progress, cancellationToken, pause, resumeFrom));

            try
            {
                await _executionStore.SaveAsync(run, CancellationToken.None);
                await Executions.RefreshAndSelectAsync(run.RunId);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LogMessages.Add($"[Error] Failed to save execution record: {ex.Message}");
            }

            EnforceCheckpointQuota(run.OutputDirectory);
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
            _runPause = null;
            IsRunPaused = false;
            IsRunning = false;
        }
    }

    /// <summary>
    /// Applies execution retention and the checkpoint quota at startup. Failures are
    /// logged but never block startup.
    /// </summary>
    public async Task RunStartupMaintenanceAsync()
    {
        try
        {
            var removed = _executionStore.Prune(
                Settings.ExecutionHistoryLimit, Settings.ExecutionHistoryRetentionDays);
            if (removed > 0)
            {
                LogMessages.Add(
                    $"[Info] Removed {removed} execution record(s) outside the retention policy.");
            }

            await Executions.RefreshAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogMessages.Add($"[Warning] Could not clean execution history: {ex.Message}");
        }

        EnforceCheckpointQuota();
    }

    [RelayCommand]
    private async Task CleanupExecutionsAsync()
    {
        try
        {
            var removed = _executionStore.Prune(
                Settings.ExecutionHistoryLimit, Settings.ExecutionHistoryRetentionDays);
            await Executions.RefreshAsync();
            StatusText = removed == 0
                ? "No execution records matched the retention policy."
                : $"Removed {removed} execution record(s).";
            LogMessages.Add($"[Info] {StatusText}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText = "Failed to clean execution history";
            LogMessages.Add($"[Error] Failed to clean execution history: {ex.Message}");
        }
    }

    private void EnforceCheckpointQuota(string? protectedRunDirectory = null)
    {
        if (Settings.CheckpointQuotaMb <= 0)
        {
            return;
        }

        try
        {
            var result = CheckpointQuota.Enforce(
                Settings.RunOutputDirectory,
                Settings.CheckpointQuotaMb * 1024L * 1024L,
                protectedRunDirectory);
            if (result.DeletedCheckpoints > 0)
            {
                var freedMb = result.FreedBytes / (1024.0 * 1024.0);
                LogMessages.Add(
                    $"[Info] Removed {result.DeletedCheckpoints} obsolete checkpoint(s) ({freedMb:F1} MB).");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogMessages.Add($"[Warning] Could not enforce the checkpoint quota: {ex.Message}");
        }
    }

    private async Task<bool> ConfirmExportOverwriteAsync(Recipe recipe)
    {
        var targets = new List<string>();
        foreach (var block in recipe.Blocks)
        {
            if (block.Config is not SftExportBlockConfig export ||
                string.IsNullOrWhiteSpace(export.OutputPath))
            {
                continue;
            }

            try
            {
                var path = Path.GetFullPath(export.OutputPath);
                if (File.Exists(path) &&
                    !targets.Contains(path, StringComparer.OrdinalIgnoreCase))
                {
                    targets.Add(path);
                }
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                LogMessages.Add($"[Warning] Could not resolve export path '{export.OutputPath}': {ex.Message}");
            }
        }

        if (targets.Count == 0 || ConfirmOverwriteAsync is null) return true;
        return await ConfirmOverwriteAsync(targets);
    }

    [RelayCommand(CanExecute = nameof(CanCancelRun))]
    private void CancelRun() => _runCancellation?.Cancel();

    private bool CanCancelRun() => IsRunning;

    [RelayCommand(CanExecute = nameof(CanPauseRun))]
    private void PauseRun()
    {
        if (_runPause is null) return;
        _runPause.Pause();
        IsRunPaused = true;
        StatusText = "Execution paused — running blocks will finish.";
        LogMessages.Add("[Info] Execution paused.");
    }

    private bool CanPauseRun() => IsRunning && !IsRunPaused;

    [RelayCommand(CanExecute = nameof(CanResumeRun))]
    private void ResumeRun()
    {
        if (_runPause is null) return;
        _runPause.Resume();
        IsRunPaused = false;
        StatusText = "Execution resumed.";
        LogMessages.Add("[Info] Execution resumed.");
    }

    private bool CanResumeRun() => IsRunning && IsRunPaused;

    private RecipeRunner CreateRecipeRunner() => new(
        _recipeValidation,
        new IBlockExecutor[]
        {
            new SeedBlockExecutor(Settings),
            new ExpressionBlockExecutor(),
            new ValidatorBlockExecutor(),
            new SamplerBlockExecutor(),
            new LlmBlockExecutor(new LlmApiClient(_llmHttpClient, new LlmCredentials(Settings))),
            new ToolProfileBlockExecutor(),
            new QuerySourceBlockExecutor(),
            new SimulationBlockExecutor(new LlmCredentials(Settings), _llmHttpClient),
            new CleanerBlockExecutor(),
            new SftExportBlockExecutor()
        },
        options: new RecipeRunOptions(
            MaxParallelism,
            NodeRetryCount,
            RunOutputDirectory,
            SpillThresholdRows,
            SpillThresholdMb * 1024L * 1024L,
            RunTempDirectory));

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
    private void AutoLayoutBlocks()
    {
        var recipe = ToRecipe();
        if (recipe.Blocks.Count == 0)
        {
            StatusText = "Nothing to lay out.";
            return;
        }

        var positions = RecipeLayoutService.Arrange(recipe);
        var moves = new List<(BlockNodeViewModel Block, double OldX, double OldY, double NewX, double NewY)>();
        foreach (var block in Blocks)
        {
            if (!positions.TryGetValue(block.Id, out var position)) continue;
            if (Math.Abs(block.X - position.X) < 0.1 && Math.Abs(block.Y - position.Y) < 0.1)
            {
                continue;
            }

            moves.Add((block, block.X, block.Y, position.X, position.Y));
        }

        if (moves.Count == 0)
        {
            StatusText = "Auto layout already applied.";
            return;
        }

        CommandHistory.Execute(new MoveBlocksCommand(moves));
        StatusText = $"Auto layout applied to {moves.Count} block(s).";
        CanvasRenderRequested?.Invoke();
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
        IsExecutionsView = false;
        IsDataView = false;
    }

    [RelayCommand]
    private async Task SwitchToExecutionsAsync()
    {
        IsEditorView = false;
        IsExecutionsView = true;
        IsDataView = false;
        await Executions.RefreshAsync();
    }

    [RelayCommand]
    private void SwitchToData()
    {
        IsEditorView = false;
        IsExecutionsView = false;
        IsDataView = true;
    }

    /// <summary>Creates a sample preview whose "open in Data" action switches views and opens the file.</summary>
    public SamplePreviewViewModel CreateSamplePreview()
    {
        var preview = new SamplePreviewViewModel(Settings);
        preview.OpenInDataRequested = OpenDatasetInDataViewAsync;
        return preview;
    }

    public async Task OpenDatasetInDataViewAsync(string path)
    {
        IsEditorView = false;
        IsExecutionsView = false;
        IsDataView = true;
        await DataWorkbench.OpenDatasetAsync(path);
    }

    #endregion

    #region Settings

    [RelayCommand]
    private void OpenSettings()
    {
        SettingsValidationMessage = string.Empty;
        LoadSettingsSnapshot();
        IsSettingsVisible = true;
    }

    [RelayCommand]
    private void CloseSettings()
    {
        SettingsValidationMessage = string.Empty;
        IsSettingsVisible = false;
    }

    [RelayCommand]
    private async Task ApplySettingsAsync()
    {
        var defaults = new AppSettings();
        var errors = ValidateSettingsInputs();
        if (errors.Count > 0)
        {
            SettingsValidationMessage = string.Join(Environment.NewLine, errors);
            StatusText = "Fix the settings errors before applying";
            foreach (var error in errors)
            {
                LogMessages.Add($"[Error] {error}");
            }

            return;
        }

        SettingsValidationMessage = string.Empty;
        var settings = new AppSettings
        {
            MaxCacheSizeMb = Math.Max(SettingsStore.MinCacheSizeMb, MaxCacheSizeMb),
            CacheDirectory = string.IsNullOrWhiteSpace(CacheDirectory)
                ? defaults.CacheDirectory
                : CacheDirectory.Trim(),
            EnableGridSnap = EnableGridSnap,
            GridSnapSize = Math.Clamp(GridSnapSize, SettingsStore.MinGridSnapSize, SettingsStore.MaxGridSnapSize),
            MaxParallelism = Math.Clamp(MaxParallelism, SettingsStore.MinParallelism, SettingsStore.MaxParallelism),
            NodeRetryCount = Math.Clamp(NodeRetryCount, 0, SettingsStore.MaxNodeRetries),
            RunOutputDirectory = string.IsNullOrWhiteSpace(RunOutputDirectory)
                ? defaults.RunOutputDirectory
                : RunOutputDirectory.Trim(),
            SpillThresholdRows = Math.Clamp(
                SpillThresholdRows, 0, SettingsStore.MaxSpillThresholdRows),
            SpillThresholdBytes = Math.Clamp(
                SpillThresholdMb, 0, SettingsStore.MaxSpillThresholdMb) * 1024L * 1024L,
            RunTempDirectory = string.IsNullOrWhiteSpace(RunTempDirectory)
                ? defaults.RunTempDirectory
                : RunTempDirectory.Trim(),
            CheckpointQuotaMb = Math.Clamp(
                CheckpointQuotaMb, 0, SettingsStore.MaxCheckpointQuotaMb),
            ExecutionHistoryLimit = Math.Clamp(
                ExecutionHistoryLimit, 0, SettingsStore.MaxExecutionHistoryLimit),
            ExecutionHistoryRetentionDays = Math.Clamp(
                ExecutionHistoryRetentionDays, 0, SettingsStore.MaxExecutionRetentionDays),
            IsDarkTheme = IsDarkTheme,
            Language = SelectedLanguageOption.Code,
            OpenAiApiKey = NullIfBlank(OpenAiApiKey),
            AnthropicApiKey = NullIfBlank(AnthropicApiKey),
            RecentFiles = RecentFiles.ToList(),
            LastRecipePath = string.IsNullOrWhiteSpace(CurrentFilePath) ? null : CurrentFilePath
        };

        try
        {
            await _settingsStore.SaveAsync(settings);
            _settingsService.Update(settings);
            ThemeManager.Apply(settings.IsDarkTheme);
            LocalizationService.Instance.SetLanguage(settings.Language);
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

    private List<string> ValidateSettingsInputs()
    {
        var errors = new List<string>();

        if (MaxCacheSizeMb < SettingsStore.MinCacheSizeMb)
        {
            errors.Add(
                $"Max cache size must be at least {SettingsStore.MinCacheSizeMb} MB; found {MaxCacheSizeMb}.");
        }

        if (GridSnapSize < SettingsStore.MinGridSnapSize || GridSnapSize > SettingsStore.MaxGridSnapSize)
        {
            errors.Add(
                $"Grid snap size must be between {SettingsStore.MinGridSnapSize} and " +
                $"{SettingsStore.MaxGridSnapSize} px; found {GridSnapSize}.");
        }

        if (MaxParallelism < SettingsStore.MinParallelism || MaxParallelism > SettingsStore.MaxParallelism)
        {
            errors.Add(
                $"Parallelism must be between {SettingsStore.MinParallelism} and " +
                $"{SettingsStore.MaxParallelism}; found {MaxParallelism}.");
        }

        if (NodeRetryCount < 0 || NodeRetryCount > SettingsStore.MaxNodeRetries)
        {
            errors.Add($"Node retries must be between 0 and {SettingsStore.MaxNodeRetries}; found {NodeRetryCount}.");
        }

        if (SpillThresholdRows < 0 || SpillThresholdRows > SettingsStore.MaxSpillThresholdRows)
        {
            errors.Add(
                $"Spill threshold must be between 0 and {SettingsStore.MaxSpillThresholdRows} rows; " +
                $"found {SpillThresholdRows}.");
        }

        if (SpillThresholdMb < 0 || SpillThresholdMb > SettingsStore.MaxSpillThresholdMb)
        {
            errors.Add(
                $"Spill byte threshold must be between 0 and {SettingsStore.MaxSpillThresholdMb} MB; " +
                $"found {SpillThresholdMb}.");
        }

        if (CheckpointQuotaMb < 0 || CheckpointQuotaMb > SettingsStore.MaxCheckpointQuotaMb)
        {
            errors.Add(
                $"Checkpoint quota must be between 0 and {SettingsStore.MaxCheckpointQuotaMb} MB; " +
                $"found {CheckpointQuotaMb}.");
        }

        if (ExecutionHistoryLimit < 0 || ExecutionHistoryLimit > SettingsStore.MaxExecutionHistoryLimit)
        {
            errors.Add(
                $"Execution history limit must be between 0 and {SettingsStore.MaxExecutionHistoryLimit}; " +
                $"found {ExecutionHistoryLimit}.");
        }

        if (ExecutionHistoryRetentionDays < 0 ||
            ExecutionHistoryRetentionDays > SettingsStore.MaxExecutionRetentionDays)
        {
            errors.Add(
                $"Execution history retention must be between 0 and " +
                $"{SettingsStore.MaxExecutionRetentionDays} days; found {ExecutionHistoryRetentionDays}.");
        }

        AddPathError(errors, "Cache directory", CacheDirectory, allowDefault: true);
        AddPathError(errors, "Run summary directory", RunOutputDirectory, allowDefault: true);
        AddPathError(errors, "Run temp directory", RunTempDirectory, allowDefault: true);

        return errors;
    }

    private static void AddPathError(List<string> errors, string label, string path, bool allowDefault)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            if (!allowDefault)
            {
                errors.Add($"{label} is required.");
            }

            return;
        }

        var expanded = Environment.ExpandEnvironmentVariables(path.Trim());
        try
        {
            var full = Path.GetFullPath(expanded);
            Directory.CreateDirectory(full);
            var probe = Path.Combine(full, $".lmtds-write-{Guid.NewGuid():N}");
            using (File.Create(probe))
            {
            }

            File.Delete(probe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            errors.Add($"{label} is not writable: {expanded} ({ex.Message})");
        }
    }

    [RelayCommand]
    private void CleanIndexCache()
    {
        var service = new IndexCacheService(Settings);
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
            using var service = new IndexCacheService(Settings);
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
