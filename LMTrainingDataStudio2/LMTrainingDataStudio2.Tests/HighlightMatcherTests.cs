using LMTrainingDataStudio2.Services;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class HighlightMatcherTests
{
    [Test]
    public void Split_Literal_MarksEveryMatch()
    {
        var segments = HighlightMatcher.Split("row-1 and row-2", "row");

        Assert.That(segments.Where(s => s.IsMatch).Select(s => s.Text),
            Is.EqualTo(new[] { "row", "row" }));
        Assert.That(string.Concat(segments.Select(s => s.Text)), Is.EqualTo("row-1 and row-2"));
    }

    [Test]
    public void Split_LiteralCaseInsensitive_ByDefault()
    {
        var segments = HighlightMatcher.Split("AbC abc", "abc");

        Assert.That(segments.Count(s => s.IsMatch), Is.EqualTo(2));
    }

    [Test]
    public void Split_CaseSensitive_OnlyMarksExactCase()
    {
        var segments = HighlightMatcher.Split("AbC abc", "abc", caseSensitive: true);

        Assert.That(segments.Count(s => s.IsMatch), Is.EqualTo(1));
        Assert.That(segments.Single(s => s.IsMatch).Text, Is.EqualTo("abc"));
    }

    [Test]
    public void Split_Regex_MarksCapturedRanges()
    {
        var segments = HighlightMatcher.Split("""{"id":42}""", @"\d+", useRegex: true);

        Assert.That(segments.Single(s => s.IsMatch).Text, Is.EqualTo("42"));
    }

    [Test]
    public void Split_InvalidRegex_ReturnsWholeTextUnmatched()
    {
        var segments = HighlightMatcher.Split("abc", "[", useRegex: true);

        Assert.That(segments, Has.Count.EqualTo(1));
        Assert.That(segments[0].IsMatch, Is.False);
        Assert.That(segments[0].Text, Is.EqualTo("abc"));
    }

    [Test]
    public void Split_EmptyQuery_ReturnsWholeText()
    {
        var segments = HighlightMatcher.Split("abc", "");

        Assert.That(segments, Has.Count.EqualTo(1));
        Assert.That(segments[0].IsMatch, Is.False);
    }

    [Test]
    public void Split_CapsSegments()
    {
        var text = string.Join(" ", Enumerable.Repeat("ab", 50));
        var segments = HighlightMatcher.Split(text, "a", maxSegments: 5);

        Assert.That(segments.Count(s => s.IsMatch), Is.LessThanOrEqualTo(5));
        Assert.That(string.Concat(segments.Select(s => s.Text)), Is.EqualTo(text));
    }

    [Test]
    public void HasMatch_ReflectsPresence()
    {
        Assert.That(HighlightMatcher.HasMatch("hello", "ell"), Is.True);
        Assert.That(HighlightMatcher.HasMatch("hello", "xyz"), Is.False);
        Assert.That(HighlightMatcher.HasMatch("hello", ""), Is.False);
    }
}
