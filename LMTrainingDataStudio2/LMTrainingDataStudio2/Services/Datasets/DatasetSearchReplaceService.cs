using System.Diagnostics;
using System.Text.RegularExpressions;
using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Services.Datasets;

/// <summary>
/// Search scope: a row window (<see cref="StartRow"/>/<see cref="RowCount"/>, zero-based)
/// or the whole file when <see cref="RowCount"/> is negative.
/// </summary>
public sealed record DatasetSearchRequest(
    string Query,
    string Replacement,
    bool UseRegex,
    bool CaseSensitive,
    long StartRow = 0,
    long RowCount = -1);

public sealed record DatasetSearchMatch(long LineNumber, int MatchCount, string Snippet);

public sealed class DatasetSearchPreview
{
    public long ScannedRows { get; internal set; }
    public long MatchingRows { get; internal set; }
    public long TotalMatches { get; internal set; }
    public bool Truncated { get; internal set; }
    public List<DatasetSearchMatch> Matches { get; } = new();
    public TimeSpan Elapsed { get; internal set; }
}

public sealed record DatasetReplaceResult(
    long ScannedRows,
    long ChangedRows,
    long TotalMatches,
    string TargetPath,
    TimeSpan Elapsed);

/// <summary>
/// Streams a dataset row window, previews literal/regex matches and writes replacements
/// to a new target file. The source file is never modified.
/// </summary>
public sealed class DatasetSearchReplaceService
{
    private const int PageSize = 500;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    private readonly AppSettings _settings;

    public DatasetSearchReplaceService(AppSettings? settings = null)
    {
        _settings = settings ?? new AppSettings();
    }

    public async Task<DatasetSearchPreview> PreviewAsync(
        string sourcePath,
        DataFileType fileType,
        DatasetSearchRequest request,
        int maxMatches = 100,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var matcher = CreateMatcher(request);
        var stopwatch = Stopwatch.StartNew();
        var preview = new DatasetSearchPreview();

        await using var reader = DatasetIO.CreateReader(fileType, _settings);
        await reader.OpenAsync(sourcePath, cancellationToken);

        var (start, count) = ResolveRange(reader.TotalRows, request);
        var end = start + count;

        for (var offset = start; offset < end; offset += PageSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var take = (int)Math.Min(PageSize, end - offset);
            var page = await reader.ReadPageAsync(offset, take, cancellationToken);
            foreach (var row in page)
            {
                preview.ScannedRows++;
                var (matchCount, firstIndex, firstLength) = matcher.Count(row.Json);
                preview.TotalMatches += matchCount;
                if (matchCount == 0)
                {
                    continue;
                }

                preview.MatchingRows++;
                if (preview.Matches.Count < maxMatches)
                {
                    preview.Matches.Add(new DatasetSearchMatch(
                        row.LineNumber + 1,
                        matchCount,
                        BuildSnippet(row.Json, firstIndex, firstLength)));
                }
                else
                {
                    preview.Truncated = true;
                }
            }

            progress?.Report(count == 0 ? 1.0 : (double)preview.ScannedRows / count);
        }

        progress?.Report(1.0);
        preview.Elapsed = stopwatch.Elapsed;
        return preview;
    }

    public async Task<DatasetReplaceResult> ReplaceAsync(
        string sourcePath,
        DataFileType sourceFileType,
        string targetPath,
        DatasetSearchRequest request,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var matcher = CreateMatcher(request);
        var source = Path.GetFullPath(sourcePath);
        var target = Path.GetFullPath(targetPath);

        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Source and target paths must differ.", nameof(targetPath));
        }

        if (!File.Exists(source))
        {
            throw new FileNotFoundException($"Dataset file not found: '{source}'.", source);
        }

        var targetFileType = DatasetIO.DetectFileType(target);
        var stopwatch = Stopwatch.StartNew();

        await using var reader = DatasetIO.CreateReader(sourceFileType, _settings);
        await reader.OpenAsync(source, cancellationToken);
        await using var writer = DatasetIO.CreateWriter(targetFileType, target);

        var (start, count) = ResolveRange(reader.TotalRows, request);
        var end = start + count;
        long scanned = 0;
        long changed = 0;
        long totalMatches = 0;

