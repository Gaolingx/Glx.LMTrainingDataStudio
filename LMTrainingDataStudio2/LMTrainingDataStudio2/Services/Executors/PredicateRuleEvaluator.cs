using System.Globalization;
using System.Text.Json;

namespace LMTrainingDataStudio2.Services.Executors;

/// <summary>
/// Minimal validator rule grammar: <c>&lt;path&gt; &lt;op&gt; &lt;value&gt;</c> or <c>&lt;path&gt; exists</c>.
/// Supported operators: ==, !=, contains, &gt;, &gt;=, &lt;, &lt;=.
/// </summary>
internal static class PredicateRuleEvaluator
{
    public static bool Evaluate(string rule, JsonElement root)
    {
        var parts = rule.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 2 && parts[1].Equals("exists", StringComparison.OrdinalIgnoreCase))
        {
            return TemplateRenderer.ResolveString(root, parts[0]) is not null;
        }

        if (parts.Length != 3)
        {
            throw new InvalidOperationException(
                $"Unsupported validator rule '{rule}'. Expected '<path> <operator> <value>' or '<path> exists'.");
        }

        var left = TemplateRenderer.ResolveString(root, parts[0]);
        var op = parts[1];
        var right = Unquote(parts[2]);

        switch (op)
        {
            case "==":
                return string.Equals(left, right, StringComparison.Ordinal);
            case "!=":
                return !string.Equals(left, right, StringComparison.Ordinal);
            case "contains":
                return left is not null && left.Contains(right, StringComparison.Ordinal);
            case ">" or ">=" or "<" or "<=":
                if (left is null ||
                    !double.TryParse(left, NumberStyles.Float, CultureInfo.InvariantCulture, out var l) ||
                    !double.TryParse(right, NumberStyles.Float, CultureInfo.InvariantCulture, out var r))
                {
                    return false;
                }

                return op switch
                {
                    ">" => l > r,
                    ">=" => l >= r,
                    "<" => l < r,
                    _ => l <= r
                };
            default:
                throw new InvalidOperationException(
                    $"Unsupported validator operator '{op}' in rule '{rule}'.");
        }
    }

    private static string Unquote(string value) =>
        value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;
}
