using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LMTrainingDataStudio2.Services.Datasets;

public enum DatasetFilterOperator
{
    Contains,
    NotContains,
    Equals,
    NotEquals,
    GreaterThan,
    GreaterOrEqual,
    LessThan,
    LessOrEqual,
    IsEmpty,
    IsNotEmpty,
    MatchesRegex
}

/// <summary>
/// Column filter and/or sort applied to a whole dataset. The spec is immutable; callers build a
/// new one from the previous one when toggling sort direction or adding a filter.
/// </summary>
public sealed record DatasetViewSpec(
    string? FilterField = null,
    DatasetFilterOperator FilterOperator = DatasetFilterOperator.Contains,
    string FilterValue = "",
    bool FilterCaseSensitive = false,
    string? SortField = null,
    bool SortDescending = false)
{
    public bool HasFilter => !string.IsNullOrWhiteSpace(FilterField);

    public bool HasSort => !string.IsNullOrWhiteSpace(SortField);

    public bool IsActive => HasFilter || HasSort;
}

/// <summary>
/// Matched rows of a dataset in view order. Read-only after construction.
/// </summary>
public sealed class DatasetViewIndex
{
    private readonly long[] _sourceRows;

    public DatasetViewIndex(
        long[] sourceRows,
        IReadOnlyList<string> fieldNames,
        bool isSorted)
    {
        ArgumentNullException.ThrowIfNull(sourceRows);
        ArgumentNullException.ThrowIfNull(fieldNames);
        _sourceRows = sourceRows;
        FieldNames = fieldNames;
        IsSorted = isSorted;
    }

    public long Count => _sourceRows.Length;

    public bool IsSorted { get; }

    /// <summary>Top-level field names discovered while scanning, in first-seen order.</summary>
    public IReadOnlyList<string> FieldNames { get; }

    public IReadOnlyList<long> SourceRows => _sourceRows;

    public long GetSourceRow(long position)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(position, _sourceRows.LongLength);
        return _sourceRows[position];
    }

    /// <summary>Finds the view position of a zero-based source row; the view is not indexed by source order.</summary>
    public bool TryGetPosition(long sourceRow, out long position)
    {
        for (var index = 0; index < _sourceRows.Length; index++)
        {
            if (_sourceRows[index] == sourceRow)
            {
                position = index;
                return true;
            }
        }

        position = -1;
        return false;
    }

    public long[] GetSourceRows(long startRow, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startRow);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        if (count == 0 || startRow >= _sourceRows.Length)
        {
            return Array.Empty<long>();
        }

        var length = (int)Math.Min(count, _sourceRows.Length - startRow);
        var page = new long[length];
        Array.Copy(_sourceRows, startRow, page, 0, length);
        return page;
    }
}

/// <summary>Raised when a view would need more in-memory rows than the workbench allows.</summary>
public sealed class DatasetViewLimitException : Exception
{
    public DatasetViewLimitException(long limit)
        : base($"The view matched more than {limit:N0} rows; narrow the filter and try again.")
    {
        Limit = limit;
    }

    public long Limit { get; }
}

/// <summary>
/// Streams a dataset once to build a filtered, optionally sorted row index. The index stores only
/// source row numbers, so it stays usable for large files up to <see cref="MaxMatchedRows"/>.
/// </summary>
public static class DatasetViewBuilder
{
    public const int ChunkRows = 4096;
    public const int MaxMatchedRows = 1_000_000;
    public const int MaxDiscoveredFields = 128;

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    public static async Task<DatasetViewIndex> BuildAsync(
        IDatasetReader reader,
        long sourceTotalRows,
        DatasetViewSpec spec,
        IProgress<double>? progress = null,
        long maxMatchedRows = MaxMatchedRows,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxMatchedRows);
        if (!spec.IsActive)
        {
            throw new ArgumentException("The view spec must contain a filter or a sort.", nameof(spec));
        }

        var regex = CreateRegex(spec);
        var capacity = (int)Math.Min(sourceTotalRows, 65536);
        var matched = new List<long>(capacity);
        var keys = spec.HasSort ? new List<string>(capacity) : null;
        var allNumeric = spec.HasSort;
        var anyNumeric = false;
        var fieldNames = new List<string>();
        var seenFields = new HashSet<string>(StringComparer.Ordinal);

        for (long start = 0; start < sourceTotalRows; start += ChunkRows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = (int)Math.Min(ChunkRows, sourceTotalRows - start);
            var rows = await reader.ReadPageAsync(start, count, cancellationToken);
            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var document = TryParse(row.Json);
                var root = document is null ? (JsonElement?)null : document.RootElement;
                var isObject = root is { ValueKind: JsonValueKind.Object };

                if (isObject && seenFields.Count < MaxDiscoveredFields)
                {
                    foreach (var property in root!.Value.EnumerateObject())
                    {
                        if (property.Name.Length == 0 || seenFields.Count >= MaxDiscoveredFields)
                        {
                            continue;
                        }

                        if (seenFields.Add(property.Name))
                        {
                            fieldNames.Add(property.Name);
                        }
                    }
                }

                if (spec.HasFilter && !Matches(root, isObject, spec, regex))
                {
                    continue;
                }

                if (matched.Count >= maxMatchedRows)
                {
                    throw new DatasetViewLimitException(maxMatchedRows);
                }

                matched.Add(row.LineNumber);
                if (keys is not null)
                {
                    var key = isObject ? DatasetJsonFields.ReadValueText(root!.Value, spec.SortField!) : string.Empty;
                    keys.Add(key);
                    if (!TryParseNumber(key, out _))
                    {
                        allNumeric = false;
                    }
                    else
                    {
                        anyNumeric = true;
                    }
                }
            }

