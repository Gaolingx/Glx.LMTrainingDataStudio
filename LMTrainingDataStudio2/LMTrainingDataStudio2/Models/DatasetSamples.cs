using System.Text.Json.Nodes;

namespace LMTrainingDataStudio2.Models;

/// <summary>
/// Canonical DPO row. Prompt may be a string or a messages array; chosen/rejected keep their original shape.
/// </summary>
public sealed record DpoSample(JsonNode? Prompt, JsonNode? Chosen, JsonNode? Rejected);

/// <summary>
/// Canonical pretraining row.
/// </summary>
public sealed record PretrainSample(string Text);
