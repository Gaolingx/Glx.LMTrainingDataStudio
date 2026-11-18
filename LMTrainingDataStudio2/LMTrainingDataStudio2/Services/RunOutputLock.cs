using System.Text;

namespace LMTrainingDataStudio2.Services;

/// <summary>
/// Cross-process advisory lock on recipe output files. Each target gets a sibling
/// <c>.lmtds.lock</c> file opened with <see cref="FileShare.None"/> for the whole run,
/// so a second process writing the same path is rejected before execution starts.
/// Locks are acquired in ordinal order to avoid deadlocks between overlapping runs.
/// </summary>
public sealed class RunOutputLock : IDisposable
{
    public const string LockFileSuffix = ".lmtds.lock";

    private readonly List<LockHandle> _handles = new();

    private RunOutputLock()
    {
    }

    /// <summary>Targets that are currently locked by this instance.</summary>
    public IReadOnlyList<string> Targets =>
        _handles.Select(handle => handle.Target).ToList();

    /// <summary>
    /// Acquires every distinct target. Returns <c>null</c> and releases the partial
    /// acquisition when any target is already locked by another process.
    /// </summary>
    public static RunOutputLock? TryAcquire(
        IEnumerable<string> targets,
        string owner,
        out IReadOnlyList<string> conflicts)
    {
        var normalized = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in targets)
        {
            var path = Resolve(candidate);
            if (path is not null)
            {
                normalized.Add(path);
            }
        }

        var handle = new RunOutputLock();
        var blocked = new List<string>();
        foreach (var target in normalized)
        {
            var conflict = handle.TryLock(target, owner);
            if (conflict is null)
            {
                continue;
            }

            blocked.Add(conflict);
        }

        if (blocked.Count == 0)
        {
            conflicts = Array.Empty<string>();
            return handle;
        }

        handle.Dispose();
        conflicts = blocked;
        return null;
    }

    public void Dispose()
    {
        foreach (var handle in _handles)
        {
            handle.Dispose();
        }

        _handles.Clear();
    }

    private string? TryLock(string target, string owner)
    {
        var lockPath = target + LockFileSuffix;
        try
        {
            var directory = Path.GetDirectoryName(lockPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var stream = new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
            try
            {
                WriteOwner(stream, target, owner);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The lock is what matters; failing to record the owner is not fatal.
            }

            _handles.Add(new LockHandle(target, lockPath, stream));
            return null;
        }
        catch (IOException)
        {
            return target;
        }
        catch (UnauthorizedAccessException ex)
        {
            return $"{target} ({ex.Message})";
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return $"{target} ({ex.Message})";
        }
    }

    private static void WriteOwner(FileStream stream, string target, string owner)
    {
        var content = Encoding.UTF8.GetBytes(
            $"pid={Environment.ProcessId}{Environment.NewLine}" +
            $"owner={owner}{Environment.NewLine}" +
            $"target={target}{Environment.NewLine}" +
            $"started={DateTimeOffset.UtcNow:O}{Environment.NewLine}");
        stream.SetLength(0);
        stream.Write(content, 0, content.Length);
        stream.Flush();
    }

    private static string? Resolve(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private sealed class LockHandle(string target, string lockPath, FileStream stream) : IDisposable
    {
        public string Target { get; } = target;

        private string LockPath { get; } = lockPath;

        private FileStream Stream { get; } = stream;

        public void Dispose()
        {
            Stream.Dispose();
            try
            {
                File.Delete(LockPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
