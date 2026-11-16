using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Services.Datasets;

/// <summary>
/// Creates readers/writers by file type and detects file types from extensions.
/// </summary>
public static class DatasetIO
{
    public static DataFileType DetectFileType(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jsonl" or ".ndjson" or ".json" => DataFileType.Jsonl,
            ".csv" => DataFileType.Csv,
            ".parquet" or ".pq" => DataFileType.Parquet,
            var extension => throw new DatasetFormatException(
                $"Unsupported dataset extension '{extension}' for '{path}'. Use .jsonl, .csv or .parquet.")
        };
    }

    public static IDatasetReader CreateReader(DataFileType fileType, AppSettings settings) => fileType switch
    {
        DataFileType.Jsonl => new JsonlDatasetReader(settings),
        DataFileType.Csv => new CsvDatasetReader(),
        DataFileType.Parquet => new ParquetDatasetReader(),
        _ => throw new ArgumentOutOfRangeException(nameof(fileType), fileType, "Unsupported dataset file type.")
    };

    public static IDatasetWriter CreateWriter(DataFileType fileType, string path) => fileType switch
    {
        DataFileType.Jsonl => new JsonlDatasetWriter(path),
        DataFileType.Csv => new CsvDatasetWriter(path),
        DataFileType.Parquet => new ParquetDatasetWriter(path),
        _ => throw new ArgumentOutOfRangeException(nameof(fileType), fileType, "Unsupported dataset file type.")
    };
}
