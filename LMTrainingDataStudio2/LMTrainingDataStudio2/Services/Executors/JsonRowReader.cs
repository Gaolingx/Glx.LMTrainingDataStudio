using System.Text.Json;
using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Services.Executors;

internal static class JsonRowReader
{
    public static JsonDocument Parse(DatasetRow row, string blockName)
    {
        try
        {
            return JsonDocument.Parse(row.Json);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"Block '{blockName}' could not parse line {row.LineNumber}: {ex.Message}", ex);
        }
    }
}
