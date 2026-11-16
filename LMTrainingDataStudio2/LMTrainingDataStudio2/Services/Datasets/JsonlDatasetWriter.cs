using System.Text;
using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Services.Datasets;

/// <summary>
/// Writes UTF-8 JSONL through a temp file and atomically replaces the target on completion.
/// </summary>
public sealed class JsonlDatasetWriter : IDatasetWriter
{
    private readonly string _targetPath;
    private readonly string _tempPath;
    private readonly StreamWriter _writer;
    private bool _completed;

    public JsonlDatasetWriter(string targetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        _targetPath = Path.GetFullPath(targetPath);
        var directory = Path.GetDirectoryName(_targetPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _tempPath = _targetPath + ".tmp-" + Guid.NewGuid().ToString("N");
        _writer = new StreamWriter(
            new FileStream(_tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public async Task WriteRowAsync(DatasetRow row, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_completed, this);
        await _writer.WriteLineAsync(row.Json.AsMemory(), cancellationToken);
    }

    public async Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        if (_completed) return;

        await _writer.FlushAsync(cancellationToken);
        await _writer.DisposeAsync();
        File.Move(_tempPath, _targetPath, overwrite: true);
        _completed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_completed) return;

        await _writer.DisposeAsync();
        TryDelete(_tempPath);
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
}
