namespace LMTrainingDataStudio2.Services;

/// <summary>
/// Enforces a size quota on resume checkpoints kept under the run output directory.
/// The oldest checkpoint directories are removed first; run summaries and logs are left intact.
/// </summary>
public static class CheckpointQuota
{
    private const string CheckpointDirectoryName = "nodes";

    public static CheckpointQuotaResult Enforce(
        string? runsDirectory,
        long quotaBytes,
        string? protectedRunDirectory = null)
    {
        if (quotaBytes <= 0 || string.IsNullOrWhiteSpace(runsDirectory))
        {
            return default;
        }

        string root;
        try
        {
            root = Path.GetFullPath(runsDirectory);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return default;
        }

        if (!Directory.Exists(root))
        {
            return default;
        }

        var entries = new List<CheckpointEntry>();
        try
        {
            foreach (var runDirectory in Directory.EnumerateDirectories(root))
            {
                var checkpointDirectory = Path.Combine(runDirectory, CheckpointDirectoryName);
                if (!Directory.Exists(checkpointDirectory))
                {
                    continue;
                }

                if (protectedRunDirectory is not null &&
                    IsSameDirectory(runDirectory, protectedRunDirectory))
                {
                    continue;
                }

                entries.Add(new CheckpointEntry(
                    checkpointDirectory,
                    Measure(checkpointDirectory),
                    Directory.GetLastWriteTimeUtc(checkpointDirectory)));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return default;
        }

        var total = entries.Sum(entry => entry.Size);
        if (total <= quotaBytes)
        {
            return default;
        }

        var deleted = 0;
        var freed = 0L;
        foreach (var entry in entries.OrderBy(entry => entry.LastWrite))
        {
            if (total <= quotaBytes)
            {
                break;
            }

            try
            {
                Directory.Delete(entry.Directory, recursive: true);
                total -= entry.Size;
                freed += entry.Size;
                deleted++;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return new CheckpointQuotaResult(deleted, freed);
    }

    private static long Measure(string directory)
    {
        var total = 0L;
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                try
                {
                    total += new FileInfo(file).Length;
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return total;
    }

    private static bool IsSameDirectory(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private readonly record struct CheckpointEntry(string Directory, long Size, DateTime LastWrite);
}

public readonly record struct CheckpointQuotaResult(int DeletedCheckpoints, long FreedBytes);
