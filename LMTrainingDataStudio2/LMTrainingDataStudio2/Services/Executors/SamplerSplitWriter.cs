using System.Text;
using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Services.Executors;

/// <summary>
/// Writes each sampler split to a temporary file and atomically replaces the
/// target files after the input stream completes successfully.
/// </summary>
internal sealed class SamplerSplitWriter : IAsyncDisposable
{
    private readonly List<SplitTarget> _targets;
    private bool _committed;
    private bool _disposed;

    private SamplerSplitWriter(string directory, IReadOnlyList<SamplerSplitSpec> specs)
    {
        Directory.CreateDirectory(directory);
        _targets = specs.Select(spec =>
        {
            var finalPath = Path.Combine(directory, $"{spec.Name}.jsonl");
            var tempPath = finalPath + ".tmp";
            var writer = new StreamWriter(
                new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return new SplitTarget(finalPath, tempPath, writer);
        }).ToList();
    }

    public static SamplerSplitWriter Create(string directory, IReadOnlyList<SamplerSplitSpec> specs) =>
        new(Path.GetFullPath(directory), specs);

    public Task WriteAsync(int splitIndex, DatasetRow row, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return _targets[splitIndex].Writer.WriteLineAsync(row.Json);
    }

    public async Task CommitAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        foreach (var target in _targets)
        {
            await target.Writer.FlushAsync(ct);
        }

        await DisposeWritersAsync();

        foreach (var target in _targets)
        {
            File.Move(target.TempPath, target.FinalPath, overwrite: true);
        }

        _committed = true;
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeWritersAsync();
        if (_committed) return;

        foreach (var target in _targets)
        {
            TryDelete(target.TempPath);
        }
    }

    private async Task DisposeWritersAsync()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var target in _targets)
        {
            await target.Writer.DisposeAsync();
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record SplitTarget(string FinalPath, string TempPath, StreamWriter Writer);
}
