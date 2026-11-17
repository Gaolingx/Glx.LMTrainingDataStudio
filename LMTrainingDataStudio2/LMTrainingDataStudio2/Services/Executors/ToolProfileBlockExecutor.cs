using System.Runtime.CompilerServices;
using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Services.Executors;

/// <summary>
/// Configuration-only node. It produces no rows; Simulation and LLM blocks reuse
/// the profile through <c>ToolProfileName</c>.
/// </summary>
public sealed class ToolProfileBlockExecutor : IBlockExecutor
{
    public BlockType Type => BlockType.ToolProfile;

    public Task<BlockExecutionResult> ExecuteAsync(BlockExecutionContext context, CancellationToken cancellationToken)
    {
        if (context.Block.Config is not ToolProfileBlockConfig config)
        {
            throw new InvalidOperationException(
                $"Tool profile block '{context.Block.Name}' has an invalid configuration.");
        }

        var profile = config.Profile ?? new ToolProfile();
        context.Log($"Tool profile '{profile.Name}' exposes {profile.Tools?.Count ?? 0} tool(s).");
        return Task.FromResult(new BlockExecutionResult(EmptyAsync(cancellationToken)));
    }

    private static async IAsyncEnumerable<DatasetRow> EmptyAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask;
        ct.ThrowIfCancellationRequested();
        yield break;
    }
}
