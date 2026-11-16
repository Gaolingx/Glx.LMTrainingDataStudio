using LMTrainingDataStudio2.Localization;
using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Services;

/// <summary>
/// A named starting point that builds a fresh recipe on every load.
/// </summary>
public sealed record RecipePreset(string Id, string NameKey, string SummaryKey, Func<Recipe> Create);

/// <summary>
/// Bundled basic node workflows shown in File → New from Preset and the Block Library.
/// </summary>
public static class RecipePresetCatalog
{
    public static IReadOnlyList<RecipePreset> All { get; } =
    [
        new("llm_batch", "preset.llm_batch.name", "preset.llm_batch.summary", CreateLlmBatch),
        new("oai_clean", "preset.oai_clean.name", "preset.oai_clean.summary", CreateOaiClean),
        new("field_map", "preset.field_map.name", "preset.field_map.summary", CreateFieldMap),
        new("validate_split", "preset.validate_split.name", "preset.validate_split.summary", CreateValidateSplit),
        new("agent_trajectory", "preset.agent_trajectory.name", "preset.agent_trajectory.summary",
            CreateAgentTrajectory),
        new("real_tools", "preset.real_tools.name", "preset.real_tools.summary", CreateRealTools)
    ];

    public static RecipePreset? Find(string id) =>
        All.FirstOrDefault(preset => string.Equals(preset.Id, id, StringComparison.OrdinalIgnoreCase));

    public static Recipe Create(string id) =>
        Find(id)?.Create() ?? throw new ArgumentException($"Unknown recipe preset '{id}'.", nameof(id));

    private static Recipe CreateLlmBatch()
    {
        var recipe = NewRecipe("preset.llm_batch.name");
        var seed = AddBlock(
            recipe, "seed", BlockType.Seed, 80, 160,
            new SeedBlockConfig(string.Empty, DataFileType.Jsonl, Streaming: false, UseIndexCache: true));
        var llm = AddBlock(
            recipe, "llm", BlockType.Llm, 360, 160,
            new LlmBlockConfig(LlmProviderIds.OpenAiChat, "gpt-4o-mini", Temperature: 0.7, MaxTokens: 2048, Prompt: null));
        var export = AddBlock(
            recipe, "export", BlockType.SftExport, 640, 160,
            new SftExportBlockConfig("out/llm-batch.jsonl"));

        Connect(recipe, seed, llm);
        Connect(recipe, llm, export);
        return recipe;
    }

    private static Recipe CreateOaiClean()
    {
        var recipe = NewRecipe("preset.oai_clean.name");
        var seed = AddBlock(
            recipe, "seed", BlockType.Seed, 80, 160,
            new SeedBlockConfig(string.Empty, DataFileType.Jsonl, Streaming: false, UseIndexCache: true));
        var cleaner = AddBlock(
            recipe, "cleaner", BlockType.Cleaner, 360, 160,
            new CleanerBlockConfig(new DatasetCleanupRules
            {
                NormalizeNewlines = true,
                NormalizeWhitespace = true,
                StripAnsi = true,
                RemoveReasoningContent = true,
                DropEmptyMessages = true
            }));
        var export = AddBlock(
            recipe, "export", BlockType.SftExport, 640, 160,
            new SftExportBlockConfig("out/clean-sft.jsonl"));

        Connect(recipe, seed, cleaner);
        Connect(recipe, cleaner, export);
        return recipe;
    }

    private static Recipe CreateFieldMap()
    {
        var recipe = NewRecipe("preset.field_map.name");
        var seed = AddBlock(
            recipe, "seed", BlockType.Seed, 80, 160,
            new SeedBlockConfig(string.Empty, DataFileType.Jsonl, Streaming: false, UseIndexCache: true));
        var expression = AddBlock(
            recipe, "expression", BlockType.Expression, 360, 160,
            new ExpressionBlockConfig(
                """{"messages":[{"role":"assistant","content":{{ text | json }}}]}""",
                new Dictionary<string, string> { ["text"] = "text" }));
        var export = AddBlock(
            recipe, "export", BlockType.SftExport, 640, 160,
            new SftExportBlockConfig("out/mapped-sft.jsonl"));

        Connect(recipe, seed, expression);
        Connect(recipe, expression, export);
        return recipe;
    }

    private static Recipe CreateValidateSplit()
    {
        var recipe = NewRecipe("preset.validate_split.name");
        var seed = AddBlock(
            recipe, "seed", BlockType.Seed, 80, 160,
            new SeedBlockConfig(string.Empty, DataFileType.Jsonl, Streaming: false, UseIndexCache: true));
        var validator = AddBlock(
            recipe, "validator", BlockType.Validator, 360, 160,
            new ValidatorBlockConfig(
                "expression",
                "messages.length > 0",
                RejectOnFailure: true,
                RejectOutputPath: "out/rejected.jsonl"));
        var sampler = AddBlock(
            recipe, "sampler", BlockType.Sampler, 640, 160,
            new SamplerBlockConfig(
                Count: null,
                Ratio: null,
                Seed: 42,
                Splits: ["train:0.8", "valid:0.1", "test:0.1"],
                OutputDirectory: "out/splits"));
        var export = AddBlock(
            recipe, "export", BlockType.SftExport, 920, 160,
            new SftExportBlockConfig("out/sampled-sft.jsonl"));

        Connect(recipe, seed, validator);
        Connect(recipe, validator, sampler);
        Connect(recipe, sampler, export);
        return recipe;
    }

