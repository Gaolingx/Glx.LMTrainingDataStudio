using System.Text;

namespace LMTrainingDataStudio2.Services.Executors;

/// <summary>
/// Writes SFT export rows to one or more temporary files and atomically replaces the
/// targets after the input stream completes. Append mode copies the existing file first
/// so a failed run never damages it.
/// </summary>
internal sealed class SftExportWriter : IAsyncDisposable
{
    private readonly string _fullPath;
    private readonly int _splitMaxRows;
    private readonly SamplerSplitPlan? _splitPlan;
    private readonly Random? _splitRandom;
    private readonly List<PartTarget> _parts = new();
    private readonly List<SplitTarget> _splitTargets = new();
    private StreamWriter? _writer;
    private int _rowsInPart;
    private long _splitRowIndex;
    private bool _needsLeadingNewline;
    private bool _splitWritersDisposed;
    private bool _committed;
    private bool _disposed;

    private SftExportWriter(
        string fullPath,
        int splitMaxRows,
        SamplerSplitPlan? splitPlan = null,
        Random? splitRandom = null)
    {
        _fullPath = fullPath;
        _splitMaxRows = splitMaxRows;
        _splitPlan = splitPlan;
        _splitRandom = splitRandom;
    }

    public IReadOnlyList<string> PartPaths => _splitPlan is not null
        ? _splitTargets.Select(part => part.FinalPath).ToArray()
        : _parts.Select(part => part.FinalPath).ToArray();

    public static SftExportWriter Create(
        string path,
        bool append,
        int splitMaxRows,
        string[]? splits = null,
        int splitSeed = 42)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (splitMaxRows < 0)
        {
            throw new InvalidOperationException("SFT export split size cannot be negative.");
        }

        var ratioSplits = splits?
            .Where(split => !string.IsNullOrWhiteSpace(split))
            .ToArray() ?? Array.Empty<string>();

        if (ratioSplits.Length > 0)
        {
            if (append)
            {
                throw new InvalidOperationException(
                    "SFT export cannot combine append mode with ratio splits.");
            }

            if (splitMaxRows > 0)
            {
                throw new InvalidOperationException(
                    "SFT export cannot combine row-count splitting with ratio splits.");
            }

            if (!SamplerSplitPlan.TryParse(ratioSplits, out var plan, out var error))
            {
                throw new InvalidOperationException($"SFT export has invalid ratio splits: {error}");
            }

            if (plan!.IsCountMode || plan.Specs.Any(spec => spec.IsCount))
            {
                throw new InvalidOperationException(
                    "SFT export ratio splits must use 'name:ratio' entries.");
            }

            var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
            var writer = new SftExportWriter(
                Path.GetFullPath(path),
                splitMaxRows: 0,
                plan,
                new Random(splitSeed));
            writer.OpenSplits(directory);
            return writer;
        }

        if (append && splitMaxRows > 0)
        {
            throw new InvalidOperationException("SFT export cannot combine append mode with file splitting.");
        }

        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        var singleWriter = new SftExportWriter(fullPath, splitMaxRows);
        if (splitMaxRows > 0)
        {
            singleWriter.OpenPart(index: 0);
        }
        else
        {
            singleWriter.OpenSingle(append);
        }

