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

    /// <summary>
    /// Output port names that have outgoing edges. Null means the executor cannot know
    /// (for example in unit tests) and should produce every port it can.
    /// </summary>
    public IReadOnlySet<string>? ConnectedOutputPorts { get; init; }
}

/// <summary>
/// Rows produced by a block. <see cref="Output"/> is the primary stream; named
/// <see cref="AdditionalOutputs"/> carry extra ports such as validator rejects or sampler splits.
/// </summary>
public sealed record BlockExecutionResult(
    IAsyncEnumerable<DatasetRow> Output,
    IReadOnlyDictionary<string, IAsyncEnumerable<DatasetRow>>? AdditionalOutputs = null,
    Task? Completion = null);

public interface IBlockExecutor
{
    BlockType Type { get; }

    Task<BlockExecutionResult> ExecuteAsync(BlockExecutionContext context, CancellationToken cancellationToken);
}
