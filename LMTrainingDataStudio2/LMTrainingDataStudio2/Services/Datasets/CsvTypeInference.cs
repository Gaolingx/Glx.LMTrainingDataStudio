using System.Globalization;
using System.Text.Json;

namespace LMTrainingDataStudio2.Services.Datasets;

/// <summary>
/// Display-only type inference for CSV columns. CSV has no schema, so the type is
/// inferred from a bounded sample and never written back to the source.
/// </summary>
public static class CsvTypeInference
{
    public const int MaxSamples = 256;

    public static string Infer(IEnumerable<string> values)
    {
        var hasValue = false;
        var allBool = true;
        var allInteger = true;
        var allNumber = true;
        var allDate = true;
        var allJson = true;
        var samples = 0;

        foreach (var raw in values)
        {
            if (samples++ >= MaxSamples)
            {
                break;
            }

            var text = raw.Trim();
            if (text.Length == 0)
            {
                continue;
            }

            hasValue = true;
            allBool &= bool.TryParse(text, out _);
            allInteger &= long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
            allNumber &= double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
            allDate &= LooksLikeDate(text);
            allJson &= LooksLikeJson(text);
        }

        if (!hasValue)
        {
            return "empty";
        }

        if (allBool)
        {
            return "bool";
        }

        if (allInteger)
        {
            return "integer";
        }

        if (allNumber)
        {
            return "number";
        }

        if (allDate)
        {
            return "date";
        }

        return allJson ? "json" : "string";
    }

    private static bool LooksLikeDate(string text) =>
        text.Length >= 8 &&
        (text.Contains('-', StringComparison.Ordinal) || text.Contains('/', StringComparison.Ordinal)) &&
        DateTimeOffset.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces,
            out _);

    private static bool LooksLikeJson(string text)
    {
        if (text.Length < 2 || (text[0] != '{' && text[0] != '['))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
