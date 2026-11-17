namespace LMTrainingDataStudio2.Tests;

internal static class TestRepository
{
    public static string Root { get; } = FindRoot();

    public static string PathOf(params string[] segments) =>
        Path.Combine([Root, .. segments]);

    public static string ReadText(params string[] segments) =>
        File.ReadAllText(PathOf(segments));

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName,
                    "LMTrainingDataStudio2",
                    "LMTrainingDataStudio2.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Repository root not found above '{AppContext.BaseDirectory}'.");
    }
}
