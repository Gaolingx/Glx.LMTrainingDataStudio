using System.Text;

namespace LMTrainingDataStudio2.Services;

/// <summary>
/// Thread-safe rolling file log for application diagnostics.
/// </summary>
public static class AppLog
{
    private static readonly object Gate = new();
    private static string _directory = GetDefaultDirectory();
    private static long _maxFileBytes = 2 * 1024 * 1024;
    private static int _maxFiles = 5;
    private static string _filePath = Path.Combine(_directory, "app.log");

    public static string LogDirectory => _directory;

    public static string FilePath => _filePath;

    public static string GetDefaultDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LMTrainingDataStudio",
        "logs");

    public static void Initialize(
        string? directory = null,
        long maxFileBytes = 2 * 1024 * 1024,
        int maxFiles = 5)
    {
        lock (Gate)
        {
            _directory = string.IsNullOrWhiteSpace(directory)
                ? GetDefaultDirectory()
                : Path.GetFullPath(directory);
            _maxFileBytes = Math.Max(1024, maxFileBytes);
            _maxFiles = Math.Max(1, maxFiles);
            _filePath = Path.Combine(_directory, "app.log");
            Directory.CreateDirectory(_directory);
            AppDirectorySecurity.TryRestrictToCurrentUser(_directory);
        }
    }

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warning(string message) => Write("WARN", message, null);

    public static void Error(string message, Exception? exception = null) =>
        Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        var builder = new StringBuilder()
            .Append(DateTimeOffset.UtcNow.ToString("O"))
            .Append(" [")
            .Append(level)
            .Append("] ")
            .Append(message);

        if (exception is not null)
        {
            builder.AppendLine().Append(exception);
        }

        var line = builder.ToString();

        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(_directory);
                RotateIfNeeded(line.Length * 2L);
                File.AppendAllText(_filePath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Logging must never take the application down.
            }
        }
    }

    private static void RotateIfNeeded(long incomingBytes)
    {
        if (!File.Exists(_filePath) || new FileInfo(_filePath).Length + incomingBytes <= _maxFileBytes)
        {
            return;
        }

        for (var index = _maxFiles - 1; index >= 1; index--)
        {
            var source = index == 1
                ? _filePath
                : Path.Combine(_directory, $"app.{index - 1}.log");
            var destination = Path.Combine(_directory, $"app.{index}.log");
            if (File.Exists(source))
            {
                File.Move(source, destination, overwrite: true);
            }
        }
    }
}