            progress?.Report(sourceTotalRows == 0
                ? 1
                : Math.Min(1, (start + rows.Count) / (double)sourceTotalRows));
        }

        progress?.Report(1);
        var sourceRows = matched.ToArray();
        if (spec.HasSort && sourceRows.Length > 1)
        {
            Sort(sourceRows, keys!, allNumeric && anyNumeric, spec.SortDescending);
        }

        return new DatasetViewIndex(sourceRows, fieldNames, spec.HasSort);
    }

    private static void Sort(long[] sourceRows, List<string> keys, bool numeric, bool descending)
    {
        var order = Enumerable.Range(0, sourceRows.Length).ToArray();
        if (numeric)
        {
            var values = new double[keys.Count];
            for (var i = 0; i < keys.Count; i++)
            {
                if (!TryParseNumber(keys[i], out values[i]))
                {
                    values[i] = double.NaN;
                }
            }

            Array.Sort(order, (left, right) =>
            {
                var comparison = values[left].CompareTo(values[right]);
                if (comparison == 0)
                {
                    return sourceRows[left].CompareTo(sourceRows[right]);
                }

                return descending ? -comparison : comparison;
            });
        }
        else
        {
            Array.Sort(order, (left, right) =>
            {
                var comparison = string.Compare(keys[left], keys[right], StringComparison.OrdinalIgnoreCase);
                if (comparison == 0)
                {
                    return sourceRows[left].CompareTo(sourceRows[right]);
                }

                return descending ? -comparison : comparison;
            });
        }

        var sorted = new long[sourceRows.Length];
        for (var i = 0; i < order.Length; i++)
        {
            sorted[i] = sourceRows[order[i]];
        }

        Array.Copy(sorted, sourceRows, sorted.Length);
    }

    private static bool Matches(
        JsonElement? root,
        bool isObject,
        DatasetViewSpec spec,
        Regex? regex)
    {
        var text = isObject
            ? DatasetJsonFields.ReadValueText(root!.Value, spec.FilterField!)
            : string.Empty;
        var comparison = spec.FilterCaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        return spec.FilterOperator switch
        {
            DatasetFilterOperator.Contains => text.Contains(spec.FilterValue, comparison),
            DatasetFilterOperator.NotContains => !text.Contains(spec.FilterValue, comparison),
            DatasetFilterOperator.Equals => text.Equals(spec.FilterValue, comparison),
            DatasetFilterOperator.NotEquals => !text.Equals(spec.FilterValue, comparison),
            DatasetFilterOperator.GreaterThan => Compare(text, spec.FilterValue, comparison) > 0,
            DatasetFilterOperator.GreaterOrEqual => Compare(text, spec.FilterValue, comparison) >= 0,
            DatasetFilterOperator.LessThan => Compare(text, spec.FilterValue, comparison) < 0,
            DatasetFilterOperator.LessOrEqual => Compare(text, spec.FilterValue, comparison) <= 0,
            DatasetFilterOperator.IsEmpty => text.Length == 0,
            DatasetFilterOperator.IsNotEmpty => text.Length > 0,
            DatasetFilterOperator.MatchesRegex => regex?.IsMatch(text) == true,
            _ => throw new ArgumentOutOfRangeException(nameof(spec), spec.FilterOperator, "Unknown filter operator.")
        };
    }

    private static int Compare(string left, string right, StringComparison comparison)
    {
        if (TryParseNumber(left, out var leftNumber) && TryParseNumber(right, out var rightNumber))
        {
            return leftNumber.CompareTo(rightNumber);
        }

        return string.Compare(left, right, comparison);
    }

    private static bool TryParseNumber(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static Regex? CreateRegex(DatasetViewSpec spec)
    {
        if (!spec.HasFilter || spec.FilterOperator != DatasetFilterOperator.MatchesRegex)
        {
            return null;
        }

        var options = RegexOptions.CultureInvariant;
        if (!spec.FilterCaseSensitive)
        {
            options |= RegexOptions.IgnoreCase;
        }

        try
        {
            return new Regex(spec.FilterValue, options, RegexTimeout);
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException($"Invalid regular expression: {ex.Message}", nameof(spec), ex);
        }
    }

    private static JsonDocument? TryParse(string json)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public readonly record struct DatasetFieldText(string Name, string Text);

/// <summary>Extracts top-level JSON object fields as display text.</summary>
public static class DatasetJsonFields
{
    public static bool TryRead(string json, out IReadOnlyList<DatasetFieldText> fields)
    {
        fields = Array.Empty<DatasetFieldText>();
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return false;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var values = new List<DatasetFieldText>();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                values.Add(new DatasetFieldText(property.Name, ValueText(property.Value)));
            }

            fields = values;
            return true;
        }
    }

    public static string ReadValueText(JsonElement root, string field)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(field, out var value))
        {
            return string.Empty;
        }

        return ValueText(value);
    }

    private static string ValueText(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
        _ => value.GetRawText()
    };
}
