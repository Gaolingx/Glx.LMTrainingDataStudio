namespace LMTrainingDataStudio2.Models;

/// <summary>
/// Supported dataset formats for SFT chat-style training data.
/// </summary>
public enum DatasetFormat
{
    /// <summary>Auto-detect format from row content.</summary>
    Auto,

    /// <summary>OpenAI-compatible messages[] format.</summary>
    OpenAI,

    /// <summary>ShareGPT/LLaMA-Factory conversations[] format.</summary>
    ShareGPT,

    /// <summary>Alpaca instruction/input/output format.</summary>
    Alpaca
}

/// <summary>
/// Supported file types for dataset IO.
/// </summary>
public enum DataFileType
{
    Jsonl,
    Csv,
    Parquet
}