    private static Recipe CreateAgentTrajectory()
    {
        var recipe = NewRecipe("preset.agent_trajectory.name");
        var queries = AddBlock(
            recipe, "queries", BlockType.QuerySource, 80, 160,
            new QuerySourceBlockConfig(
                ["Explain the TCP three-way handshake with a small example."],
                QuerySetMode.MultipleQueries,
                Repeat: 1,
                SystemPrompt: "You are a concise network engineering tutor."));
        var simulation = AddBlock(
            recipe, "simulation", BlockType.Simulation, 360, 120,
            new SimulationBlockConfig(
                "demo-model",
                MaxRounds: 6,
                SystemPrompt: "Use the python tool when a calculation helps.",
                Tools: SimulatedToolCatalog.Common(),
                FinalContent: "The TCP handshake establishes a reliable connection with SYN, SYN-ACK and ACK."));
        var cleaner = AddBlock(
            recipe, "cleaner", BlockType.Cleaner, 640, 160,
            new CleanerBlockConfig(new DatasetCleanupRules
            {
                NormalizeNewlines = true,
                NormalizeWhitespace = true,
                RemoveReasoningContent = true,
                DropEmptyMessages = true
            }));
        var export = AddBlock(
            recipe, "export", BlockType.SftExport, 920, 160,
            new SftExportBlockConfig("out/agent-sft.jsonl"));

        Connect(recipe, queries, simulation);
        Connect(recipe, simulation, cleaner);
        Connect(recipe, cleaner, export);
        return recipe;
    }

    private static Recipe CreateRealTools()
    {
        var recipe = NewRecipe("preset.real_tools.name");
        var queries = AddBlock(
            recipe, "queries", BlockType.QuerySource, 80, 160,
            new QuerySourceBlockConfig(
                ["Use the python tool to compute 21 * 2 and explain the result."],
                QuerySetMode.MultipleQueries,
                Repeat: 1,
                SystemPrompt: "You are a precise tool-using assistant."));
        var simulation = AddBlock(
            recipe, "simulation", BlockType.Simulation, 360, 120,
            new SimulationBlockConfig(
                "gpt-4o-mini",
                MaxRounds: 4,
                SystemPrompt: "Call the python tool when a calculation is needed.",
                Tools: [SimulatedToolCatalog.Python()],
                Rules: new ToolHarnessRules { AllowedTools = ["python"], MaxRetries = 1 },
                Provider: LlmProviderIds.OpenAiChat,
                ModelMode: SimulationModelMode.Provider,
                ToolMode: ToolExecutionMode.Real,
                FailureOutputPath: "out/real-tools-failed.jsonl"));
        var cleaner = AddBlock(
            recipe, "cleaner", BlockType.Cleaner, 640, 160,
            new CleanerBlockConfig(new DatasetCleanupRules
            {
                NormalizeNewlines = true,
                NormalizeWhitespace = true,
                RemoveReasoningContent = true,
                DropEmptyMessages = true
            }));
        var export = AddBlock(
            recipe, "export", BlockType.SftExport, 920, 160,
            new SftExportBlockConfig("out/real-tools-sft.jsonl"));

        Connect(recipe, queries, simulation);
        Connect(recipe, simulation, cleaner);
        Connect(recipe, cleaner, export);
        return recipe;
    }

    private static Recipe NewRecipe(string nameKey) =>
        new() { Name = LocalizationService.L(nameKey) };

    private static RecipeBlock AddBlock(
        Recipe recipe,
        string id,
        BlockType type,
        double x,
        double y,
        BlockConfig config)
    {
        var block = new RecipeBlock
        {
            Id = id,
            Type = type,
            Name = id,
            DisplayName = BlockCatalog.GetName(type),
            X = x,
            Y = y,
            Config = config,
            InputPorts = BlockPortSchema.CreateInputPorts(type),
            OutputPorts = BlockPortSchema.CreateOutputPorts(type)
        };

        recipe.Blocks.Add(block);
        return block;
    }

    private static void Connect(Recipe recipe, RecipeBlock source, RecipeBlock target)
    {
        var sourcePort = source.OutputPorts[0];
        recipe.Edges.Add(new RecipeEdge
        {
            Id = $"edge_{source.Id}_{target.Id}",
            SourceBlockId = source.Id,
            SourcePortId = sourcePort.Id,
            TargetBlockId = target.Id,
            TargetPortId = target.InputPorts[0].Id,
            Label = sourcePort.DataType
        });
    }
}