        return singleWriter;
    }

    public Task WriteAsync(string line, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_splitPlan is not null)
        {
            var split = _splitPlan.Assign(_splitRowIndex++, _splitRandom!);
            return _splitTargets[split].Writer.WriteLineAsync(line.AsMemory(), cancellationToken);
        }

        if (_writer is null)
        {
            throw new InvalidOperationException("The SFT export writer is not open.");
        }

        return WriteCoreAsync(line, cancellationToken);
    }

    private async Task WriteCoreAsync(string line, CancellationToken cancellationToken)
    {
        if (_splitMaxRows > 0 && _rowsInPart >= _splitMaxRows)
        {
            await CloseWriterAsync();
            OpenPart(_parts.Count);
        }

        if (_needsLeadingNewline)
        {
            await _writer!.WriteAsync("\n".AsMemory(), cancellationToken);
            _needsLeadingNewline = false;
        }

        await _writer!.WriteLineAsync(line.AsMemory(), cancellationToken);
        _rowsInPart++;
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (_splitPlan is not null)
        {
            foreach (var target in _splitTargets)
            {
                await target.Writer.FlushAsync(cancellationToken);
            }

            await DisposeSplitWritersAsync();

            foreach (var target in _splitTargets)
            {
                File.Move(target.TempPath, target.FinalPath, overwrite: true);
            }

            _committed = true;
            return;
        }

        if (_writer is not null)
        {
            await _writer.FlushAsync(cancellationToken);
        }

        await CloseWriterAsync();

        foreach (var part in _parts)
        {
            File.Move(part.TempPath, part.FinalPath, overwrite: true);
        }

        _committed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await CloseWriterAsync();
        await DisposeSplitWritersAsync();

        if (_committed) return;

        foreach (var part in _parts)
        {
            TryDelete(part.TempPath);
        }

        foreach (var part in _splitTargets)
        {
            TryDelete(part.TempPath);
        }
    }

    private void OpenSingle(bool append)
    {
        var tempPath = _fullPath + ".tmp";
        _parts.Add(new PartTarget(_fullPath, tempPath));

        if (append && File.Exists(_fullPath))
        {
            File.Copy(_fullPath, tempPath, overwrite: true);
            _needsLeadingNewline = Length(_fullPath) > 0 && !EndsWithNewline(_fullPath);
            _writer = CreateWriter(tempPath, FileMode.Append);
        }
        else
        {
            _writer = CreateWriter(tempPath, FileMode.Create);
        }

        _rowsInPart = 0;
    }

    private void OpenPart(int index)
    {
        var finalPath = BuildPartPath(_fullPath, index + 1);
        var tempPath = finalPath + ".tmp";
        _parts.Add(new PartTarget(finalPath, tempPath));
        _writer = CreateWriter(tempPath, FileMode.Create);
        _rowsInPart = 0;
    }

    private void OpenSplits(string directory)
    {
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var spec in _splitPlan!.Specs)
            {
                var finalPath = Path.Combine(directory, $"{spec.Name}.jsonl");
                var tempPath = finalPath + ".tmp";
                var writer = CreateWriter(tempPath, FileMode.Create);
                _splitTargets.Add(new SplitTarget(finalPath, tempPath, writer));
            }
        }
        catch
        {
            foreach (var target in _splitTargets)
            {
                target.Writer.Dispose();
                TryDelete(target.TempPath);
            }

            _splitTargets.Clear();
            throw;
        }
    }

    private static StreamWriter CreateWriter(string path, FileMode mode) =>
        new(
            new FileStream(path, mode, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    private async Task CloseWriterAsync()
    {
        if (_writer is null) return;

        await _writer.DisposeAsync();
        _writer = null;
    }

    private async Task DisposeSplitWritersAsync()
    {
        if (_splitWritersDisposed) return;
        _splitWritersDisposed = true;

        foreach (var target in _splitTargets)
        {
            await target.Writer.DisposeAsync();
        }
    }

    internal static string BuildPartPath(string fullPath, int partNumber)
    {
        var directory = Path.GetDirectoryName(fullPath) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(fullPath);
        var extension = Path.GetExtension(fullPath);
        return Path.Combine(directory, $"{name}.part{partNumber:000}{extension}");
    }

    private static long Length(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (IOException)
        {
            return 0;
        }
    }

    private static bool EndsWithNewline(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length == 0) return false;

            stream.Seek(-1, SeekOrigin.End);
            return stream.ReadByte() == '\n';
        }
        catch (IOException)
        {
            return false;
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

    private sealed record PartTarget(string FinalPath, string TempPath);

    private sealed record SplitTarget(string FinalPath, string TempPath, StreamWriter Writer);
}
