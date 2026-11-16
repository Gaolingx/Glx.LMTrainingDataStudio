using System.Text.Json;
using Json.Schema;

namespace LMTrainingDataStudio2.Services.Executors;

/// <summary>
/// Parses JSON Schema documents and evaluates one JSON row at a time.
/// </summary>
internal static class JsonSchemaRuleEvaluator
{
    public static JsonSchema Parse(string schemaJson) => JsonSchema.FromText(schemaJson);

    /// <summary>
    /// Returns null when the row is valid, otherwise a human-readable reason.
    /// </summary>
    public static string? Validate(JsonSchema schema, string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return $"Invalid JSON: {ex.Message}";
        }

        using (document)
        {
            var results = schema.Evaluate(document.RootElement, new EvaluationOptions
            {
                OutputFormat = OutputFormat.List
            });

            if (results.IsValid)
            {
                return null;
            }

            var reasons = new List<string>();
            CollectErrors(results, reasons);
            return reasons.Count == 0
                ? "JSON Schema validation failed."
                : string.Join("; ", reasons.Take(3));
        }
    }

    private static void CollectErrors(EvaluationResults results, List<string> reasons)
    {
        if (results.Errors is { Count: > 0 } errors)
        {
            foreach (var error in errors)
            {
                reasons.Add($"{results.InstanceLocation}: {error.Value}");
            }
        }

        if (results.Details is null) return;
        foreach (var detail in results.Details)
        {
            CollectErrors(detail, reasons);
        }
    }
}