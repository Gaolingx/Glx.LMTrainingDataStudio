using System.Globalization;

namespace LMTrainingDataStudio2.Services.Executors;

internal sealed record SamplerSplitSpec(string Name, double Ratio, long Count, bool IsCount);

/// <summary>
/// Parses sampler split specifications. Each entry uses <c>name:ratio</c>
/// (for example <c>train:0.8</c>) or <c>name:count</c> (for example <c>valid:100</c>).
/// Ratio mode assigns every row to exactly one split; count mode fills splits in order
/// and gives the remaining rows to the last split.
/// </summary>
internal sealed class SamplerSplitPlan
{
    private readonly IReadOnlyList<SamplerSplitSpec> _specs;
    private readonly double _totalRatio;

    private SamplerSplitPlan(IReadOnlyList<SamplerSplitSpec> specs, bool isCountMode)
    {
        _specs = specs;
        IsCountMode = isCountMode;
        _totalRatio = isCountMode ? 0 : specs.Sum(spec => spec.Ratio);
    }

    public bool IsCountMode { get; }

    public IReadOnlyList<SamplerSplitSpec> Specs => _specs;

    public static bool TryParse(
        IReadOnlyList<string> splits,
        out SamplerSplitPlan? plan,
        out string? error)
    {
        plan = null;
        error = null;

        var specs = new List<SamplerSplitSpec>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hasCounts = false;
        var hasRatios = false;

        foreach (var raw in splits)
        {
            var entry = raw?.Trim();
            if (string.IsNullOrWhiteSpace(entry))
            {
                continue;
            }

            var separator = entry.LastIndexOf(':');
            if (separator <= 0 || separator == entry.Length - 1)
            {
                error = $"Split '{entry}' must use 'name:ratio' or 'name:count'.";
                return false;
            }

            var name = entry[..separator].Trim();
            var valueText = entry[(separator + 1)..].Trim();
            if (name is "." or ".." ||
                name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                name.Contains('/') ||
                name.Contains('\\'))
            {
                error = $"Split name '{name}' is not a valid file name.";
                return false;
            }

            if (!names.Add(name))
            {
                error = $"Duplicate split name '{name}'.";
                return false;
            }

            if (valueText.Contains('.'))
            {
                if (!double.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out var ratio) ||
                    ratio <= 0)
                {
                    error = $"Split '{name}' has invalid ratio '{valueText}'; ratios must be greater than 0.";
                    return false;
                }

                hasRatios = true;
                specs.Add(new SamplerSplitSpec(name, ratio, 0, false));
            }
            else
            {
                if (!long.TryParse(valueText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) ||
                    count <= 0)
                {
                    error = $"Split '{name}' has invalid count '{valueText}'; counts must be positive integers.";
                    return false;
                }

                hasCounts = true;
                specs.Add(new SamplerSplitSpec(name, 0, count, true));
            }
        }

        if (specs.Count == 0)
        {
            error = "At least one split is required.";
            return false;
        }

        if (hasCounts && hasRatios)
        {
            error = "Split specifications cannot mix ratios and counts.";
            return false;
        }

        plan = new SamplerSplitPlan(specs, hasCounts);
        return true;
    }

    public int Assign(long rowIndex, Random random)
    {
        if (IsCountMode)
        {
            var remaining = rowIndex;
            for (var i = 0; i < _specs.Count - 1; i++)
            {
                if (remaining < _specs[i].Count)
                {
                    return i;
                }

                remaining -= _specs[i].Count;
            }

            return _specs.Count - 1;
        }

        var roll = random.NextDouble() * _totalRatio;
        var cumulative = 0d;
        for (var i = 0; i < _specs.Count; i++)
        {
            cumulative += _specs[i].Ratio;
            if (roll < cumulative)
            {
                return i;
            }
        }

        return _specs.Count - 1;
    }
}