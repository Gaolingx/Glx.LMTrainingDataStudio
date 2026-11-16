using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Services.Executors;

/// <summary>
/// Inputs available to a block executor while it runs.
/// </summary>
public sealed class BlockExecutionContext
{
    public required RecipeBlock Block { get; init; }
    public IAsyncEnumerable<DatasetRow>? Input { get; init; }
    public required Action<string> Log { get; init; }
    public IProgress<long>? RowsProcessed { get; init; }

    /// <summary>Tool profile resolved from a referenced Tool Profile block, when configured.</summary>
    public ToolProfile? ToolProfile { get; init; }
}

/// <summary>
/// Stream of rows produced by a block.
/// </summary>
public sealed record BlockExecutionResult(IAsyncEnumerable<DatasetRow> Output);

public interface IBlockExecutor
{
    BlockType Type { get; }

    Task<BlockExecutionResult> ExecuteAsync(BlockExecutionContext context, CancellationToken cancellationToken);
}