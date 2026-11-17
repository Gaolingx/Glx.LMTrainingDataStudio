using System.Text.RegularExpressions;

namespace LMTrainingDataStudio2.Services;

/// <summary>One slice of text produced for search highlighting.</summary>
public readonly record struct HighlightSegment(string Text, bool IsMatch);

/// <summary>
/// Splits text into alternating plain/matched segments for literal or regex search.
/// Invalid patterns and timeouts degrade to "no highlight" instead of throwing.
/// </summary>
public static class HighlightMatcher
{
    public const int DefaultMaxSegments = 400;

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    public static IReadOnlyList<HighlightSegment> Split(
        string? text,
        string? query,
        bool useRegex = false,
        bool caseSensitive = false,
        int maxSegments = DefaultMaxSegments)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Array.Empty<HighlightSegment>();
        }

        if (string.IsNullOrEmpty(query))
        {
            return new[] { new HighlightSegment(text, false) };
        }

        return useRegex
            ? SplitRegex(text, query, caseSensitive, maxSegments)
            : SplitLiteral(text, query, caseSensitive, maxSegments);
    }

    public static bool HasMatch(string? text, string? query, bool useRegex = false, bool caseSensitive = false) =>
        Split(text, query, useRegex, caseSensitive, maxSegments: 1).Any(segment => segment.IsMatch);

    private static IReadOnlyList<HighlightSegment> SplitLiteral(
        string text,
        string query,
        bool caseSensitive,
        int maxSegments)
    {
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var segments = new List<HighlightSegment>();
        var index = 0;

        while (index < text.Length && query.Length > 0 && segments.Count < maxSegments)
        {
            var match = text.IndexOf(query, index, comparison);
            if (match < 0)
            {
                break;
            }

            if (match > index)
            {
                segments.Add(new HighlightSegment(text[index..match], false));
            }

            segments.Add(new HighlightSegment(text.Substring(match, query.Length), true));
            index = match + query.Length;
        }

        if (index < text.Length)
        {
            segments.Add(new HighlightSegment(text[index..], false));
        }

        return segments;
    }

    private static IReadOnlyList<HighlightSegment> SplitRegex(
        string text,
        string query,
        bool caseSensitive,
        int maxSegments)
    {
        Regex regex;
        try
        {
            var options = caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
            regex = new Regex(query, options, RegexTimeout);
        }
        catch (ArgumentException)
        {
            return new[] { new HighlightSegment(text, false) };
        }

        var segments = new List<HighlightSegment>();
        var index = 0;

        try
        {
            foreach (Match match in regex.Matches(text))
            {
                if (segments.Count >= maxSegments)
                {
                    break;
                }

                if (match.Length == 0)
                {
                    continue;
                }

                if (match.Index > index)
                {
                    segments.Add(new HighlightSegment(text[index..match.Index], false));
                }

                segments.Add(new HighlightSegment(match.Value, true));
                index = match.Index + match.Length;
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return new[] { new HighlightSegment(text, false) };
        }

        if (index < text.Length)
        {
            segments.Add(new HighlightSegment(text[index..], false));
        }

        return segments;
    }
}
