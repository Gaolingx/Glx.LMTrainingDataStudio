namespace LMTrainingDataStudio2.Services;

/// <summary>
/// Thrown when a recipe file cannot be parsed or does not match the supported schema.
/// Line and byte positions are zero-based, matching <see cref="System.Text.Json.JsonException"/>.
/// </summary>
public sealed class RecipeFormatException : Exception
{
    public RecipeFormatException(
        string filePath,
        string message,
        long? lineNumber = null,
        long? bytePositionInLine = null,
        Exception? innerException = null)
        : base(BuildMessage(filePath, message, lineNumber, bytePositionInLine), innerException)
    {
        FilePath = filePath;
        LineNumber = lineNumber;
        BytePositionInLine = bytePositionInLine;
    }

    public string FilePath { get; }
    public long? LineNumber { get; }
    public long? BytePositionInLine { get; }

    private static string BuildMessage(string filePath, string message, long? lineNumber, long? bytePositionInLine)
    {
        var location = lineNumber is null
            ? string.Empty
            : $" at line {lineNumber}, byte {bytePositionInLine}";

        return $"Failed to load recipe from '{filePath}'{location}: {message}";
    }
}
