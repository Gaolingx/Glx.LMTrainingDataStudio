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
    Alpaca,

    /// <summary>Direct preference optimization prompt/chosen/rejected format.</summary>
    Dpo,

    /// <summary>Plain-text pretraining corpus with a top-level text field.</summary>
    Pretrain
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
