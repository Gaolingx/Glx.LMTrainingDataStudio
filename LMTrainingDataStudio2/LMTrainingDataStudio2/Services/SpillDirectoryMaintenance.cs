namespace LMTrainingDataStudio2.Services;

/// <summary>
/// Removes run-scoped spill directories left behind by a crashed or killed process.
/// A normal run deletes its own <c>&lt;runId&gt;-&lt;guid&gt;</c> directory in a finally
/// block, so anything still present at startup is stale. Directories that cannot be
/// removed (for example because another process holds a file open) are skipped and
/// retried on the next startup.
/// </summary>
public static class SpillDirectoryMaintenance
{
    public static int Cleanup(string? spillDirectory)
    {
        var root = ResolveRoot(spillDirectory);
        if (root is null || !Directory.Exists(root))
        {
            return 0;
        }

        string[] directories;
        try
        {
            directories = Directory.GetDirectories(root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }

        var removed = 0;
        foreach (var directory in directories)
        {
            try
            {
                Directory.Delete(directory, recursive: true);
                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return removed;
    }

    private static string? ResolveRoot(string? spillDirectory)
    {
        var value = string.IsNullOrWhiteSpace(spillDirectory)
            ? RecipeRunOptions.DefaultSpillDirectory
            : spillDirectory.Trim();
        try
        {
            return Path.GetFullPath(value);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
