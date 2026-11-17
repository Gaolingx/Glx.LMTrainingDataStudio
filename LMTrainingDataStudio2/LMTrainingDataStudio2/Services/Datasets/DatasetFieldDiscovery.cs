using System.Text.Json;
using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Services.Datasets;

/// <summary>
/// Reads the first dataset row and returns its top-level field names.
/// Used by the SFT export field selector so typos surface before a run.
/// </summary>
public static class DatasetFieldDiscovery
{
    public static async Task<IReadOnlyList<string>> ReadTopLevelFieldsAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fileType = DatasetIO.DetectFileType(path);
        await using var reader = DatasetIO.CreateReader(fileType, new AppSettings());
        await reader.OpenAsync(path, cancellationToken);

        var page = await reader.ReadPageAsync(0, 1, cancellationToken);
        if (page.Count == 0)
        {
            return Array.Empty<string>();
        }

        using var document = JsonDocument.Parse(page[0].Json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return Array.Empty<string>();
        }

        return document.RootElement
            .EnumerateObject()
            .Select(property => property.Name)
            .ToArray();
    }
}