        for (var offset = start; offset < end; offset += PageSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var take = (int)Math.Min(PageSize, end - offset);
            var page = await reader.ReadPageAsync(offset, take, cancellationToken);
            foreach (var row in page)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var (matchCount, replaced) = matcher.Replace(row.Json);
                totalMatches += matchCount;

                var json = replaced ?? row.Json;
                if (replaced is not null && !string.Equals(replaced, row.Json, StringComparison.Ordinal))
                {
                    changed++;
                }

                await writer.WriteRowAsync(new DatasetRow(row.LineNumber, json), cancellationToken);
                scanned++;
            }

            progress?.Report(count == 0 ? 1.0 : (double)scanned / count);
        }

        await writer.CompleteAsync(cancellationToken);
        progress?.Report(1.0);

        return new DatasetReplaceResult(
            scanned, changed, totalMatches, target, stopwatch.Elapsed);
    }

    private static (long Start, long Count) ResolveRange(long totalRows, DatasetSearchRequest request)
    {
        var start = Math.Clamp(request.StartRow, 0, totalRows);
        var available = totalRows - start;
        var count = request.RowCount < 0
            ? available
            : Math.Clamp(request.RowCount, 0, available);
        return (start, count);
    }

    private static Matcher CreateMatcher(DatasetSearchRequest request)
    {
        if (string.IsNullOrEmpty(request.Query))
        {
            throw new ArgumentException("Search query is required.", nameof(request));
        }

        if (!request.UseRegex)
        {
            return new LiteralMatcher(
                request.Query,
                request.Replacement,
                request.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
        }

        var options = RegexOptions.CultureInvariant;
        if (!request.CaseSensitive)
        {
            options |= RegexOptions.IgnoreCase;
        }

        try
        {
            return new RegexMatcher(new Regex(request.Query, options, RegexTimeout), request.Replacement);
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException($"Invalid regular expression: {ex.Message}", nameof(request), ex);
        }
    }

    private static string BuildSnippet(string text, int index, int length)
    {
        var start = Math.Max(0, index - 40);
        var end = Math.Min(text.Length, index + Math.Max(length, 1) + 40);
        var snippet = text[start..end].Replace('\r', ' ').Replace('\n', ' ');
        if (snippet.Length > 240)
        {
            snippet = snippet[..240] + "…";
        }

        return (start > 0 ? "…" : string.Empty) + snippet + (end < text.Length ? "…" : string.Empty);
    }

    private abstract class Matcher
    {
        public abstract (int Count, int FirstIndex, int FirstLength) Count(string text);

        public abstract (int Count, string? Replaced) Replace(string text);
    }

    private sealed class LiteralMatcher : Matcher
    {
        private readonly string _query;
        private readonly string _replacement;
        private readonly StringComparison _comparison;

        public LiteralMatcher(string query, string replacement, StringComparison comparison)
        {
            _query = query;
            _replacement = replacement;
            _comparison = comparison;
        }

        public override (int, int, int) Count(string text)
        {
            var count = 0;
            var firstIndex = -1;
            var index = 0;

            while (index <= text.Length - _query.Length)
            {
                var found = text.IndexOf(_query, index, _comparison);
                if (found < 0)
                {
                    break;
                }

                if (firstIndex < 0)
                {
                    firstIndex = found;
                }

                count++;
                index = found + Math.Max(1, _query.Length);
            }

            return (count, Math.Max(0, firstIndex), _query.Length);
        }

        public override (int, string?) Replace(string text)
        {
            var (count, _, _) = Count(text);
            return count == 0
                ? (0, null)
                : (count, text.Replace(_query, _replacement, _comparison));
        }
    }

    private sealed class RegexMatcher : Matcher
    {
        private readonly Regex _regex;
        private readonly string _replacement;

        public RegexMatcher(Regex regex, string replacement)
        {
            _regex = regex;
            _replacement = replacement;
        }

        public override (int, int, int) Count(string text)
        {
            try
            {
                var matches = _regex.Matches(text);
                return matches.Count == 0
                    ? (0, 0, 0)
                    : (matches.Count, matches[0].Index, matches[0].Length);
            }
            catch (RegexMatchTimeoutException)
            {
                throw new DatasetFormatException($"Regex timed out while scanning a row.");
            }
        }

        public override (int, string?) Replace(string text)
        {
            try
            {
                var (count, _, _) = Count(text);
                return count == 0
                    ? (0, null)
                    : (count, _regex.Replace(text, _replacement));
            }
            catch (RegexMatchTimeoutException)
            {
                throw new DatasetFormatException("Regex timed out while replacing a row.");
            }
        }
    }
}